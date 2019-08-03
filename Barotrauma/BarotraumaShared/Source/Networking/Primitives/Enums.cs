using System;

namespace Barotrauma.Networking
{
    public enum DeliveryMethod : byte
    {
        Unreliable = 0x0,
        Reliable = 0x1,
        ReliableOrdered = 0x2
    }

    public enum ConnectionInitialization : byte
    {
        //used by all peer implementations
        SteamTicketAndVersion = 0x1,
        Password = 0x2,
        Success = 0x0,

        //used only by SteamP2P implementations
        ConnectionStarted = 0x3
    }

    [Flags]
    public enum PacketHeader : byte
    {
        //used by all peer implementations
        None = 0x0,
        IsCompressed = 0x1,
        IsConnectionInitializationStep = 0x2,

        //used only by SteamP2P implementations
        IsDisconnectMessage = 0x4,
        IsServerMessage = 0x8,
        IsHeartbeatMessage = 0x10,
        IsPing = 0x20
    }
}

