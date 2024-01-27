using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.DataProvider.Rtorrent.Server
{
    public static class SCGIPayloadBuilder
    {
        static readonly ReadOnlyMemory<byte> FirstPart = Encoding.ASCII.GetBytes(":CONTENT_LENGTH\x00");
        static readonly ReadOnlyMemory<byte> SecondPart = Encoding.ASCII.GetBytes("\x00SCGI\x00" + "1\x00,");

        public static Memory<byte> BuildPayload(string XmlBody)
        {
            int cursor = 0;
            Memory<byte> all = new byte[2 /* for first length */ + FirstPart.Length + 8 /* max 99999 KiB xml */ + SecondPart.Length + XmlBody.Length];
            Span<byte> firstPartLen = Encoding.ASCII.GetBytes((FirstPart.Length - 1 /* remove : */ + XmlBody.Length.ToString().Length + SecondPart.Length - 1 /* also don't count the random comma because why not */).ToString());

            firstPartLen.CopyTo(all.Span[0..firstPartLen.Length]);
            cursor += firstPartLen.Length;

            // XXX - first part length

            FirstPart.Span.CopyTo(all.Span[cursor..(cursor + FirstPart.Length)]);
            cursor += FirstPart.Length;

            // XXX:CONTENT_LENGTH\0

            Span<byte> xmlLen = Encoding.ASCII.GetBytes(XmlBody.Length.ToString());

            xmlLen.CopyTo(all.Span[cursor..(cursor + xmlLen.Length)]);
            cursor += xmlLen.Length;

            // XXX:CONTENT_LENGTH\0XXXXX

            SecondPart.Span.CopyTo(all.Span[cursor..(cursor + SecondPart.Length)]);
            cursor += SecondPart.Length;

            // XXX:CONTENT_LENGTH\0XXXXX\0SCGI\01\0,

            Encoding.ASCII.GetBytes(XmlBody).CopyTo(all.Span[cursor..(cursor + XmlBody.Length)]);
            cursor += XmlBody.Length;
            
            // XXX:CONTENT_LENGTH\0XXXXX\0SCGI\01\0,content

            return all[0..cursor];
        }
    }
}
