#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Networking
{
    internal abstract class ServerPeer
    {
        public readonly record struct Callbacks(
            Callbacks.MessageCallback OnMessageReceived,
            Callbacks.DisconnectCallback OnDisconnect,
            Callbacks.InitializationCompleteCallback OnInitializationComplete,
            Callbacks.ShutdownCallback OnShutdown,
            Callbacks.OwnerDeterminedCallback OnOwnerDetermined)
        {
            public delegate void MessageCallback(NetworkConnection connection, IReadMessage message);
            public delegate void DisconnectCallback(NetworkConnection connection, PeerDisconnectPacket peerDisconnectPacket);
            public delegate void InitializationCompleteCallback(NetworkConnection connection, string? clientName);
            public delegate void ShutdownCallback();
            public delegate void OwnerDeterminedCallback(NetworkConnection connection);
        }

        protected readonly Callbacks callbacks;

        protected ServerPeer(Callbacks callbacks)
        {
            this.callbacks = callbacks;
        }
        
        public abstract void InitializeSteamServerCallbacks();

        public abstract void Start();
        public abstract void Close();
        public abstract void Update(float deltaTime);

        protected sealed class PendingClient
        {
            public string? Name;
            public Option<int> OwnerKey;
            public readonly NetworkConnection Connection;
            public ConnectionInitialization InitializationStep;
            public double UpdateTime;
            public double TimeOut;
            public int Retries;
            public Int32? PasswordSalt;
            public bool AuthSessionStarted;
            
            public AccountInfo AccountInfo => Connection.AccountInfo;

            public PendingClient(NetworkConnection conn)
            {
                OwnerKey = Option<int>.None();
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicketAndVersion;
                Retries = 0;
                PasswordSalt = null;
                UpdateTime = Timing.TotalTime + Timing.Step * 3.0;
                TimeOut = NetworkConnection.TimeoutThreshold;
                AuthSessionStarted = false;
            }

            public void Heartbeat()
            {
                TimeOut = NetworkConnection.TimeoutThreshold;
            }
        }

        protected List<NetworkConnection> connectedClients = null!;
        protected List<PendingClient> pendingClients = null!;
        protected ServerSettings serverSettings = null!;
        protected Option<int> ownerKey = null!;
        protected NetworkConnection? OwnerConnection;

        protected void ReadConnectionInitializationStep(PendingClient pendingClient, IReadMessage inc, ConnectionInitialization initializationStep)
        {
            pendingClient.TimeOut = NetworkConnection.TimeoutThreshold;

            if (pendingClient.InitializationStep != initializationStep) { return; }

            pendingClient.UpdateTime = Timing.TotalTime + Timing.Step;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    var authPacket = INetSerializableStruct.Read<ClientSteamTicketAndVersionPacket>(inc);

                    if (!Client.IsValidName(authPacket.Name, serverSettings))
                    {
                        RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.InvalidName));
                        return;
                    }

                    bool isCompatibleVersion =
                        Version.TryParse(authPacket.GameVersion, out var remoteVersion)
                        && NetworkMember.IsCompatible(remoteVersion, GameMain.Version);
                    if (!isCompatibleVersion)
                    {
                        RemovePendingClient(pendingClient, PeerDisconnectPacket.InvalidVersion());

                        GameServer.Log($"{authPacket.Name} ({authPacket.SteamId}) couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage($"{authPacket.Name} ({authPacket.SteamId}) couldn't join the server (incompatible game version)", Microsoft.Xna.Framework.Color.Red);
                        return;
                    }

                    pendingClient.Connection.Language = authPacket.Language.ToLanguageIdentifier();

                    Client nameTaken = GameMain.Server.ConnectedClients.Find(c => Homoglyphs.Compare(c.Name.ToLower(), authPacket.Name.ToLower()));
                    if (nameTaken != null)
                    {
                        RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.NameTaken));
                        GameServer.Log($"{authPacket.Name} ({authPacket.SteamId}) couldn't join the server (name too similar to the name of the client \"" + nameTaken.Name + "\").", ServerLog.MessageType.Error);
                        return;
                    }

                    if (!pendingClient.AuthSessionStarted)
                    {
                        ProcessAuthTicket(authPacket, pendingClient);
                    }

                    break;
                case ConnectionInitialization.Password:
                    var passwordPacket = INetSerializableStruct.Read<ClientPeerPasswordPacket>(inc);

                    if (pendingClient.PasswordSalt is null)
                    {
                        DebugConsole.ThrowError("Received password message from client without salt");
                        return;
                    }

                    if (serverSettings.IsPasswordCorrect(passwordPacket.Password, pendingClient.PasswordSalt.Value))
                    {
                        pendingClient.InitializationStep = ConnectionInitialization.ContentPackageOrder;
                    }
                    else
                    {
                        pendingClient.Retries++;
                        if (serverSettings.BanAfterWrongPassword && pendingClient.Retries > serverSettings.MaxPasswordRetriesBeforeBan)
                        {
                            const string banMsg = "Failed to enter correct password too many times";
                            BanPendingClient(pendingClient, banMsg, null);
                            RemovePendingClient(pendingClient, PeerDisconnectPacket.Banned(banMsg));
                            return;
                        }
                    }

                    pendingClient.UpdateTime = Timing.TotalTime;
                    break;
                case ConnectionInitialization.ContentPackageOrder:
                    pendingClient.InitializationStep = ConnectionInitialization.Success;
                    pendingClient.UpdateTime = Timing.TotalTime;
                    break;
            }
        }

        protected abstract void ProcessAuthTicket(ClientSteamTicketAndVersionPacket packet, PendingClient pendingClient);

        protected void BanPendingClient(PendingClient pendingClient, string banReason, TimeSpan? duration)
        {
            void banAccountId(AccountId accountId)
            {
                serverSettings.BanList.BanPlayer(pendingClient.Name ?? "Player", accountId, banReason, duration);
            }

            if (pendingClient.AccountInfo.AccountId.TryUnwrap(out var id)) { banAccountId(id); }

            pendingClient.AccountInfo.OtherMatchingIds.ForEach(banAccountId);
            if (pendingClient.AccountInfo.AccountId.TryUnwrap(out var accountId))
            {
                serverSettings.BanList.BanPlayer(pendingClient.Name ?? "Player", accountId, banReason, duration);
            }
            else
            {
                serverSettings.BanList.BanPlayer(pendingClient.Name ?? "Player", pendingClient.Connection.Endpoint, banReason, duration);
            }
        }

        protected bool IsPendingClientBanned(PendingClient pendingClient, out string? banReason)
        {
            bool isAccountIdBanned(AccountId accountId, out string? banReason)
            {
                return serverSettings.BanList.IsBanned(accountId, out banReason);
            }

            banReason = default;
            bool isBanned = pendingClient.AccountInfo.AccountId.TryUnwrap(out var id)
                            && isAccountIdBanned(id, out banReason);
            foreach (var otherId in pendingClient.AccountInfo.OtherMatchingIds)
            {
                if (isBanned) { break; }

                isBanned |= isAccountIdBanned(otherId, out banReason);
            }

            return isBanned;
        }

        protected abstract void SendMsgInternal(NetworkConnection conn, PeerPacketHeaders headers, INetSerializableStruct? body);

        protected void UpdatePendingClient(PendingClient pendingClient)
        {
            if (IsPendingClientBanned(pendingClient, out string? banReason))
            {
                RemovePendingClient(pendingClient, PeerDisconnectPacket.Banned(banReason));
                return;
            }

            if (connectedClients.Count >= serverSettings.MaxPlayers)
            {
                RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.ServerFull));
            }

            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                NetworkConnection newConnection = pendingClient.Connection;
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);

                callbacks.OnInitializationComplete.Invoke(newConnection, pendingClient.Name);

                CheckOwnership(pendingClient);
            }

            pendingClient.TimeOut -= Timing.Step;
            if (pendingClient.TimeOut < 0.0)
            {
                RemovePendingClient(pendingClient, PeerDisconnectPacket.WithReason(DisconnectReason.Timeout));
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }

            pendingClient.UpdateTime = Timing.TotalTime + 1.0;

            PeerPacketHeaders headers = new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsConnectionInitializationStep | PacketHeader.IsServerMessage,
                Initialization = pendingClient.InitializationStep
            };

            INetSerializableStruct? structToSend = null;

            switch (pendingClient.InitializationStep)
            {
                case ConnectionInitialization.ContentPackageOrder:

                    SerializableDateTime timeNow = SerializableDateTime.UtcNow;
                    structToSend = new ServerPeerContentPackageOrderPacket
                    {
                        ServerName = GameMain.Server.ServerName,
                        ContentPackages = ContentPackageManager.EnabledPackages.All
                            .Where(cp => cp.Files.Any())
                            .Where(cp => cp.HasMultiplayerSyncedContent || cp.Files.All(f => f is SubmarineFile))
                            .Select(contentPackage => new ServerContentPackage(contentPackage, timeNow))
                            .ToImmutableArray()
                    };

                    break;
                case ConnectionInitialization.Password:
                    structToSend = new ServerPeerPasswordPacket
                    {
                        Salt = GetSalt(pendingClient),
                        RetriesLeft = Option<int>.Some(pendingClient.Retries)
                    };

                    static Option<int> GetSalt(PendingClient client)
                    {
                        if (client.PasswordSalt is { } salt) { return Option<int>.Some(salt); }

                        salt = Lidgren.Network.CryptoRandom.Instance.Next();
                        client.PasswordSalt = salt;
                        return Option<int>.Some(salt);
                    }

                    break;
            }

            SendMsgInternal(pendingClient.Connection, headers, structToSend);
        }

        protected virtual void CheckOwnership(PendingClient pendingClient) { }

        protected void RemovePendingClient(PendingClient pendingClient, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (pendingClients.Contains(pendingClient))
            {
                Disconnect(pendingClient.Connection, peerDisconnectPacket);

                pendingClients.Remove(pendingClient);

                if (pendingClient.AuthSessionStarted && pendingClient.AccountInfo.AccountId is Some<AccountId> { Value: SteamId steamId })
                {
                    Steam.SteamManager.StopAuthSession(steamId);
                    pendingClient.Connection.SetAccountInfo(AccountInfo.None);
                    pendingClient.AuthSessionStarted = false;
                }
            }
        }

        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod, bool compressPastThreshold = true);
        public abstract void Disconnect(NetworkConnection conn, PeerDisconnectPacket peerDisconnectPacket);
    }
}