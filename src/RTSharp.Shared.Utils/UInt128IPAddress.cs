using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace RTSharp.Shared.Utils
{
	public static class UInt128IPAddress
	{
		public static UInt128 IPAddressToUInt128(IPAddress In)
		{
			if (In.AddressFamily == AddressFamily.InterNetwork && !In.IsIPv4MappedToIPv6)
				In = In.MapToIPv6();

			Span<byte> bytes = stackalloc byte[16];
			In.TryWriteBytes(bytes, out _);

			ulong high = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
			ulong low = BinaryPrimitives.ReadUInt64BigEndian(bytes[..8]);
			return new UInt128(low, high);
		}

		public static IPAddress UInt128ToIPAddress(UInt128 In)
		{
			var (high, low) = In.ToHighLow();
			Span<byte> bytes = stackalloc byte[16];
			BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], low);
			BinaryPrimitives.WriteUInt64BigEndian(bytes[..8], high);

			var ip = new IPAddress(bytes);
			if (ip.IsIPv4MappedToIPv6)
				return ip.MapToIPv4();
			return ip;
		}
	}
}
