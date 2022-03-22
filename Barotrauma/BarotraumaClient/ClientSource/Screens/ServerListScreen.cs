using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    class ServerListScreen : Screen
    {
        //how often the client is allowed to refresh servers
        private readonly TimeSpan AllowedRefreshInterval = new TimeSpan(0, 0, 3);

        public ImmutableDictionary<UInt64, ContentPackage> ContentPackagesByWorkshopId { get; private set; }
            = ImmutableDictionary<UInt64, ContentPackage>.Empty;
        public ImmutableDictionary<string, ContentPackage> ContentPackagesByHash { get; private set; }
            = ImmutableDictionary<string, ContentPackage>.Empty;

        private GUIFrame menu;

        private GUIListBox serverList;
        private GUIFrame serverPreviewContainer;
        private GUIListBox serverPreview;

        private GUIButton joinButton;
        private ServerInfo selectedServer;

        private GUIButton scanServersButton;

        //friends list
        private GUILayoutGroup friendsButtonHolder;

        private GUIButton friendsDropdownButton;
        private GUIListBox friendsDropdown;

        //Workshop downloads
        public struct PendingWorkshopDownload
        {
            public readonly string ExpectedHash;
            public readonly ulong Id;
            public readonly Steamworks.Ugc.Item? Item;
            
            public PendingWorkshopDownload(string expectedHash, Steamworks.Ugc.Item item)
            {
                ExpectedHash = expectedHash;
                Item = item;
                Id = item.Id;
            }

            public PendingWorkshopDownload(string expectedHash, ulong id)
            {
                ExpectedHash = expectedHash;
                Item = null;
                Id = id;
            }
        }

        private GUIFrame workshopDownloadsFrame = null;
        private Steamworks.Ugc.Item? currentlyDownloadingWorkshopItem = null;
        private Dictionary<ulong, PendingWorkshopDownload> pendingWorkshopDownloads = null;
        private string autoConnectName;
        private string autoConnectEndpoint;

        private enum TernaryOption
        {
            Any,
            Enabled,
            Disabled
        }

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

        private readonly Dictionary<string, int> activePings = new Dictionary<string, int>();

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
        private Dictionary<string, GUIDropDown> ternaryFilters;
        private Dictionary<string, GUITickBox> filterTickBoxes;
        private Dictionary<string, GUITickBox> playStyleTickBoxes;
        private Dictionary<string, GUITickBox> gameModeTickBoxes;
        private GUITickBox filterOffensive;

        //GUIDropDown sends the OnSelected event before SelectedData is set, so we have to cache it manually.
        private TernaryOption filterFriendlyFireValue = TernaryOption.Any;
        private TernaryOption filterKarmaValue = TernaryOption.Any;
        private TernaryOption filterTraitorValue = TernaryOption.Any;
        private TernaryOption filterVoipValue = TernaryOption.Any;
        private TernaryOption filterModdedValue = TernaryOption.Any;

        private string sortedBy;
        
        private GUIButton serverPreviewToggleButton;

        //a timer for preventing the client from spamming the refresh button faster than AllowedRefreshInterval
        private DateTime refreshDisableTimer;
        private bool waitingForRefresh;

        private bool steamPingInfoReady;

        private const float sidebarWidth = 0.2f;
        public ServerListScreen()
        {
            GameMain.Instance.ResolutionChanged += CreateUI;
            CreateUI();
        }

        private void AddTernaryFilter(RectTransform parent, float elementHeight, string tag, Action<TernaryOption> valueSetter)
        {
            var filterLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, elementHeight), parent), isHorizontal: true)
            {
                Stretch = true
            };

            var box = new GUIFrame(new RectTransform(Vector2.One, filterLayoutGroup.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight)
            {
                IsFixedSize = true,
            }, null)
            {
                HoverColor = Color.Gray,
                SelectedColor = Color.DarkGray,
                CanBeFocused = false
            };
            if (box.RectTransform.MinSize.Y > 0)
            {
                box.RectTransform.MinSize = new Point(box.RectTransform.MinSize.Y);
                box.RectTransform.Resize(box.RectTransform.MinSize);
            }
            Vector2 textBlockScale = new Vector2((float)(filterLayoutGroup.Rect.Width - filterLayoutGroup.Rect.Height) / (float)Math.Max(filterLayoutGroup.Rect.Width, 1.0), 1.0f);

            var filterLabel = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f) * textBlockScale, filterLayoutGroup.RectTransform, Anchor.CenterLeft), TextManager.Get("servertag." + tag + ".label"), textAlignment: Alignment.CenterLeft)
            {
                UserData = TextManager.Get("servertag." + tag + ".label")
            };
            GUI.Style.Apply(filterLabel, "GUITextBlock", null);

            var dropDown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f) * textBlockScale, filterLayoutGroup.RectTransform, Anchor.CenterLeft), elementCount: 3);
            dropDown.AddItem(TextManager.Get("any"), TernaryOption.Any);
            dropDown.AddItem(TextManager.Get("servertag." + tag + ".true"), TernaryOption.Enabled, TextManager.Get("servertagdescription." + tag + ".true"));
            dropDown.AddItem(TextManager.Get("servertag." + tag + ".false"), TernaryOption.Disabled, TextManager.Get("servertagdescription." + tag + ".false"));
            dropDown.SelectItem(TernaryOption.Any);
            dropDown.OnSelected = (_, data) => {
                valueSetter((TernaryOption)data);
                FilterServers();
                return true;
            };

            ternaryFilters.Add(tag, dropDown);
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
                listContainerSubtract += serverPreviewContainer.Visible ? sidebarWidth : 0.0f;

                float toggleButtonsSubtract = 1.1f * filterToggle.Rect.Width / serverListHolder.Rect.Width;
                listContainerSubtract += filterToggle.Visible ? toggleButtonsSubtract : 0.0f;
                listContainerSubtract += serverPreviewContainer.Visible ? toggleButtonsSubtract : 0.0f;

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

            ternaryFilters = new Dictionary<string, GUIDropDown>();
            filterTickBoxes = new Dictionary<string, GUITickBox>();

            filterSameVersion = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterSameVersion"))
            {
                UserData = TextManager.Get("FilterSameVersion"),
                Selected = true,
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterSameVersion", filterSameVersion);

            filterPassword = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterPassword"))
            {
                UserData = TextManager.Get("FilterPassword"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterPassword", filterPassword);

            filterIncompatible = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterIncompatibleServers"))
            {
                UserData = TextManager.Get("FilterIncompatibleServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterIncompatibleServers", filterIncompatible);

            filterFull = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterFullServers"))
            {
                UserData = TextManager.Get("FilterFullServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterFullServers", filterFull);

            filterEmpty = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterEmptyServers"))
            {
                UserData = TextManager.Get("FilterEmptyServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterEmptyServers", filterEmpty);

            filterWhitelisted = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterWhitelistedServers"))
            {
                UserData = TextManager.Get("FilterWhitelistedServers"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterWhitelistedServers", filterWhitelisted);

            filterOffensive = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("FilterOffensiveServers"))
            {
                UserData = TextManager.Get("FilterOffensiveServers"),
                ToolTip = TextManager.Get("FilterOffensiveServersToolTip"),
                OnSelected = (tickBox) => { FilterServers(); return true; }
            };
            filterTickBoxes.Add("FilterOffensiveServers", filterOffensive);

            // Filter Tags
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("servertags"), font: GUI.SubHeadingFont)
            {
                CanBeFocused = false
            };

            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "karma", (value) => { filterKarmaValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "traitors", (value) => { filterTraitorValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "friendlyfire", (value) => { filterFriendlyFireValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "voip", (value) => { filterVoipValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "modded", (value) => { filterModdedValue = value; });

            // Play Style Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("ServerSettingsPlayStyle"), font: GUI.SubHeadingFont)
            {
                CanBeFocused = false
            };

            playStyleTickBoxes = new Dictionary<string, GUITickBox>();
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                var selectionTick = new GUITickBox(new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform), TextManager.Get("servertag." + playStyle))
                {
                    ToolTip = TextManager.Get("servertag." + playStyle),
                    Selected = true,
                    OnSelected = (tickBox) => { FilterServers(); return true; },
                    UserData = playStyle
                };
                playStyleTickBoxes.Add("servertag." + playStyle, selectionTick);
                filterTickBoxes.Add("servertag." + playStyle, selectionTick);
            }

            // Game mode Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("gamemode"), font: GUI.SubHeadingFont) { CanBeFocused = false };

            gameModeTickBoxes = new Dictionary<string, GUITickBox>();
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
                gameModeTickBoxes.Add(mode.Identifier, selectionTick);
                filterTickBoxes.Add(mode.Identifier, selectionTick);
            }

            filters.Content.RectTransform.SizeChanged += () =>
            {
                filters.Content.RectTransform.RecalculateChildren(true, true);
                filterTickBoxes.ForEach(t => t.Value.Text = t.Value.UserData as string);
                gameModeTickBoxes.ForEach(tb => tb.Value.Text = tb.Value.ToolTip);
                playStyleTickBoxes.ForEach(tb => tb.Value.Text = tb.Value.ToolTip);
                GUITextBlock.AutoScaleAndNormalize(
                    filterTickBoxes.Values.Select(tb => tb.TextBlock)
                    .Concat(ternaryFilters.Values.Select(dd => dd.Parent.GetChild<GUITextBlock>())),
                    defaultScale: 1.0f);
                if (filterTickBoxes.Values.First().TextBlock.TextScale < 0.8f)
                {
                    filterTickBoxes.ForEach(t => t.Value.TextBlock.TextScale = 1.0f);
                    filterTickBoxes.ForEach(t => t.Value.TextBlock.Text = ToolBox.LimitString(t.Value.TextBlock.Text, t.Value.TextBlock.Font, (int)(filters.Content.Rect.Width * 0.8f)));
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
                        if (!serverPreviewContainer.Visible)
                        {
                            serverPreviewContainer.RectTransform.RelativeSize = new Vector2(sidebarWidth, 1.0f);
                            serverPreviewToggleButton.Visible = true;
                            serverPreviewToggleButton.IgnoreLayoutGroups = false;
                            serverPreviewContainer.Visible = true;
                            serverPreviewContainer.IgnoreLayoutGroups = false;
                            RecalculateHolder();
                        }
                        serverInfo.CreatePreviewWindow(serverPreview.Content);
                        serverPreview.ForceLayoutRecalculation();
                        btn.Children.ForEach(c => c.SpriteEffects = serverPreviewContainer.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
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
                    serverPreviewContainer.RectTransform.RelativeSize = new Vector2(0.2f, 1.0f);
                    serverPreviewContainer.Visible = !serverPreviewContainer.Visible;
                    serverPreviewContainer.IgnoreLayoutGroups = !serverPreviewContainer.Visible;

                    RecalculateHolder();

                    btn.Children.ForEach(c => c.SpriteEffects = serverPreviewContainer.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };

            serverPreviewContainer = new GUIFrame(new RectTransform(new Vector2(sidebarWidth, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false
            };
            serverPreview = new GUIListBox(new RectTransform(Vector2.One, serverPreviewContainer.RectTransform, Anchor.Center))
            {
                Padding = Vector4.One * 10 * GUI.Scale
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

            scanServersButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListRefresh"))
            {
				OnClicked = (btn, userdata) => { RefreshServers(); return true; }
			};

            var directJoinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("serverlistdirectjoin"))
            {
                OnClicked = (btn, userdata) => 
                {
                    if (string.IsNullOrWhiteSpace(ClientNameBox.Text))
                    {
                        ClientNameBox.Flash();
                        ClientNameBox.Select();
                        SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                        return false;
                    }
                    ShowDirectJoinPrompt(); 
                    return true; 
                }
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
            servers.Clear();

            if (!File.Exists(file)) { return; }

            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null)
            {
                DebugConsole.NewMessage("Failed to load file \"" + file + "\". Attempting to recreate the file...");
                try
                {
                    doc = new XDocument(new XElement("servers"));
                    doc.Save(file);
                    DebugConsole.NewMessage("Recreated \"" + file + "\".");
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to recreate the file \"" + file + "\".", e);
                }
                return;
            }

            bool saveCleanup = false;
            foreach (XElement element in doc.Root.Elements())
            {
                if (element.Name != "ServerInfo") { continue; }
                var info = ServerInfo.FromXElement(element);
                if (!servers.Any(s => s.Equals(info)))
                {
                    servers.Add(info);
                }
                else
                {
                    saveCleanup = true;
                }
            }
            if (saveCleanup) { WriteServerMemToFile(file, servers); }
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

            doc.SaveSafe(file);
        }

        public ServerInfo UpdateServerInfoWithServerSettings(NetworkConnection endpoint, ServerSettings serverSettings)
        {
            UInt64 steamId = 0;
            string ip = ""; string port = "";
            if (endpoint is SteamP2PConnection steamP2PConnection) { steamId = steamP2PConnection.SteamID; }
            else if (endpoint is LidgrenConnection lidgrenConnection)
            {
                ip = lidgrenConnection.IPString;
                port = lidgrenConnection.Port.ToString();
            }

            bool isInfoNew = false;
            ServerInfo info = serverList.Content.FindChild(d => (d.UserData is ServerInfo serverInfo) &&
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
            info.PlayStyle = serverSettings.PlayStyle;
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
            ServerInfo existingInfo = recentServers.Find(info.MatchesByEndpoint);
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

        public bool IsFavorite(ServerInfo info)
        {
            return favoriteServers.Any(info.MatchesByEndpoint);
        }

        public void AddToFavoriteServers(ServerInfo info)
        {
            info.Favorite = true;
            ServerInfo existingInfo = favoriteServers.Find(info.MatchesByEndpoint);
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
            ServerInfo existingInfo = favoriteServers.Find(info.MatchesByEndpoint);
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
                        if (s1Compatible.HasValue) { s1Compatible = s1Compatible.Value && s1.ContentPackagesMatch(); };

                        bool? s2Compatible = NetworkMember.IsCompatible(GameMain.Version.ToString(), s2.GameVersion);
                        if (!s2.ContentPackageHashes.Any()) { s2Compatible = null; }
                        if (s2Compatible.HasValue) { s2Compatible = s2Compatible.Value && s2.ContentPackagesMatch(); };

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

            ContentPackagesByWorkshopId = ContentPackage.AllPackages
                .Select(p => new KeyValuePair<UInt64, ContentPackage>(p.SteamWorkshopId, p))
                .Where(p => p.Key != 0)
                .GroupBy(x => x.Key).Select(g => g.First())
                .ToImmutableDictionary();
            ContentPackagesByHash = ContentPackage.AllPackages
                .Select(p => new KeyValuePair<string, ContentPackage>(p.MD5hash.Hash, p))
                .GroupBy(x => x.Key).Select(g => g.First())
                .ToImmutableDictionary();

            SelectedTab = ServerListTab.All;
            LoadServerFilters(GameMain.Config.ServerFilterElement);
            if (GameSettings.ShowOffensiveServerPrompt)
            {
                var filterOffensivePrompt = new GUIMessageBox(string.Empty, TextManager.Get("filteroffensiveserversprompt"), new string[] { TextManager.Get("yes"), TextManager.Get("no") });
                filterOffensivePrompt.Buttons[0].OnClicked = (btn, userData) =>
                {
                    filterOffensive.Selected = true;
                    filterOffensivePrompt.Close();
                    return true;
                };
                filterOffensivePrompt.Buttons[1].OnClicked = filterOffensivePrompt.Close;
                GameSettings.ShowOffensiveServerPrompt = false;
            }

            Steamworks.SteamMatchmaking.ResetActions();

            if (GameMain.Client != null)
            {
                GameMain.Client.Disconnect();
                GameMain.Client = null;
            }

            RefreshServers();
        }

        public override void Deselect()
        {
            ContentPackagesByWorkshopId = ImmutableDictionary<UInt64, ContentPackage>.Empty;
            ContentPackagesByHash = ImmutableDictionary<string, ContentPackage>.Empty;
            base.Deselect();

            GameMain.Config.SaveNewPlayerConfig();

            pendingWorkshopDownloads?.Clear();
            workshopDownloadsFrame = null;
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            UpdateFriendsList();
            UpdateInfoQueries();

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

            if (currentlyDownloadingWorkshopItem == null)
            {
                if (pendingWorkshopDownloads?.Any() ?? false)
                {
                    Steamworks.Ugc.Item? item = pendingWorkshopDownloads.Values.FirstOrDefault(it => it.Item != null).Item;
                    if (item != null)
                    {
                        ulong itemId = item.Value.Id;
                        currentlyDownloadingWorkshopItem = item;
                        SteamManager.ForceRedownload(item.Value.Id, () =>
                        {
                            if (!(item?.IsSubscribed ?? false))
                            {
                                TaskPool.Add("SubscribeToServerMod", item?.Subscribe(), (t) => { });
                            }
                            PendingWorkshopDownload clearedDownload = pendingWorkshopDownloads[itemId];
                            pendingWorkshopDownloads.Remove(itemId);
                            currentlyDownloadingWorkshopItem = null;

                            void onInstall(ContentPackage resultingPackage)
                            {
                                if (!resultingPackage.MD5hash.Hash.Equals(clearedDownload.ExpectedHash))
                                {
                                    workshopDownloadsFrame?.FindChild((c) => c.UserData is ulong l && l == itemId, true)?.Flash(GUI.Style.Red);
                                    CancelWorkshopDownloads();
                                    GameMain.Client?.Disconnect();
                                    GameMain.Client = null;
                                    new GUIMessageBox(
                                        TextManager.Get("ConnectionLost"),
                                        TextManager.GetWithVariable("DisconnectMessage.MismatchedWorkshopMod", "[incompatiblecontentpackage]", $"\"{resultingPackage.Name}\" (hash {resultingPackage.MD5hash.ShortHash})"));
                                }
                            }

                            if (SteamManager.CheckWorkshopItemInstalled(item))
                            {
                                SteamManager.UninstallWorkshopItem(item, false, out _);
                            }
                            if (SteamManager.InstallWorkshopItem(item, out string errorMsg, enableContentPackage: false, suppressInstallNotif: true, onInstall: onInstall))
                            {
                                workshopDownloadsFrame?.FindChild((c) => c.UserData is ulong l && l == itemId, true)?.Flash(GUI.Style.Green);
                            }
                            else
                            {
                                workshopDownloadsFrame?.FindChild((c) => c.UserData is ulong l && l == itemId, true)?.Flash(GUI.Style.Red);
                                DebugConsole.ThrowError(errorMsg);
                            }
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(autoConnectEndpoint))
                {
                    JoinServer(autoConnectEndpoint, autoConnectName);
                    autoConnectEndpoint = null;
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
                    ToolBox.VersionNewerIgnoreRevision(GameMain.Version, remoteVersion))
                {
                    child.Visible = false;
                }
                else
                {
                    bool incompatible =
                        (serverInfo.ContentPackageHashes.Any() && !serverInfo.ContentPackagesMatch()) ||
                        (remoteVersion != null && !NetworkMember.IsCompatible(GameMain.Version, remoteVersion));

                    var karmaFilterPassed = filterKarmaValue == TernaryOption.Any|| (filterKarmaValue == TernaryOption.Enabled) == serverInfo.KarmaEnabled;
                    var friendlyFireFilterPassed = filterFriendlyFireValue == TernaryOption.Any || (filterFriendlyFireValue == TernaryOption.Enabled) == serverInfo.FriendlyFireEnabled;
                    var traitorsFilterPassed = filterTraitorValue == TernaryOption.Any || (filterTraitorValue == TernaryOption.Enabled) == (serverInfo.TraitorsEnabled == YesNoMaybe.Yes || serverInfo.TraitorsEnabled == YesNoMaybe.Maybe);
                    var voipFilterPassed = filterVoipValue == TernaryOption.Any || (filterVoipValue == TernaryOption.Enabled) == serverInfo.VoipEnabled;
                    var moddedFilterPassed = filterModdedValue == TernaryOption.Any || (filterModdedValue == TernaryOption.Enabled) == serverInfo.GetPlayStyleTags().Any(t => t.Contains("modded.true"));

                    child.Visible =
                        serverInfo.OwnerVerified &&
                        serverInfo.ServerName.Contains(searchBox.Text, StringComparison.OrdinalIgnoreCase) &&
                        (!filterSameVersion.Selected || (remoteVersion != null && NetworkMember.IsCompatible(remoteVersion, GameMain.Version))) &&
                        (!filterPassword.Selected || !serverInfo.HasPassword) &&
                        (!filterIncompatible.Selected || !incompatible) &&
                        (!filterFull.Selected || serverInfo.PlayerCount < serverInfo.MaxPlayers) &&
                        (!filterEmpty.Selected || serverInfo.PlayerCount > 0) &&
                        (!filterWhitelisted.Selected || serverInfo.UsingWhiteList == true) &&
                        (!filterOffensive.Selected || !ForbiddenWordFilter.IsForbidden(serverInfo.ServerName)) &&
                        karmaFilterPassed &&
                        friendlyFireFilterPassed &&
                        traitorsFilterPassed &&
                        voipFilterPassed &&
                        moddedFilterPassed &&
                        ((selectedTab == ServerListTab.All && (serverInfo.LobbyID != 0 || !string.IsNullOrWhiteSpace(serverInfo.Port))) ||
                         (selectedTab == ServerListTab.Recent && serverInfo.Recent) ||
                         (selectedTab == ServerListTab.Favorites && serverInfo.Favorite));
                }

                foreach (GUITickBox tickBox in playStyleTickBoxes.Values)
                {
                    var playStyle = (PlayStyle)tickBox.UserData;
                    if (!tickBox.Selected && (serverInfo.PlayStyle == playStyle || !serverInfo.PlayStyle.HasValue))
                    {
                        child.Visible = false;
                        break;
                    }
                }

                foreach (GUITickBox tickBox in gameModeTickBoxes.Values)
                {
                    var gameMode = (string)tickBox.UserData;
                    if (!tickBox.Selected && serverInfo.GameMode != null && serverInfo.GameMode.Equals(gameMode, StringComparison.OrdinalIgnoreCase))
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

        private Queue<ServerInfo> pendingQueries = new Queue<ServerInfo>();
        int activeQueries = 0;
        private void QueueInfoQuery(ServerInfo info)
        {
            pendingQueries.Enqueue(info);
        }

        private void OnQueryDone(ServerInfo info)
        {
            activeQueries--;
        }

        public void UpdateInfoQueries()
        {
            while (activeQueries < 25 && pendingQueries.Count > 0)
            {
                activeQueries++;
                var info = pendingQueries.Dequeue();
                info.QueryLiveInfo(UpdateServerInfo, OnQueryDone);
            }
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
                    PlayStyle = null
                };

                var serverFrame = serverList.Content.FindChild(d => (d.UserData is ServerInfo info) &&
                                                                info.MatchesByEndpoint(serverInfo));

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

                QueueInfoQuery(serverInfo);

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
                int framePadding = 5;

                friendPopup = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas));

                var serverNameText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), friendPopup.RectTransform, Anchor.CenterLeft), info.ConnectName ?? "[Unnamed]");
                serverNameText.RectTransform.AbsoluteOffset = new Point(framePadding, 0);

                var joinButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), friendPopup.RectTransform, Anchor.CenterRight), TextManager.Get("ServerListJoin"))
                {
                    UserData = info
                };
                joinButton.OnClicked = JoinFriend;
                joinButton.RectTransform.AbsoluteOffset = new Point(framePadding, 0);

                Point joinButtonTextSize = joinButton.Font.MeasureString(joinButton.Text).ToPoint();
                int joinButtonHeight = joinButton.RectTransform.NonScaledSize.Y;
                int totalAdditionalTextPadding = (joinButtonHeight - joinButtonTextSize.Y);

                // Make the final button sized so that the space between the text and the edges in the X direction is the same as the Y direction.
                Point finalButtonSize = new Point(joinButtonTextSize.X + totalAdditionalTextPadding, joinButtonHeight);

                // Add padding to the server name to match the padding on the button text.
                serverNameText.Padding = new Vector4(totalAdditionalTextPadding / 2);

                // Get the dimensions of the text we want to show, plus the extra padding we added.
                Point serverNameSize = serverNameText.Font.MeasureString(serverNameText.Text).ToPoint() + new Point(totalAdditionalTextPadding, totalAdditionalTextPadding);

                // Now determine how large the parent frame has to be to exactly fit our two controls.
                Point frameDims = new Point(serverNameSize.X + finalButtonSize.X + framePadding*2, Math.Max(serverNameSize.Y, finalButtonSize.Y) + framePadding * 2);

                var popupPos = PlayerInput.MousePosition.ToPoint();
                if(popupPos.X+frameDims.X > GUI.Canvas.NonScaledSize.X)
                {
                    // Prevent the Join button from going off the end of the screen if the server name is long or we click a user towards the edge.
                    popupPos.X = GUI.Canvas.NonScaledSize.X - frameDims.X;
                }

                // Apply the size and position changes.
                friendPopup.RectTransform.NonScaledSize = frameDims;
                friendPopup.RectTransform.RelativeOffset = Vector2.Zero;
                friendPopup.RectTransform.AbsoluteOffset = popupPos;

                joinButton.RectTransform.NonScaledSize = finalButtonSize;

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
                    TaskPool.Add($"Get{avatarSize}AvatarAsync", avatarFunc(friend.Id), (task) =>
                    {
                        if (!task.TryGetResult(out Steamworks.Data.Image? img)) { return; }
                        if (!img.HasValue) { return; }

                        var avatarImage = img.Value;

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
                        DebugConsole.Log($"Failed to parse a Steam friend's connect command ({connectCommand})\n" + e.StackTrace.CleanupStackTrace());
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

            if (SteamManager.IsInitialized)
            {
                TaskPool.Add("WaitForPingDataAsync (serverlist)", Steamworks.SteamNetworkingUtils.WaitForPingDataAsync(), (task) =>
                {
                    steamPingInfoReady = true;
                });
            }

            friendsListUpdateTime = Timing.TotalTime - 1.0;
            UpdateFriendsList();

            serverList.ClearChildren();
            serverPreview.Content.ClearChildren();
            joinButton.Enabled = false;
            selectedServer = null;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), serverList.Content.RectTransform),
                TextManager.Get("RefreshingServerList"), textAlignment: Alignment.Center)
            {
                CanBeFocused = false
            };

            CoroutineManager.StartCoroutine(WaitForRefresh());
        }

        private IEnumerable<CoroutineStatus> WaitForRefresh()
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
                    scanServersButton.Enabled = false;
                }
                else
                {
                    List<ServerInfo> knownServers = recentServers.Concat(favoriteServers).ToList();
                    foreach (ServerInfo info in knownServers)
                    {
                        AddToServerList(info);
                        QueueInfoQuery(info);
                    }
                    scanServersButton.Enabled = true;
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
                    (NetworkMember.IsCompatible(GameMain.Version.ToString(), serverInfo.GameVersion) ?? true) &&
                    serverInfo.ContentPackagesMatch(),
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

            if (serverInfo.ContentPackageNames.Any())
            {
                if (serverInfo.ContentPackageNames.Any(cp => !cp.Equals(GameMain.VanillaContent.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    serverName.TextColor = new Color(219, 125, 217);
                }
            }

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
                    bool listAsIncompatible = false;
                    if (serverInfo.ContentPackageWorkshopIds[i] == 0)
                    {
                        listAsIncompatible = !GameMain.Config.AllEnabledPackages.Any(cp => cp.MD5hash.Hash == serverInfo.ContentPackageHashes[i]);
                    }
                    else
                    {
                        listAsIncompatible = GameMain.Config.AllEnabledPackages.Any(cp => cp.MD5hash.Hash != serverInfo.ContentPackageHashes[i] &&
                                                                                          cp.SteamWorkshopId == serverInfo.ContentPackageWorkshopIds[i]);
                    }
                    if (listAsIncompatible)
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
            else
            {
                string toolTip = "";
                for (int i = 0; i < serverInfo.ContentPackageNames.Count; i++)
                {
                    if (!GameMain.Config.AllEnabledPackages.Any(cp => cp.MD5hash.Hash == serverInfo.ContentPackageHashes[i]))
                    {
                        if (toolTip != "") toolTip += "\n";
                        toolTip += TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", serverInfo.ContentPackageNames[i]);
                        break;
                    }
                }
                serverContent.Children.ForEach(c => c.ToolTip = toolTip);
            }

            serverContent.Recalculate();

            if (serverInfo.Favorite)
            {
                AddToFavoriteServers(serverInfo);
            }

            SortList(sortedBy, toggle: false);
            FilterServers();
        }

        private IEnumerable<CoroutineStatus> EstimateLobbyPing(ServerInfo serverInfo, GUITextBlock serverPingText)
        {
            while (!steamPingInfoReady)
            {
                yield return CoroutineStatus.Running;
            }

            Steamworks.Data.NetPingLocation pingLocation = serverInfo.PingLocation.Value;
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

        private IEnumerable<CoroutineStatus> SendMasterServerRequest()
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
            else if (masterServerResponse.StatusCode != HttpStatusCode.OK)
            {
                serverList.ClearChildren();
                
                switch (masterServerResponse.StatusCode)
                {
                    case HttpStatusCode.NotFound:
                        new GUIMessageBox(TextManager.Get("MasterServerErrorLabel"),
                           TextManager.GetWithVariable("MasterServerError404", "[masterserverurl]", NetConfig.MasterServerUrl));
                        break;
                    case HttpStatusCode.ServiceUnavailable:
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

        public void DownloadWorkshopItems(IEnumerable<PendingWorkshopDownload> downloads, string serverName, string endPointString)
        {
            if (workshopDownloadsFrame != null) { return; }
            int rowCount = downloads.Count() + 2;

            autoConnectName = serverName; autoConnectEndpoint = endPointString;

            workshopDownloadsFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), null, Color.Black * 0.5f);
            currentlyDownloadingWorkshopItem = null;
            pendingWorkshopDownloads = new Dictionary<ulong, PendingWorkshopDownload>();

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f + 0.03f * rowCount), workshopDownloadsFrame.RectTransform, Anchor.Center, Pivot.Center));
            var innerLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, (float)rowCount / (float)(rowCount + 3)), innerFrame.RectTransform, Anchor.Center, Pivot.Center));

            foreach (PendingWorkshopDownload entry in downloads)
            {
                pendingWorkshopDownloads.Add(entry.Id, entry);

                var itemLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f / rowCount), innerLayout.RectTransform), true, Anchor.CenterLeft)
                {
                    UserData = entry.Id
                };
                TaskPool.Add("RetrieveWorkshopItemData", Steamworks.SteamUGC.QueryFileAsync(entry.Id), (t) =>
                {
                    if (t.IsFaulted)
                    {
                        TaskPool.PrintTaskExceptions(t, $"Failed to retrieve Workshop item info (ID {entry.Id})");
                        return;
                    }
                    t.TryGetResult(out Steamworks.Ugc.Item? item);

                    if (!item.HasValue)
                    {
                        DebugConsole.ThrowError($"Failed to find a Steam Workshop item with the ID {entry.Id}.");
                        return;
                    }

                    if (pendingWorkshopDownloads.ContainsKey(entry.Id))
                    {
                        pendingWorkshopDownloads[entry.Id] = new PendingWorkshopDownload(entry.ExpectedHash, item.Value);

                        new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.67f), itemLayout.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft), item.Value.Title);

                        new GUIProgressBar(new RectTransform(new Vector2(0.6f, 0.67f), itemLayout.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft), 0f, Color.Lime)
                        {
                            ProgressGetter = () =>
                            {
                                if (item.Value.IsInstalled) { return 1.0f; }
                                else if (!item.Value.IsDownloading) { return 0.0f; }
                                return item.Value.DownloadAmount;
                            }
                        };
                    }
                });
            }

            var buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 2.0f / rowCount), innerLayout.RectTransform), true, Anchor.CenterLeft)
            {
                UserData = "buttons"
            };

            new GUIButton(new RectTransform(new Vector2(0.3f, 0.67f), buttonLayout.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft), TextManager.Get("Cancel"))
            {
                OnClicked = (btn, obj) =>
                {
                    CancelWorkshopDownloads();
                    return true;
                }
            };
        }

        public void CancelWorkshopDownloads()
        {
            autoConnectEndpoint = null;
            autoConnectName = null;
            pendingWorkshopDownloads.Clear();
            currentlyDownloadingWorkshopItem = null;
            workshopDownloadsFrame = null;
        }

        private bool JoinServer(string endpoint, string serverName)
        {
            if (string.IsNullOrWhiteSpace(ClientNameBox.Text))
            {
                ClientNameBox.Flash();
                ClientNameBox.Select();
                SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                return false;
            }

            GameMain.Config.PlayerName = ClientNameBox.Text;
            GameMain.Config.SaveNewPlayerConfig();

            CoroutineManager.StartCoroutine(ConnectToServer(endpoint, serverName), "ConnectToServer");

            return true;
        }
        
        private IEnumerable<CoroutineStatus> ConnectToServer(string endpoint, string serverName)
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
                if (activePings.ContainsKey(serverInfo.IP)) { return; }
                activePings.Add(serverInfo.IP, activePings.Any() ? activePings.Values.Max()+1 : 0);
            }

            serverInfo.PingChecked = false;
            serverInfo.Ping = -1;

            TaskPool.Add($"PingServerAsync ({serverInfo?.IP ?? "NULL"})", PingServerAsync(serverInfo.IP, 1000),
                new Tuple<ServerInfo, GUITextBlock>(serverInfo, serverPingText),
                (rtt, obj) =>
                {
                    var info = obj.Item1;
                    var text = obj.Item2;
                    rtt.TryGetResult(out info.Ping); info.PingChecked = true;
                    text.TextColor = GetPingTextColor(info.Ping);
                    text.Text = info.Ping > -1 ? info.Ping.ToString() : "?";
                    lock (activePings)
                    {
                        activePings.Remove(info.IP);
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
            bool shouldGo = false;
            while (!shouldGo)
            {
                lock (activePings)
                {
                    shouldGo = activePings.Count(kvp => kvp.Value < activePings[ip]) < 25;
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
                        GameAnalyticsManager.AddErrorEventOnce("ServerListScreen.PingServer:PingException" + ip, GameAnalyticsManager.ErrorSeverity.Warning, "Failed to ping a server - " + (ex?.InnerException?.Message ?? ex.Message));
#if DEBUG
                        DebugConsole.NewMessage("Failed to ping a server (" + ip + ") - " + (ex?.InnerException?.Message ?? ex.Message), Color.Red);
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
            workshopDownloadsFrame?.AddToGUIUpdateList();
        }

        public void SaveServerFilters(XElement element)
        {
            element.RemoveAttributes();
            foreach (KeyValuePair<string, GUITickBox> filterBox in filterTickBoxes)
            {
                element.Add(new XAttribute(filterBox.Key, filterBox.Value.Selected.ToString()));
            }
            foreach (KeyValuePair<string, GUIDropDown> ternaryFilter in ternaryFilters)
            {
                element.Add(new XAttribute(ternaryFilter.Key, ternaryFilter.Value.SelectedData.ToString()));
            }
        }

        public void LoadServerFilters(XElement element)
        {
            if (element == null) { return; }

            foreach (KeyValuePair<string, GUITickBox> filterBox in filterTickBoxes)
            {
                filterBox.Value.Selected = element.GetAttributeBool(filterBox.Key, filterBox.Value.Selected);
            }
            foreach (KeyValuePair<string, GUIDropDown> ternaryFilter in ternaryFilters)
            {
                string valueStr = element.GetAttributeString(ternaryFilter.Key, "");
                TernaryOption ternaryOption = (TernaryOption)ternaryFilter.Value.SelectedData;
                Enum.TryParse<TernaryOption>(valueStr, true, out ternaryOption);

                var child = ternaryFilter.Value.ListBox.Content.GetChildByUserData(ternaryOption);
                ternaryFilter.Value.Select(ternaryFilter.Value.ListBox.Content.GetChildIndex(child));
            }
        }
        
    }
}
