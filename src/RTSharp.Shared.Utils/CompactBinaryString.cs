using System.Text;

namespace RTSharp.Shared.Utils;

/// <summary>
/// Packs arbitrary bytes into a string using 7 bits per output byte, so the result only ever contains
/// ASCII 0..127, with the characters that are illegal unescaped inside a JSON string (NUL, LF, CR, double
/// quote, backslash) promoted into a 2-byte UTF-8 sequence instead.
/// </summary>
public static class CompactBinaryString
{
    static readonly byte[] IllegalChars = [
        0,  // null
        10, // newline
        13, // carriage return
        34, // double quote
        92  // backslash
    ];

    public static string Encode(ReadOnlySpan<byte> In)
    {
        var encoded = new byte[In.Length * 2];
        var len = Encode(encoded, In);

        return Encoding.UTF8.GetString(encoded.AsSpan(0, len));
    }

    public static byte[] Decode(string In)
    {
        var decoded = new byte[(In.Length * 2) + 8];
        var len = Decode(decoded, In);

        return decoded[..len];
    }

    private static int Encode(Span<byte> encoded, ReadOnlySpan<byte> rawData)
    {
        int curIndex = 0;
        int curBit = 0;
        int encodedCursor = 0;

        byte get7(ReadOnlySpan<byte> rawData)
        {
            var firstByte = rawData[curIndex];
            byte firstPart = (byte)((((0b11111110 >> curBit) & firstByte) << curBit) >> 1);
            curBit += 7;
            if (curBit < 8)
                return firstPart;
            curBit -= 8;
            curIndex++;
            if (curIndex >= rawData.Length)
                return firstPart;
            var secondByte = rawData[curIndex];
            var secondPart = ((0xFF00 >> curBit) & secondByte & 0xFF) >> (8 - curBit);
            return (byte)(firstPart | secondPart);
        }

        while (true) {
            if (curIndex >= rawData.Length)
                break;
            var bits = get7(rawData);

            int illegalIndex;
            if (bits <= 34 && (illegalIndex = Array.IndexOf(IllegalChars, bits)) != -1) {
                byte b1, nextBits;

                if (curIndex >= rawData.Length) {
                    b1 = 0b11011110;
                    nextBits = bits;
                } else {
                    nextBits = get7(rawData);
                    b1 = (byte)(0b11000010 | (0b111 & illegalIndex) << 2);
                }

                encoded[encodedCursor++] = (byte)(b1 | ((nextBits & 0b01000000) >> 6));
                encoded[encodedCursor++] = (byte)(0b10000000 | (nextBits & 0b00111111));
            } else {
                encoded[encodedCursor++] = bits;
            }
        }

        return encodedCursor;
    }

    private static int Decode(Span<byte> decoded, string In)
    {
        int decodedCursor = 0;
        byte curByte = 0;
        int bitOfByte = 0;

        void push7(Span<byte> decoded, byte byt)
        {
            byt <<= 1;
            curByte |= (byte)(byt >> bitOfByte);
            bitOfByte += 7;
            if (bitOfByte >= 8) {
                decoded[decodedCursor++] = curByte;
                bitOfByte -= 8;
                curByte = (byte)((byt << (7 - bitOfByte)) & 255);
            }
        }

        for (var i = 0; i < In.Length; i++) {
            var c = In[i];
            if (c > 0x7F) {
                var illegalIndex = (c >> 8) & 0b111;
                if (illegalIndex != 0b111)
                    push7(decoded, IllegalChars[illegalIndex]);
                push7(decoded, (byte)(c & 0x7F));
            } else {
                push7(decoded, (byte)c);
            }
        }

        return decodedCursor;
    }
}
