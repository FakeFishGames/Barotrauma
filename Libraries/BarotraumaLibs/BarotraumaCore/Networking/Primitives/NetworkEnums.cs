﻿using System;

namespace Barotrauma.Networking
{
    public enum DeliveryMethod : int
    {
        Unreliable = 0x0,
        Reliable = 0x1
    }

    public enum ConnectionInitialization : int
    {
        //used by all peer implementations
        AuthInfoAndVersion = 0x1,
        ContentPackageOrder = 0x2,
        Password = 0x3,
        Success = 0x0,

        //used only by P2P implementations
        ConnectionStarted = 0x4
    }

    [Flags]
    public enum PacketHeader : int
    {
        //used by all peer implementations
        None = 0x0,
        IsCompressed = 0x1,
        IsConnectionInitializationStep = 0x2,

        //used only by P2P implementations
        IsDisconnectMessage = 0x4,
        IsServerMessage = 0x8,
        IsHeartbeatMessage = 0x10,
        IsDataFragment = 0x20
    }

    public static class NetworkEnumExtensions
    {
        public static bool IsCompressed(this PacketHeader h)
            => h.HasFlag(PacketHeader.IsCompressed);

        public static bool IsConnectionInitializationStep(this PacketHeader h)
            => h.HasFlag(PacketHeader.IsConnectionInitializationStep);

        public static bool IsDisconnectMessage(this PacketHeader h)
            => h.HasFlag(PacketHeader.IsDisconnectMessage);

        public static bool IsServerMessage(this PacketHeader h)
            => h.HasFlag(PacketHeader.IsServerMessage);

        public static bool IsHeartbeatMessage(this PacketHeader h)
            => h.HasFlag(PacketHeader.IsHeartbeatMessage);
        
        public static bool IsDataFragment(this PacketHeader h)
            => h.HasFlag(PacketHeader.IsDataFragment);
    }
    
    public static class NetworkMagicStrings
    {
        // This separator exists because Lidgren's disconnect messages
        // can only readily support strings. We want to send something that
        // isn't exactly a string, so we use this as part of its encoding.
        public const string LidgrenDisconnectSeparator = "}Separator[";
    }
}

