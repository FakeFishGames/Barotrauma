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
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        //how often the client is allowed to refresh servers
        private TimeSpan AllowedRefreshInterval = new TimeSpan(0, 0, 3);

        private GUIFrame menu;

        private GUIListBox serverList;
        private GUIFrame serverPreview;

        private GUIButton joinButton;
        private ServerInfo selectedServer;

        //friends list
        private GUILayoutGroup friendsButtonHolder;

        private GUIButton friendsDropdownButton;
        private GUIListBox friendsDropdown;

        private class FriendInfo
        {
            public UInt64 SteamID;
            public string Name;
            public Sprite Sprite;
            public string StatusText;
            public bool PlayingThisGame;
            public bool PlayingAnotherGame;
            public string ConnectName;
            public string ConnectEndpoint;
            public UInt64 ConnectLobby;

            public bool InServer
            {
                get
                {
                    return PlayingThisGame && !string.IsNullOrWhiteSpace(StatusText) && (!string.IsNullOrWhiteSpace(ConnectEndpoint) || ConnectLobby != 0);
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

        private readonly HashSet<string> activePings = new HashSet<string>();

        private enum ServerListTab
        {
            All = 0,
            Favorites = 1,
            Recent = 2
        };
        private ServerListTab selectedTab;
        private ServerListTab SelectedTab
        {
            get { return selectedTab; }
            set
            {
                if (selectedTab == value) { return; }
                var tabVals = Enum.GetValues(typeof(ServerListTab));
                for (int i = 0; i < tabVals.Length; i++)
                {
                    tabButtons[i].Selected = false;
                }
                tabButtons[(int)value].Selected = true;
                selectedTab = value;
                FilterServers();
            }
        }
        private GUIButton[] tabButtons;

        private static Sprite[] playStyleBanners;
        //server playstyle and tags
        public static Sprite[] PlayStyleBanners
        {
            get
            {
                if (playStyleBanners == null)
                {
                    LoadPlayStyleBanners();
                }
                return playStyleBanners;
            }
        }
        public static Color[] PlayStyleColors
        {
            get; private set;
        }

        public GUITextBox ClientNameBox { get; private set; }

        public static Dictionary<string, Sprite> PlayStyleIcons
        {
            get; private set;
        }
        public static Dictionary<string, Color> PlayStyleIconColors
        {
            get; private set;
        }

        private bool masterServerResponded;
        private IRestResponse masterServerResponse;
        
        private readonly float[] columnRelativeWidth = new float[] { 0.1f, 0.1f, 0.7f, 0.12f, 0.08f, 0.08f };
        private readonly string[] columnLabel = new string[] { "ServerListCompatible", "ServerListHasPassword", "ServerListName", "ServerListRoundStarted", "ServerListPlayers", "ServerListPing" };
        
        private GUILayoutGroup labelHolder;
        private readonly List<GUITextBlock> labelTexts = new List<GUITextBlock>();

        //filters
        private GUITextBox searchBox;
        private GUITickBox filterSameVersion;
        private GUITickBox filterPassword;
        private GUITickBox filterIncompatible;
        private GUITickBox filterFull;
        private GUITickBox filterEmpty;
        private GUITickBox filterWhitelisted;
        private GUITickBox filterFriendlyFire;
        private GUITickBox filterKarma;
        private GUITickBox filterTraitor;
        private GUITickBox filterModded;
        private GUITickBox filterVoip;
        private List<GUITickBox> playStyleTickBoxes;
        private List<GUITickBox> gameModeTickBoxes;

        private string sortedBy;
        
        private GUIButton serverPreviewToggleButton;

        //a timer for preventing the client from spamming the refresh button faster than AllowedRefreshInterval
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;

        private bool steamPingInfoReady;

        private const float sidebarWidth = 0.2f;
        public ServerListScreen()
        {
            GameMain.Instance.OnResolutionChanged += CreateUI;
            CreateUI();
        }

        private void CreateUI()
        {
            menu = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.85f), GUI.Canvas, Anchor.Center) { MinSize = new Point(GameMain.GraphicsHeight, 0) });

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.98f), menu.RectTransform, Anchor.Center))
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
                AutoScaleHorizontal = true
            };

            var infoHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), isHorizontal: true, Anchor.BottomLeft) { RelativeSpacing = 0.01f,  Stretch = false };

            var clientNameHolder = new GUILayoutGroup(new RectTransform(new Vector2(sidebarWidth, 1.0f), infoHolder.RectTransform)) { RelativeSpacing = 0.05f };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), clientNameHolder.RectTransform), TextManager.Get("YourName"), font: GUI.SubHeadingFont);
            ClientNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), clientNameHolder.RectTransform), "")
            {
                Text = GameMain.Config.PlayerName,
                MaxTextLength = Client.MaxNameLength,
                OverflowClip = true
            };

            if (string.IsNullOrEmpty(ClientNameBox.Text))
            {
                ClientNameBox.Text = SteamManager.GetUsername();
            }
            ClientNameBox.OnTextChanged += (textbox, text) =>
            {
                GameMain.Config.PlayerName = text;
                return true;
            };

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - sidebarWidth - infoHolder.RelativeSpacing, 0.5f), infoHolder.RectTransform), isHorizontal: true);

            var tabVals = Enum.GetValues(typeof(ServerListTab));
            tabButtons = new GUIButton[tabVals.Length];
            foreach (ServerListTab tab in tabVals)
            {
                tabButtons[(int)tab] = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), tabButtonHolder.RectTransform), 
                    TextManager.Get("ServerListTab." + tab.ToString()), style: "GUITabButton")
                {
                    OnClicked = (btn, usrdat) =>
                    {
                        SelectedTab = tab;
                        return false;
                    }
                };
            }

            var friendsButtonFrame = new GUIFrame(new RectTransform(new Vector2(0.31f, 2.0f), tabButtonHolder.RectTransform, Anchor.BottomRight), style: "InnerFrame")
            {
                IgnoreLayoutGroups = true
            };

            friendsButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.9f), friendsButtonFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopLeft) { RelativeSpacing = 0.01f, IsHorizontal = true };
            friendsList = new List<FriendInfo>();

            //-------------------------------------------------------------------------------------
            // Bottom row
            //-------------------------------------------------------------------------------------

            var bottomRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f - topRow.RectTransform.RelativeSize.Y),
                paddedFrame.RectTransform, Anchor.CenterRight))
            {
                Stretch = true
            };

            var serverListHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), bottomRow.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                OutlineColor = Color.Black
            };

            GUILayoutGroup serverListContainer = null;
            GUIFrame filtersHolder = null;
            GUIButton filterToggle = null;

            void RecalculateHolder()
            {
                float listContainerSubtract = filtersHolder.Visible ? sidebarWidth : 0.0f;
                listContainerSubtract += serverPreview.Visible ? sidebarWidth : 0.0f;

                float toggleButtonsSubtract = 1.1f * filterToggle.Rect.Width / serverListHolder.Rect.Width;
                listContainerSubtract += filterToggle.Visible ? toggleButtonsSubtract : 0.0f;
                listContainerSubtract += serverPreviewToggleButton.Visible ? toggleButtonsSubtract : 0.0f;

                serverListContainer.RectTransform.RelativeSize = new Vector2(1.0f - listContainerSubtract, 1.0f);
                serverListHolder.Recalculate();
            }

            // filters -------------------------------------------

            filtersHolder = new GUIFrame(new RectTransform(new Vector2(sidebarWidth, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black
            };

            float elementHeight = 0.05f;
            var filterTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), filtersHolder.RectTransform), TextManager.Get("FilterServers"), font: GUI.SubHeadingFont)
            {
                Padding = Vector4.Zero,
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };

            var searchHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, elementHeight), filtersHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, elementHeight) }, isHorizontal: true) { Stretch = true };

            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), searchHolder.RectTransform), TextManager.Get("Search") + "...");
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), searchHolder.RectTransform), "");
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };

            var filters = new GUIListBox(new RectTransform(new Vector2(0.98f, 1.0f - elementHeight * 2), filtersHolder.RectTransform, Anchor.BottomLeft))
            {
                ScrollBarVisible = true,
                Spacing = (int)(5 * GUI.Scale)
            };

            filterToggle = new GUIButton(new RectTransform(new Vector2(0.01f, 1.0f), serverListHolder.RectTransform) 
                { MinSize = new Point(20, 0), MaxSize = new Point(int.MaxValue, (int)(150 * GUI.Scale)) }, 
                style: "UIToggleButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    filtersHolder.RectTransform.RelativeSize = new Vector2(sidebarWidth, 1.0f);
                    filtersHolder.Visible = !filtersHolder.Visible;
                    filtersHolder.IgnoreLayoutGroups = !filtersHolder.Visible;

                    RecalculateHolder();

                    btn.Children.ForEach(c => c.SpriteEffects = !filtersHolder.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };
            filterToggle.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);

            List<GUITextBlock> filterTextList = new List<GUITextBlock>();

            filterSameVersion = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterSameVersion"))
            {
                ToolTip = TextManager.Get("FilterSameVersion"),
                Selected = true,
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterSameVersion.TextBlock);

            filterPassword = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterPassword"))
            {
                ToolTip = TextManager.Get("FilterPassword"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterPassword.TextBlock);

            filterIncompatible = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterIncompatibleServers"))
            {
                ToolTip = TextManager.Get("FilterIncompatibleServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterIncompatible.TextBlock);

            filterFull = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterFullServers"))
            {
                ToolTip = TextManager.Get("FilterFullServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterFull.TextBlock);

            filterEmpty = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterEmptyServers"))
            {
                ToolTip = TextManager.Get("FilterEmptyServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterEmpty.TextBlock);

            filterWhitelisted = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterWhitelistedServers"))
            {
                ToolTip = TextManager.Get("FilterWhitelistedServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterWhitelisted.TextBlock);

            // Filter Tags
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("servertags"), font: GUI.SubHeadingFont)
            {
                CanBeFocused = false
            };

            filterKarma = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag.karma.true"))
            {
                ToolTip = TextManager.Get("servertag.karma.true"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterKarma.TextBlock);

            filterTraitor = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag.traitors.true"))
            {
                ToolTip = TextManager.Get("servertag.traitors.true"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterTraitor.TextBlock);

            filterFriendlyFire = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag.friendlyfire.false"))
            {
                ToolTip = TextManager.Get("servertag.friendlyfire.false"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterFriendlyFire.TextBlock);

            filterVoip = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag.voip.false"))
            {
                ToolTip = TextManager.Get("servertag.voip.false"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterVoip.TextBlock);
            
            filterModded = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag.modded.true"))
            {
                ToolTip = TextManager.Get("servertag.modded.true"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTextList.Add(filterModded.TextBlock);

            // Play Style Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("ServerSettingsPlayStyle"), font: GUI.SubHeadingFont)
            {
                CanBeFocused = false
            };

            playStyleTickBoxes = new List<GUITickBox>();
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag." + playStyle))
                {
                    ToolTip = TextManager.Get("servertag." + playStyle),
                    Selected = true,
                    OnSelected = (tickBox) => { FilterServers(); return true; },
                    UserData = playStyle
                };
                playStyleTickBoxes.Add(selectionTick);
                filterTextList.Add(selectionTick.TextBlock);
            }

            // Game mode Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("gamemode")) { CanBeFocused = false };

            gameModeTickBoxes = new List<GUITickBox>();
            foreach (GameModePreset mode in GameModePreset.List)
            {
                if (mode.IsSinglePlayer) continue;

                var selectionTick = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), mode.Name)
                {
                    ToolTip = mode.Name,
                    Selected = true,
                    OnSelected = (tickBox) => { FilterServers(); return true; },
                    UserData = mode.Identifier
                };
                gameModeTickBoxes.Add(selectionTick);
                filterTextList.Add(selectionTick.TextBlock);
            }

            filters.Content.RectTransform.SizeChanged += () =>
            {
                filters.Content.RectTransform.RecalculateChildren(true, true);
                filterTextList.ForEach(t => t.Text = t.ToolTip);
                GUITextBlock.AutoScaleAndNormalize(filterTextList, defaultScale: 1.0f);
                if (filterTextList[0].TextScale < 0.8f)
                {
                    filterTextList.ForEach(t => t.TextScale = 1.0f);
                    filterTextList.ForEach(t => t.Text = ToolBox.LimitString(t.Text, t.Font, (int)(filters.Content.Rect.Width * 0.8f)));
                }
            };

            // server list ---------------------------------------------------------------------

            serverListContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), serverListHolder.RectTransform)) { Stretch = true };

            labelHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.99f, 0.05f), serverListContainer.RectTransform) { MinSize = new Point(0, 15) },
                isHorizontal: true, childAnchor: Anchor.BottomLeft)
            {
                Stretch = true
            };
            
            for (int i = 0; i < columnRelativeWidth.Length; i++)
            {
                var btn = new GUIButton(new RectTransform(new Vector2(columnRelativeWidth[i], 1.0f), labelHolder.RectTransform),
                    text: TextManager.Get(columnLabel[i]), textAlignment: Alignment.Center, style: "GUIButtonSmall")
                {
                    ToolTip = TextManager.Get(columnLabel[i]),
                    ForceUpperCase = true,
                    UserData = columnLabel[i],
                    OnClicked = SortList
                };
                btn.Color *= 0.5f;
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
                            serverPreview.RectTransform.RelativeSize = new Vector2(sidebarWidth, 1.0f);
                            serverPreviewToggleButton.Visible = true;
                            serverPreviewToggleButton.IgnoreLayoutGroups = false;
                            serverPreview.Visible = true;
                            serverPreview.IgnoreLayoutGroups = false;
                            RecalculateHolder();
                        }
                        serverInfo.CreatePreviewWindow(serverPreview);
                        btn.Children.ForEach(c => c.SpriteEffects = serverPreview.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    }
                    return true;
                }
            };

            //server preview panel --------------------------------------------------

            serverPreviewToggleButton = new GUIButton(new RectTransform(new Vector2(0.01f, 1.0f), serverListHolder.RectTransform) 
                { MinSize = new Point(20, 0), MaxSize = new Point(int.MaxValue, (int)(150 * GUI.Scale)) }, 
                style: "UIToggleButton")
            {
                Visible = false,
                OnClicked = (btn, userdata) =>
                {
                    serverPreview.RectTransform.RelativeSize = new Vector2(0.2f, 1.0f);
                    serverPreview.Visible = !serverPreview.Visible;
                    serverPreview.IgnoreLayoutGroups = !serverPreview.Visible;

                    RecalculateHolder();

                    btn.Children.ForEach(c => c.SpriteEffects = serverPreview.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };

            serverPreview = new GUIFrame(new RectTransform(new Vector2(sidebarWidth, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
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
                TextManager.Get("Back"))
            {
                OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu
            };

            new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListRefresh"))
            {
				OnClicked = (btn, userdata) => { RefreshServers(); return true; }
			};

            var directJoinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("serverlistdirectjoin"))
            {
                OnClicked = (btn, userdata) => { ShowDirectJoinPrompt(); return true; }
            };

            joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListJoin"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (selectedServer != null)
                    {
                        if (!string.IsNullOrWhiteSpace(selectedServer.IP) && !string.IsNullOrWhiteSpace(selectedServer.Port) && int.TryParse(selectedServer.Port, out _))
                        {
                            JoinServer(selectedServer.IP + ":" + selectedServer.Port, selectedServer.ServerName);
                        }
                        else if (selectedServer.LobbyID != 0)
                        {
                            Steam.SteamManager.JoinLobby(selectedServer.LobbyID, true);
                        }
                        else
                        {
                            new GUIMessageBox("", TextManager.Get("ServerOffline"));
                            return false;
                        }
                    }
                    return true;
                },
                Enabled = false
            };

            buttonContainer.RectTransform.MinSize = new Point(0, (int)(buttonContainer.RectTransform.Children.Max(c => c.MinSize.Y) * 1.2f));

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
                RecalculateHolder();
            };

            button.SelectedColor = button.Color;
            refreshDisableTimer = DateTime.Now;
            
            //recent and favorite servers
            ReadServerMemFromFile(recentServersFile, ref recentServers);
            ReadServerMemFromFile(favoriteServersFile, ref favoriteServers);
            recentServers.ForEach(s => s.Recent = true);
            favoriteServers.ForEach(s => s.Favorite = true);

            SelectedTab = ServerListTab.All;
            tabButtons[(int)selectedTab].Selected = true;

            RecalculateHolder();
        }


        private static void LoadPlayStyleBanners()
        {
            //playstyle banners
            playStyleBanners = new Sprite[Enum.GetValues(typeof(PlayStyle)).Length];
            PlayStyleColors = new Color[Enum.GetValues(typeof(PlayStyle)).Length];
            PlayStyleIcons = new Dictionary<string, Sprite>();
            PlayStyleIconColors = new Dictionary<string, Color>();

            XDocument playStylesDoc = XMLExtensions.TryLoadXml("Content/UI/Server/PlayStyles.xml");

            XElement rootElement = playStylesDoc.Root;
            foreach (var element in rootElement.Elements())
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "playstylebanner":
                        if (Enum.TryParse(element.GetAttributeString("identifier", ""), out PlayStyle playStyle))
                        {
                            PlayStyleBanners[(int)playStyle] = new Sprite(element, lazyLoad: true);
                            PlayStyleColors[(int)playStyle] = element.GetAttributeColor("color", Color.White);
                        }
                        break;
                    case "playstyleicon":
                        string identifier = element.GetAttributeString("identifier", "");
                        if (string.IsNullOrEmpty(identifier)) { continue; }
                        PlayStyleIcons[identifier] = new Sprite(element, lazyLoad: true);
                        PlayStyleIconColors[identifier] = element.GetAttributeColor("color", Color.White);
                        break;
                }
            }
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

        public ServerInfo UpdateServerInfoWithServerSettings(object endpoint, ServerSettings serverSettings)
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

            bool isInfoNew = false;
            ServerInfo info = serverList.Content.FindChild(d => (d.UserData is ServerInfo serverInfo) && serverInfo != null &&
                                                        (steamId != 0 ? steamId == serverInfo.OwnerID : (ip == serverInfo.IP && port == serverInfo.Port)))?.UserData as ServerInfo;
            if (info == null)
            {
                isInfoNew = true;
                info = new ServerInfo();
            }

            info.ServerName = serverSettings.ServerName;
            info.ServerMessage = serverSettings.ServerMessageText;
            info.OwnerID = steamId;
            info.LobbyID = SteamManager.CurrentLobbyID;
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
            info.FriendlyFireEnabled = serverSettings.AllowFriendlyFire;
            info.KarmaEnabled = serverSettings.KarmaEnabled;
            info.PlayerCount = GameMain.Client.ConnectedClients.Count;
            info.PingChecked = false;
            info.HasPassword = serverSettings.HasPassword;
            info.OwnerVerified = true;

            if (isInfoNew)
            {
                AddToServerList(info);
            }

            return info;
        }

        public void AddToRecentServers(ServerInfo info)
        {
            if (!string.IsNullOrEmpty(info.IP))
            {
                //don't add localhost to recent servers
                if (IPAddress.TryParse(info.IP, out IPAddress ip) && IPAddress.IsLoopback(ip)) { return; }
            }

            info.Recent = true;
            ServerInfo existingInfo = recentServers.Find(serverInfo => info.OwnerID == serverInfo.OwnerID && (info.OwnerID != 0 ? true : (info.IP == serverInfo.IP && info.Port == serverInfo.Port)));
            if (existingInfo == null)
            {
                recentServers.Add(info);
            }
            else
            {
                int index = recentServers.IndexOf(existingInfo);
                recentServers[index] = info;
            }

            WriteServerMemToFile(recentServersFile, recentServers);
        }

        public void AddToFavoriteServers(ServerInfo info)
        {
            info.Favorite = true;
            ServerInfo existingInfo = favoriteServers.Find(serverInfo => info.OwnerID == serverInfo.OwnerID && (info.OwnerID != 0 ? true : (info.IP == serverInfo.IP && info.Port == serverInfo.Port)));
            if (existingInfo == null)
            {
                favoriteServers.Add(info);
            }
            else
            {
                int index = favoriteServers.IndexOf(existingInfo);
                favoriteServers[index] = info;
            }

            WriteServerMemToFile(favoriteServersFile, favoriteServers);
        }

        public void RemoveFromFavoriteServers(ServerInfo info)
        {
            info.Favorite = false;
            ServerInfo existingInfo = favoriteServers.Find(serverInfo => info.OwnerID == serverInfo.OwnerID && (info.OwnerID != 0 ? true : (info.IP == serverInfo.IP && info.Port == serverInfo.Port)));
            if (existingInfo != null)
            {
                favoriteServers.Remove(existingInfo);
                WriteServerMemToFile(favoriteServersFile, favoriteServers);
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
            SelectedTab = ServerListTab.All;

            Steamworks.SteamMatchmaking.ResetActions();

            RefreshServers();
        }

        public override void Deselect()
        {
            base.Deselect();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            UpdateFriendsList();

            if (PlayerInput.PrimaryMouseButtonClicked())
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

                //never show newer versions
                //(ignore revision number, it doesn't affect compatibility)
                if (remoteVersion != null &&
                    (remoteVersion.Major > GameMain.Version.Major || remoteVersion.Minor > GameMain.Version.Minor || remoteVersion.Build > GameMain.Version.Build))
                {
                    child.Visible = false;
                }
                else
                {
                    bool incompatible =
                        (!serverInfo.ContentPackageHashes.Any() && serverInfo.ContentPackagesMatch(GameMain.Config.SelectedContentPackages)) ||
                        (remoteVersion != null && !NetworkMember.IsCompatible(GameMain.Version, remoteVersion));

                    child.Visible =
                        serverInfo.OwnerVerified &&
                        serverInfo.ServerName.Contains(searchBox.Text, StringComparison.OrdinalIgnoreCase) &&
                        (!filterSameVersion.Selected || (remoteVersion != null && NetworkMember.IsCompatible(remoteVersion, GameMain.Version))) &&
                        (!filterPassword.Selected || !serverInfo.HasPassword) &&
                        (!filterIncompatible.Selected || !incompatible) &&
                        (!filterFull.Selected || serverInfo.PlayerCount < serverInfo.MaxPlayers) &&
                        (!filterEmpty.Selected || serverInfo.PlayerCount > 0) &&
                        (!filterWhitelisted.Selected || serverInfo.UsingWhiteList == true) &&
                        (!filterKarma.Selected || serverInfo.KarmaEnabled == true) &&
                        (!filterFriendlyFire.Selected || serverInfo.FriendlyFireEnabled == false) &&
                        (!filterTraitor.Selected || serverInfo.TraitorsEnabled == YesNoMaybe.Yes || serverInfo.TraitorsEnabled == YesNoMaybe.Maybe) &&
                        (!filterVoip.Selected || serverInfo.VoipEnabled == false) &&
                        (!filterModded.Selected || serverInfo.GetPlayStyleTags().Any(t => t.Contains("modded.true"))) &&
                        ((selectedTab == ServerListTab.All && (serverInfo.LobbyID != 0 || !string.IsNullOrWhiteSpace(serverInfo.Port))) ||
                         (selectedTab == ServerListTab.Recent && serverInfo.Recent) ||
                         (selectedTab == ServerListTab.Favorites && serverInfo.Favorite));
                }

                foreach (GUITickBox tickBox in playStyleTickBoxes)
                {
                    var playStyle = (PlayStyle)tickBox.UserData;

                    if (!tickBox.Selected && serverInfo.PlayStyle == playStyle)
                    {
                        child.Visible = false;
                        break;
                    }
                }

                foreach (GUITickBox tickBox in gameModeTickBoxes)
                {
                    var gameMode = (string)tickBox.UserData;
                    if (!tickBox.Selected && serverInfo.GameMode.Equals(gameMode, StringComparison.OrdinalIgnoreCase))
                    {
                        child.Visible = false;
                        break;
                    }
                }
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
            var msgBox = new GUIMessageBox(TextManager.Get("ServerListDirectJoin"), "",
                new string[] { TextManager.Get("ServerListJoin"), TextManager.Get("AddToFavorites"), TextManager.Get("Cancel") },
                relativeSize: new Vector2(0.25f, 0.2f), minSize: new Point(400, 150));
            msgBox.Content.ChildAnchor = Anchor.TopCenter;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), msgBox.Content.RectTransform), childAnchor: Anchor.TopCenter)
            {
                IgnoreLayoutGroups = false,
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), TextManager.Get("ServerEndpoint"), textAlignment: Alignment.Center);
            var endpointBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform));

            content.RectTransform.NonScaledSize = new Point(content.Rect.Width, (int)(content.RectTransform.Children.Sum(c => c.Rect.Height)));
            content.RectTransform.IsFixedSize = true;
            msgBox.InnerFrame.RectTransform.MinSize = new Point(0, (int)((content.RectTransform.NonScaledSize.Y + msgBox.Content.RectTransform.Children.Sum(c => c.NonScaledSize.Y + msgBox.Content.AbsoluteSpacing)) * 1.1f));

            var okButton = msgBox.Buttons[0];
            okButton.Enabled = false;
            okButton.OnClicked = (btn, userdata) =>
            {
                JoinServer(endpointBox.Text, "");
                msgBox.Close();
                return true;
            };

            var favoriteButton = msgBox.Buttons[1];
            favoriteButton.Enabled = false;
            favoriteButton.OnClicked = (button, userdata) =>
            {
                UInt64 steamId = SteamManager.SteamIDStringToUInt64(endpointBox.Text);
                string ip = ""; int port = 0;
                if (steamId == 0)
                {
                    string hostIP = endpointBox.Text;

                    string[] address = hostIP.Split(':');
                    if (address.Length == 1)
                    {
                        ip = hostIP;
                        port = NetConfig.DefaultPort;
                    }
                    else
                    {
                        ip = string.Join(":", address.Take(address.Length - 1));
                        if (!int.TryParse(address[address.Length - 1], out port))
                        {
                            DebugConsole.ThrowError("Invalid port: " + address[address.Length - 1] + "!");
                            port = NetConfig.DefaultPort;
                        }
                    }
                }

                //TODO: add a better way to get the query port, right now we're just assuming that it'll always be the default
                ServerInfo serverInfo = new ServerInfo()
                {
                    ServerName = "Server",
                    OwnerID = steamId,
                    IP = ip,
                    Port = port.ToString(),
                    QueryPort = NetConfig.DefaultQueryPort.ToString(),
                    GameVersion = GameMain.Version.ToString(),
                    PlayStyle = PlayStyle.Serious
                };

                var serverFrame = serverList.Content.FindChild(d => (d.UserData is ServerInfo info) &&
                                                                info.OwnerID == serverInfo.OwnerID &&
                                                                (serverInfo.OwnerID != 0 ? true : (info.IP == serverInfo.IP && info.Port == serverInfo.Port)));

                if (serverFrame != null)
                {
                    serverInfo = serverFrame.UserData as ServerInfo;
                }
                else
                {
                    AddToServerList(serverInfo);
                }

                AddToFavoriteServers(serverInfo);

                SelectedTab = ServerListTab.Favorites;
                FilterServers();

                serverInfo.QueryLiveInfo(UpdateServerInfo);

                msgBox.Close();
                return false;
            };

            var cancelButton = msgBox.Buttons[2];
            cancelButton.OnClicked = msgBox.Close;

            endpointBox.OnTextChanged += (textBox, text) =>
            {
                okButton.Enabled = favoriteButton.Enabled = !string.IsNullOrEmpty(text);                
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
                var joinButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), friendPopup.RectTransform, Anchor.TopRight), TextManager.Get("ServerListJoin"))
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

        private enum AvatarSize
        {
            Small,
            Medium,
            Large
        }

        private void UpdateFriendsList()
        {
            if (!SteamManager.IsInitialized) { return; }

            if (friendsListUpdateTime > Timing.TotalTime) { return; }
            friendsListUpdateTime = Timing.TotalTime + 5.0;

            float prevDropdownScroll = friendsDropdown?.ScrollBar.BarScrollValue ?? 0.0f;

            if (friendsDropdown == null) 
            {
                friendsDropdown = new GUIListBox(new RectTransform(Vector2.One, GUI.Canvas))
                {
                    OutlineColor = Color.Black,
                    Visible = false
                };
            }
            friendsDropdown.ClearChildren();

            AvatarSize avatarSize = AvatarSize.Large;
            if (friendsButtonHolder.RectTransform.Rect.Height <= 24)
            {
                avatarSize = AvatarSize.Small;
            }
            else if (friendsButtonHolder.RectTransform.Rect.Height <= 48)
            {
                avatarSize = AvatarSize.Medium;
            }

            List<Steamworks.Friend> friends = Steamworks.SteamFriends.GetFriends().ToList();

            for (int i = friendsList.Count - 1; i >= 0; i--)
            {
                var friend = friendsList[i];
                if (!friends.Any(g => g.Id == friend.SteamID && g.IsOnline))
                {
                    friend.Sprite?.Remove();
                    friendsList.RemoveAt(i);
                }
            }

            foreach (var friend in friends)
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
                    Func<Steamworks.SteamId, Task<Steamworks.Data.Image?>> avatarFunc = null;
                    switch (avatarSize)
                    {
                        case AvatarSize.Small:
                            avatarFunc = Steamworks.SteamFriends.GetSmallAvatarAsync;
                            break;
                        case AvatarSize.Medium:
                            avatarFunc = Steamworks.SteamFriends.GetMediumAvatarAsync;
                            break;
                        case AvatarSize.Large:
                            avatarFunc = Steamworks.SteamFriends.GetLargeAvatarAsync;
                            break;
                    }
                    TaskPool.Add(avatarFunc(friend.Id), (Task<Steamworks.Data.Image?> task) =>
                    {
                        if (!task.Result.HasValue) { return; }

                        var avatarImage = task.Result.Value;

                        const int desaturatedWeight = 180;

                        byte[] avatarData = (byte[])avatarImage.Data.Clone();
                        for (int i = 0; i < avatarData.Length; i += 4)
                        {
                            int luma = (avatarData[i + 0] * 299 + avatarData[i + 1] * 587 + avatarData[i + 2] * 114) / 1000;
                            luma = (int)(luma * 0.7f + ((luma / 100.0f) * (luma / 255.0f) * 255.0f * 0.3f));
                            int chn0 = ((avatarData[i + 0] * (255 - desaturatedWeight)) / 255) + ((luma * desaturatedWeight) / 255);
                            int chn1 = ((avatarData[i + 1] * (255 - desaturatedWeight)) / 255) + ((luma * desaturatedWeight) / 255);
                            int chn2 = ((avatarData[i + 2] * (255 - desaturatedWeight)) / 255) + ((luma * desaturatedWeight) / 255);
                            int chn3 = 255;

                            chn0 = chn0 * chn3 / 255;
                            chn1 = chn1 * chn3 / 255;
                            chn2 = chn2 * chn3 / 255;

                            avatarData[i + 0] = chn0 > 255 ? (byte)255 : (byte)chn0;
                            avatarData[i + 1] = chn1 > 255 ? (byte)255 : (byte)chn1;
                            avatarData[i + 2] = chn2 > 255 ? (byte)255 : (byte)chn2;
                            avatarData[i + 3] = chn3 > 255 ? (byte)255 : (byte)chn3;
                        }
                        CrossThread.RequestExecutionOnMainThread(() =>
                        {
                            //TODO: create an avatar atlas?
                            var avatarTexture = new Texture2D(GameMain.Instance.GraphicsDevice, (int)avatarImage.Width, (int)avatarImage.Height);
                            avatarTexture.SetData(avatarData);
                            info.Sprite = new Sprite(avatarTexture, null, null);
                        });
                    });
                }

                info.Name = friend.Name;

                info.ConnectName = null;
                info.ConnectEndpoint = null;
                info.ConnectLobby = 0;

                info.PlayingThisGame = friend.IsPlayingThisGame;
                info.PlayingAnotherGame = friend.GameInfo.HasValue;

                if (friend.IsPlayingThisGame)
                {
                    info.StatusText = friend.GetRichPresence("status") ?? "";
                    string connectCommand = friend.GetRichPresence("connect") ?? "";

                    try
                    {
                        ToolBox.ParseConnectCommand(ToolBox.SplitCommand(connectCommand), out info.ConnectName, out info.ConnectEndpoint, out info.ConnectLobby);
                    }
                    catch (IndexOutOfRangeException e)
                    {
#if DEBUG
                        DebugConsole.ThrowError($"Failed to parse a Steam friend's connect command ({connectCommand})", e);
#else
                        DebugConsole.Log($"Failed to parse a Steam friend's connect command ({connectCommand})\n" + e.StackTrace);
#endif
                        info.ConnectName = null;
                        info.ConnectEndpoint = null;
                        info.ConnectLobby = 0;
                    }
                }
                else
                {
                    info.StatusText = TextManager.Get(info.PlayingAnotherGame ? "FriendPlayingAnotherGame" : "FriendNotPlaying");
                }
            }

            friendsList.Sort((a, b) =>
            {
                if (a.InServer && !b.InServer) { return -1; }
                if (b.InServer && !a.InServer) { return 1; }
                if (a.PlayingThisGame && !b.PlayingThisGame) { return -1; }
                if (b.PlayingThisGame && !a.PlayingThisGame) { return 1; }
                return 0;
            });

            friendsButtonHolder.ClearChildren();

            if (friendsList.Count > 0)
            {
                friendsDropdownButton = new GUIButton(new RectTransform(Vector2.One, friendsButtonHolder.RectTransform, Anchor.BottomRight, Pivot.BottomRight, scaleBasis: ScaleBasis.BothHeight), "\u2022 \u2022 \u2022", style: "GUIButtonFriendsDropdown")
                {
                    Font = GUI.GlobalFont,
                    OnClicked = (button, udt) =>
                    {
                        friendsDropdown.RectTransform.NonScaledSize = new Point(friendsButtonHolder.Rect.Height * 5 * 166 / 100, friendsButtonHolder.Rect.Height * 4 * 166 / 100);
                        friendsDropdown.RectTransform.AbsoluteOffset = new Point(friendsButtonHolder.Rect.X, friendsButtonHolder.Rect.Bottom);
                        friendsDropdown.RectTransform.RecalculateChildren(true);
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

            int buttonCount = 0;

            for (int i = 0; i < friendsList.Count; i++)
            {
                var friend = friendsList[i];
                buttonCount++;

                if (buttonCount <= 5)
                {
                    string style = "GUIButtonFriendNotPlaying";
                    if (friend.InServer)
                    {
                        style = "GUIButtonFriendPlaying";
                    }
                    else
                    {
                        style = friend.PlayingThisGame ? "GUIButtonFriendPlaying" : "GUIButtonFriendNotPlaying";
                    }

                    var guiButton = new GUIButton(new RectTransform(Vector2.One, friendsButtonHolder.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: style)
                    {
                        UserData = friend,
                        OnClicked = OpenFriendPopup
                    };
                    guiButton.ToolTip = friend.Name + "\n" + friend.StatusText;

                    if (friend.Sprite != null)
                    {
                        static Color BrightenColor(Color color)
                        {
                            Vector3 hls = ToolBox.RgbToHLS(color);
                            hls.Y = hls.Y * 0.3f + 0.7f;
                            hls.Z = hls.Z * 0.6f + 0.4f;

                            return ToolBox.HLSToRGB(hls);
                        }

                        var imgColor = BrightenColor(guiButton.Color);
                        var imgHoverColor = BrightenColor(guiButton.HoverColor);
                        var imgSelectColor = BrightenColor(guiButton.SelectedColor);
                        var imgPressColor = BrightenColor(guiButton.PressedColor);
                        var guiImage = new GUIImage(new RectTransform(Vector2.One * 0.925f, guiButton.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.025f, 0.025f) }, friend.Sprite, null, true)
                        {
                            Color = imgColor,
                            HoverColor = imgHoverColor,
                            SelectedColor = imgSelectColor,
                            PressedColor = imgPressColor,
                            CanBeFocused = false
                        };
                        guiImage = new GUIImage(new RectTransform(Vector2.One * 0.925f, guiButton.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.025f, 0.025f) }, friend.Sprite, null, true)
                        {
                            Color = Color.White * 0.8f,
                            HoverColor = Color.White * 0.8f,
                            SelectedColor = Color.White * 0.8f,
                            PressedColor = Color.White * 0.8f,
                            BlendState = BlendState.Additive,
                            CanBeFocused = false
                        };
                    }
                }

                var friendFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.167f), friendsDropdown.Content.RectTransform), style: "GUIFrameFriendsDropdown");
                var guiImage2TheSequel = new GUIImage(new RectTransform(Vector2.One * 0.9f, friendFrame.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight) { RelativeOffset = new Vector2(0.02f, 0.02f) } , friend.Sprite, null, true);

                var textBlock = new GUITextBlock(new RectTransform(Vector2.One * 0.8f, friendFrame.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight) { RelativeOffset = new Vector2(1.0f / 7.7f, 0.0f) }, friend.Name + "\n" + friend.StatusText)
                {
                    Font = GUI.SmallFont
                };
                if (friend.PlayingThisGame) { textBlock.TextColor = GUI.Style.Green; }
                if (friend.PlayingAnotherGame) { textBlock.TextColor = GUI.Style.Blue; }

                if (friend.InServer)
                {
                    var joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.6f), friendFrame.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.05f, 0.0f) }, TextManager.Get("ServerListJoin"), style: "GUIButtonJoinFriend")
                    {
                        UserData = friend
                    };
                    joinButton.OnClicked = JoinFriend;
                }
            }

            friendsDropdown.RectTransform.NonScaledSize = new Point(friendsButtonHolder.Rect.Height * 5 * 166 / 100, friendsButtonHolder.Rect.Height * 4 * 166 / 100);
            friendsDropdown.RectTransform.AbsoluteOffset = new Point(friendsButtonHolder.Rect.X, friendsButtonHolder.Rect.Bottom);
            friendsDropdown.RectTransform.RecalculateChildren(true);

            friendsDropdown.ScrollBar.BarScrollValue = prevDropdownScroll;
        }

        private void RefreshServers()
        {
            if (waitingForRefresh) { return; }

            steamPingInfoReady = false;

            CoroutineManager.StopCoroutines("EstimateLobbyPing");

            TaskPool.Add(Steamworks.SteamNetworkingUtils.WaitForPingDataAsync(), (task) =>
            {
                steamPingInfoReady = true;
            });

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

            recentServers.Concat(favoriteServers).ForEach(si => si.OwnerVerified = false);
            if (GameMain.Config.UseSteamMatchmaking)
            {
                serverList.ClearChildren();
                if (!SteamManager.GetServers(AddToServerList, ServerQueryFinished))
                {
                    serverList.ClearChildren();
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                        TextManager.Get("ServerListNoSteamConnection"), textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };
                }
                else
                {
                    List<ServerInfo> knownServers = recentServers.Concat(favoriteServers).ToList();
                    foreach (ServerInfo info in knownServers)
                    {
                        AddToServerList(info);
                        info.QueryLiveInfo(UpdateServerInfo);
                    }
                }
            }
            else
            {
                CoroutineManager.StartCoroutine(SendMasterServerRequest());
                waitingForRefresh = false;
            }

            refreshDisableTimer = DateTime.Now + AllowedRefreshInterval;

            yield return CoroutineStatus.Success;
        }

        private void UpdateServerList(string masterServerData)
        {
            serverList.ClearChildren();
                        
            if (masterServerData.Substring(0, 5).Equals("error", StringComparison.OrdinalIgnoreCase))
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
                    GameVersion = gameVersion,
                    OwnerVerified = true
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
                                                                (info.LobbyID == serverInfo.LobbyID ||
                                                                (info.LobbyID == 0 && info.OwnerID == serverInfo.OwnerID &&
                                                                serverInfo.OwnerVerified)) &&
                                                                (serverInfo.OwnerID != 0 ? true : (info.IP == serverInfo.IP && info.Port == serverInfo.Port)));

            if (serverFrame == null)
            {
                serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 35) },
                    style: "ListBoxElement")
                {
                    UserData = serverInfo
                };
                new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 1.0f), serverFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    //RelativeSpacing = 0.02f
                };
            }
            else
            {
                int index = recentServers.IndexOf(serverFrame.UserData as ServerInfo);
                if (index >= 0)
                {
                    recentServers[index] = serverInfo;
                    serverInfo.Recent = true;
                }
                index = favoriteServers.IndexOf(serverFrame.UserData as ServerInfo);
                if (index >= 0)
                {
                    favoriteServers[index] = serverInfo;
                    serverInfo.Favorite = true;
                }
            }
            serverFrame.UserData = serverInfo;

            if (serverInfo.OwnerVerified)
            {
                var childrenToRemove = serverList.Content.FindChildren(c => (c.UserData is ServerInfo info) && info != serverInfo &&
                                                                            (serverInfo.OwnerID != 0 ? info.OwnerID == serverInfo.OwnerID : info.IP == serverInfo.IP)).ToList();
                foreach (var child in childrenToRemove)
                {
                    serverList.Content.RemoveChild(child);
                }
            }

            UpdateServerInfo(serverInfo);

            SortList(sortedBy, toggle: false);
            FilterServers();
        }

        private void UpdateServerInfo(ServerInfo serverInfo)
        {
            var serverFrame = serverList.Content.FindChild(d => (d.UserData is ServerInfo info) &&
                                                                (info.LobbyID == serverInfo.LobbyID ||
                                                                (info.LobbyID == 0 && info.OwnerID == serverInfo.OwnerID &&
                                                                serverInfo.OwnerVerified)) &&
                                                                (serverInfo.OwnerID != 0 ? true : (info.IP == serverInfo.IP && info.Port == serverInfo.Port)));
            if (serverFrame == null) return;

            var serverContent = serverFrame.Children.First() as GUILayoutGroup;
            serverContent.ClearChildren();

            var compatibleBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[0], 0.9f), serverContent.RectTransform, Anchor.Center), label: "")
            {
                CanBeFocused = false,
                Selected =
                    serverInfo.GameVersion == GameMain.Version.ToString() &&
                    serverInfo.ContentPackagesMatch(GameMain.SelectedPackages),
                UserData = "compatible"
            };
            
            var passwordBox = new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[1], 0.5f), serverContent.RectTransform, Anchor.Center), label: "", style: "GUIServerListPasswordTickBox")
            {
				ToolTip = TextManager.Get((serverInfo.HasPassword) ? "ServerListHasPassword" : "FilterPassword"),
				Selected = serverInfo.HasPassword,
                CanBeFocused = false,
                UserData = "password"
            };

			var serverName = new GUITextBlock(new RectTransform(new Vector2(columnRelativeWidth[2] * 1.1f, 1.0f), serverContent.RectTransform),
#if !DEBUG
                serverInfo.ServerName,
#else
                ((serverInfo.OwnerID != 0 || serverInfo.LobbyID != 0) ? "[STEAMP2P] " : "[LIDGREN] ") + serverInfo.ServerName,
#endif
                style: "GUIServerListTextBox");
            serverName.UserData = serverName.Text;
            serverName.RectTransform.SizeChanged += () =>
            {
                serverName.Text = ToolBox.LimitString(serverName.Text, serverName.Font, serverName.Rect.Width);
            };

            new GUITickBox(new RectTransform(new Vector2(columnRelativeWidth[3], 0.9f), serverContent.RectTransform, Anchor.Center), label: "")
            {
				ToolTip = TextManager.Get((serverInfo.GameStarted) ? "ServerListRoundStarted" : "ServerListRoundNotStarted"),
				Selected = serverInfo.GameStarted,
				CanBeFocused = false
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
            else if (serverInfo.PingLocation != null)
            {
                CoroutineManager.StartCoroutine(EstimateLobbyPing(serverInfo, serverPingText), "EstimateLobbyPing");
            }

            if (serverInfo.LobbyID == 0 && (string.IsNullOrWhiteSpace(serverInfo.IP) || string.IsNullOrWhiteSpace(serverInfo.Port)))
            {
                string toolTip = TextManager.Get("ServerOffline");
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);
                serverName.TextColor *= 0.8f;
                serverPlayers.TextColor *= 0.8f;
            }
            else if (GameMain.Config.UseSteamMatchmaking && serverInfo.RespondedToSteamQuery.HasValue && serverInfo.RespondedToSteamQuery.Value == false)
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
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), compatibleBox.Box.RectTransform, Anchor.Center), " ? ", GUI.Style.Orange * 0.85f, textAlignment: Alignment.Center)
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

            if (serverInfo.Favorite)
            {
                AddToFavoriteServers(serverInfo);
            }

            SortList(sortedBy, toggle: false);
            FilterServers();
        }

        private IEnumerable<object> EstimateLobbyPing(ServerInfo serverInfo, GUITextBlock serverPingText)
        {
            while (!steamPingInfoReady)
            {
                yield return CoroutineStatus.Running;
            }

            Steamworks.Data.PingLocation pingLocation = serverInfo.PingLocation.Value;
            serverInfo.Ping = Steamworks.SteamNetworkingUtils.LocalPingLocation?.EstimatePingTo(pingLocation) ?? -1;
            serverInfo.PingChecked = true;
            serverPingText.TextColor = GetPingTextColor(serverInfo.Ping);
            serverPingText.Text = serverInfo.Ping > -1 ? serverInfo.Ping.ToString() : "?";

            yield return CoroutineStatus.Success;
        }

        private void ServerQueryFinished()
        {
            if (!serverList.Content.Children.Any())
            {
                new GUITextBlock(new RectTransform(Vector2.One, serverList.Content.RectTransform),
                    TextManager.Get("NoServers"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false
                };
            }
            else if (serverList.Content.Children.All(c => !c.Visible))
            {
                new GUITextBlock(new RectTransform(Vector2.One, serverList.Content.RectTransform),
                    TextManager.Get("NoMatchingServers"), textAlignment: Alignment.Center)
                {
                    CanBeFocused = false,
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

        private bool JoinServer(string endpoint, string serverName)
        {
            if (string.IsNullOrWhiteSpace(ClientNameBox.Text))
            {
                ClientNameBox.Flash();
                return false;
            }

            GameMain.Config.PlayerName = ClientNameBox.Text;
            GameMain.Config.SaveNewPlayerConfig();

            CoroutineManager.StartCoroutine(ConnectToServer(endpoint, serverName), "ConnectToServer");

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
                GameMain.Client = new GameClient(GameMain.Config.PlayerName, serverIP, serverSteamID, serverName);
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
            if (CoroutineManager.IsCoroutineRunning("ConnectToServer")) { return; }

            lock (activePings)
            {
                if (activePings.Contains(serverInfo.IP)) { return; }
                activePings.Add(serverInfo.IP);
            }

            serverInfo.PingChecked = false;
            serverInfo.Ping = -1;

            TaskPool.Add(PingServerAsync(serverInfo?.IP, 1000),
                new Tuple<ServerInfo, GUITextBlock>(serverInfo, serverPingText),
                (rtt, obj) =>
                {
                    var info = obj.Item1;
                    var text = obj.Item2;
                    info.Ping = rtt.Result; info.PingChecked = true;
                    text.TextColor = GetPingTextColor(info.Ping);
                    text.Text = info.Ping > -1 ? info.Ping.ToString() : "?";
                    lock (activePings)
                    {
                        activePings.Remove(serverInfo.IP);
                    }
                });
        }

        private Color GetPingTextColor(int ping)
        {
            if (ping < 0) { return Color.DarkRed; }
            return ToolBox.GradientLerp(ping / 200.0f, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
        }

        public async Task<int> PingServerAsync(string ip, int timeOut)
        {
            await Task.Yield();
            int activePingCount = 100;
            while (activePingCount > 25)
            {
                lock (activePings)
                {
                    activePingCount = activePings.Count;
                }
                await Task.Delay(25);
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                return -1;
            }

            long rtt = -1;
            IPAddress address = null;
            IPAddress.TryParse(ip, out address);
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
                    catch (Exception ex)
                    {
                        string errorMsg = "Failed to ping a server (" + ip + ") - " + (ex?.InnerException?.Message ?? ex.Message);
                        GameAnalyticsManager.AddErrorEventOnce("ServerListScreen.PingServer:PingException" + ip, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, errorMsg);
#if DEBUG
                        DebugConsole.NewMessage(errorMsg, Color.Red);
#endif
                    }
                }
            }

            return (int)rtt;
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
