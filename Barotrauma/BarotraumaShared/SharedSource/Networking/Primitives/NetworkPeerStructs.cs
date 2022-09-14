#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

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

        public readonly IReadMessage GetReadMessageUncompressed() => new ReadWriteMessage(Buffer, 0, Length, copyBuf: false);
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
    internal readonly struct PeerDisconnectPacket : INetSerializableStruct
    {
        public readonly DisconnectReason DisconnectReason;

        public readonly string AdditionalInformation;

        private PeerDisconnectPacket(
            DisconnectReason disconnectReason,
            string additionalInformation = "")
        {
            DisconnectReason = disconnectReason;
            AdditionalInformation = additionalInformation;
        }

        public LocalizedString ChatMessage(Client c)
            => DisconnectReason switch
            {
                DisconnectReason.Disconnected => TextManager.GetWithVariable("ServerMessage.ClientLeftServer",
                        "[client]", c.Name),
                _ => TextManager.GetWithVariables("ChatMsg.DisconnectedWithReason",
                        ("[client]", c.Name),
                        ("[reason]", TextManager.Get($"ChatMsg.DisconnectReason.{DisconnectReason}")))
            };

        private LocalizedString msgWithReason
            => TextManager.Get($"DisconnectReason.{DisconnectReason}")
               + "\n\n"
               + TextManager.Get("banreason") + " " + AdditionalInformation;

        private LocalizedString serverMessage
            => TextManager.Get($"ServerMessage.{DisconnectReason}");
        
        public LocalizedString PopupMessage
            => DisconnectReason switch
            {
                DisconnectReason.Banned => msgWithReason,
                DisconnectReason.Kicked => msgWithReason,
                DisconnectReason.InvalidVersion => TextManager.GetWithVariables("DisconnectMessage.InvalidVersion",
                    ("[version]", AdditionalInformation),
                    ("[clientversion]", GameMain.Version.ToString())),
                DisconnectReason.ExcessiveDesyncOldEvent => serverMessage,
                DisconnectReason.ExcessiveDesyncRemovedEvent => serverMessage,
                DisconnectReason.SyncTimeout => serverMessage,
                _ => TextManager.Get($"DisconnectReason.{DisconnectReason}").Fallback(TextManager.Get("ConnectionLost"))
            };

        public LocalizedString ReconnectMessage
            => PopupMessage + "\n\n" + TextManager.Get("ConnectionLostReconnecting");

        public PlayerConnectionChangeType ConnectionChangeType
            => DisconnectReason switch
            {
                DisconnectReason.Banned => PlayerConnectionChangeType.Banned,
                DisconnectReason.Kicked => PlayerConnectionChangeType.Kicked,
                _ => PlayerConnectionChangeType.Disconnected
            };

        public bool ShouldAttemptReconnect
            => DisconnectReason
                is DisconnectReason.ExcessiveDesyncOldEvent
                or DisconnectReason.ExcessiveDesyncRemovedEvent
                or DisconnectReason.Timeout
                or DisconnectReason.SyncTimeout
                or DisconnectReason.SteamP2PTimeOut;

        public bool IsEventSyncError
            => DisconnectReason
                is DisconnectReason.ExcessiveDesyncOldEvent
                or DisconnectReason.ExcessiveDesyncRemovedEvent
                or DisconnectReason.SyncTimeout;

        public bool ShouldCreateAnalyticsEvent
            => DisconnectReason is not (
                   DisconnectReason.Disconnected
                   or DisconnectReason.Banned
                   or DisconnectReason.Kicked
                   or DisconnectReason.TooManyFailedLogins
                   or DisconnectReason.InvalidVersion);

        public bool ShouldShowMessage
            => DisconnectReason is not DisconnectReason.Disconnected;

        private const string lidgrenSeparator = ":hankey:";

        /// <summary>
        /// This exists because Lidgren is a piece of shit and
        /// doesn't readily support sending anything other than
        /// a string through a disconnect packet, so this thing
        /// needs a sufficiently nasty string representation that
        /// can be decoded with some certainty that it won't get
        /// mangled by user input.
        /// </summary>
        public string ToLidgrenStringRepresentation()
        {
            static string strToBase64(string str)
                => Convert.ToBase64String(Encoding.UTF8.GetBytes(str));

            return DisconnectReason
                   + lidgrenSeparator
                   + strToBase64(AdditionalInformation);
        }

        public static Option<PeerDisconnectPacket> FromLidgrenStringRepresentation(string str)
        {
            // Lidgren has some hardcoded disconnect strings that it uses
            // when it detects that a connection has failed. We can handle
            // timeouts, so let's look for strings related to that and return
            // an appropriate PeerDisconnectPacket.
            switch (str)
            {
                case Lidgren.Network.NetConnection.NoResponseMessage:
                case "Connection timed out":
                case "Reconnecting":
                    return Option<PeerDisconnectPacket>.Some(WithReason(DisconnectReason.Timeout));
            }
            
            static string base64ToStr(string base64)
                => Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            string[] split = str.Split(lidgrenSeparator);
            if (split.Length != 2) { return Option<PeerDisconnectPacket>.None(); }
            if (!Enum.TryParse(split[0], out DisconnectReason disconnectReason)) { return Option<PeerDisconnectPacket>.None(); }
            return Option<PeerDisconnectPacket>.Some(new PeerDisconnectPacket(disconnectReason, base64ToStr(split[1])));
        }
        
        public static PeerDisconnectPacket Custom(string customMessage)
            => new PeerDisconnectPacket(
                DisconnectReason.Unknown,
                customMessage);

        public static PeerDisconnectPacket WithReason(DisconnectReason disconnectReason)
            => new PeerDisconnectPacket(disconnectReason);
        
        public static PeerDisconnectPacket Kicked(string? msg)
            => new PeerDisconnectPacket(DisconnectReason.Kicked, msg ?? "");

        public static PeerDisconnectPacket Banned(string? msg)
            => new PeerDisconnectPacket(DisconnectReason.Banned, msg ?? "");
        
        public static PeerDisconnectPacket InvalidVersion()
            => new PeerDisconnectPacket(
                DisconnectReason.InvalidVersion,
                GameMain.Version.ToString());

        public static PeerDisconnectPacket SteamP2PError(Steamworks.P2PSessionError error)
            => new PeerDisconnectPacket(
                DisconnectReason.SteamP2PError,
                error.ToString());
        
        public static PeerDisconnectPacket SteamAuthError(Steamworks.BeginAuthResult error)
            => new PeerDisconnectPacket(
                DisconnectReason.SteamAuthenticationFailed,
                $"{nameof(Steamworks.BeginAuthResult)}.{error}");
        
        public static PeerDisconnectPacket SteamAuthError(Steamworks.AuthResponse error)
            => new PeerDisconnectPacket(
                DisconnectReason.SteamAuthenticationFailed,
                $"{nameof(Steamworks.AuthResponse)}.{error}");
    }

    // ReSharper disable MemberCanBePrivate.Global, FieldCanBeMadeReadOnly.Global, UnassignedField.Global
    public sealed class ServerContentPackage : INetSerializableStruct
    {
        [NetworkSerialize]
        public string Name = "";

        [NetworkSerialize(ArrayMaxSize = ushort.MaxValue)]
        public byte[] HashBytes = Array.Empty<byte>();

        [NetworkSerialize]
        public string UgcId = "";

        [NetworkSerialize]
        public uint InstallTimeDiffInSeconds;

        [NetworkSerialize]
        public bool IsMandatory;

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
            UgcId = contentPackage.UgcId.TryUnwrap(out var ugcId)
                ? ugcId.StringRepresentation
                : "";
            IsMandatory = !contentPackage.Files.All(f => f is SubmarineFile);
            InstallTimeDiffInSeconds =
                contentPackage.InstallTime.TryUnwrap(out var installTime)
                    ? (uint)(installTime - referenceTime).TotalSeconds
                    : 0;
        }

        public string GetPackageStr() => $"\"{Name}\" (hash {Hash.ShortRepresentation})";
    }
}