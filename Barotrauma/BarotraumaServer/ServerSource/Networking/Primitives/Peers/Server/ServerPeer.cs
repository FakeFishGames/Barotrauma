using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ServerPeer
    {
        public delegate void MessageCallback(NetworkConnection connection, IReadMessage message);
        public delegate void DisconnectCallback(NetworkConnection connection, string reason);
        public delegate void InitializationCompleteCallback(NetworkConnection connection);
        public delegate void ShutdownCallback();
        public delegate void OwnerDeterminedCallback(NetworkConnection connection);

        public MessageCallback OnMessageReceived;
        public DisconnectCallback OnDisconnect;
        public InitializationCompleteCallback OnInitializationComplete;
        public ShutdownCallback OnShutdown;
        public OwnerDeterminedCallback OnOwnerDetermined;

        protected int? ownerKey;

        public NetworkConnection OwnerConnection { get; protected set; }

        public abstract void InitializeSteamServerCallbacks();

        public abstract void Start();
        public abstract void Close(string msg = null);
        public abstract void Update(float deltaTime);

        protected class PendingClient
        {
            public string Name;
            public int OwnerKey;
            public NetworkConnection Connection;
            public ConnectionInitialization InitializationStep;
            public double UpdateTime;
            public double TimeOut;
            public int Retries;
            private UInt64? steamId;
            public UInt64? SteamID
            {
                get { return steamId; }
                set
                {
                    steamId = value;
                    Connection.SetSteamIDIfUnknown(value ?? 0);
                }
            }
            private UInt64? ownerSteamId;
            public UInt64? OwnerSteamID
            {
                get { return ownerSteamId; }
                set
                {
                    ownerSteamId = value;
                    Connection.SetOwnerSteamIDIfUnknown(value ?? 0);
                }
            }
            public Int32? PasswordSalt;
            public bool AuthSessionStarted;

            public PendingClient(NetworkConnection conn)
            {
                OwnerKey = 0;
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicketAndVersion;
                Retries = 0;
                SteamID = null;
                OwnerSteamID = null;
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
                    UInt64 steamId = inc.ReadUInt64();
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
                        ProcessAuthTicket(name, ownerKey, steamId, pendingClient, ticketBytes);
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

        protected abstract void ProcessAuthTicket(string name, int ownKey, ulong steamId, PendingClient pendingClient, byte[] ticket);

        protected void BanPendingClient(PendingClient pendingClient, string banReason, TimeSpan? duration)
        {
            if (pendingClient.Connection is LidgrenConnection l)
            {
                serverSettings.BanList.BanPlayer(pendingClient.Name, l.NetConnection.RemoteEndPoint.Address, banReason, duration);
            }
            else if (pendingClient.Connection is SteamP2PConnection s)
            {
                serverSettings.BanList.BanPlayer(pendingClient.Name, s.SteamID, banReason, duration);
                serverSettings.BanList.BanPlayer(pendingClient.Name, s.OwnerSteamID, banReason, duration);
            }
        }

        protected bool IsPendingClientBanned(PendingClient pendingClient, out string banReason)
        {
            if (pendingClient.Connection is LidgrenConnection l)
            {
                return serverSettings.BanList.IsBanned(l.NetConnection.RemoteEndPoint.Address, out banReason);
            }
            else if (pendingClient.Connection is SteamP2PConnection s)
            {
                return serverSettings.BanList.IsBanned(s.SteamID, out banReason) ||
                       serverSettings.BanList.IsBanned(s.OwnerSteamID, out banReason);
            }
            banReason = null;
            return false;
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

                OnInitializationComplete?.Invoke(newConnection);
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
                    outMsg.Write(GameMain.Server.ServerName);

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

                if (pendingClient.AuthSessionStarted)
                {
                    Steam.SteamManager.StopAuthSession(pendingClient.SteamID.Value);
                    pendingClient.SteamID = null;
                    pendingClient.OwnerSteamID = null;
                    pendingClient.AuthSessionStarted = false;
                }
            }
        }

        public abstract void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod, bool compressPastThreshold = true);
        public abstract void Disconnect(NetworkConnection conn, string msg = null);
    }
}
