using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        //how often the client is allowed to refresh servers
        private TimeSpan AllowedRefreshInterval = new TimeSpan(0, 0, 3);

        private readonly GUIFrame menu;

        private readonly GUIListBox serverList;
        private readonly GUIFrame serverPreview;

        private readonly GUIButton joinButton;

        private readonly GUITextBox clientNameBox;
        private ServerInfo selectedServer;

        //friends list
        private readonly GUILayoutGroup friendsButtonHolder;

        private GUIButton friendsDropdownButton;
        private GUIListBox friendsDropdown;

        private class FriendInfo
        {
            public UInt64 SteamID;
            public string Name;
            public Sprite Sprite;
            public string Status;
            public bool PlayingThisGame;
            public string ConnectName;
            public string ConnectEndpoint;
            public UInt64 ConnectLobby;

            public bool InServer
            {
                get
                {
                    return PlayingThisGame && !string.IsNullOrWhiteSpace(Status) && (!string.IsNullOrWhiteSpace(ConnectEndpoint) || ConnectLobby != 0);
                }
            }
        }
        private List<FriendInfo> friendsList;
        private GUIFrame friendPopup;
        private double friendsListUpdateTime;

        //favorite servers/history
        private const string recentServersFile = "Data/recentservers.xml";
        private const string favoriteServersFile = "Data/favoriteservers.xml";
        private List<ServerInfo> favoriteServers;
        private List<ServerInfo> recentServers;

        //server playstyle and tags
        public Sprite[] PlayStyleBanners
        {
            get;
            private set;
        }

        private bool masterServerResponded;
        private IRestResponse masterServerResponse;
        
        private readonly float[] columnRelativeWidth = new float[] { 0.1f, 0.1f, 0.7f, 0.12f, 0.08f, 0.08f };
        private readonly string[] columnLabel = new string[] { "ServerListCompatible", "ServerListHasPassword", "ServerListName", "ServerListRoundStarted", "ServerListPlayers", "ServerListPing" };
        
        private readonly GUILayoutGroup labelHolder;
        private readonly List<GUITextBlock> labelTexts = new List<GUITextBlock>();

        //filters
        private readonly GUITextBox searchBox;
        private readonly GUITickBox filterPassword;
        private readonly GUITickBox filterIncompatible;
        private readonly GUITickBox filterFull;
        private readonly GUITickBox filterEmpty;

        private string sortedBy;
        
        private readonly GUIButton serverPreviewToggleButton;

        //a timer for preventing the client from spamming the refresh button faster than AllowedRefreshInterval
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;

        public ServerListScreen()
        {
            GameMain.Instance.OnResolutionChanged += OnResolutionChanged;

            menu = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.85f), GUI.Canvas, Anchor.Center) { MinSize = new Point(GameMain.GraphicsHeight, 0) });

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.97f, 0.95f), menu.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            //-------------------------------------------------------------------------------------
            //Top row
            //-------------------------------------------------------------------------------------

            var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform)) { Stretch = true };

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), TextManager.Get("JoinServer"), font: GUI.LargeFont)
            {
                Padding = Vector4.Zero,
                ForceUpperCase = true,
                AutoScale = true
            };

            var infoHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f,  Stretch = true };

            var clientNameHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 1.0f), infoHolder.RectTransform)) { RelativeSpacing = 0.05f };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), clientNameHolder.RectTransform), TextManager.Get("YourName"));
            clientNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), clientNameHolder.RectTransform), "")
            {
                Text = GameMain.Config.DefaultPlayerName,
                MaxTextLength = Client.MaxNameLength,
                OverflowClip = true
            };

            if (string.IsNullOrEmpty(clientNameBox.Text))
            {
                clientNameBox.Text = SteamManager.GetUsername();
            }

            friendsButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1.0f), infoHolder.RectTransform, Anchor.BottomRight), childAnchor: Anchor.BottomRight) { RelativeSpacing = 0.005f, IsHorizontal = true };
            friendsList = new List<FriendInfo>();

            //-------------------------------------------------------------------------------------
            // Bottom row
            //-------------------------------------------------------------------------------------

            var bottomRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f - topRow.RectTransform.RelativeSize.Y),
                paddedFrame.RectTransform, Anchor.CenterRight))
            {
                Stretch = true
            };

            var serverListHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), bottomRow.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            // filters -------------------------------------------

            var filters = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black
            };
            var filterToggle = new GUIButton(new RectTransform(new Vector2(0.02f, 1.0f), serverListHolder.RectTransform, Anchor.CenterRight) { MinSize = new Point(20, 0) }, style: "UIToggleButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    filters.RectTransform.RelativeSize = new Vector2(0.25f, 1.0f);
                    filters.Visible = !filters.Visible;
                    filters.IgnoreLayoutGroups = !filters.Visible;
                    serverListHolder.Recalculate();
                    btn.Children.ForEach(c => c.SpriteEffects = !filters.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };
            filterToggle.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);

            var filterContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.99f), filters.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            var filterTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), filterContainer.RectTransform), TextManager.Get("FilterServers"), font: GUI.LargeFont)
            {
                Padding = Vector4.Zero,
                AutoScale = true
            };

            float elementHeight = 0.05f;

            var searchHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, elementHeight), filterContainer.RectTransform), isHorizontal: true) { Stretch = true };

            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), searchHolder.RectTransform), TextManager.Get("Search") + "...");
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), searchHolder.RectTransform), "");
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };

            var filterHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), filterContainer.RectTransform)) { RelativeSpacing = 0.005f };

            List<GUITextBlock> filterTextList = new List<GUITextBlock>();
            filterPassword = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterPassword"))
            {
                ToolTip = TextManager.Get("FilterPassword"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterPassword.TextBlock);
            filterIncompatible = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterIncompatibleServers"))
            {
                ToolTip = TextManager.Get("FilterIncompatibleServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterIncompatible.TextBlock);
            filterFull = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterFullServers"))
            {
                ToolTip = TextManager.Get("FilterFullServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterFull.TextBlock);
            filterEmpty = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filterHolder.RectTransform), TextManager.Get("FilterEmptyServers"))
            {
                ToolTip = TextManager.Get("FilterEmptyServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterEmpty.TextBlock);

            filterContainer.RectTransform.SizeChanged += () =>
            {
                filterContainer.RectTransform.RecalculateChildren(true, true);
                filterTextList.ForEach(t => t.Text = t.ToolTip);
                GUITextBlock.AutoScaleAndNormalize(filterTextList);
                if (filterTextList[0].TextScale < 0.8f)
                {
                    filterTextList.ForEach(t => t.TextScale = 1.0f);
                    filterTextList.ForEach(t => t.Text = ToolBox.LimitString(t.Text, t.Font, (int)(filterContainer.Rect.Width * 0.8f)));
                }
            };

            // server list ---------------------------------------------------------------------

            var serverListContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), serverListHolder.RectTransform)) { Stretch = true };

            labelHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.05f), serverListContainer.RectTransform) { MinSize = new Point(0, 15) },
                isHorizontal: true)
            {
                Stretch = true
            };
            
            for (int i = 0; i < columnRelativeWidth.Length; i++)
            {
                var btn = new GUIButton(new RectTransform(new Vector2(columnRelativeWidth[i], 1.0f), labelHolder.RectTransform),
                    text: TextManager.Get(columnLabel[i]), textAlignment: Alignment.Center, style: null)
                {
                    Color = new Color(12, 14, 15, 255) * 0.5f,
                    HoverColor = new Color(12, 14, 15, 255) * 2.5f,
                    SelectedColor = Color.Gray * 0.7f,
                    PressedColor = Color.Gray * 0.7f,
                    OutlineColor = Color.Black,
                    ToolTip = TextManager.Get(columnLabel[i]),
                    ForceUpperCase = true,
                    UserData = columnLabel[i],
                    OnClicked = SortList
                };
                labelTexts.Add(btn.TextBlock);
                
                new GUIImage(new RectTransform(new Vector2(0.5f, 0.3f), btn.RectTransform, Anchor.BottomCenter, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrow", scaleToFit: true)
                {
                    CanBeFocused = false,
                    UserData = "arrowup",
                    Visible = false
                };
                new GUIImage(new RectTransform(new Vector2(0.5f, 0.3f), btn.RectTransform, Anchor.BottomCenter, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrow", scaleToFit: true)
                {
                    CanBeFocused = false,
                    UserData = "arrowdown",
                    SpriteEffects = SpriteEffects.FlipVertically,
                    Visible = false
                };
            }

            serverList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), serverListContainer.RectTransform, Anchor.Center))
            {
                ScrollBarVisible = true,
                OnSelected = (btn, obj) =>
                {
                    if (obj is ServerInfo serverInfo)
                    {
                        joinButton.Enabled = true;
                        selectedServer = serverInfo;
                        if (!serverPreview.Visible)
                        {
                            serverPreview.RectTransform.RelativeSize = new Vector2(0.3f, 1.0f);
                            serverPreviewToggleButton.Visible = true;
                            serverPreviewToggleButton.IgnoreLayoutGroups = false;
                            serverPreview.Visible = true;
                            serverPreview.IgnoreLayoutGroups = false;
                            serverListHolder.Recalculate();
                        }
                        serverInfo.CreatePreviewWindow(serverPreview);
                        btn.Children.ForEach(c => c.SpriteEffects = serverPreview.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    }
                    return true;
                }
            };

            //server preview panel --------------------------------------------------

            serverPreviewToggleButton = new GUIButton(new RectTransform(new Vector2(0.02f, 1.0f), serverListHolder.RectTransform, Anchor.CenterRight) { MinSize = new Point(20, 0) }, style: "UIToggleButton")
            {
                Visible = false,
                OnClicked = (btn, userdata) =>
                {
                    serverPreview.RectTransform.RelativeSize = new Vector2(0.25f, 1.0f);
                    serverPreview.Visible = !serverPreview.Visible;
                    serverPreview.IgnoreLayoutGroups = !serverPreview.Visible;
                    serverListHolder.Recalculate();
                    btn.Children.ForEach(c => c.SpriteEffects = serverPreview.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };

            serverPreview = new GUIFrame(new RectTransform(new Vector2(0.3f, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false
            };

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), bottomRow.RectTransform), style: null);

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.075f), bottomRow.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("Back"), style: "GUIButtonLarge")
            {
                OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu
            };

            new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListRefresh"), style: "GUIButtonLarge")
            {
				OnClicked = (btn, userdata) => { RefreshServers(); return true; }
			};

            var directJoinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("serverlistdirectjoin"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, userdata) => { ShowDirectJoinPrompt(); return true; }
            };

            joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListJoin"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, userdata) =>
                {
                    if (selectedServer != null)
                    {
                        if (selectedServer.LobbyID == 0)
                        {
                            JoinServer(selectedServer.IP + ":" + selectedServer.Port, selectedServer.ServerName);
                        }
                        else
                        {
                            Steam.SteamManager.JoinLobby(selectedServer.LobbyID, true);
                        }
                    }
                    return true;
                },
                Enabled = false
            };

            //--------------------------------------------------------

            bottomRow.Recalculate();
            serverListHolder.Recalculate();
            serverListContainer.Recalculate();
            labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
            labelHolder.Recalculate();

            serverList.Content.RectTransform.SizeChanged += () =>
            {
                labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
                labelHolder.Recalculate();
                foreach (GUITextBlock labelText in labelTexts)
                {
                    labelText.Text = ToolBox.LimitString(labelText.ToolTip, labelText.Font, labelText.Rect.Width);
                }
            };

            button.SelectedColor = button.Color;
            refreshDisableTimer = DateTime.Now;

            //playstyle banners
            //TODO: expose to content package?
            PlayStyleBanners = new Sprite[Enum.GetValues(typeof(PlayStyle)).Length];

            XDocument playStylesDoc = XMLExtensions.TryLoadXml("Content/UI/Server/PlayStyleBanners/PlayStyleBanners.xml");

            XElement rootElement = playStylesDoc.Root;
            foreach (var element in rootElement.Elements())
            {
                if (Enum.TryParse(element.Name.LocalName, out PlayStyle playStyle))
                {
                    PlayStyleBanners[(int)playStyle] = new Sprite(element, lazyLoad: true);
                }
            }

            //recent and favorite servers
            ReadServerMemFromFile(recentServersFile, ref recentServers);
        }

        private void ReadServerMemFromFile(string file, ref List<ServerInfo> servers)
        {
            if (servers == null) { servers = new List<ServerInfo>(); }

            if (!File.Exists(file)) { return; }

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null) { return; }

            foreach (XElement element in doc.Root.Elements())
            {
                if (element.Name != "ServerInfo") { continue; }
                servers.Add(ServerInfo.FromXElement(element));
            }
        }

        private void WriteServerMemToFile(string file, List<ServerInfo> servers)
        {
            if (servers == null) { return; }

            XDocument doc = new XDocument();
            XElement rootElement = new XElement("servers");
            doc.Add(rootElement);

            foreach (ServerInfo info in servers)
            {
                rootElement.Add(info.ToXElement());
            }

            doc.Save(file);
        }

        public void AddToRecentServers(object endpoint, ServerSettings serverSettings)
        {
            UInt64 steamId = 0;
            string ip = ""; string port = "";
            if (endpoint is UInt64 id) { steamId = id; }
            else if (endpoint is string strEndpoint)
            {
                string[] address = strEndpoint.Split(':');
                if (address.Length == 1)
                {
                    ip = strEndpoint;
                    port = NetConfig.DefaultPort.ToString();
                }
                else
                {
                    ip = string.Join(":", address.Take(address.Length - 1));
                    port = address[address.Length - 1];
                }
            }
            

            ServerInfo info = recentServers.Find(s => s.IP == ip && s.Port == port && s.OwnerID == steamId);
            if (info == null)
            {
                info = new ServerInfo();
                recentServers.Add(info);
            }

            info.ServerName = serverSettings.ServerName;
            info.ServerMessage = serverSettings.ServerMessageText;
            info.OwnerID = steamId;
            info.LobbyID = SteamManager.LobbyID;
            info.IP = ip;
            info.Port = port;
            info.GameMode = GameMain.NetLobbyScreen.SelectedMode?.Identifier ?? "";
            info.GameStarted = Screen.Selected != GameMain.NetLobbyScreen;
            info.GameVersion = GameMain.Version.ToString();
            info.MaxPlayers = serverSettings.MaxPlayers;
            info.PlayStyle = PlayStyle.SomethingDifferent;
            info.RespondedToSteamQuery = true;
            info.UsingWhiteList = serverSettings.Whitelist.Enabled;
            info.TraitorsEnabled = serverSettings.TraitorsEnabled;
            info.SubSelectionMode = serverSettings.SubSelectionMode;
            info.ModeSelectionMode = serverSettings.ModeSelectionMode;
            info.VoipEnabled = serverSettings.VoiceChatEnabled;
            info.PlayerCount = GameMain.Client.ConnectedClients.Count;
            info.PingChecked = false;
            info.HasPassword = serverSettings.HasPassword;

            WriteServerMemToFile(recentServersFile, recentServers);
        }

        private void OnResolutionChanged()
        {
            menu.RectTransform.MinSize = new Point(GameMain.GraphicsHeight, 0);
            labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
            foreach (GUITextBlock labelText in labelTexts)
            {
                labelText.Text = ToolBox.LimitString(labelText.ToolTip, labelText.Font, labelText.Rect.Width);
            }
        }

        private bool SortList(GUIButton button, object obj)
        {
            if (!(obj is string sortBy)) { return false; }
            SortList(sortBy, toggle: true);
            return true;
        }

        private void SortList(string sortBy, bool toggle)
        {
            GUIButton button = labelHolder.GetChildByUserData(sortBy) as GUIButton;
            if (button == null) { return; }

            sortedBy = sortBy;

            var arrowUp = button.GetChildByUserData("arrowup");
            var arrowDown = button.GetChildByUserData("arrowdown");
            
            //disable arrow buttons in other labels
            foreach (var child in button.Parent.Children)
            {
                if (child != button)
                {
                    child.GetChildByUserData("arrowup").Visible = false;
                    child.GetChildByUserData("arrowdown").Visible = false;
                }
            }

            bool ascending = arrowUp.Visible;
            if (toggle)
            {
                ascending = !ascending;
            }

            arrowUp.Visible = ascending;
            arrowDown.Visible = !ascending;
            serverList.Content.RectTransform.SortChildren((c1, c2) => 
            {
                ServerInfo s1 = c1.GUIComponent.UserData as ServerInfo;
                ServerInfo s2 = c2.GUIComponent.UserData as ServerInfo;

                if (s1 == null && s2 == null)
                {
                    return 0;
                }
                else if (s1 == null)
                {
                    return ascending ? 1 : -1;
                }
                else if (s2 == null)
                {
                    return ascending ? -1 : 1;
                }

                switch (sortBy)
                {
                    case "ServerListCompatible":                        
                        bool? s1Compatible = NetworkMember.IsCompatible(GameMain.Version.ToString(), s1.GameVersion);
                        if (!s1.ContentPackageHashes.Any()) { s1Compatible = null; }
                        if (s1Compatible.HasValue) { s1Compatible = s1Compatible.Value && s1.ContentPackagesMatch(GameMain.SelectedPackages); };

                        bool? s2Compatible = NetworkMember.IsCompatible(GameMain.Version.ToString(), s2.GameVersion);
                        if (!s2.ContentPackageHashes.Any()) { s2Compatible = null; }
                        if (s2Compatible.HasValue) { s2Compatible = s2Compatible.Value && s2.ContentPackagesMatch(GameMain.SelectedPackages); };

                        //convert to int to make sorting easier
                        //1 Compatible
                        //0 Unknown 
                        //-1 Incompatible
                        int s1CompatibleInt = s1Compatible.HasValue ?
                            (s1Compatible.Value ? 1 : -1) :
                            0;
                        int s2CompatibleInt = s2Compatible.HasValue ?
                            (s2Compatible.Value ? 1 : -1) :
                            0;
                        return s2CompatibleInt.CompareTo(s1CompatibleInt) * (ascending ? 1 : -1);
                    case "ServerListHasPassword":
                        if (s1.HasPassword == s2.HasPassword) { return 0; }
                        return (s1.HasPassword ? 1 : -1) * (ascending ? 1 : -1);
                    case "ServerListName":
                        return string.Compare(s1.ServerName, s2.ServerName) * (ascending ? 1 : -1);
                    case "ServerListRoundStarted":
                        if (s1.GameStarted == s2.GameStarted) { return 0; }
                        return (s1.GameStarted ? 1 : -1) * (ascending ? 1 : -1);
                    case "ServerListPlayers":
                        return s2.PlayerCount.CompareTo(s1.PlayerCount) * (ascending ? 1 : -1);
                    case "ServerListPing":
                        return s2.Ping.CompareTo(s1.Ping) * (ascending ? 1 : -1);
                    default:
                        return 0;
                }
            });
        }
        
        public override void Select()
        {
            base.Select();
            RefreshServers();
        }

        public override void Deselect()
        {
            base.Deselect();
            if (SteamManager.IsInitialized && SteamManager.Instance.LobbyList != null)
            {
                SteamManager.Instance.LobbyList.OnLobbiesUpdated = null;
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            UpdateFriendsList();

            if (PlayerInput.LeftButtonClicked())
            {
                friendPopup = null;
                if (friendsDropdown != null && friendsDropdownButton != null &&
                    !friendsDropdown.Rect.Contains(PlayerInput.MousePosition) &&
                    !friendsDropdownButton.Rect.Contains(PlayerInput.MousePosition))
                {
                    friendsDropdown.Visible = false;
                }
            }
        }

        private void FilterServers()
        {
            serverList.Content.RemoveChild(serverList.Content.FindChild("noresults"));
            
            foreach (GUIComponent child in serverList.Content.Children)
            {
                if (!(child.UserData is ServerInfo)) continue;
                ServerInfo serverInfo = (ServerInfo)child.UserData;

                Version remoteVersion = null;
                if (!string.IsNullOrEmpty(serverInfo.GameVersion))
                {
                    Version.TryParse(serverInfo.GameVersion, out remoteVersion);
                }

                bool incompatible =
                    (!serverInfo.ContentPackageHashes.Any() && serverInfo.ContentPackagesMatch(GameMain.Config.SelectedContentPackages)) ||
                    (remoteVersion != null && !NetworkMember.IsCompatible(GameMain.Version, remoteVersion));

                child.Visible =
                    serverInfo.ServerName.ToLowerInvariant().Contains(searchBox.Text.ToLowerInvariant()) &&
                    (!filterPassword.Selected || !serverInfo.HasPassword) &&
                    (!filterIncompatible.Selected || !incompatible) &&
                    (!filterFull.Selected || serverInfo.PlayerCount < serverInfo.MaxPlayers) &&
                    (!filterEmpty.Selected || serverInfo.PlayerCount > 0);
            }

            if (serverList.Content.Children.All(c => !c.Visible))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoMatchingServers"))
                {
                    UserData = "noresults"
                };
            }

            serverList.UpdateScrollBarSize();
        }

        private void ShowDirectJoinPrompt()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("ServerListDirectJoin"), "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                relativeSize: new Vector2(0.25f, 0.2f), minSize: new Point(400, 150));

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.3f), msgBox.InnerFrame.RectTransform, Anchor.Center) { MinSize = new Point(0, 50) })
            {
                IgnoreLayoutGroups = true,
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), TextManager.Get("ServerIP"));
            var ipBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform));

            var okButton = msgBox.Buttons[0];
            okButton.Enabled = false;
            okButton.OnClicked = (btn, userdata) =>
            {
                JoinServer(ipBox.Text, "");
                msgBox.Close();
                return true;
            };

            var cancelButton = msgBox.Buttons[1];
            cancelButton.OnClicked = msgBox.Close;

            ipBox.OnTextChanged += (textBox, text) =>
            {
                okButton.Enabled = !string.IsNullOrEmpty(text);
                return true;
            };
        }

        private bool JoinFriend(GUIButton button, object userdata)
        {
            FriendInfo info = userdata as FriendInfo;

            if (info.InServer)
            {
                if (info.ConnectLobby != 0)
                {
                    GameMain.Instance.ConnectLobby = info.ConnectLobby;
                    GameMain.Instance.ConnectEndpoint = null;
                    GameMain.Instance.ConnectName = null;
                }
                else
                {
                    GameMain.Instance.ConnectLobby = 0;
                    GameMain.Instance.ConnectEndpoint = info.ConnectEndpoint;
                    GameMain.Instance.ConnectName = info.ConnectName;
                }
            }
            return false;
        }

        private bool OpenFriendPopup(GUIButton button, object userdata)
        {
            FriendInfo info = userdata as FriendInfo;

            if (info.InServer)
            {
                friendPopup = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas));
                var serverNameText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), friendPopup.RectTransform), info.ConnectName ?? "[Unnamed]");
                var joinButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), friendPopup.RectTransform, Anchor.TopRight), "Join")
                {
                    UserData = info
                };
                joinButton.OnClicked = JoinFriend;

                Vector2 frameDims = joinButton.Font.MeasureString(info.ConnectName ?? "[Unnamed]");
                frameDims.X /= 0.6f;
                frameDims.Y *= 1.5f;
                friendPopup.RectTransform.NonScaledSize = frameDims.ToPoint();
                friendPopup.RectTransform.RelativeOffset = Vector2.Zero;
                friendPopup.RectTransform.AbsoluteOffset = PlayerInput.MousePosition.ToPoint();
                friendPopup.RectTransform.RecalculateChildren(true);
                friendPopup.RectTransform.SetPosition(Anchor.TopLeft);
            }

            return false;
        }

        private void UpdateFriendsList()
        {
            if (!SteamManager.IsInitialized) { return; }

            if (friendsListUpdateTime > Timing.TotalTime) { return; }
            friendsListUpdateTime = Timing.TotalTime + 5.0;

            float prevDropdownScroll = friendsDropdown?.ScrollBar.BarScrollValue ?? 0.0f;

            if (friendsDropdown == null) {
                friendsDropdown = new GUIListBox(new RectTransform(Vector2.One, GUI.Canvas))
                {
                    OutlineColor = Color.Black,
                    Visible = false
                };
            }
            friendsDropdown.ClearChildren();

            Facepunch.Steamworks.Friends.AvatarSize avatarSize = Facepunch.Steamworks.Friends.AvatarSize.Large;
            if (friendsButtonHolder.RectTransform.Rect.Height <= 24)
            {
                avatarSize = Facepunch.Steamworks.Friends.AvatarSize.Small;
            }
            else if (friendsButtonHolder.RectTransform.Rect.Height <= 48)
            {
                avatarSize = Facepunch.Steamworks.Friends.AvatarSize.Medium;
            }

            SteamManager.Instance.Friends.Refresh();

            for (int i=friendsList.Count-1;i>=0;i--)
            {
                var friend = friendsList[i];
                if (!SteamManager.Instance.Friends.AllFriends.Any(g => g.Id == friend.SteamID && g.IsOnline))
                {
                    friend.Sprite?.Remove();
                    friendsList.RemoveAt(i);
                }
            }

            foreach (var friend in SteamManager.Instance.Friends.AllFriends)
            {
                if (!friend.IsOnline) { continue; }

                FriendInfo info = friendsList.Find(f => f.SteamID == friend.Id);
                if (info == null)
                {
                    info = new FriendInfo()
                    {
                        SteamID = friend.Id
                    };
                    friendsList.Insert(0, info);
                }

                if (info.Sprite == null)
                {
                    var avatarImage = friend.GetAvatar(avatarSize);
                    if (avatarImage != null)
                    {
                        //TODO: create an avatar atlas?
                        var avatarTexture = new Texture2D(GameMain.Instance.GraphicsDevice, avatarImage.Width, avatarImage.Height);
                        avatarTexture.SetData(avatarImage.Data);

                        info.Sprite = new Sprite(avatarTexture, null, null);
                    }
                }

                info.Name = friend.Name;

                info.ConnectName = null;
                info.ConnectEndpoint = null;
                info.ConnectLobby = 0;

                info.PlayingThisGame = friend.IsPlayingThisGame;

                if (friend.IsPlayingThisGame)
                {
                    info.Status = friend.GetRichPresence("status") ?? "";
                    string connectCommand = friend.GetRichPresence("connect") ?? "";

                    ToolBox.ParseConnectCommand(connectCommand.Split(' '), out info.ConnectName, out info.ConnectEndpoint, out info.ConnectLobby);
                }
                else
                {
                    info.Status = friend.IsPlaying ? "Playing other game" : "Not playing";
                }
            }

            friendsList.Sort((a, b) =>
            {
                if (a.InServer && !b.InServer) { return 1; }
                if (b.InServer && !a.InServer) { return -1; }
                return 0;
            });

            Color mainColor = new Color(58, 93, 43);
            Color hoverColor = new Color(53, 72, 76);
            Color pressColor = new Color(255, 255, 255);

            friendsButtonHolder.ClearChildren();

            int buttonCount = 0;

            for (int i = 0; i < friendsList.Count; i++)
            {
                var friend = friendsList[i];
                buttonCount++;

                if (buttonCount <= 5)
                {
                    if (friend.InServer)
                    {
                        mainColor = new Color(58, 93, 43);
                    }
                    else
                    {
                        mainColor = friend.PlayingThisGame ? new Color(58, 93, 43) : new Color(83, 164, 196);
                    }

                    var guiButton = new GUIButton(new RectTransform(Vector2.One * 0.6f, friendsButtonHolder.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: null)
                    {
                        Color = mainColor,
                        HoverColor = hoverColor,
                        SelectedColor = hoverColor,
                        PressedColor = pressColor,
                        OutlineColor = Color.Transparent,
                        UserData = friend,
                        OnClicked = OpenFriendPopup
                    };

                    if (friend.Sprite != null)
                    {
                        var avatarHolder = new GUILayoutGroup(new RectTransform(Vector2.One * 0.925f, guiButton.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.025f, 0.025f) });

                        var guiImage = new GUIImage(new RectTransform(Vector2.One, avatarHolder.RectTransform), friend.Sprite, null, true);
                        guiImage.ToolTip = friend.Name + "\n" + friend.Status;
                    }
                }

                var friendFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.167f), friendsDropdown.Content.RectTransform), style: null)
                {
                    Color = new Color(27, 36, 38),
                    HoverColor = new Color(44, 59, 62),
                    SelectedColor = new Color(27, 36, 38),
                    PressedColor = pressColor,
                    OutlineColor = Color.Black
                };
                var guiImage2TheSequel = new GUIImage(new RectTransform(Vector2.One * 0.9f, friendFrame.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight) { RelativeOffset = new Vector2(0.02f, 0.02f) } , friend.Sprite, null, true);

                var textBlock = new GUITextBlock(new RectTransform(Vector2.One * 0.8f, friendFrame.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight) { RelativeOffset = new Vector2(1.0f / 7.7f, 0.0f) }, friend.Name + "\n" + friend.Status)
                {
                    TextColor = mainColor,
                    Font = GUI.SmallFont
                };

                if (friend.InServer)
                {
                    var joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.6f), friendFrame.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.05f, 0.0f) }, "Join", style: null)
                    {
                        Color = new Color(43, 71, 93),
                        TextColor = new Color(90, 190, 249),
                        HoverColor = new Color(53, 88, 115),
                        SelectedColor = new Color(53, 88, 115),
                        PressedColor = pressColor,
                        UserData = friend
                    };
                    joinButton.OnClicked = JoinFriend;
                }
            }

            mainColor = new Color(73, 98, 103);

            if (friendsList.Count > 0)
            {
                friendsDropdownButton = new GUIButton(new RectTransform(Vector2.One * 0.6f, friendsButtonHolder.RectTransform, Anchor.BottomRight, Pivot.BottomRight, scaleBasis: ScaleBasis.BothHeight), "\u2022 \u2022 \u2022", style: null)
                {
                    Color = mainColor,
                    SelectedColor = hoverColor,
                    HoverColor = hoverColor,
                    OutlineColor = new Color(27, 36, 38),
                    PressedColor = hoverColor,
                    TextColor = Color.White,
                    Font = GUI.ObjectiveNameFont,
                    OnClicked = (button, udt) =>
                    {
                        friendsDropdown.RectTransform.NonScaledSize = new Point(friendsButtonHolder.Rect.Height * 5, friendsButtonHolder.Rect.Height * 4);
                        friendsDropdown.RectTransform.RelativeOffset = new Vector2(0.155f, 0.215f);
                        friendsDropdown.RectTransform.RecalculateChildren(true);
                        friendsDropdown.RectTransform.SetPosition(Anchor.TopRight);

                        friendsDropdown.Visible = !friendsDropdown.Visible;
                        return false;
                    }
                };
            }
            else
            {
                friendsDropdownButton = null;
                friendsDropdown.Visible = false;
            }

            friendsDropdown.RectTransform.NonScaledSize = new Point(friendsButtonHolder.Rect.Height * 5, friendsButtonHolder.Rect.Height * 4);
            friendsDropdown.RectTransform.RelativeOffset = new Vector2(0.155f, 0.215f);
            friendsDropdown.RectTransform.RecalculateChildren(true);
            friendsDropdown.RectTransform.SetPosition(Anchor.TopRight);

            friendsDropdown.ScrollBar.BarScrollValue = prevDropdownScroll;
        }

        private void RefreshServers()
        {
            if (waitingForRefresh) { return; }

            friendsListUpdateTime = Timing.TotalTime - 1.0;
            UpdateFriendsList();

            serverList.ClearChildren();
            serverPreview.ClearChildren();
            joinButton.Enabled = false;
            selectedServer = null;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                TextManager.Get("RefreshingServerList"), textAlignment: Alignment.Center)
            {
                CanBeFocused = false
            };

            CoroutineManager.StartCoroutine(WaitForRefresh());
        }

        private IEnumerable<object> WaitForRefresh()
        {
            waitingForRefresh = true;
            if (refreshDisableTimer > DateTime.Now)
            {
                yield return new WaitForSeconds((float)(refreshDisableTimer - DateTime.Now).TotalSeconds);
            }
            
            if (GameMain.Config.UseSteamMatchmaking)
            {
                serverList.ClearChildren();
                if (!SteamManager.GetServers(AddToServerList, UpdateServerInfo, ServerQueryFinished))
                {
                    serverList.ClearChildren();
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                        TextManager.Get("ServerListNoSteamConnection"), textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };
                }
            }
            else
            {
                CoroutineManager.StartCoroutine(SendMasterServerRequest());
                waitingForRefresh = false;
            }

            refreshDisableTimer = DateTime.Now + AllowedRefreshInterval;

            //TODO: remove, recent servers list needs some processing first
            foreach (ServerInfo info in recentServers)
            {
                DebugConsole.NewMessage(info.ServerName + " " + info.ServerMessage);
                AddToServerList(info);
            }

            yield return CoroutineStatus.Success;
        }

        private void UpdateServerList(string masterServerData)
        {
            serverList.ClearChildren();
                        
            if (masterServerData.Substring(0, 5).ToLowerInvariant() == "error")
            {
                DebugConsole.ThrowError("Error while connecting to master server (" + masterServerData + ")!");
                return;
            }

            string[] lines = masterServerData.Split('\n');
            List<ServerInfo> serverInfos = new List<ServerInfo>();
            for (int i = 0; i < lines.Length; i++)
            {
                string[] arguments = lines[i].Split('|');
                if (arguments.Length < 3) continue;

                string ip =                 arguments[0];
                string port =               arguments[1];
                string serverName =         arguments[2];
                bool gameStarted =          arguments.Length > 3 && arguments[3] == "1";
                string currPlayersStr =     arguments.Length > 4 ? arguments[4] : "";
                string maxPlayersStr =      arguments.Length > 5 ? arguments[5] : "";
                bool hasPassWord =          arguments.Length > 6 && arguments[6] == "1";
                string gameVersion =        arguments.Length > 7 ? arguments[7] : "";
                string contentPackageNames = arguments.Length > 8 ? arguments[8] : "";
                string contentPackageHashes = arguments.Length > 9 ? arguments[9] : "";

                int.TryParse(currPlayersStr, out int playerCount);
                int.TryParse(maxPlayersStr, out int maxPlayers);

                var serverInfo = new ServerInfo()
                {
                    IP = ip,
                    Port = port,
                    ServerName = serverName,
                    GameStarted = gameStarted,
                    PlayerCount = playerCount,
                    MaxPlayers = maxPlayers,
                    HasPassword = hasPassWord,
                    GameVersion = gameVersion
                };
                foreach (string contentPackageName in contentPackageNames.Split(','))
                {
                    if (string.IsNullOrEmpty(contentPackageName)) continue;
                    serverInfo.ContentPackageNames.Add(contentPackageName);
                }
                foreach (string contentPackageHash in contentPackageHashes.Split(','))
                {
                    if (string.IsNullOrEmpty(contentPackageHash)) continue;
                    serverInfo.ContentPackageHashes.Add(contentPackageHash);
                }

                serverInfos.Add(serverInfo);
            }

            serverList.Content.ClearChildren();
            if (serverInfos.Count() == 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                    TextManager.Get("NoServers"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
                return;
            }
            foreach (ServerInfo serverInfo in serverInfos)
            {
                AddToServerList(serverInfo);
            }
        }

        private void AddToServerList(ServerInfo serverInfo)
        {
            var serverFrame = serverList.Content.FindChild(d => (d.UserData is ServerInfo info) &&
                                                                (info.LobbyID==serverInfo.LobbyID && info.IP==serverInfo.IP && info.Port==serverInfo.Port));

            if (serverFrame == null)
            {
                serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 35) },
                style: "InnerFrame", color: Color.White * 0.5f)
                {
                    UserData = serverInfo
                };
                new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 1.0f), serverFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    //RelativeSpacing = 0.02f
                };
            }
            serverFrame.UserData = serverInfo;
            
            UpdateServerInfo(serverInfo);

            SortList(sortedBy, toggle: false);
            FilterServers();
        }

        private void UpdateServerInfo(ServerInfo serverInfo)
        {
            var serverFrame = serverList.Content.FindChild(serverInfo);
            if (serverFrame == null) return;

            var serverContent = serverFrame.Children.First() as GUILayoutGroup;
            serverContent.ClearChildren();

            var compatibleBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[0], 0.9f), serverContent.RectTransform, Anchor.Center), label: "")
            {
                Enabled = false,
                Selected =
                    serverInfo.GameVersion == GameMain.Version.ToString() &&
                    serverInfo.ContentPackagesMatch(GameMain.SelectedPackages),
                UserData = "compatible"
            };
            
            var passwordBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[1], 0.5f), serverContent.RectTransform, Anchor.Center), label: "", style: "GUIServerListPasswordTickBox")
            {
				ToolTip = TextManager.Get((serverInfo.HasPassword) ? "ServerListHasPassword" : "FilterPassword"),
				Selected = serverInfo.HasPassword,
                Enabled = false,
                UserData = "password"
            };

			var serverName = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[2] * 1.1f, 1.0f), serverContent.RectTransform), serverInfo.ServerName, style: "GUIServerListTextBox");

            new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[3], 0.9f), serverContent.RectTransform, Anchor.Center), label: "")
            {
				ToolTip = TextManager.Get((serverInfo.GameStarted) ? "ServerListRoundStarted" : "ServerListRoundNotStarted"),
				Selected = serverInfo.GameStarted,
				Enabled = false
			};

            var serverPlayers = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[4], 1.0f), serverContent.RectTransform),
                serverInfo.PlayerCount + "/" + serverInfo.MaxPlayers, style: "GUIServerListTextBox", textAlignment: Alignment.Right)
            {
                ToolTip = TextManager.Get("ServerListPlayers")
            };

            var serverPingText = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[5], 1.0f), serverContent.RectTransform), "?", 
                style: "GUIServerListTextBox", textColor: Color.White * 0.5f, textAlignment: Alignment.Right)
            {
                ToolTip = TextManager.Get("ServerListPing")
            };

            if (serverInfo.PingChecked)
            {
                serverPingText.Text = serverInfo.Ping > -1 ? serverInfo.Ping.ToString() : "?";
                serverPingText.TextColor = GetPingTextColor(serverInfo.Ping);
            }
            else if (!string.IsNullOrEmpty(serverInfo.IP))
            {
                try
                {
                    GetServerPing(serverInfo, serverPingText);
                }
                catch (NullReferenceException ex)
                {
                    DebugConsole.ThrowError("Ping is null", ex);
                }
            }

            if (GameMain.Config.UseSteamMatchmaking && serverInfo.RespondedToSteamQuery.HasValue && serverInfo.RespondedToSteamQuery.Value == false)
            {
                string toolTip = TextManager.Get("ServerListNoSteamQueryResponse");
                compatibleBox.Selected = false;
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);
                serverName.TextColor *= 0.8f;
                serverPlayers.TextColor *= 0.8f;
            }
            else if (string.IsNullOrEmpty(serverInfo.GameVersion) || !serverInfo.ContentPackageHashes.Any())
            {
                compatibleBox.Selected = false;
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), compatibleBox.Box.RectTransform, Anchor.Center), " ? ", Color.Yellow * 0.85f, textAlignment: Alignment.Center)
                {
                    ToolTip = TextManager.Get(string.IsNullOrEmpty(serverInfo.GameVersion) ?
                        "ServerListUnknownVersion" :
                        "ServerListUnknownContentPackage")
                };
            }
            else if (!compatibleBox.Selected)
            {
                string toolTip = "";
                if (serverInfo.GameVersion != GameMain.Version.ToString())
                    toolTip = TextManager.GetWithVariable("ServerListIncompatibleVersion", "[version]", serverInfo.GameVersion);

                for (int i = 0; i < serverInfo.ContentPackageNames.Count; i++)
                {
                    if (!GameMain.SelectedPackages.Any(cp => cp.MD5hash.Hash == serverInfo.ContentPackageHashes[i]))
                    {
                        if (toolTip != "") toolTip += "\n";
                        toolTip += TextManager.GetWithVariables("ServerListIncompatibleContentPackage", new string[2] { "[contentpackage]", "[hash]" },
                            new string[2] { serverInfo.ContentPackageNames[i], Md5Hash.GetShortHash(serverInfo.ContentPackageHashes[i]) });
                    }
                }
                
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);

                serverName.TextColor *= 0.5f;
                serverPlayers.TextColor *= 0.5f;
            }

            serverContent.Recalculate();
            SortList(sortedBy, toggle: false);
            FilterServers();
        }

        private void ServerQueryFinished()
        {
            if (serverList.Content.Children.All(c => !c.Visible))
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), serverList.Content.RectTransform),
                    TextManager.Get("NoMatchingServers"))
                {
                    UserData = "noresults"
                };
            }
            waitingForRefresh = false;
        }

        private IEnumerable<object> SendMasterServerRequest()
        {
            RestClient client = null;
            try
            {
                client = new RestClient(NetConfig.MasterServerUrl);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while connecting to master server", e);                
            }

            if (client == null) yield return CoroutineStatus.Success;

            var request = new RestRequest("masterserver2.php", Method.GET);
            request.AddParameter("gamename", "barotrauma");
            request.AddParameter("action", "listservers");
            
            // execute the request
            masterServerResponded = false;
            var restRequestHandle = client.ExecuteAsync(request, response => MasterServerCallBack(response));

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 8);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                {
                    serverList.ClearChildren();
                    restRequestHandle.Abort();
                    new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.Get("MasterServerTimeOutError"));
                    yield return CoroutineStatus.Success;
                }
                yield return CoroutineStatus.Running;
            }

            if (masterServerResponse.ErrorException != null)
            {
                serverList.ClearChildren();
                new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), TextManager.GetWithVariable("MasterServerErrorException", "[error]", masterServerResponse.ErrorException.ToString()));
            }
            else if (masterServerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                serverList.ClearChildren();
                
                switch (masterServerResponse.StatusCode)
                {
                    case System.Net.HttpStatusCode.NotFound:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                           TextManager.GetWithVariable("MasterServerError404", "[masterserverurl]", NetConfig.MasterServerUrl));
                        break;
                    case System.Net.HttpStatusCode.ServiceUnavailable:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"), 
                            TextManager.Get("MasterServerErrorUnavailable"));
                        break;
                    default:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                            TextManager.GetWithVariables("MasterServerErrorDefault", new string[2] { "[statuscode]", "[statusdescription]" }, 
                            new string[2] { masterServerResponse.StatusCode.ToString(), masterServerResponse.StatusDescription }));
                        break;
                }
                
            }
            else
            {
                UpdateServerList(masterServerResponse.Content);
            }

            yield return CoroutineStatus.Success;

        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponse = response;
            masterServerResponded = true;
        }

        private bool JoinServer(string ip, string serverName)
        {
            if (string.IsNullOrWhiteSpace(clientNameBox.Text))
            {
                clientNameBox.Flash();
                return false;
            }

            GameMain.Config.DefaultPlayerName = clientNameBox.Text;
            GameMain.Config.SaveNewPlayerConfig();

            CoroutineManager.StartCoroutine(ConnectToServer(ip, serverName));

            return true;
        }
        
        private IEnumerable<object> ConnectToServer(string endpoint, string serverName)
        {
            string serverIP = null;
            UInt64 serverSteamID = SteamManager.SteamIDStringToUInt64(endpoint);
            if (serverSteamID == 0) { serverIP = endpoint; }

#if !DEBUG
            try
            {
#endif
                GameMain.Client = new GameClient(clientNameBox.Text, serverIP, serverSteamID, serverName);
#if !DEBUG
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start the client", e);
            }
#endif

            yield return CoroutineStatus.Success;
        }

        public void GetServerPing(ServerInfo serverInfo, GUITextBlock serverPingText)
        {
            serverInfo.PingChecked = false;
            serverInfo.Ping = -1;

            var pingThread = new Thread(() => { PingServer(serverInfo, 1000); })
            {
                IsBackground = true
            };
            pingThread.Start();

            CoroutineManager.StartCoroutine(UpdateServerPingText(serverInfo, serverPingText, 1000));
        }

        private IEnumerable<object> UpdateServerPingText(ServerInfo serverInfo, GUITextBlock serverPingText, int timeOut)
        {
			DateTime timeOutTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, milliseconds: timeOut);
            while (DateTime.Now < timeOutTime)
            {
                if (serverInfo.PingChecked)
                {
                    if (serverInfo.Ping != -1)
                    {
                        serverPingText.TextColor = GetPingTextColor(serverInfo.Ping);
					}
                    serverPingText.Text = serverInfo.Ping > -1 ? serverInfo.Ping.ToString() : "?";
                    yield return CoroutineStatus.Success;
                }

                yield return CoroutineStatus.Running;
            }
            yield return CoroutineStatus.Success;
        }

        private Color GetPingTextColor(int ping)
        {
            if (ping < 0) { return Color.DarkRed; }
            return ToolBox.GradientLerp(ping / 200.0f, Color.LightGreen, Color.Yellow * 0.8f, Color.Red * 0.75f);
        }

        public void PingServer(ServerInfo serverInfo, int timeOut)
        {
            if (string.IsNullOrWhiteSpace(serverInfo?.IP))
            {
                serverInfo.PingChecked = true;
                serverInfo.Ping = -1;
                return;
            }

            long rtt = -1;
            IPAddress address = null;
            IPAddress.TryParse(serverInfo.IP, out address);
            if (address != null)
            {
                //don't attempt to ping if the address is IPv6 and it's not supported
                if (address.AddressFamily != AddressFamily.InterNetworkV6 || Socket.OSSupportsIPv6)
                {
                    Ping ping = new Ping();
                    byte[] buffer = new byte[32];
                    try
                    {
                        PingReply pingReply = ping.Send(address, timeOut, buffer, new PingOptions(128, true));

                        if (pingReply != null)
                        {
                            switch (pingReply.Status)
                            {
                                case IPStatus.Success:
                                    rtt = pingReply.RoundtripTime;
                                    break;
                                default:
                                    rtt = -1;
                                    break;
                            }
                        }
                    }
                    catch (PingException ex)
                    {
                        string errorMsg = "Failed to ping a server (" + serverInfo.ServerName + ", " + serverInfo.IP + ") - " + (ex?.InnerException?.Message ?? ex.Message);
                        GameAnalyticsManager.AddErrorEventOnce("ServerListScreen.PingServer:PingException" + serverInfo.IP, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, errorMsg);
#if DEBUG
                        DebugConsole.NewMessage(errorMsg, Color.Red);
#endif
                    }
                }
            }

            serverInfo.PingChecked = true;
            serverInfo.Ping = (int)rtt;
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();

            friendPopup?.AddToGUIUpdateList();

            friendsDropdown?.AddToGUIUpdateList();
        }
        
    }
}
