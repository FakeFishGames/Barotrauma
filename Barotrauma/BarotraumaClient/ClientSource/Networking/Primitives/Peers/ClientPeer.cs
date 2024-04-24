#nullable enable
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    internal abstract class ClientPeer<TEndpoint> : ClientPeer where TEndpoint : Endpoint
    {
        public new TEndpoint ServerEndpoint => (base.ServerEndpoint as TEndpoint)!;
        protected ClientPeer(TEndpoint serverEndpoint, ImmutableArray<Endpoint> allServerEndpoints, Callbacks callbacks, Option<int> ownerKey)
            : base(serverEndpoint, allServerEndpoints, callbacks, ownerKey) { }
    }
    
    internal abstract class ClientPeer
    {
        public ImmutableArray<ServerContentPackage> ServerContentPackages { get; set; } =
            ImmutableArray<ServerContentPackage>.Empty;

        public bool AllowModDownloads { get; private set; } = true;

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
        public readonly ImmutableArray<Endpoint> AllServerEndpoints;
        public NetworkConnection? ServerConnection { get; protected set; }

        protected bool IsOwner => ownerKey.IsSome();
        protected readonly Option<int> ownerKey;

        public bool IsActive => isActive;

        protected bool isActive;

        protected ClientPeer(Endpoint serverEndpoint, ImmutableArray<Endpoint> allServerEndpoints, Callbacks callbacks, Option<int> ownerKey)
        {
            ServerEndpoint = serverEndpoint;
            AllServerEndpoints = allServerEndpoints;
            this.callbacks = callbacks;
            this.ownerKey = ownerKey;
        }

        public abstract void Start();
        public abstract void Close(PeerDisconnectPacket peerDisconnectPacket);
        public abstract void Update(float deltaTime);
        public abstract void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true);
        public abstract void SendPassword(string password);

        protected abstract void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body);

        protected ConnectionInitialization initializationStep;
        public bool ContentPackageOrderReceived { get; set; }
        protected int passwordSalt;
        protected Option<AuthenticationTicket> authTicket;
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

        protected abstract Task<Option<AccountId>> GetAccountId();

        protected void OnInitializationComplete()
        {
            passwordMsgBox?.Close();
            if (initializationStep == ConnectionInitialization.Success) { return; }

            callbacks.OnInitializationComplete.Invoke();
            initializationStep = ConnectionInitialization.Success;
        }
        
        protected void ReadConnectionInitializationStep(IncomingInitializationMessage inc)
        {
            if (inc.InitializationStep != ConnectionInitialization.Password)
            {
                passwordMsgBox?.Close();
            }

            switch (inc.InitializationStep)
            {
                case ConnectionInitialization.AuthInfoAndVersion:
                {
                    if (initializationStep != ConnectionInitialization.AuthInfoAndVersion) { return; }

                    TaskPool.Add($"{GetType().Name}.{nameof(GetAccountId)}", GetAccountId(), t =>
                    {
                        if (GameMain.Client?.ClientPeer is null) { return; }
                        
                        if (!t.TryGetResult(out Option<AccountId> accountId))
                        {
                            Close(PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
                        }
                        
                        var headers = new PeerPacketHeaders
                        {
                            DeliveryMethod = DeliveryMethod.Reliable,
                            PacketHeader = PacketHeader.IsConnectionInitializationStep,
                            Initialization = ConnectionInitialization.AuthInfoAndVersion
                        };

                        var body = new ClientAuthTicketAndVersionPacket
                        {
                            Name = GameMain.Client.Name,
                            OwnerKey = ownerKey,
                            AccountId = accountId,
                            AuthTicket = authTicket,
                            GameVersion = GameMain.Version.ToString(),
                            Language = GameSettings.CurrentConfig.Language.Value
                        };

                        SendMsgInternal(headers, body);
                    });
                    break;
                }
                case ConnectionInitialization.ContentPackageOrder:
                {
                    if (initializationStep
                        is ConnectionInitialization.AuthInfoAndVersion
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
                        AllowModDownloads = orderPacket.AllowModDownloads;
                        if (ServerContentPackages.Length == 0)
                        {
                            string errorMsg = "Error in ContentPackageOrder message: list of content packages enabled on the server was empty.";
                            GameAnalyticsManager.AddErrorEventOnce("ClientPeer.ReadConnectionInitializationStep:NoContentPackages", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                            DebugConsole.ThrowError(errorMsg);
                        }
                        ContentPackageOrderReceived = true;
                    }
                    SendMsgInternal(headers, null);                    

                    break;
                }
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.AuthInfoAndVersion)
                    {
                        initializationStep = ConnectionInitialization.Password;
                    }

                    if (initializationStep != ConnectionInitialization.Password) { return; }

                    var passwordPacket = INetSerializableStruct.Read<ServerPeerPasswordPacket>(inc.Message);

                    if (WaitingForPassword) { return; }
                    
                    passwordPacket.Salt.TryUnwrap(out passwordSalt);
                    passwordPacket.RetriesLeft.TryUnwrap(out var retries);

                    LocalizedString pwMsg = TextManager.Get("PasswordRequired");

                    passwordMsgBox?.Close();
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

        public abstract void DebugSendRawMessage(IWriteMessage msg);
#endif
    }
}