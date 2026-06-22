using System.Buffers.Text;
using System.Text;

namespace RTSharp.Daemon.Services.rtorrent
{
    public static class SCGIPayloadBuilder
    {
        static readonly ReadOnlyMemory<byte> FirstPart = Encoding.ASCII.GetBytes(":CONTENT_LENGTH\x00");
        static readonly ReadOnlyMemory<byte> SecondPart = Encoding.ASCII.GetBytes("\x00SCGI\x00" + "1\x00,");

        public static Memory<byte> BuildPayload(string XmlBody)
        {
            var bodyLen = XmlBody.Length;

            // XXX - first part length
            var xmlLenDigits = bodyLen == 0 ? 1 : (int)Math.Floor(Math.Log10(bodyLen)) + 1;

            var firstPartLen = FirstPart.Length - 1 + xmlLenDigits + SecondPart.Length - 1;
            var firstPartLenDigitLen = firstPartLen == 0 ? 1 : (int)Math.Floor(Math.Log10(firstPartLen)) + 1;

            var total = firstPartLenDigitLen + FirstPart.Length + xmlLenDigits + SecondPart.Length + bodyLen;
            Memory<byte> all = new byte[total];
            var dst = all.Span;

            var cursor = 0;

            // XXX
            Utf8Formatter.TryFormat(firstPartLen, dst[cursor..], out var written);
            cursor += written;

            // XXX:CONTENT_LENGTH\0
            FirstPart.Span.CopyTo(dst[cursor..]);
            cursor += FirstPart.Length;

            // XXX:CONTENT_LENGTH\0XXXXX
            Utf8Formatter.TryFormat(bodyLen, dst[cursor..], out written);
            cursor += written;

            // XXX:CONTENT_LENGTH\0XXXXX\0SCGI\01\0,
            SecondPart.Span.CopyTo(dst[cursor..]);
            cursor += SecondPart.Length;

            // XXX:CONTENT_LENGTH\0XXXXX\0SCGI\01\0,content
            cursor += Encoding.ASCII.GetBytes(XmlBody, dst[cursor..]);

            return all[0..cursor];
        }
    }
}
