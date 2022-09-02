#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Networking
{
    [NetworkSerialize]
    internal struct PeerPacketHeaders : INetSerializableStruct
    {
        public DeliveryMethod DeliveryMethod;
        public PacketHeader PacketHeader;
        public ConnectionInitialization? Initialization;

        public readonly void Deconstruct(
            out DeliveryMethod deliveryMethod,
            out PacketHeader packetHeader,
            out ConnectionInitialization? initialization)
        {
            deliveryMethod = DeliveryMethod;
            packetHeader = PacketHeader;
            initialization = Initialization;
        }
    }

    [NetworkSerialize(ArrayMaxSize = ushort.MaxValue)]
    internal struct ClientSteamTicketAndVersionPacket : INetSerializableStruct
    {
        public string Name;
        public Option<int> OwnerKey;
        
        #warning TODO: do something about the type of this
        // It probably should be Option<SteamId> but we shouldn't build support for
        // writing SteamIDs to INetSerializableStruct; we should consider adding
        // attributes to give custom behaviors to specific members of a struct
        public Option<AccountId> SteamId;
        
        public Option<byte[]> SteamAuthTicket;
        public string GameVersion;
        public Identifier Language;
    }

    [NetworkSerialize]
    internal struct SteamP2PInitializationRelayPacket : INetSerializableStruct
    {
        public ulong LobbyID;
        public PeerPacketMessage Message;
    }

    [NetworkSerialize]
    internal struct SteamP2PInitializationOwnerPacket : INetSerializableStruct
    {
        public string OwnerName;
    }


    [NetworkSerialize(ArrayMaxSize = ushort.MaxValue)]
    internal struct ServerPeerContentPackageOrderPacket : INetSerializableStruct
    {
        public string ServerName;
        public ImmutableArray<ServerContentPackage> ContentPackages;
    }

    [NetworkSerialize(ArrayMaxSize = ushort.MaxValue)]
    internal struct PeerPacketMessage : INetSerializableStruct
    {
        public byte[] Buffer;
        public readonly int Length => Buffer.Length;

        public readonly IReadMessage GetReadMessage() => new ReadWriteMessage(Buffer, 0, Length, copyBuf: false);
        public readonly IReadMessage GetReadMessage(bool isCompressed, NetworkConnection conn) => new ReadOnlyMessage(Buffer, isCompressed, 0, Length, conn);
    }

    [NetworkSerialize(ArrayMaxSize = byte.MaxValue)]
    internal struct ClientPeerPasswordPacket : INetSerializableStruct
    {
        public byte[] Password;
    }

    [NetworkSerialize]
    internal struct ServerPeerPasswordPacket : INetSerializableStruct
    {
        public Option<int> Salt;
        public Option<int> RetriesLeft;
    }

    [NetworkSerialize]
    internal struct PeerDisconnectPacket : INetSerializableStruct
    {
        public string Message;
    }

    // ReSharper disable MemberCanBePrivate.Global, FieldCanBeMadeReadOnly.Global, UnassignedField.Global
    public sealed class ServerContentPackage : INetSerializableStruct
    {
        [NetworkSerialize]
        public string Name = "";

        [NetworkSerialize(ArrayMaxSize = ushort.MaxValue)]
        public byte[] HashBytes = Array.Empty<byte>();

        [NetworkSerialize]
        public ulong WorkshopId;

        [NetworkSerialize]
        public uint InstallTimeDiffInSeconds;

        private Md5Hash? cachedHash;
        private DateTime? cachedDateTime;

        public Md5Hash Hash
        {
            get => cachedHash ??= Md5Hash.BytesAsHash(HashBytes);
            set
            {
                cachedHash = value;
                HashBytes = value.ByteRepresentation;
            }
        }

        public DateTime InstallTime => cachedDateTime ??= DateTime.UtcNow + TimeSpan.FromSeconds(InstallTimeDiffInSeconds);
        public RegularPackage? RegularPackage => ContentPackageManager.RegularPackages.FirstOrDefault(p => p.Hash.Equals(Hash));
        public CorePackage? CorePackage => ContentPackageManager.CorePackages.FirstOrDefault(p => p.Hash.Equals(Hash));
        public ContentPackage? ContentPackage => (ContentPackage?)RegularPackage ?? CorePackage;

        public ServerContentPackage() { }
        
        public ServerContentPackage(ContentPackage contentPackage, DateTime referenceTime)
        {
            Name = contentPackage.Name;
            Hash = contentPackage.Hash;
            WorkshopId = contentPackage.SteamWorkshopId;
            InstallTimeDiffInSeconds =
                contentPackage.InstallTime is { } installTime
                    ? (uint)(installTime - referenceTime).TotalSeconds
                    : 0;
        }

        public string GetPackageStr() => $"\"{Name}\" (hash {Hash.ShortRepresentation})";
    }
}