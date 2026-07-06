using System.Buffers;
using System.Text;

using RTSharp.Daemon.Services.rtorrent;

using Xunit;

namespace RTSharp.Daemon.Tests
{
    /// <summary>
    /// Every fixture is parsed once per <see cref="ReadOnlySequenceFactory"/> shape - single-segment
    /// (array/Memory/owned-memory backed) and the worst-case segment-per-byte fragmentation (with
    /// interspersed empty segments) - asserting identical output. This is what actually catches
    /// boundary bugs in the SequenceReader-based rewrite of XMLUtils - a bug that only reproduces
    /// when a tag/token straddles a segment boundary would pass silently under single-segment-only
    /// testing. The factory is aspnetcore's Buffers.Testing helper (Buffers.Testing/ReadOnlySequenceFactory.cs).
    /// </summary>
    public class XMLUtilsTests
    {
        public static IEnumerable<object[]> Factories => new ReadOnlySequenceFactory[] {
            ReadOnlySequenceFactory.SingleSegmentFactory,
            ReadOnlySequenceFactory.ArrayFactory,
            ReadOnlySequenceFactory.MemoryFactory,
            ReadOnlySequenceFactory.OwnedMemoryFactory,
            ReadOnlySequenceFactory.SegmentPerByteFactory,
        }.Select(f => new object[] { f });

        private static SequenceReader<byte> MakeReader(string Text, ReadOnlySequenceFactory Factory)
        {
            var bytes = Encoding.UTF8.GetBytes(Text);
            return new SequenceReader<byte>(Factory.CreateWithContent(bytes));
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void GetValue_String_ParsesAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            var reader = MakeReader("<value><string>hello</string></value>\r\n", Factory);

            var result = XMLUtils.GetValue<string>(ref reader);

            Assert.Equal("hello", result);
            Assert.Equal(0, reader.Remaining);
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void GetValue_I4_ParsesAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            var reader = MakeReader("<value><i4>-501</i4></value>\r\n", Factory);

            Assert.Equal(-501, XMLUtils.GetValue<int>(ref reader));
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void GetValue_I8_ParsesAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            var reader = MakeReader("<value><i8>9876543210</i8></value>\r\n", Factory);

            Assert.Equal(9876543210UL, XMLUtils.GetValue<ulong>(ref reader));
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void GetValue_Bool_ParsesAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            var readerTrue = MakeReader("<value><i4>1</i4></value>\r\n", Factory);
            Assert.True(XMLUtils.GetValue<bool>(ref readerTrue));

            var readerFalse = MakeReader("<value><i4>0</i4></value>\r\n", Factory);
            Assert.False(XMLUtils.GetValue<bool>(ref readerFalse));
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void GetValueType_DetectsAllTypesAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            Assert.Equal(SCGI_DATA_TYPE.STRUCT, XMLUtils.GetValueType(MakeReader("<value><struct>\r\n", Factory)));
            Assert.Equal(SCGI_DATA_TYPE.STRING, XMLUtils.GetValueType(MakeReader("<value><string>abc</string></value>", Factory)));
            Assert.Equal(SCGI_DATA_TYPE.I4, XMLUtils.GetValueType(MakeReader("<value><i4>1</i4></value>", Factory)));
            Assert.Equal(SCGI_DATA_TYPE.I8, XMLUtils.GetValueType(MakeReader("<value><i8>1</i8></value>", Factory)));
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void GetFaultStruct_ParsesAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            var reader = MakeReader(
                "<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not find info-hash.</string></value></member>\r\n</struct></value>\r\n",
                Factory);

            var fault = XMLUtils.GetFaultStruct(ref reader, "f.multicall");

            Assert.Equal("f.multicall", fault.Command);
            Assert.Equal("-501", fault.FaultCode);
            Assert.Equal("Could not find info-hash.", fault.FaultString);
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void TryGetFaultStruct_ReturnsNullAndLeavesCursorForNonFaultValue(ReadOnlySequenceFactory Factory)
        {
            var reader = MakeReader("<value><string>not a fault</string></value>\r\n", Factory);

            var fault = XMLUtils.TryGetFaultStruct(ref reader, "d.directory");
            Assert.Null(fault);

            // TryGetFaultStruct must not have consumed the caller's cursor on a failed match
            var value = XMLUtils.GetValue<string>(ref reader);
            Assert.Equal("not a fault", value);
        }

        [Theory]
        [MemberData(nameof(Factories))]
        public void MulticallResponse_RoundTripsAcrossFragmentation(ReadOnlySequenceFactory Factory)
        {
            var entries = new (string Hash, ulong Size)[] {
                ("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 123456UL),
                ("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 789UL),
            };

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
            sb.Append("<methodResponse>\r\n<params>\r\n<param><value><array><data>\r\n");
            foreach (var (hash, size) in entries) {
                sb.Append("<value><array><data>\r\n");
                sb.Append($"<value><string>{hash}</string></value>\r\n");
                sb.Append($"<value><i8>{size}</i8></value>\r\n");
                sb.Append("</data></array></value>\r\n");
            }
            sb.Append("</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n");

            var reader = MakeReader(sb.ToString(), Factory);

            XMLUtils.SeekTo(ref reader, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref reader, XMLUtils.MULTICALL_START);

            var parsed = new List<(string Hash, ulong Size)>();
            while (!XMLUtils.CheckFor(reader, XMLUtils.DATA_TOKEN_END)) {
                XMLUtils.SeekFixed(ref reader, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                var hash = XMLUtils.GetValue<string>(ref reader);
                var size = XMLUtils.GetValue<ulong>(ref reader);
                XMLUtils.SeekFixed(ref reader, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
                parsed.Add((hash, size));
            }

            Assert.Equal(entries, parsed);

            var trailer = "</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n";
            Assert.Equal(Encoding.UTF8.GetByteCount(trailer), (int)reader.Remaining);
        }
    }
}
