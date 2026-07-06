using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class XMLUtils
    {
        public static readonly byte[] NEWLINE = "\r\n"u8.ToArray();

        public static readonly byte[] METHOD_RESPONSE = "<methodResponse>\r\n"u8.ToArray();

        public static readonly byte[] SINGLE_PARAM = "<params>\r\n<param>"u8.ToArray();

        public static readonly byte[] MULTICALL_START = "<params>\r\n<param><value><array><data>\r\n"u8.ToArray();
        public static readonly byte[] MULTICALL_RESPONSE_ENTRY_START = "<value><array><data>\r\n"u8.ToArray();
        public static readonly byte[] MULTICALL_RESPONSE_ENTRY_END = "</data></array></value>\r\n"u8.ToArray();

        public static readonly byte[] FAULT_TOKEN = "<fault>"u8.ToArray();
        public static readonly byte[] FAULT_TOKEN_END = "</fault>"u8.ToArray();

        public static readonly byte[] VALUE_TOKEN = "<value>"u8.ToArray();
        public static readonly byte[] VALUE_TOKEN_END = "</value>"u8.ToArray();

        public static readonly byte[] STRING_TOKEN = "<string>"u8.ToArray();
        public static readonly byte[] STRING_TOKEN_END = "</string>"u8.ToArray();

        public static readonly byte[] STRUCT_TOKEN = "<struct>"u8.ToArray();
        public static readonly byte[] STRUCT_TOKEN_END = "</struct>"u8.ToArray();

        public static readonly byte[] DATA_TOKEN = "<data>"u8.ToArray();
        public static readonly byte[] DATA_TOKEN_END = "</data>"u8.ToArray();

        public static readonly byte[] NAME_TOKEN = "<name>"u8.ToArray();
        public static readonly byte[] NAME_TOKEN_END = "</name>"u8.ToArray();

        public static readonly byte[] MEMBER_TOKEN = "<member>"u8.ToArray();
        public static readonly byte[] MEMBER_TOKEN_END = "</member>"u8.ToArray();

        public static readonly byte[] I4_TOKEN = "<i4>"u8.ToArray();
        public static readonly byte[] I4_TOKEN_END = "</i4>"u8.ToArray();

        public static readonly byte[] VALUE_I8_TOKENS = "<value><i8>"u8.ToArray();

        public static readonly byte[] I8_TOKEN = "<i8>"u8.ToArray();
        public static readonly byte[] I8_TOKEN_END = "</i8>"u8.ToArray();

        public static readonly byte[] ENDING_TAG = "</"u8.ToArray();

        public record FaultStatus
        {
            public required string Command { get; set; }
            public required string FaultCode { get; set; }
            public required string FaultString { get; set; }

            public string ToFailureString()
            {
                return $"{Command}: {FaultCode} {FaultString}";
            }
        }
        public class TorrentsResult
        {
            public byte[] InfoHash { get; set; }
            public FaultStatus[] Status { get; set; }

            public TorrentsResult(byte[] InfoHash, FaultStatus[] Status)
            {
                this.InfoHash = InfoHash;
                this.Status = Status;
            }
        }

        private const int VALUETYPE_BUF_SIZE = 32;

        public static string GetUtf8String(ReadOnlySequence<byte> Seq)
        {
            if (Seq.IsSingleSegment)
                return Encoding.UTF8.GetString(Seq.FirstSpan);

            if (Seq.Length <= 256) {
                Span<byte> buf = stackalloc byte[(int)Seq.Length];
                Seq.CopyTo(buf);
                return Encoding.UTF8.GetString(buf);
            }

            var rented = ArrayPool<byte>.Shared.Rent((int)Seq.Length);
            try {
                Seq.CopyTo(rented);
                return Encoding.UTF8.GetString(rented, 0, (int)Seq.Length);
            } finally {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlySpan<byte> GetContiguous(ReadOnlySequence<byte> Seq, Span<byte> Scratch)
        {
            if (Seq.IsSingleSegment)
                return Seq.FirstSpan;

            Debug.Assert(Seq.Length <= Scratch.Length);
            var slice = Scratch[..(int)Seq.Length];
            Seq.CopyTo(slice);
            return slice;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekTo(ref SequenceReader<byte> In, ReadOnlySpan<byte> What)
        {
            var found = In.TryReadTo(out ReadOnlySequence<byte> _, What, advancePastDelimiter: true);
            Debug.Assert(found);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekToBefore(ref SequenceReader<byte> In, ReadOnlySpan<byte> What)
        {
            var found = In.TryReadTo(out ReadOnlySequence<byte> _, What, advancePastDelimiter: false);
            Debug.Assert(found);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekFixed(ref SequenceReader<byte> In, int Length)
        {
            In.Advance(Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekFixed(ref SequenceReader<byte> In, ReadOnlySpan<byte> What)
        {
            Debug.Assert(In.IsNext(What, advancePast: false));
            In.Advance(What.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MaybeSeekFixed(ref SequenceReader<byte> In, ReadOnlySpan<byte> What)
        {
            return In.IsNext(What, advancePast: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckFor(SequenceReader<byte> In, ReadOnlySpan<byte> What)
        {
            return In.IsNext(What, advancePast: false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySequence<byte> GetBytesUntilNextTag(ref SequenceReader<byte> In)
        {
            var found = In.TryReadTo(out ReadOnlySequence<byte> ret, ENDING_TAG, advancePastDelimiter: true);
            Debug.Assert(found);

            found = In.TryAdvanceTo((byte)'>', advancePastDelimiter: true);
            Debug.Assert(found);

            return ret;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetUntilNextTag(ref SequenceReader<byte> In)
            => GetUtf8String(GetBytesUntilNextTag(ref In));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekNextTag(ref SequenceReader<byte> In)
        {
            var found = In.TryAdvanceTo((byte)'>', advancePastDelimiter: true);
            Debug.Assert(found);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetAnyValue(ref SequenceReader<byte> In)
        {
            SeekFixed(ref In, VALUE_TOKEN);
            SeekNextTag(ref In);

            var ret = GetUntilNextTag(ref In);

            SeekFixed(ref In, VALUE_TOKEN_END);
            MaybeSeekFixed(ref In, NEWLINE);

            return ret;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetValue<T>(ref SequenceReader<byte> In)
        {
            SeekFixed(ref In, VALUE_TOKEN);
            T ret;

            switch (typeof(T)) {
                case Type strType when strType == typeof(string):
                    SeekFixed(ref In, STRING_TOKEN);
                    var str = GetUntilNextTag(ref In);
                    ret = Unsafe.As<string, T>(ref str);
                    break;
                case Type strType when strType == typeof(int): {
                    SeekFixed(ref In, I4_TOKEN);
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out int i32, out _);
                    ret = Unsafe.As<int, T>(ref i32);
                    break;
                }
                case Type strType when strType == typeof(uint): {
                    SeekFixed(ref In, I4_TOKEN);
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out uint u32, out _);
                    ret = Unsafe.As<uint, T>(ref u32);
                    break;
                }
                case Type strType when strType == typeof(short): {
                    SeekFixed(ref In, 4);
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out short i16, out _);
                    ret = Unsafe.As<short, T>(ref i16);
                    break;
                }
                case Type strType when strType == typeof(ushort): {
                    SeekFixed(ref In, 4);
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out ushort u16, out _);
                    ret = Unsafe.As<ushort, T>(ref u16);
                    break;
                }
                case Type strType when strType == typeof(long): {
                    SeekFixed(ref In, I8_TOKEN);
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out long i64, out _);
                    ret = Unsafe.As<long, T>(ref i64);
                    break;
                }
                case Type strType when strType == typeof(ulong): {
                    SeekFixed(ref In, I8_TOKEN);
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out ulong u64, out _);
                    ret = Unsafe.As<ulong, T>(ref u64);
                    break;
                }
                case Type strType when strType == typeof(bool): {
                    SeekFixed(ref In, 4); // Works for i4 too
                    Span<byte> val = stackalloc byte[VALUETYPE_BUF_SIZE];
                    Utf8Parser.TryParse(GetContiguous(GetBytesUntilNextTag(ref In), val), out long boolVal, out _);
                    var @bool = boolVal == 1;
                    ret = Unsafe.As<bool, T>(ref @bool);
                    break;
                }
                default:
                    throw new ArgumentException(nameof(T));
            }

            SeekFixed(ref In, VALUE_TOKEN_END);
            MaybeSeekFixed(ref In, NEWLINE);

            return ret;
        }

        public static IEnumerable<KeyValuePair<string, string>> GetStruct(ref SequenceReader<byte> In)
        {
            var ret = new List<KeyValuePair<string, string>>();

            SeekFixed(ref In, VALUE_TOKEN);
            SeekFixed(ref In, STRUCT_TOKEN);
            MaybeSeekFixed(ref In, NEWLINE);

            while (!CheckFor(In, ENDING_TAG)) {
                Debug.Assert(In.IsNext("<member><name>"u8, advancePast: false));

                string name, value;
                SeekFixed(ref In, MEMBER_TOKEN);
                {
                    SeekFixed(ref In, NAME_TOKEN);
                    name = GetUntilNextTag(ref In);

                    MaybeSeekFixed(ref In, NEWLINE);

                    SeekFixed(ref In, VALUE_TOKEN);
                    {
                        SeekNextTag(ref In);
                        value = GetUntilNextTag(ref In);
                    }
                    SeekFixed(ref In, VALUE_TOKEN_END);

                }
                SeekFixed(ref In, MEMBER_TOKEN_END);
                MaybeSeekFixed(ref In, NEWLINE);

                ret.Add(new KeyValuePair<string, string>(name, value));
                Debug.Assert(In.Remaining >= 2);
            }

            SeekFixed(ref In, STRUCT_TOKEN_END);
            SeekFixed(ref In, VALUE_TOKEN_END);
            MaybeSeekFixed(ref In, NEWLINE);

            return ret;
        }

        public static FaultStatus GetFaultStruct(ref SequenceReader<byte> In, string Command)
        {
            // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-506</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Method 'base_path' not defined</string></value></member>\r\n</struct></value>\r\n
            string faultCode = "", faultString = "";
            foreach (var kv in GetStruct(ref In)) {
                if (kv.Key == "faultCode")
                    faultCode = kv.Value;
                if (kv.Key == "faultString")
                    faultString = kv.Value;
            }
            var ret = new FaultStatus {
                Command = Command,
                FaultCode = faultCode,
                FaultString = faultString
            };

            return ret;
        }

        public static FaultStatus? TryGetFaultStruct(ref SequenceReader<byte> In, string Command)
        {
            // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-506</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Method 'base_path' not defined</string></value></member>\r\n</struct></value>\r\n

            var check = In;
            if (!MaybeSeekFixed(ref check, VALUE_TOKEN))
                return null;
            if (!MaybeSeekFixed(ref check, STRUCT_TOKEN))
                return null;
            MaybeSeekFixed(ref check, NEWLINE);
            if (!MaybeSeekFixed(ref check, MEMBER_TOKEN))
                return null;
            if (!MaybeSeekFixed(ref check, NAME_TOKEN))
                return null;
            if (!check.IsNext("faultCode"u8, advancePast: false))
                return null;

            string faultCode = "", faultString = "";
            foreach (var kv in GetStruct(ref In)) {
                if (kv.Key == "faultCode")
                    faultCode = kv.Value;
                if (kv.Key == "faultString")
                    faultString = kv.Value;
            }

            var ret = new FaultStatus {
                Command = Command,
                FaultCode = faultCode,
                FaultString = faultString
            };

            return ret;
        }

        public static SCGI_DATA_TYPE? GetValueType(SequenceReader<byte> In)
        {
            Span<byte> buf = stackalloc byte[12];
            var len = 0;
            {
                var peek = In;
                while (len < buf.Length && peek.TryRead(out var b))
                    buf[len++] = b;
            }

            Debug.Assert(len >= 8);
            Debug.Assert(buf[..8].SequenceEqual("<value><"u8));

            if (len < 9)
                return null;

            switch (buf[8]) {
                case (byte)'s':
                    Debug.Assert(len > 11);
                    if (len < 12)
                        return null;
                    if (buf[11] == (byte)'u') // stru ct
                        return SCGI_DATA_TYPE.STRUCT;
                    else if (buf[11] == (byte)'i') // stri ng
                        return SCGI_DATA_TYPE.STRING;
                    return null;
                case (byte)'i':
                    if (len < 10)
                        return null;
                    if (buf[9] == (byte)'4')
                        return SCGI_DATA_TYPE.I4;
                    else if (buf[9] == (byte)'8')
                        return SCGI_DATA_TYPE.I8;
                    return null;
                default:
                    return null;
            }
        }

        public static string EncodeWithSpaces(string In)
        {
            var ret = WebUtility.UrlEncode(In);

            return ret.Replace("%7e", "~", StringComparison.OrdinalIgnoreCase).Replace("*", "%2A").Replace("(", "%28").Replace(")", "%29");
        }

        private static readonly System.Buffers.SearchValues<char> XmlSpecialChars =
            System.Buffers.SearchValues.Create("&\"'<>");

        public static string Encode(string In)
        {
            if (In.AsSpan().IndexOfAny(XmlSpecialChars) < 0)
                return In;

            var sb = new StringBuilder(In.Length + 16);

            foreach (var chr in In) {
                switch (chr) {
                    case '&': sb.Append("&amp;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(chr); break;
                }
            }

            return sb.ToString();
        }

        public static string Decode(string In)
        {
            var span = In.AsSpan();
            var andIndex = span.IndexOf('&');
            if (andIndex < 0)
                return In;

            var sb = new StringBuilder(In.Length);
            sb.Append(span[..andIndex]);

            var x = andIndex;
            while (x < span.Length) {
                var right = span[(x + 1)..];

                if (right.StartsWith("amp;")) { sb.Append('&'); x += 1 + 4; }
                else if (right.StartsWith("quot;")) { sb.Append('"'); x += 1 + 5; }
                else if (right.StartsWith("apos;")) { sb.Append('\''); x += 1 + 5; }
                else if (right.StartsWith("lt;")) { sb.Append('<'); x += 1 + 3; }
                else if (right.StartsWith("gt;")) { sb.Append('>'); x += 1 + 3; }
                else { sb.Append('&'); x += 1; }

                var rest = span[x..];
                var next = rest.IndexOf('&');
                if (next < 0) {
                    sb.Append(rest);
                    break;
                }
                sb.Append(rest[..next]);
                x += next;
            }

            return sb.ToString();
        }
    }
}
