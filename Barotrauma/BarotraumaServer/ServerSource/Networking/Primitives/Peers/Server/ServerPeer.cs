using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void MessageCallback(NetworkConnection connection, IReadMessage message);
        public delegate void DisconnectCallback(NetworkConnection connection, string reason);
        public delegate void InitializationCompleteCallback(NetworkConnection connection, string clientName);
        public delegate void ShutdownCallback();
        public delegate void OwnerDeterminedCallback(NetworkConnection connection);

        public MessageCallback OnMessageReceived;
        public DisconnectCallback OnDisconnect;
        public InitializationCompleteCallback OnInitializationComplete;
        public ShutdownCallback OnShutdown;
        public OwnerDeterminedCallback OnOwnerDetermined;

        protected Option<int> ownerKey;

        public NetworkConnection OwnerConnection { get; protected set; }

        public abstract void InitializeSteamServerCallbacks();

        public abstract void Start();
        public abstract void Close(string msg = null);
        public abstract void Update(float deltaTime);

        protected class PendingClient
        {
            public string Name;
            public Option<int> OwnerKey;
            public NetworkConnection Connection;
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
        protected List<NetworkConnection> connectedClients;
        protected List<PendingClient> pendingClients;

        protected ServerSettings serverSettings;

        protected void ReadConnectionInitializationStep(PendingClient pendingClient, IReadMessage inc)
        {
            pendingClient.TimeOut = NetworkConnection.TimeoutThreshold;

            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();

            if (pendingClient.InitializationStep != initializationStep) return;

            pendingClient.UpdateTime = Timing.TotalTime + Timing.Step;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    string name = Client.SanitizeName(inc.ReadString());
                    int ownerKey = inc.ReadInt32();
                    UInt64 steamIdVal = inc.ReadUInt64();
                    Option<SteamId> steamId = steamIdVal != 0
                        ? Option<SteamId>.Some(new SteamId(steamIdVal))
                        : Option<SteamId>.None();
                    UInt16 ticketLength = inc.ReadUInt16();
                    byte[] ticketBytes = inc.ReadBytes(ticketLength);

                    if (!Client.IsValidName(name, serverSettings))
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.InvalidName, "");
                        return;
                    }

                    string version = inc.ReadString();
                    bool isCompatibleVersion = NetworkMember.IsCompatible(version, GameMain.Version.ToString()) ?? false;
                    if (!isCompatibleVersion)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.InvalidVersion,
                                    $"DisconnectMessage.InvalidVersion~[version]={GameMain.Version}~[clientversion]={version}");

                        GameServer.Log($"{name} ({steamId}) couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage($"{name} ({steamId}) couldn't join the server (incompatible game version)", Microsoft.Xna.Framework.Color.Red);
                        return;
                    }

                    LanguageIdentifier language = inc.ReadIdentifier().ToLanguageIdentifier();
                    pendingClient.Connection.Language = language;

                    Client nameTaken = GameMain.Server.ConnectedClients.Find(c => Homoglyphs.Compare(c.Name.ToLower(), name.ToLower()));
                    if (nameTaken != null)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.NameTaken, "");
                        GameServer.Log($"{name} ({steamId}) couldn't join the server (name too similar to the name of the client \"" + nameTaken.Name + "\").", ServerLog.MessageType.Error);
                        return;
                    }

                    if (!pendingClient.AuthSessionStarted)
                    {
                        ProcessAuthTicket(name, ownerKey != 0 ? Option<int>.Some(ownerKey) : Option<int>.None(), steamId, pendingClient, ticketBytes);
                    }
                    break;
                case ConnectionInitialization.Password:
                    int pwLength = inc.ReadByte();
                    byte[] incPassword = inc.ReadBytes(pwLength);
                    if (pendingClient.PasswordSalt == null)
                    {
                        DebugConsole.ThrowError("Received password message from client without salt");
                        return;
                    }
                    if (serverSettings.IsPasswordCorrect(incPassword, pendingClient.PasswordSalt.Value))
                    {
                        pendingClient.InitializationStep = ConnectionInitialization.ContentPackageOrder;
                    }
                    else
                    {
                        pendingClient.Retries++;
                        if (serverSettings.BanAfterWrongPassword && pendingClient.Retries > serverSettings.MaxPasswordRetriesBeforeBan)
                        {
                            string banMsg = "Failed to enter correct password too many times";
                            BanPendingClient(pendingClient, banMsg, null);

                            RemovePendingClient(pendingClient, DisconnectReason.Banned, banMsg);
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

        protected abstract void ProcessAuthTicket(string name, Option<int> ownKey, Option<SteamId> steamId, PendingClient pendingClient, byte[] ticket);

        protected void BanPendingClient(PendingClient pendingClient, string banReason, TimeSpan? duration)
        {
            void banAccountId(AccountId accountId)
            {
                serverSettings.BanList.BanPlayer(pendingClient.Name, accountId, banReason, duration);
            }
            
            if (pendingClient.AccountInfo.AccountId.TryUnwrap(out var id)) { banAccountId(id); }
            pendingClient.AccountInfo.OtherMatchingIds.ForEach(banAccountId);
            serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.Connection.Endpoint, banReason, duration);
        }
        
        protected bool IsPendingClientBanned(PendingClient pendingClient, out string banReason)
        {
            bool isAccountIdBanned(AccountId accountId, out string banReason)
            {
                banReason = default;
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

        protected abstract void SendMsgInternal(NetworkConnection conn, DeliveryMethod deliveryMethod, IWriteMessage msg);

        protected void UpdatePendingClient(PendingClient pendingClient)
        {
            if (IsPendingClientBanned(pendingClient, out string banReason))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                return;
            }

            if (connectedClients.Count >= serverSettings.MaxPlayers)
            {
                RemovePendingClient(pendingClient, DisconnectReason.ServerFull, "");
            }

            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                NetworkConnection newConnection = pendingClient.Connection;
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);

                CheckOwnership(pendingClient);

                OnInitializationComplete?.Invoke(newConnection, pendingClient.Name);
            }

            pendingClient.TimeOut -= Timing.Step;
            if (pendingClient.TimeOut < 0.0)
            {
                RemovePendingClient(pendingClient, DisconnectReason.Unknown, Lidgren.Network.NetConnection.NoResponseMessage);
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }
            pendingClient.UpdateTime = Timing.TotalTime + 1.0;

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep |
                                PacketHeader.IsServerMessage));
            outMsg.Write((byte)pendingClient.InitializationStep);
            switch (pendingClient.InitializationStep)
            {
                case ConnectionInitialization.ContentPackageOrder:
                    var mpContentPackages = ContentPackageManager.EnabledPackages.All.Where(cp => cp.HasMultiplayerSyncedContent).ToList();
                    outMsg.WriteVariableUInt32((UInt32)mpContentPackages.Count);
                    for (int i = 0; i < mpContentPackages.Count; i++)
                    {
                        outMsg.Write(mpContentPackages[i].Name);
                        byte[] hashBytes = mpContentPackages[i].Hash.ByteRepresentation;
                        outMsg.WriteVariableUInt32((UInt32)hashBytes.Length);
                        outMsg.Write(hashBytes, 0, hashBytes.Length);
                        outMsg.Write(mpContentPackages[i].SteamWorkshopId);
                        UInt32 installTimeDiffSeconds = (UInt32)((mpContentPackages[i].InstallTime ?? DateTime.UtcNow) - DateTime.UtcNow).TotalSeconds;
                        outMsg.Write(installTimeDiffSeconds);
                    }
                    break;
                case ConnectionInitialization.Password:
                    outMsg.Write(pendingClient.PasswordSalt == null); outMsg.WritePadBits();
                    if (pendingClient.PasswordSalt == null)
                    {
                        pendingClient.PasswordSalt = Lidgren.Network.CryptoRandom.Instance.Next();
                        outMsg.Write(pendingClient.PasswordSalt.Value);
                    }
                    else
                    {
                        outMsg.Write(pendingClient.Retries);
                    }
                    break;
            }

            SendMsgInternal(pendingClient.Connection, DeliveryMethod.Reliable, outMsg);
        }

        protected virtual void CheckOwnership(PendingClient pendingClient) { }

        protected void RemovePendingClient(PendingClient pendingClient, DisconnectReason reason, string msg)
        {
            if (pendingClients.Contains(pendingClient))
            {
                Disconnect(pendingClient.Connection, reason + "/" + msg);

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
        public abstract void Disconnect(NetworkConnection conn, string msg = null);
    }
}
