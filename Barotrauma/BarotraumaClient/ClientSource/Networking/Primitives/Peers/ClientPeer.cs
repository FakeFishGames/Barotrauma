#nullable enable
using Barotrauma.Steam;
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    internal abstract class ClientPeer
    {
        public ImmutableArray<ServerContentPackage> ServerContentPackages { get; set; } =
            ImmutableArray<ServerContentPackage>.Empty;

        public readonly record struct Callbacks(
            Callbacks.MessageCallback OnMessageReceived,
            Callbacks.DisconnectCallback OnDisconnect,
            Callbacks.InitializationCompleteCallback OnInitializationComplete)
        {
            public delegate void MessageCallback(IReadMessage message);
            public delegate void DisconnectCallback(PeerDisconnectPacket disconnectPacket);
            public delegate void InitializationCompleteCallback();
        }

        protected readonly Callbacks callbacks;

        public readonly Endpoint ServerEndpoint;
        public NetworkConnection? ServerConnection { get; protected set; }

        protected readonly bool isOwner;
        protected readonly Option<int> ownerKey;

        protected bool isActive;

        public ClientPeer(Endpoint serverEndpoint, Callbacks callbacks, Option<int> ownerKey)
        {
            ServerEndpoint = serverEndpoint;
            this.callbacks = callbacks;
            this.ownerKey = ownerKey;
            isOwner = ownerKey.IsSome();
        }

        public abstract void Start();
        public abstract void Close(PeerDisconnectPacket peerDisconnectPacket);
        public abstract void Update(float deltaTime);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true);
        public abstract void SendPassword(string password);

        protected abstract void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body);

        protected ConnectionInitialization initializationStep;
        public bool ContentPackageOrderReceived { get; protected set; }
        protected int passwordSalt;
        protected Steamworks.AuthTicket? steamAuthTicket;
        private GUIMessageBox? passwordMsgBox;

        public bool WaitingForPassword
            => isActive && initializationStep == ConnectionInitialization.Password
               && passwordMsgBox != null
               && GUIMessageBox.MessageBoxes.Contains(passwordMsgBox);

        public struct IncomingInitializationMessage
        {
            public ConnectionInitialization InitializationStep;
            public IReadMessage Message;
        }

        protected void ReadConnectionInitializationStep(IncomingInitializationMessage inc)
        {
            switch (inc.InitializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                {
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }

                    PeerPacketHeaders headers = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsConnectionInitializationStep,
                        Initialization = ConnectionInitialization.SteamTicketAndVersion
                    };

                    ClientSteamTicketAndVersionPacket body = new ClientSteamTicketAndVersionPacket
                    {
                        Name = GameMain.Client.Name,
                        OwnerKey = ownerKey,
                        SteamId = SteamManager.GetSteamId().Select(id => (AccountId)id),
                        SteamAuthTicket = steamAuthTicket switch
                        {
                            null => Option<byte[]>.None(),
                            var ticket => Option<byte[]>.Some(ticket.Data)
                        },
                        GameVersion = GameMain.Version.ToString(),
                        Language = GameSettings.CurrentConfig.Language.Value
                    };

                    SendMsgInternal(headers, body);
                    break;
                }
                case ConnectionInitialization.ContentPackageOrder:
                {
                    if (initializationStep
                        is ConnectionInitialization.SteamTicketAndVersion
                        or ConnectionInitialization.Password)
                    {
                        initializationStep = ConnectionInitialization.ContentPackageOrder;
                    }

                    if (initializationStep != ConnectionInitialization.ContentPackageOrder) { return; }

                    PeerPacketHeaders headers = new PeerPacketHeaders
                    {
                        DeliveryMethod = DeliveryMethod.Reliable,
                        PacketHeader = PacketHeader.IsConnectionInitializationStep,
                        Initialization = ConnectionInitialization.ContentPackageOrder
                    };

                    var orderPacket = INetSerializableStruct.Read<ServerPeerContentPackageOrderPacket>(inc.Message);

                    if (!ContentPackageOrderReceived)
                    {
                        ServerContentPackages = orderPacket.ContentPackages;
                        if (ServerContentPackages.Length == 0)
                        {
                            string errorMsg = "Error in ContentPackageOrder message: list of content packages enabled on the server was empty.";
                            GameAnalyticsManager.AddErrorEventOnce("ClientPeer.ReadConnectionInitializationStep:NoContentPackages", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                            DebugConsole.ThrowError(errorMsg);
                        }
                        ContentPackageOrderReceived = true;
                        
                        SendMsgInternal(headers, null);
                    }

                    break;
                }
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion)
                    {
                        initializationStep = ConnectionInitialization.Password;
                    }

                    if (initializationStep != ConnectionInitialization.Password) { return; }

                    var passwordPacket = INetSerializableStruct.Read<ServerPeerPasswordPacket>(inc.Message);

                    if (WaitingForPassword) { return; }
                    
                    passwordPacket.Salt.TryUnwrap(out passwordSalt);
                    passwordPacket.RetriesLeft.TryUnwrap(out var retries);

                    LocalizedString pwMsg = TextManager.Get("PasswordRequired");

                    passwordMsgBox = new GUIMessageBox(pwMsg, "", new LocalizedString[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                        relativeSize: new Vector2(0.25f, 0.1f), minSize: new Point(400, GUI.IntScale(170)));
                    var passwordHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), passwordMsgBox.Content.RectTransform), childAnchor: Anchor.TopCenter);
                    var passwordBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1f), passwordHolder.RectTransform) { MinSize = new Point(0, 20) })
                    {
                        Censor = true
                    };

                    if (retries > 0)
                    {
                        var incorrectPasswordText = new GUITextBlock(new RectTransform(new Vector2(1f, 0.0f), passwordHolder.RectTransform), TextManager.Get("incorrectpassword"), GUIStyle.Red, GUIStyle.Font, textAlignment: Alignment.Center);
                        incorrectPasswordText.RectTransform.MinSize = new Point(0, (int)incorrectPasswordText.TextSize.Y);
                        passwordHolder.Recalculate();
                    }

                    passwordMsgBox.Content.Recalculate();
                    passwordMsgBox.Content.RectTransform.MinSize = new Point(0, passwordMsgBox.Content.RectTransform.Children.Sum(c => c.Rect.Height));
                    passwordMsgBox.Content.Parent.RectTransform.MinSize = new Point(0, (int)(passwordMsgBox.Content.RectTransform.MinSize.Y / passwordMsgBox.Content.RectTransform.RelativeSize.Y));

                    var okButton = passwordMsgBox.Buttons[0];
                    okButton.OnClicked += (_, __) =>
                    {
                        SendPassword(passwordBox.Text);
                        return true;
                    };
                    okButton.OnClicked += passwordMsgBox.Close;
                    
                    var cancelButton = passwordMsgBox.Buttons[1];
                    cancelButton.OnClicked = (_, __) =>
                    {
                        Close(PeerDisconnectPacket.WithReason(DisconnectReason.Disconnected));
                        passwordMsgBox?.Close(); passwordMsgBox = null;

                        return true;
                    };

                    passwordBox.OnEnterPressed += (_, __) =>
                    {
                        okButton.OnClicked.Invoke(okButton, okButton.UserData);
                        return true;
                    };

                    passwordBox.Select();
                    
                    break;
            }
        }

#if DEBUG
        public abstract void ForceTimeOut();
#endif
    }
}