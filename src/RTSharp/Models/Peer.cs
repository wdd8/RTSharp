using System;
using System.Net;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Shared.Utils;
using static RTSharp.Shared.Abstractions.Peer;

namespace RTSharp.Models
{
    public partial class Peer : ObservableObject
    {
        /// <summary>
        /// Icon representing <see cref="Origin"/>
        /// </summary>
        [ObservableProperty]
        public IImage? icon;

        [ObservableProperty]
        public string? origin;

        /// <summary>
        /// Peer IP:Port
        /// </summary>
        public IPEndPoint IPPort { get; init; }

        /// <summary>
        /// Peer client
        /// </summary>
        public string Client { get; init; }

        /// <summary>
        /// Peer flags
        /// </summary>
        [ObservableProperty]
        public string flags;

        private PEER_FLAGS FlagsInternal { get; set; }

        /// <summary>
        /// Done percentage
        /// </summary>
        [ObservableProperty]
        public float done;

        /// <summary>
        /// Downloaded bytes from peer
        /// </summary>
        [ObservableProperty]
        public ulong downloaded;

        /// <summary>
        /// Uploaded bytes to peer
        /// </summary>
        [ObservableProperty]
        public ulong uploaded;

        /// <summary>
        /// Download speed from peer
        /// </summary>
        [ObservableProperty]
        public ulong dLSpeed;

        /// <summary>
        /// Upload speed to peer
        /// </summary>
        [ObservableProperty]
        public ulong uPSpeed;

        public void UpdateFromPluginModel(Shared.Abstractions.Peer In)
        {
            this.Flags = FlagsMapper.MapConcat(In.Flags, x => x switch {
                PEER_FLAGS.I_INCOMING => "I",
                PEER_FLAGS.E_ENCRYPTED => "E",
                PEER_FLAGS.S_SNUBBED => "S",
                PEER_FLAGS.O_OBFUSCATED => "O",
                PEER_FLAGS.P_PREFERRED => "P",
                PEER_FLAGS.U_UNWANTED => "U",
                _ => throw new ArgumentOutOfRangeException()
            });
            this.FlagsInternal = In.Flags;

            this.Done = In.Done;
            this.Downloaded = In.Downloaded;
            this.Uploaded = In.Uploaded;
            this.DLSpeed = In.DLSpeed;
            this.UPSpeed = In.UPSpeed;
        }

        public static Peer FromPluginModel(Shared.Abstractions.Peer In)
        {
            var ret = new Peer {
                IPPort = In.IPPort,
                Client = In.Client
            };

            ret.UpdateFromPluginModel(In);

            return ret;
        }
    }
}
