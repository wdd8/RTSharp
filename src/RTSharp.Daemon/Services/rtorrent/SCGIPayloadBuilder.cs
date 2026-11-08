using System.Buffers.Text;
using System.Text;

namespace RTSharp.Daemon.Services.rtorrent
{
    public static class SCGIPayloadBuilder
    {
        static readonly ReadOnlyMemory<byte> FirstPart = Encoding.ASCII.GetBytes(":CONTENT_LENGTH\x00");
        static readonly ReadOnlyMemory<byte> SecondPart = Encoding.ASCII.GetBytes("\x00SCGI\x00" + "1\x00,");

        public static byte[] BuildHeader(int BodyLength)
        {
            Span<byte> bodyLen = stackalloc byte[10];
            Utf8Formatter.TryFormat(BodyLength, bodyLen, out var bodyLenDigits);

            var firstPartLen = FirstPart.Length - 1 + bodyLenDigits + SecondPart.Length - 1;
            Span<byte> firstPartLenRaw = stackalloc byte[10];
            Utf8Formatter.TryFormat(firstPartLen, firstPartLenRaw, out var firstPartLenDigits);

            var ret = new byte[firstPartLenDigits + FirstPart.Length + bodyLenDigits + SecondPart.Length];
            var dst = ret.AsSpan();

            var cursor = 0;

            // XXX
            firstPartLenRaw[..firstPartLenDigits].CopyTo(dst);
            cursor += firstPartLenDigits;

            // XXX:CONTENT_LENGTH\0
            FirstPart.Span.CopyTo(dst[cursor..]);
            cursor += FirstPart.Length;

            // XXX:CONTENT_LENGTH\0XXXXX
            bodyLen[..bodyLenDigits].CopyTo(dst[cursor..]);
            cursor += bodyLenDigits;

            // XXX:CONTENT_LENGTH\0XXXXX\0SCGI\01\0,
            SecondPart.Span.CopyTo(dst[cursor..]);

            return ret;
        }
    }
}
