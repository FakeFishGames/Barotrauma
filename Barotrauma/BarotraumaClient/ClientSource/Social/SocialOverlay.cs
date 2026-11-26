#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma;

sealed class SocialOverlay : IDisposable
{
    public static readonly LocalizedString ShortcutBindText = TextManager.Get("SocialOverlayShortcutBind");

    public static SocialOverlay? Instance { get; private set; }
    public static void Init()
    {
        Instance ??= new SocialOverlay();
    }

    private sealed class NotificationHandler
    {
        public record Notification(
            DateTime ReceiveTime,
            GUIComponent GuiElement);
        private readonly List<Notification> notifications = new();

        private static readonly TimeSpan notificationDuration = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan notificationEasingTimeSpan = TimeSpan.FromSeconds(0.5);
        public readonly GUIFrame NotificationContainer =
            new GUIFrame(new RectTransform((0.4f, 0.15f), GUI.Canvas, Anchor.BottomRight, scaleBasis: ScaleBasis.BothHeight), style: null)
            {
                CanBeFocused = false
            };

        public void Update()
        {
            var now = DateTime.Now;
            float cumulativeNotificationOffset = 0;

            for (int i = notifications.Count - 1; i >= 0; i--)
            {
                var notification = notifications[i];

                var expiryTime = notification.ReceiveTime + notificationDuration;
                if (now > expiryTime
                    || notification.GuiElement.Parent is null)
                {
                    RemoveNotification(notification);
                    continue;
                }

                TimeSpan diffToStart = now - notification.ReceiveTime;
                TimeSpan diffToEnd = expiryTime - now;

                float offsetToAdd = 1f;
                offsetToAdd = Math.Min(
                    offsetToAdd,
                    (float)diffToStart.TotalSeconds / (float)notificationEasingTimeSpan.TotalSeconds);
                offsetToAdd = Math.Min(
                    offsetToAdd,
                    (float)diffToEnd.TotalSeconds / (float)notificationEasingTimeSpan.TotalSeconds);

                offsetToAdd = Math.Max(offsetToAdd, 0f);

                cumulativeNotificationOffset += offsetToAdd;

                notification.GuiElement.RectTransform.RelativeOffset = (0, cumulativeNotificationOffset - 1f);
            }
        }

        public void AddToGuiUpdateList()
        {
            NotificationContainer.AddToGUIUpdateList();
        }

        public void AddNotification(Notification notification)
        {
            notifications.Add(notification);
        }

        public void RemoveNotification(Notification notification)
        {
            notifications.Remove(notification);
            NotificationContainer.RemoveChild(notification.GuiElement);
        }
    }

    private sealed class InviteHandler : IDisposable
    {
        private readonly record struct Invite(
            FriendInfo Sender,
            DateTime ReceiveTime,
            Option<NotificationHandler.Notification> NotificationOption);

        private readonly SocialOverlay socialOverlay;
        private readonly FriendProvider friendProvider;
        private readonly NotificationHandler notificationHandler;

        private readonly List<Invite> invites = new List<Invite>();
        private static readonly TimeSpan inviteDuration = TimeSpan.FromMinutes(5);

        public InviteHandler(
            SocialOverlay inSocialOverlay,
            FriendProvider inFriendProvider,
            NotificationHandler inNotificationHandler)
        {
            socialOverlay = inSocialOverlay;
            friendProvider = inFriendProvider;
            notificationHandler = inNotificationHandler;

            Steamworks.SteamFriends.OnChatMessage += OnSteamChatMsgReceived;
        }

        private void OnSteamChatMsgReceived(Steamworks.Friend steamFriend, string msgType, string msgContent)
        {
            if (!string.Equals(msgType, "InviteGame")) { return; }

            var friendId = new SteamId(steamFriend.Id);
            TaskPool.Add(
                $"ReceivedInviteFrom{friendId}",
                friendProvider.RetrieveFriend(friendId),
                t =>
                {
                    if (!t.TryGetResult(out Option<FriendInfo> friendInfoOption)) { return; }
                    if (!friendInfoOption.TryUnwrap(out var friendInfo)) { return; }
                    RegisterInvite(friendInfo, showNotification: false);
                });
        }

        public bool HasInviteFrom(AccountId sender)
            => invites.Any(invite => invite.Sender.Id == sender);

        public void ClearInvitesFrom(AccountId sender)
        {
            foreach (var invite in invites)
            {
                if (invite.Sender.Id == sender && invite.NotificationOption.TryUnwrap(out var notification))
                {
                    notificationHandler.RemoveNotification(notification);
                }
            }
            invites.RemoveAll(invite => invite.Sender.Id == sender);
        }

        public void Update()
        {
            var now = DateTime.Now;

            for (int i = invites.Count - 1; i >= 0; i--)
            {
                var invite = invites[i];

                var expiryTime = invite.ReceiveTime + inviteDuration;
                if (now > expiryTime)
                {
                    if (invite.NotificationOption.TryUnwrap(out var notification))
                    {
                        notificationHandler.RemoveNotification(notification);
                    }
                    invites.RemoveAt(i);
                }
            }
        }

        private void RegisterInvite(FriendInfo senderInfo, bool showNotification)
        {
            var now = DateTime.Now;

            var invite = new Invite(
                Sender: senderInfo,
                ReceiveTime: now,
                NotificationOption: Option.None);

            if (showNotification)
            {
                var baseButton = new GUIButton(
                    new RectTransform(Vector2.One, notificationHandler.NotificationContainer.RectTransform, Anchor.BottomRight)
                    {
                        RelativeOffset = (0, -1)
                    }, style: "SocialOverlayPopup");
                baseButton.Frame.OutlineThickness = 1f;

                var topLayout = new GUILayoutGroup(new RectTransform(Vector2.One, baseButton.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var avatarContainer = new GUIFrame(new RectTransform(Vector2.One, topLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: null);

                var avatarComponent = new GUICustomComponent(
                    new RectTransform(
                        Vector2.One * 0.8f,
                        avatarContainer.RectTransform,
                        Anchor.Center,
                        scaleBasis: ScaleBasis.BothHeight),
                    onDraw: (sb, component) =>
                    {
                        if (!senderInfo.Avatar.TryUnwrap(out var avatar)) { return; }

                        var rect = component.Rect;
                        sb.Draw(avatar.Texture, rect, avatar.Texture.Bounds, Color.White);
                    });
                
                var textLayout = new GUILayoutGroup(new RectTransform(Vector2.One, topLayout.RectTransform))
                {
                    Stretch = true
                };

                void addPadding()
                    => new GUIFrame(new RectTransform((1.0f, 0.2f), textLayout.RectTransform), style: null);

                void addText(LocalizedString text, GUIFont font)
                    => new GUITextBlock(new RectTransform((1.0f, 0.2f), textLayout.RectTransform), text, font: font);
                
                addPadding();
                addText(senderInfo.Name, GUIStyle.SubHeadingFont);
                addText(TextManager.Get("InvitedYou"), GUIStyle.Font);
                addPadding();
                addText(TextManager.GetWithVariable("ClickHereOrPressSocialOverlayShortcut", "[shortcut]", ShortcutBindText), GUIStyle.SmallFont);
                addPadding();

                var notification = new NotificationHandler.Notification(
                    ReceiveTime: now,
                    GuiElement: baseButton);
                baseButton.OnClicked = (_, _) =>
                {
                    socialOverlay.IsOpen = true;
                    notificationHandler.RemoveNotification(notification);
                    return false;
                };
                baseButton.OnSecondaryClicked = (_, _) =>
                {
                    notificationHandler.RemoveNotification(notification);
                    return false;
                };

                notificationHandler.AddNotification(notification);

                invite = invite with { NotificationOption = Option.Some(notification) };
            }

            invites.Add(invite);
        }

        public void Dispose()
        {
            Steamworks.SteamFriends.OnChatMessage -= OnSteamChatMsgReceived;
        }
    }

    private readonly NotificationHandler notificationHandler;
    private readonly InviteHandler inviteHandler;
    private readonly GUIFrame background;
    private readonly GUIButton linkHint;
    private readonly GUILayoutGroup contentLayout;

    private readonly GUIFrame selectedFriendInfoFrame;

    private const float WidthToHeightRatio = 7f;

    private readonly TimeSpan refreshInterval = TimeSpan.FromSeconds(30);
    private DateTime lastRefreshTime;

    public bool IsOpen;

    private static RectTransform CreateRowRectT(GUIComponent parent, float heightScale = 1f)
        => new RectTransform((1.0f, heightScale / WidthToHeightRatio), parent.RectTransform, scaleBasis: ScaleBasis.BothWidth);
    
    private static GUILayoutGroup CreateRowLayout(GUIComponent parent, float heightScale = 1f)
    {
        var rowLayout = new GUILayoutGroup(CreateRowRectT(parent, heightScale), isHorizontal: true)
        {
            Stretch = true
        };

        new GUICustomComponent(new RectTransform(Vector2.Zero, rowLayout.RectTransform),
            onUpdate: (f, component) =>
            {
                rowLayout.RectTransform.NonScaledSize = calculateSize();
            });

        return rowLayout;

        Point calculateSize() => new Point(parent.Rect.Width, (int)((parent.Rect.Width * heightScale) / WidthToHeightRatio));
    }

    private readonly struct PlayerRow
    {
        public readonly GUIFrame AvatarContainer;
        public readonly GUIFrame InfoContainer;
        public readonly FriendInfo FriendInfo;

        internal PlayerRow(FriendInfo friendInfo, GUILayoutGroup containerLayout, bool invitedYou, IEnumerable<LocalizedString>? metadataText = null)
        {
            FriendInfo = friendInfo;
            AvatarContainer = new GUIFrame(new RectTransform(Vector2.One, containerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: null);
            InfoContainer = new GUIFrame(new RectTransform(Vector2.One, containerLayout.RectTransform, scaleBasis: ScaleBasis.Normal), style: null);

            friendInfo.RetrieveOrInheritAvatar(Option.None, AvatarContainer.Rect.Height);

            var avatarBackground = new GUIFrame(new RectTransform(Vector2.One * 0.9f, AvatarContainer.RectTransform, Anchor.Center),
                style: invitedYou
                    ? "FriendInvitedYou"
                    : $"Friend{friendInfo.CurrentStatus}");

            var textLayout = new GUILayoutGroup(new RectTransform(Vector2.One, InfoContainer.RectTransform)) { Stretch = true };
            var textBlocks = new List<GUITextBlock>();

            addTextLayoutPadding();
            addTextBlock(friendInfo.Name, font: GUIStyle.SubHeadingFont);
            metadataText ??= new[] { friendInfo.StatusText };
            foreach (var line in metadataText)
            {
                addTextBlock(line, font: GUIStyle.Font);
            }
            addTextLayoutPadding();

            new GUICustomComponent(new RectTransform(Vector2.One, avatarBackground.RectTransform),
                onUpdate: updateTextAlignments,
                onDraw: drawAvatar);

            if (invitedYou)
            {
                var inviteIcon = new GUIImage(new RectTransform(new Vector2(0.5f), avatarBackground.RectTransform, Anchor.TopRight, Pivot.Center) 
                    { RelativeOffset = Vector2.One * 0.15f }, style: "InviteNotification")
                {
                    ToolTip = TextManager.Get("InviteNotification")
                };
                inviteIcon.OnAddedToGUIUpdateList += (GUIComponent component) =>
                {
                    if (component.FlashTimer <= 0.0f)
                    {
                        component.Flash(GUIStyle.Green, useCircularFlash: true);
                        component.Pulsate(Vector2.One, Vector2.One * 1.5f, 0.5f);
                    }
                };
            }

            void addTextLayoutPadding()
                => new GUIFrame(new RectTransform(Vector2.One, textLayout.RectTransform), style: null);

            void addTextBlock(LocalizedString text, GUIFont font)
                => textBlocks.Add(new GUITextBlock(new RectTransform(Vector2.One, textLayout.RectTransform), text,
                    textColor: Color.White, font: font, textAlignment: Alignment.CenterLeft)
                {
                    ForceUpperCase = ForceUpperCase.No,
                    TextColor = avatarBackground.Color,
                    HoverTextColor = avatarBackground.HoverColor,
                    SelectedTextColor = avatarBackground.SelectedColor,
                    PressedColor = avatarBackground.PressedColor,
                });

            void updateTextAlignments(float deltaTime, GUICustomComponent component)
            {
                foreach (var textBlock in textBlocks)
                {
                    int height = (int)textBlock.Font.LineHeight + GUI.IntScale(2);
                    textBlock.RectTransform.NonScaledSize =
                        (textBlock.RectTransform.NonScaledSize.X, height);
                }
                textLayout.NeedsToRecalculate = true;
            }

            void drawAvatar(SpriteBatch sb, GUICustomComponent component)
            {
                if (!friendInfo.Avatar.TryUnwrap(out var avatar)) { return; }
                Rectangle rect = component.Rect;
                rect.Inflate(-GUI.IntScale(4f), -GUI.IntScale(4f));
                sb.Draw(avatar.Texture, rect, Color.White);
            }
        }
    }

    private readonly FriendProvider friendProvider;

    private readonly GUILayoutGroup selfPlayerRowLayout;

    private readonly GUILayoutGroup? eosStatusTextContainer;

    private readonly GUIListBox friendPlayerListBox;
    private readonly List<PlayerRow> friendPlayerRows = new List<PlayerRow>();

    private void RecreateSelfPlayerRow()
    {
        if (SteamManager.GetSteamId().TryUnwrap(out var steamId))
        {
            selfPlayerRowLayout.ClearChildren();
            _ = new PlayerRow(
                new FriendInfo(
                    name: SteamManager.GetUsername(),
                    id: steamId,
                    status: FriendStatus.PlayingBarotrauma,
                    serverName: "",
                    connectCommand: Option.None,
                    provider: friendProvider),
                selfPlayerRowLayout,
                invitedYou: false);
        }
    }

    private SocialOverlay()
    {
        background =
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas, Anchor.Center), style: "SocialOverlayBackground");
        var rightSideLayout =
            new GUILayoutGroup(
                new RectTransform((0.9f, 1.0f), background.RectTransform, Anchor.CenterRight,
                    scaleBasis: ScaleBasis.BothHeight), isHorizontal: true, childAnchor: Anchor.BottomLeft);

        linkHint = new GUIButton(new RectTransform((0.5f, 0.9f / WidthToHeightRatio), rightSideLayout.RectTransform, Anchor.BottomRight, scaleBasis: ScaleBasis.BothWidth), style: "FriendsButton")
        {
            OnClicked = (btn, _) =>
            {
                btn.Visible = false;
                return false;
            },
            Visible = false
        };
        _ = new GUITextBlock(new RectTransform(Vector2.One * 0.95f, linkHint.RectTransform, Anchor.Center),
            text: TextManager.Get("EosSettings.RecommendLinkingToEpicAccount"),
            wrap: true,
            style: "FriendsButton");

        var content = new GUIFrame(
            new RectTransform((0.5f, 1.0f), rightSideLayout.RectTransform),
            style: "SocialOverlayFriendsList");

        _ = new GUIButton(
            new RectTransform(Vector2.One * 0.08f, content.RectTransform, Anchor.TopLeft, Pivot.TopRight,
                scaleBasis: ScaleBasis.BothWidth)
            {
                RelativeOffset = (-0.03f, 0.015f)
            },
            style: "SocialOverlayCloseButton")
        {
            OnClicked = (_, _) =>
            {
                IsOpen = false;
                return false;
            }
        };

        friendProvider = new CompositeFriendProvider(new SteamFriendProvider());

        notificationHandler = new NotificationHandler();
        inviteHandler = new InviteHandler(
            inSocialOverlay: this,
            inFriendProvider: friendProvider,
            inNotificationHandler: notificationHandler);

        selectedFriendInfoFrame = new GUIFrame(new RectTransform((0.25f, 0.28f), background.RectTransform,
            Anchor.TopRight, scaleBasis: ScaleBasis.BothHeight), style: "SocialOverlayPopup")
        {
            OutlineThickness = 1f,
            Visible = false
        };

        contentLayout = new GUILayoutGroup(new RectTransform(Vector2.One, content.RectTransform)) { Stretch = true };

        selfPlayerRowLayout = CreateRowLayout(contentLayout);
        RecreateSelfPlayerRow();

        friendPlayerListBox =
            new GUIListBox(new RectTransform(Vector2.One, contentLayout.RectTransform), style: null)
            {
                OnSelected = (component, userData) =>
                {
                    if (userData is not FriendInfo friendInfo) { return false; }
                    selectedFriendInfoFrame.Visible = true;
                    selectedFriendInfoFrame.RectTransform.AbsoluteOffset = (
                        X: background.Rect.Right - component.Rect.X,
                        Y: Math.Clamp(
                            value: component.Rect.Center.Y - selectedFriendInfoFrame.Rect.Height / 2,
                            min: 0,
                            max: background.Rect.Bottom - selectedFriendInfoFrame.Rect.Height));
                    PopulateSelectedFriendInfoFrame(friendInfo);
                    return true;
                }
            };
        friendPlayerListBox.ScrollBar.OnMoved += (_, _) => { friendPlayerListBox.Deselect(); return true; };

        RefreshFriendList();
    }

    public void DisplayBindHintToPlayer()
    {
        if (IsOpen) { return; }

        var baseButton = new GUIButton(
            new RectTransform(Vector2.One, notificationHandler.NotificationContainer.RectTransform, Anchor.BottomRight)
            {
                RelativeOffset = (0, -1)
            }, style: "SocialOverlayPopup");
        baseButton.Frame.OutlineThickness = 1f;

        var notification = new NotificationHandler.Notification(
            ReceiveTime: DateTime.Now,
            GuiElement: baseButton);
        baseButton.OnClicked = (_, _) =>
        {
            IsOpen = true;
            notificationHandler.RemoveNotification(notification);
            return false;
        };
        baseButton.OnSecondaryClicked = (_, _) =>
        {
            notificationHandler.RemoveNotification(notification);
            return false;
        };

        _ = new GUITextBlock(
            new RectTransform(Vector2.One * 0.98f, baseButton.RectTransform, Anchor.Center),
            text: TextManager.GetWithVariable("SocialOverlayShortcutHint", "[shortcut]", ShortcutBindText),
            textAlignment: Alignment.Center,
            wrap: true)
        {
            CanBeFocused = false
        };

        notificationHandler.AddNotification(notification);
    }

    private void PopulateSelectedFriendInfoFrame(FriendInfo friendInfo)
    {
        selectedFriendInfoFrame.ClearChildren();
        var layout =
            new GUILayoutGroup(new RectTransform(Vector2.One * 0.9f, selectedFriendInfoFrame.RectTransform,
                Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

        addPadding();
        new GUITextBlock(
            new RectTransform((1.0f, 0.08f), layout.RectTransform),
            text: friendInfo.Name,
            font: GUIStyle.SubHeadingFont,
            textAlignment: Alignment.Center)
        {
            ForceUpperCase = ForceUpperCase.No
        };
        new GUITextBlock(
            new RectTransform((1.0f, 0.08f), layout.RectTransform),
            text: friendInfo.StatusText,
            font: GUIStyle.Font,
            textAlignment: Alignment.TopCenter)
        {
            ForceUpperCase = ForceUpperCase.No
        };
        addPadding();
        var viewProfileButton = addButton(friendInfo.Id.ViewProfileLabel());
        viewProfileButton.OnClicked = (_, _) =>
        {
            friendInfo.Id.OpenProfile();
            return false;
        };
        if (friendInfo.IsInServer && 
            /* don't allow joining other servers when hosting */
            GameMain.Client is not { IsServerOwner: true } &&
            /* can't join if already joined */
            friendInfo.ConnectCommand.TryUnwrap(out var command) && !command.IsClientConnectedToEndpoint())
        {
            var joinButton = addButton(TextManager.Get("ServerListJoin"));
            joinButton.OnClicked = (_, _) =>
            {
                GameMain.Instance.ConnectCommand = friendInfo.ConnectCommand;
                selectedFriendInfoFrame.Visible = false;
                IsOpen = false;
                return false;
            };
        }
        if (inviteHandler.HasInviteFrom(friendInfo.Id))
        {
            var declineButton = addButton(TextManager.Get("DeclineInvite"));
            declineButton.OnClicked = (_, _) =>
            {
                inviteHandler.ClearInvitesFrom(friendInfo.Id);
                selectedFriendInfoFrame.Visible = false;
                return false;
            };
        }
        if (GameMain.Client is not null)
        {
            var inviteButton = addButton(TextManager.Get("InviteFriend"));
            inviteButton.OnClicked = (_, _) =>
            {
                selectedFriendInfoFrame.Visible = false;
                var connectCommandOption = (GameMain.Client?.ClientPeer.ServerEndpoint) switch
                {
                    LidgrenEndpoint lidgrenEndpoint => Option.Some(new ConnectCommand(GameMain.Client.Name, lidgrenEndpoint)),
                    P2PEndpoint or PipeEndpoint => Option.Some(new ConnectCommand(GameMain.Client.Name, GameMain.Client.ClientPeer.AllServerEndpoints.OfType<P2PEndpoint>().ToImmutableArray())),
                    _ => Option.None
                };
                if (!connectCommandOption.TryUnwrap(out var connectCommand)) 
                {
                    DebugConsole.AddWarning($"Could not create an invite for the endpoint {GameMain.Client?.ClientPeer.ServerEndpoint}.");
                    return false; 
                }

                if (friendInfo.Id is SteamId friendSteamId && SteamManager.IsInitialized)
                {
                    var steamFriend = new Steamworks.Friend(friendSteamId.Value);
                    steamFriend.InviteToGame(connectCommand.ToString());
                }
                return false;
            };
        }
        addPadding();

        void addPadding()
            => new GUIFrame(new RectTransform((1.0f, 0.05f), layout.RectTransform), style: null);

        GUIButton addButton(LocalizedString label)
            => new GUIButton(new RectTransform((1.0f, 0.08f), layout.RectTransform), label, style: "SocialOverlayButton");
    }



    public void RefreshFriendList()
    {
        lastRefreshTime = DateTime.Now;

        if (!SteamManager.IsInitialized)
        {
            friendPlayerListBox.ClearChildren();
            var offlineLabel = insertLabel(TextManager.Get("SocialOverlayOffline"), heightScale: 4.0f);
            offlineLabel.Wrap = true;

            return;
        }

        TaskPool.Add(
                "RefreshFriendList",
                friendProvider.RetrieveFriends(),
                t =>
                {
                    if (!t.TryGetResult(out ImmutableArray<FriendInfo> friends))
                    {
                        return;
                    }

                    friendPlayerListBox.ClearChildren();
                    friendPlayerRows.ForEach(f => f.FriendInfo.Dispose());
                    friendPlayerRows.Clear();

                    var friendsOrdered = friends
                        .OrderByDescending(f => f.CurrentStatus)
                        .ThenByDescending(f => inviteHandler.HasInviteFrom(f.Id))
                        .ThenBy(f => f.Name)
                        .ToImmutableArray();
                    bool prevWasOnline = true;
                    if (friendsOrdered.Length > 0 && friendsOrdered[0].IsOnline)
                    {
                        insertLabel(TextManager.Get("Label.OnlineLabel"));
                    }

                    for (int friendIndex = 0; friendIndex < friendsOrdered.Length; friendIndex++)
                    {
                        var friend = friendsOrdered[friendIndex];
                        if (prevWasOnline && !friend.IsOnline)
                        {
                            if (friendIndex > 0)
                            {
                                insertLabel("");
                            }

                            insertLabel(TextManager.Get("Label.OfflineLabel"));
                        }

                        var friendFrame = new GUIFrame(CreateRowRectT(friendPlayerListBox.Content),
                            style: "ListBoxElement")
                        {
                            UserData = friend
                        };
                        GUILayoutGroup newRowLayout = CreateRowLayout(friendFrame);
                        newRowLayout.RectTransform.RelativeSize = Vector2.One;
                        newRowLayout.RectTransform.ScaleBasis = ScaleBasis.Normal;
                        var newRow = new PlayerRow(friend, newRowLayout,
                            invitedYou: inviteHandler.HasInviteFrom(friend.Id));
                        friendPlayerRows.Add(newRow);

                        prevWasOnline = friend.IsOnline;
                    }

                    contentLayout.Recalculate();
                    friendPlayerListBox.UpdateScrollBarSize();
                });

        GUITextBlock insertLabel(LocalizedString text, float heightScale = 0.5f)
        {
            var labelContainer = new GUIFrame(CreateRowRectT(friendPlayerListBox.Content), style: null)
            {
                CanBeFocused = false
            };
            Vector2 oldRelativeSize = labelContainer.RectTransform.RelativeSize;
            labelContainer.RectTransform.RelativeSize
                = (oldRelativeSize.X, oldRelativeSize.Y * heightScale);
            return new GUITextBlock(new RectTransform(Vector2.One, labelContainer.RectTransform),
                text: text,
                font: GUIStyle.SubHeadingFont);
        }
    }

    public void AddToGuiUpdateList()
    {
        if (IsOpen)
        {
            background.AddToGUIUpdateList();
        }
        notificationHandler.AddToGuiUpdateList();
    }

    public void Update()
    {
        inviteHandler.Update();
        notificationHandler.Update();

        if (!IsOpen) { return; }

        if (selectedFriendInfoFrame.Visible)
        {
            if (PlayerInput.PrimaryMouseButtonClicked()
                && selectedFriendInfoFrame.Visible
                && !GUI.IsMouseOn(friendPlayerListBox)
                && !GUI.IsMouseOn(selectedFriendInfoFrame))
            {
                friendPlayerListBox.Deselect();
            }

            if (GUI.IsMouseOn(friendPlayerListBox)
                && PlayerInput.ScrollWheelSpeed != 0)
            {
                friendPlayerListBox.Deselect();
            }

            if (!friendPlayerListBox.Selected)
            { 
                selectedFriendInfoFrame.Visible = false;
            }
        }

        if (DateTime.Now < lastRefreshTime + refreshInterval) { return; }

        RefreshFriendList();
    }

    public void Dispose()
    {
        inviteHandler.Dispose();
    }
}