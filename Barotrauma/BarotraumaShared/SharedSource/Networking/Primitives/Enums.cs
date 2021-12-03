using System;

namespace Barotrauma.Networking
{
    public enum DeliveryMethod : int
    {
        Unreliable = 0x0,
        Reliable = 0x1,
        ReliableOrdered = 0x2
    }

    public enum ConnectionInitialization : int
    {
        //used by all peer implementations
        SteamTicketAndVersion = 0x1,
        ContentPackageOrder = 0x2,
        Password = 0x3,
        Success = 0x0,

        //used only by SteamP2P implementations
        ConnectionStarted = 0x4
    }

    [Flags]
    public enum PacketHeader : int
    {
        //used by all peer implementations
        None = 0x0,
        IsCompressed = 0x1,
        IsConnectionInitializationStep = 0x2,

        //used only by SteamP2P implementations
        IsDisconnectMessage = 0x4,
        IsServerMessage = 0x8,
        IsHeartbeatMessage = 0x10
    }

    public static class NetworkEnumExtensions
    {
        public static bool IsCompressed(this PacketHeader h)
            => h.IsBitSet(PacketHeader.IsCompressed);

        public static bool IsConnectionInitializationStep(this PacketHeader h)
            => h.IsBitSet(PacketHeader.IsConnectionInitializationStep);

        public static bool IsDisconnectMessage(this PacketHeader h)
            => h.IsBitSet(PacketHeader.IsDisconnectMessage);

        public static bool IsServerMessage(this PacketHeader h)
            => h.IsBitSet(PacketHeader.IsServerMessage);

        public static bool IsHeartbeatMessage(this PacketHeader h)
            => h.IsBitSet(PacketHeader.IsHeartbeatMessage);
    }
}

