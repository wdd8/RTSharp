using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

using rm.Trie;

using RTSharp.DataProvider.Rtorrent.Protocols.Types;

namespace RTSharp.DataProvider.Rtorrent.Server.Utils
{
    public class XMLUtils
    {
        static Trie XmlEscapeTrie;
        static Dictionary<string, char> XmlEscapeDict;

        static XMLUtils()
        {
            XmlEscapeTrie = new Trie();
            XmlEscapeTrie.AddWord("amp;");
            XmlEscapeTrie.AddWord("quot;");
            XmlEscapeTrie.AddWord("apos;");
            XmlEscapeTrie.AddWord("lt;");
            XmlEscapeTrie.AddWord("gt;");

            XmlEscapeDict = new Dictionary<string, char>() {
                { "amp;", '&' },
                { "quot;", '"' },
                { "apos;", '\'' },
                { "lt;", '<' },
                { "gt;", '>' }
            };
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekTo(ref ReadOnlyMemory<byte> In, ReadOnlySpan<byte> What)
        {
            In = In[(In.Span.IndexOf(What) + What.Length)..];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekToBefore(ref ReadOnlyMemory<byte> In, ReadOnlySpan<byte> What)
        {
            In = In[In.Span.IndexOf(What)..];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekFixed(ref ReadOnlyMemory<byte> In, int Length)
        {
            In = In[Length..];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekFixed(ref ReadOnlyMemory<byte> In, ReadOnlySpan<byte> What)
        {
            Debug.Assert(In[..What.Length].Span.SequenceEqual(What));
            In = In[What.Length..];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool MaybeSeekFixed(ref ReadOnlyMemory<byte> In, ReadOnlySpan<byte> What)
        {
            if (In[..What.Length].Span.SequenceEqual(What)) {
                In = In[What.Length..];
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckFor(ReadOnlyMemory<byte> In, ReadOnlySpan<byte> What)
        {
            return In.Length >= What.Length && In[..What.Length].Span.SequenceEqual(What);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetUntilNextTag(ref ReadOnlyMemory<byte> In)
        {
            var end = In.Span.IndexOf(ENDING_TAG);
            Debug.Assert(end != -1);

            var ret = In[..end];
            In = In[(end + 2)..];

            end = In.Span.IndexOf((byte)'>');
            Debug.Assert(end != -1);

            In = In[(end + 1)..];

            return Encoding.UTF8.GetString(ret.Span);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SeekNextTag(ref ReadOnlyMemory<byte> In)
        {
            var i = In.Span.IndexOf((byte)'<');
            Debug.Assert(i != -1);

            var ret = In[..i];

            i = In.Span.IndexOf((byte)'>');
            Debug.Assert(i != -1);

            In = In[(i + 1)..];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetAnyValue(ref ReadOnlyMemory<byte> In)
        {
            SeekFixed(ref In, VALUE_TOKEN);
            SeekNextTag(ref In);

            var ret = GetUntilNextTag(ref In);

            SeekFixed(ref In, VALUE_TOKEN_END);
            MaybeSeekFixed(ref In, NEWLINE);

            return ret;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T GetValue<T>(ref ReadOnlyMemory<byte> In)
        {
            SeekFixed(ref In, VALUE_TOKEN);
            T ret;

            switch (typeof(T)) {
                case Type strType when strType == typeof(string):
                    SeekFixed(ref In, STRING_TOKEN);
                    ret = (T)(object)GetUntilNextTag(ref In);
                    break;
                case Type strType when strType == typeof(int):
                    SeekFixed(ref In, I4_TOKEN);
                    ret = (T)(object)Int32.Parse(GetUntilNextTag(ref In));
                    break;
                case Type strType when strType == typeof(uint):
                    SeekFixed(ref In, I4_TOKEN);
                    ret = (T)(object)UInt32.Parse(GetUntilNextTag(ref In));
                    break;
                case Type strType when strType == typeof(short):
                    SeekFixed(ref In, 4);
                    ret = (T)(object)Int16.Parse(GetUntilNextTag(ref In));
                    break;
                case Type strType when strType == typeof(ushort):
                    SeekFixed(ref In, 4);
                    ret = (T)(object)UInt16.Parse(GetUntilNextTag(ref In));
                    break;
                case Type strType when strType == typeof(long):
                    SeekFixed(ref In, I8_TOKEN);
                    ret = (T)(object)Int64.Parse(GetUntilNextTag(ref In));
                    break;
                case Type strType when strType == typeof(ulong):
                    SeekFixed(ref In, I8_TOKEN);
                    ret = (T)(object)UInt64.Parse(GetUntilNextTag(ref In));
                    break;
                case Type strType when strType == typeof(bool):
                    SeekFixed(ref In, 4); // Works for i4 too
                    ret = (T)(object)(Int64.Parse(GetUntilNextTag(ref In)) == 1);
                    break;
                default:
                    throw new ArgumentException(nameof(T));
            }

            SeekFixed(ref In, VALUE_TOKEN_END);
            MaybeSeekFixed(ref In, NEWLINE);

            return ret;
        }

        public static IEnumerable<KeyValuePair<string, string>> GetStruct(ref ReadOnlyMemory<byte> In)
        {
            var ret = new List<KeyValuePair<string, string>>();

            SeekFixed(ref In, VALUE_TOKEN);
            SeekFixed(ref In, STRUCT_TOKEN);
            MaybeSeekFixed(ref In, NEWLINE);

            while (!CheckFor(In, ENDING_TAG)) {
                Debug.Assert(In.Span[..14].SequenceEqual("<member><name>"u8));

                string name, value;
                SeekFixed(ref In, MEMBER_TOKEN); {
                    SeekFixed(ref In, NAME_TOKEN);
                    name = GetUntilNextTag(ref In);

                    MaybeSeekFixed(ref In, NEWLINE);

                    SeekFixed(ref In, VALUE_TOKEN); {
                        SeekNextTag(ref In);
                        value = GetUntilNextTag(ref In);
                    } SeekFixed(ref In, VALUE_TOKEN_END);

                } SeekFixed(ref In, MEMBER_TOKEN_END);
                MaybeSeekFixed(ref In, NEWLINE);

                ret.Add(new KeyValuePair<string, string>(name, value));
                Debug.Assert(In.Length >= 2);
            }

            SeekFixed(ref In, STRUCT_TOKEN_END);
            SeekFixed(ref In, VALUE_TOKEN_END);
            MaybeSeekFixed(ref In, NEWLINE);

            return ret;
        }

        public static Status GetFaultStruct(ref ReadOnlyMemory<byte> In, string Command)
        {
            // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-506</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Method 'base_path' not defined</string></value></member>\r\n</struct></value>\r\n
            var ret = new Status() {
                Command = Command
            };
            foreach (var kv in GetStruct(ref In)) {
                if (kv.Key == "faultCode")
                    ret.FaultCode = kv.Value;
                if (kv.Key == "faultString")
                    ret.FaultString = kv.Value;
            }

            return ret;
        }

        public static Status? TryGetFaultStruct(ref ReadOnlyMemory<byte> In, string Command)
        {
            // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-506</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Method 'base_path' not defined</string></value></member>\r\n</struct></value>\r\n
            var ret = new Status() {
                Command = Command
            };

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
            if (check.Length < 10)
                return null;
            if (!check[..9].Span.SequenceEqual("faultCode"u8))
                return null;

            foreach (var kv in GetStruct(ref In)) {
                if (kv.Key == "faultCode")
                    ret.FaultCode = kv.Value;
                if (kv.Key == "faultString")
                    ret.FaultString = kv.Value;
            }

            return ret;
        }

        public static SCGI_DATA_TYPE? GetValueType(ReadOnlyMemory<byte> In)
        {
            Debug.Assert(In.Span[..8].SequenceEqual(Encoding.UTF8.GetBytes("<value><")));

            In = In[8..];

            switch (In.Span[0]) {
                case (byte)'s':
                    Debug.Assert(In.Length > 6);

                    if (In.Span[3] == (byte)'u') // stru ct
                        return SCGI_DATA_TYPE.STRUCT;
                    else if (In.Span[3] == (byte)'i') // stri ng
                        return SCGI_DATA_TYPE.STRING;
                    goto default;
                case (byte)'i':
                    if (In.Span[1] == (byte)'4')
                        return SCGI_DATA_TYPE.I4;
                    else if (In.Span[1] == (byte)'8')
                        return SCGI_DATA_TYPE.I8;
                    goto default;
                default:
                    return null;
            }
        }

        public static string EncodeWithSpaces(string In)
        {
            var ret = WebUtility.UrlEncode(In);

            return ret.Replace("%7e", "~", StringComparison.OrdinalIgnoreCase).Replace("*", "%2A").Replace("(", "%28").Replace(")", "%29");
        }

        public static string Encode(string In)
        {
            var sb = new StringBuilder();

            foreach (var rune in In.EnumerateRunes()) {
                sb.Append(rune.Value switch {
                    '&' => "&amp;",
                    '"' => "&quot;",
                    '\'' => "&apos;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    _ => rune.ToString()
                });
            }
            
            return sb.ToString();
        }

        public static string Decode(string In)
        {
            static string? check(ReadOnlySpan<char> span)
            {
                var x = 0;
                var root = XmlEscapeTrie.GetRootTrieNode();
                var sb = new StringBuilder();

                foreach (var chr in span) {
                    var c = root.GetChild(chr);
                    if (c == null) {
                        if (!root.GetChildren().Any()) {
                            return sb.ToString();
                        }
                        return null;
                    }

                    root = c;
                    sb.Append(c.Character);
                    x++;
                }

                if (!root.GetChildren().Any()) {
                    return sb.ToString();
                }

                return null;
            }

            var sb = new StringBuilder();

            for (var x = 0;x < In.Length;x++) {
                if (In[x] == '&') {
                    var s = check(In[(x + 1)..].AsSpan());
                    if (s != null) {
                        sb.Append(XmlEscapeDict[s]);
                        x += s.Length;
                    } else
                        sb.Append('&');
                } else
                    sb.Append(In[x]);
            }

            return sb.ToString();
        }
    }
}
