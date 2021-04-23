using Barotrauma.Extensions;
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Barotrauma.Networking
{
    abstract class ClientPeer
    {
        protected class ServerContentPackage
        {
            public string Name;
            public string Hash;
            public UInt64 WorkshopId;

            public ContentPackage RegularPackage
            {
                get
                {
                    return ContentPackage.RegularPackages.Find(p => p.MD5hash.Hash.Equals(Hash));
                }
            }

            public ContentPackage CorePackage
            {
                get
                {
                    return ContentPackage.CorePackages.Find(p => p.MD5hash.Hash.Equals(Hash));
                }
            }

            public ServerContentPackage(string name, string hash, UInt64 workshopId)
            {
                Name = name;
                Hash = hash;
                WorkshopId = workshopId;
            }
        }

        protected string GetPackageStr(ContentPackage contentPackage)
        {
            return "\"" + contentPackage.Name + "\" (hash " + contentPackage.MD5hash.ShortHash + ")";
        }
        protected string GetPackageStr(ServerContentPackage contentPackage)
        {
            return "\"" + contentPackage.Name + "\" (hash " + Md5Hash.GetShortHash(contentPackage.Hash) + ")";
        }

        public delegate void MessageCallback(IReadMessage message);
        public delegate void DisconnectCallback(bool disableReconnect);
        public delegate void DisconnectMessageCallback(string message);
        public delegate void PasswordCallback(int salt, int retries);
        public delegate void InitializationCompleteCallback();
        
        public MessageCallback OnMessageReceived;
        public DisconnectCallback OnDisconnect;
        public DisconnectMessageCallback OnDisconnectMessageReceived;
        public PasswordCallback OnRequestPassword;
        public InitializationCompleteCallback OnInitializationComplete;

        public string Name;

        public string Version { get; protected set; }

        public NetworkConnection ServerConnection { get; protected set; }

        public abstract void Start(object endPoint, int ownerKey);
        public abstract void Close(string msg = null, bool disableReconnect = false);
        public abstract void Update(float deltaTime);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod);
        public abstract void SendPassword(string password);

        protected abstract void SendMsgInternal(DeliveryMethod deliveryMethod, IWriteMessage msg);

        protected ConnectionInitialization initializationStep;
        protected bool contentPackageOrderReceived;
        protected int ownerKey = 0;
        protected int passwordSalt;
        protected Steamworks.AuthTicket steamAuthTicket;
        protected void ReadConnectionInitializationStep(IReadMessage inc)
        {
            ConnectionInitialization step = (ConnectionInitialization)inc.ReadByte();

            IWriteMessage outMsg;

            switch (step)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }
                    outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicketAndVersion);
                    outMsg.Write(Name);
                    outMsg.Write(ownerKey);
                    outMsg.Write(SteamManager.GetSteamID());
                    if (steamAuthTicket == null)
                    {
                        outMsg.Write((UInt16)0);
                    }
                    else
                    {
                        outMsg.Write((UInt16)steamAuthTicket.Data.Length);
                        outMsg.Write(steamAuthTicket.Data, 0, steamAuthTicket.Data.Length);
                    }
                    outMsg.Write(GameMain.Version.ToString());
                    outMsg.Write(GameMain.Config.Language);

                    SendMsgInternal(DeliveryMethod.Reliable, outMsg);
                    break;
                case ConnectionInitialization.ContentPackageOrder:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion ||
                        initializationStep == ConnectionInitialization.Password) { initializationStep = ConnectionInitialization.ContentPackageOrder; }
                    if (initializationStep != ConnectionInitialization.ContentPackageOrder) { return; }
                    outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.ContentPackageOrder);

                    string serverName = inc.ReadString();

                    UInt32 cpCount = inc.ReadVariableUInt32();
                    ServerContentPackage corePackage = null;
                    List<ServerContentPackage> regularPackages = new List<ServerContentPackage>();
                    List<ServerContentPackage> missingPackages = new List<ServerContentPackage>();
                    for (int i = 0; i < cpCount; i++)
                    {
                        string name = inc.ReadString();
                        string hash = inc.ReadString();
                        UInt64 workshopId = inc.ReadUInt64();
                        var pkg = new ServerContentPackage(name, hash, workshopId);
                        if (pkg.CorePackage != null)
                        {
                            corePackage = pkg;
                        }
                        else if (pkg.RegularPackage != null)
                        {
                            regularPackages.Add(pkg);
                        }
                        else
                        {
                            missingPackages.Add(pkg);
                        }
                    }

                    if (missingPackages.Count > 0)
                    {
                        var nonDownloadable = missingPackages.Where(p => p.WorkshopId == 0);
                        var mismatchedButDownloaded = missingPackages.Where(p =>
                        {
                            var localMatching = ContentPackage.RegularPackages.Find(l => l.SteamWorkshopId != 0 && p.WorkshopId == l.SteamWorkshopId);
                            localMatching ??= ContentPackage.CorePackages.Find(l => l.SteamWorkshopId != 0 && p.WorkshopId == l.SteamWorkshopId);

                            return localMatching != null;
                        });

                        if (mismatchedButDownloaded.Any())
                        {
                            string disconnectMsg;
                            if (mismatchedButDownloaded.Count() == 1)
                            {
                                disconnectMsg = $"DisconnectMessage.MismatchedWorkshopMod~[incompatiblecontentpackage]={GetPackageStr(mismatchedButDownloaded.First())}";
                            }
                            else
                            {
                                List<string> packageStrs = new List<string>();
                                mismatchedButDownloaded.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                                disconnectMsg = $"DisconnectMessage.MismatchedWorkshopMods~[incompatiblecontentpackages]={string.Join(", ", packageStrs)}";
                            }
                            Close(disconnectMsg, disableReconnect: true);
                            OnDisconnectMessageReceived?.Invoke(DisconnectReason.MissingContentPackage + "/" + disconnectMsg);
                        }
                        else if (nonDownloadable.Any())
                        {
                            string disconnectMsg;
                            if (nonDownloadable.Count() == 1)
                            {
                                disconnectMsg = $"DisconnectMessage.MissingContentPackage~[missingcontentpackage]={GetPackageStr(nonDownloadable.First())}";
                            }
                            else
                            {
                                List<string> packageStrs = new List<string>();
                                nonDownloadable.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                                disconnectMsg = $"DisconnectMessage.MissingContentPackages~[missingcontentpackages]={string.Join(", ", packageStrs)}";
                            }
                            Close(disconnectMsg, disableReconnect: true);
                            OnDisconnectMessageReceived?.Invoke(DisconnectReason.MissingContentPackage + "/" + disconnectMsg);
                        }
                        else
                        {
                            Close(disableReconnect: true);

                            string missingModNames = "\n";
                            int displayedModCount = 0;
                            foreach (ServerContentPackage missingPackage in missingPackages)
                            {
                                missingModNames += "\n- " + GetPackageStr(missingPackage);
                                displayedModCount++;
                                if (GUI.Font.MeasureString(missingModNames).Y > GameMain.GraphicsHeight * 0.5f)
                                {
                                    missingModNames += "\n\n" + TextManager.GetWithVariable("workshopitemdownloadprompttruncated", "[number]", (missingPackages.Count - displayedModCount).ToString());
                                    break;
                                }
                            }
                            missingModNames += "\n\n";

                            var msgBox = new GUIMessageBox(
                                TextManager.Get("WorkshopItemDownloadTitle"),
                                TextManager.GetWithVariable("WorkshopItemDownloadPrompt", "[items]", missingModNames),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                            msgBox.Buttons[0].OnClicked = (yesBtn, userdata) =>
                            {
                                GameMain.ServerListScreen.Select();
                                GameMain.ServerListScreen.DownloadWorkshopItems(missingPackages.Select(p => p.WorkshopId), serverName, ServerConnection.EndPointString);
                                return true;
                            };
                            msgBox.Buttons[0].OnClicked += msgBox.Close;
                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                        }

                        return;
                    }

                    if (!contentPackageOrderReceived)
                    {
                        GameMain.Config.BackUpModOrder();
                        GameMain.Config.SwapPackages(corePackage.CorePackage, regularPackages.Select(p => p.RegularPackage).ToList());
                        contentPackageOrderReceived = true;
                    }

                    SendMsgInternal(DeliveryMethod.Reliable, outMsg);
                    break;
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion) { initializationStep = ConnectionInitialization.Password; }
                    if (initializationStep != ConnectionInitialization.Password) { return; }
                    bool incomingSalt = inc.ReadBoolean(); inc.ReadPadBits();
                    int retries = 0;
                    if (incomingSalt)
                    {
                        passwordSalt = inc.ReadInt32();
                    }
                    else
                    {
                        retries = inc.ReadInt32();
                    }
                    OnRequestPassword?.Invoke(passwordSalt, retries);
                    break;
            }
        }

#if DEBUG
        public abstract void ForceTimeOut();
#endif
    }
}
