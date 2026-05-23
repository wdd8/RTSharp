using BencodeNET.Objects;

using System.Globalization;
using System.Security.Cryptography;

namespace RTSharp.Shared.Utils
{
    public partial class TorrentCreator
    {
        private const int V1PaddingBufferSize = 16 * 1024;
        private static readonly byte[] V1PaddingBuffer = new byte[V1PaddingBufferSize];

        private static void AddV1Info(BDictionary Info, bool SingleFile, List<TorrentFileEntry> AllFiles, MemoryStream V1PieceHashes, int PieceLength, bool Hybrid)
        {
            Info["pieces"] = new BString(GetV1PieceHashBytes(V1PieceHashes));

            if (SingleFile) {
                Info.Add("length", AllFiles[0].Info.Length);
                return;
            }

            var files = new BList();
            int lastNonEmptyFileIndex = AllFiles.FindLastIndex(file => file.Info.Length > 0);

            for (var x = 0;x < AllFiles.Count;x++) {
                var entry = AllFiles[x];
                files.Value.Add(new BDictionary {
                    { "length", entry.Info.Length },
                    { "path", new BList(entry.RelativePath) }
                });

                if (Hybrid && x < lastNonEmptyFileIndex && entry.Info.Length > 0) {
                    long padding = GetPaddingLength(entry.Info.Length, PieceLength);
                    if (padding > 0) {
                        files.Value.Add(new BDictionary {
                            { "length", padding },
                            { "attr", new BString("p") },
                            { "path", new BList([".pad", padding.ToString(CultureInfo.InvariantCulture)]) }
                        });
                    }
                }
            }

            Info["files"] = files;
        }

        private static long GetV1PayloadLength(long DataLength, List<TorrentFileEntry> AllFiles, int PieceLength, int LastNonEmptyFileIndex, bool Hybrid)
        {
            if (!Hybrid)
                return DataLength;

            long length = DataLength;
            for (var x = 0;x < LastNonEmptyFileIndex;x++) {
                var entryLength = AllFiles[x].Info.Length;
                if (entryLength == 0)
                    continue;

                length += GetPaddingLength(entryLength, PieceLength);
            }

            return length;
        }

        private static int GetV1PieceHashCapacity(long PayloadLength, int PieceLength)
        {
            long pieceCount = (long)Math.Ceiling((decimal)PayloadLength / PieceLength);
            if (pieceCount > int.MaxValue / 20)
                return int.MaxValue;

            return (int)pieceCount * 20;
        }

        private static byte[] GetV1PieceHashBytes(MemoryStream PieceHashes)
        {
            if (PieceHashes.TryGetBuffer(out var buffer) &&
                buffer.Offset == 0 &&
                buffer.Array != null &&
                buffer.Count == buffer.Array.Length) {
                return buffer.Array;
            }

            return PieceHashes.ToArray();
        }

        private class V1PieceHasher(TorrentCreationProgress TorrentCreationProgress, int PieceLength, int HashCapacity)
        {
            private byte[]? PieceBuffer;
            private int PieceOffset;

            public MemoryStream Hashes { get; } = new(HashCapacity);

            public void Add(Span<byte> In)
            {
                if (PieceOffset > 0) {
                    var pieceBuffer = PieceBuffer!;
                    int bytesToCopy = Math.Min(PieceLength - PieceOffset, In.Length);
                    In[..bytesToCopy].CopyTo(pieceBuffer.AsSpan(PieceOffset));
                    PieceOffset += bytesToCopy;
                    In = In[bytesToCopy..];

                    if (PieceOffset < PieceLength)
                        return;

                    AddPieceHash(pieceBuffer);
                }

                while (In.Length >= PieceLength) {
                    AddPieceHash(In[..PieceLength]);
                    In = In[PieceLength..];
                }

                if (In.Length > 0) {
                    PieceBuffer ??= new byte[PieceLength];
                    In.CopyTo(PieceBuffer);
                    PieceOffset = In.Length;
                }
            }

            public void AddPadding(long Length, int PieceLength)
            {
                if (Length == 0)
                    return;

                var zeroes = V1PaddingBuffer.AsSpan()[..Math.Min(V1PaddingBufferSize, PieceLength)];
                while (Length > 0) {
                    int bytesToHash = (int)Math.Min(zeroes.Length, Length);
                    Add(zeroes[..bytesToHash]);
                    Length -= bytesToHash;
                }
            }

            public void Flush()
            {
                if (PieceOffset == 0)
                    return;

                AddPieceHash(PieceBuffer!.AsSpan(0, PieceOffset));
            }

            private void AddPieceHash(ReadOnlySpan<byte> In)
            {
                Span<byte> hash = stackalloc byte[20];
                SHA1.HashData(In, hash);
                TorrentCreationProgress.ReportHashProgress(hash);
                Hashes.Write(hash);
                PieceOffset = 0;
            }
        }
    }
}
