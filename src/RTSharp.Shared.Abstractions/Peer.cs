using System.Net;

namespace RTSharp.Shared.Abstractions
{
	public class Peer
    {
		[Flags]
		public enum PEER_FLAGS : byte
		{
			I_INCOMING = 1 << 0,
			E_ENCRYPTED = 1 << 1,
			S_SNUBBED = 1 << 2,
			O_OBFUSCATED = 1 << 3,
			P_PREFERRED = 1 << 4,
			U_UNWANTED = 1 << 5,
			Max = 1 << 6
		}

		/// <summary>
		/// Data provider specific peer ID
		/// </summary>
		public object PeerId { get; set; }

		/// <summary>
		/// Peer IP address and port
		/// </summary>
		public IPEndPoint IPPort { get; set; }

		/// <summary>
		/// Peer client
		/// </summary>
		public string Client { get; set; }

		/// <summary>
		/// Peer flags
		/// </summary>
		public PEER_FLAGS Flags { get; set; }

		/// <summary>
		/// Done percentage
		/// </summary>
		public float Done { get; set; }

		/// <summary>
		/// Downloaded bytes from peer
		/// </summary>
		public ulong Downloaded { get; set; }

		/// <summary>
		/// Uploaded bytes to peer
		/// </summary>
		public ulong Uploaded { get; set; }

		/// <summary>
		/// Download speed from peer
		/// </summary>
		public ulong DLSpeed { get; set; }

		/// <summary>
		/// Upload speed to peer
		/// </summary>
		public ulong UPSpeed { get; set; }
	}
}
