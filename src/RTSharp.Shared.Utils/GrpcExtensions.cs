namespace RTSharp.Shared.Utils;

public static class GrpcExtensions
{
    public static Google.Protobuf.ByteString ToByteString(this byte[] In) => Google.Protobuf.ByteString.CopyFrom(In);
    public static Google.Protobuf.ByteString ToByteString(this Span<byte> In) => Google.Protobuf.ByteString.CopyFrom(In);

    public static Google.Protobuf.WellKnownTypes.BytesValue ToBytesValue(this byte[] In) => ToBytesValue((ReadOnlySpan<byte>)In);
    public static Google.Protobuf.WellKnownTypes.BytesValue ToBytesValue(this ReadOnlySpan<byte> In)
    {
        return new Google.Protobuf.WellKnownTypes.BytesValue() { Value = Google.Protobuf.ByteString.CopyFrom(In) };
    }
}
