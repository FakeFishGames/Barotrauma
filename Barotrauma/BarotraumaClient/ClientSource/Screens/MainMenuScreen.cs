//#define TEST_REMOTE_CONTENT

using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Tutorials;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using Barotrauma.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Steam;

namespace Barotrauma
{
    class MainMenuScreen : Screen
    {
        private enum Tab
        {
            NewGame = 0,
            LoadGame = 1,
            HostServer = 2,
            Settings = 3,
            Tutorials = 4,
            JoinServer = 5,
            CharacterEditor = 6,
            SubmarineEditor = 7,
            SteamWorkshop = 8,
            Credits = 9,
            Empty = 10
        }

        private readonly GUIComponent buttonsParent;

        private readonly Dictionary<Tab, GUIFrame> menuTabs;

        private SinglePlayerCampaignSetupUI campaignSetupUI;

        private GUITextBox serverNameBox, passwordBox, maxPlayersBox;
        private GUITickBox isPublicBox, wrongPasswordBanBox, karmaBox;
        private GUIDropDown serverExecutableDropdown;
        private readonly GUIButton joinServerButton, hostServerButton;

        private readonly GUIFrame modsButtonContainer;
        private readonly GUIButton modsButton, modUpdatesButton;
        private Task<IReadOnlyList<Steamworks.Ugc.Item>> modUpdateTask;
        private float modUpdateTimer = 0.0f;
        private const float ModUpdateInterval = 60.0f;
        
        private readonly GameMain game;

        private GUIImage playstyleBanner;
        private GUITextBlock playstyleDescription;

        private const string RemoteContentUrl = "http://www.barotraumagame.com/gamedata/";
        private readonly GUIComponent remoteContentContainer;
        private XDocument remoteContentDoc;

        private Tab selectedTab = Tab.Empty;

        private Sprite backgroundSprite;

        private readonly GUIComponent titleText;

        private readonly CreditsPlayer creditsPlayer;

        public static readonly Queue<ulong> WorkshopItemsToUpdate = new Queue<ulong>();

        private GUIImage tutorialBanner;
        private GUITextBlock tutorialHeader, tutorialDescription;
        private GUIListBox tutorialList;

        #region Creation
        public MainMenuScreen(GameMain game)
        {
            GameMain.Instance.ResolutionChanged += () =>
            {
                CreateHostServerFields();
                CreateCampaignSetupUI();
                SettingsMenu.Create(menuTabs[Tab.Settings].RectTransform);
                if (remoteContentDoc?.Root != null)
                {
                    remoteContentContainer.ClearChildren();
                    try
                    {
                        foreach (var subElement in remoteContentDoc.Root.Elements())
                        {
                            GUIComponent.FromXML(subElement.FromContent(ContentPath.Empty), remoteContentContainer.RectTransform);
                        }
                    }
                    catch (Exception e)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Reading received remote main menu content failed.", e);
#endif
                        GameAnalyticsManager.AddErrorEventOnce("MainMenuScreen.RemoteContentParse:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                            "Reading received remote main menu content failed. " + e.Message);
                    }
                }
            };

            new GUIImage(new RectTransform(new Vector2(0.4f, 0.25f), Frame.RectTransform, Anchor.BottomRight)
            { RelativeOffset = new Vector2(0.08f, 0.05f), AbsoluteOffset = new Point(-8, -8) },
                style: "TitleText")
            {
                Color = Color.Black * 0.5f,
                CanBeFocused = false
            };
            titleText = new GUIImage(new RectTransform(new Vector2(0.4f, 0.25f), Frame.RectTransform, Anchor.BottomRight)
            { RelativeOffset = new Vector2(0.08f, 0.05f) },
                style: "TitleText");

            buttonsParent = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.85f), parent: Frame.RectTransform, anchor: Anchor.CenterLeft)
            {
                AbsoluteOffset = new Point(50, 0)
            })
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            remoteContentContainer = new GUIFrame(new RectTransform(Vector2.One, parent: Frame.RectTransform), style: null)
            {
                CanBeFocused = false
            };

#if TEST_REMOTE_CONTENT

            var doc = XMLExtensions.TryLoadXml("Content/UI/MenuContent.xml");
            if (doc?.Root != null)
            {
                foreach (var subElement in doc?.Root.Elements())
                {
                    GUIComponent.FromXML(subElement.FromPackage(null), remoteContentContainer.RectTransform);
                }
            }   
#else
            FetchRemoteContent();
#endif

            float labelHeight = 0.18f;


            // === CAMPAIGN
            var campaignHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1.0f), parent: buttonsParent.RectTransform) { RelativeOffset = new Vector2(0.1f, 0.0f) }, isHorizontal: true);
       
            new GUIImage(new RectTransform(new Vector2(0.2f, 0.7f), campaignHolder.RectTransform), "MainMenuCampaignIcon")
            {
                CanBeFocused = false
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.02f, 0.0f), campaignHolder.RectTransform), style: null);

            var campaignNavigation = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.75f), parent: campaignHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.25f) });

            new GUITextBlock(new RectTransform(new Vector2(1.0f, labelHeight), campaignNavigation.RectTransform),
                TextManager.Get("CampaignLabel"), textAlignment: Alignment.Left, font: GUIStyle.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = ForceUpperCase.Yes };

            var campaignButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: campaignNavigation.RectTransform), style: "MainMenuGUIFrame");

            var campaignList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: campaignButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), campaignList.RectTransform), TextManager.Get("TutorialButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.Tutorials,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), campaignList.RectTransform), TextManager.Get("LoadGameButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.LoadGame,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), campaignList.RectTransform), TextManager.Get("NewGameButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.NewGame,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            // === MULTIPLAYER
            var multiplayerHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1.0f), parent: buttonsParent.RectTransform) { RelativeOffset = new Vector2(0.05f, 0.0f) }, isHorizontal: true);

            new GUIImage(new RectTransform(new Vector2(0.2f, 0.7f), multiplayerHolder.RectTransform), "MainMenuMultiplayerIcon")
            {
                CanBeFocused = false
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.02f, 0.0f), multiplayerHolder.RectTransform), style: null);

            var multiplayerNavigation = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.75f), parent: multiplayerHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.25f) });

            new GUITextBlock(new RectTransform(new Vector2(1.0f, labelHeight), multiplayerNavigation.RectTransform),
                TextManager.Get("MultiplayerLabel"), textAlignment: Alignment.Left, font: GUIStyle.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = ForceUpperCase.Yes };

            var multiplayerButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: multiplayerNavigation.RectTransform), style: "MainMenuGUIFrame");

            var multiplayerList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: multiplayerButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            joinServerButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), multiplayerList.RectTransform), TextManager.Get("JoinServerButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.JoinServer,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };
            hostServerButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), multiplayerList.RectTransform), TextManager.Get("HostServerButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.HostServer,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            // === CUSTOMIZE
            var customizeHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1.0f), parent: buttonsParent.RectTransform) { RelativeOffset = new Vector2(0.15f, 0.0f) }, isHorizontal: true);

            new GUIImage(new RectTransform(new Vector2(0.2f, 0.7f), customizeHolder.RectTransform), "MainMenuCustomizeIcon")
            {
                CanBeFocused = false
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.02f, 0.0f), customizeHolder.RectTransform), style: null);

            var customizeNavigation = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.75f), parent: customizeHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.25f) });

            new GUITextBlock(new RectTransform(new Vector2(1.0f, labelHeight), customizeNavigation.RectTransform),
                TextManager.Get("CustomizeLabel"), textAlignment: Alignment.Left, font: GUIStyle.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = ForceUpperCase.Yes };

            var customizeButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: customizeNavigation.RectTransform), style: "MainMenuGUIFrame");

            var customizeList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: customizeButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            modsButtonContainer = new GUIFrame(new RectTransform(Vector2.One, customizeList.RectTransform),
                style: null);
            
            modsButton = new GUIButton(new RectTransform(Vector2.One, modsButtonContainer.RectTransform),
                TextManager.Get("settingstab.mods"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                Enabled = true,
                UserData = Tab.SteamWorkshop,
                OnClicked = SelectTab
            };

            modUpdatesButton = new GUIButton(new RectTransform(Vector2.One * 0.95f, modsButtonContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                style: "GUIUpdateButton")
            {
                ToolTip = TextManager.Get("ModUpdatesAvailable"),
                OnClicked = (_, _) =>
                {
                    BulkDownloader.PrepareUpdates();
                    return false;
                },
                Visible = false
            };
            
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), customizeList.RectTransform), TextManager.Get("SubEditorButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.SubmarineEditor,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), customizeList.RectTransform), TextManager.Get("CharacterEditorButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.CharacterEditor,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            // === OPTION
            var optionHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), parent: buttonsParent.RectTransform), isHorizontal: true);

            new GUIImage(new RectTransform(new Vector2(0.15f, 0.6f), optionHolder.RectTransform), "MainMenuOptionIcon")
            {
                CanBeFocused = false
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.0f), optionHolder.RectTransform), style: null);

            var optionButtons = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), parent: optionHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.0f) });

            var optionList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.25f), parent: optionButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            var settingsButtonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), style: null);

            new GUIButton(new RectTransform(Vector2.One, settingsButtonContainer.RectTransform), TextManager.Get("SettingsButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.Settings,
                OnClicked = SelectTab
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("EditorDisclaimerWikiLink"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                OnClicked = (button, userData) =>
                {
                    string url = TextManager.Get("EditorDisclaimerWikiUrl").Fallback("https://barotraumagame.com/wiki").Value;
                    GameMain.ShowOpenUrlInWebBrowserPrompt(url, promptExtensionTag: "wikinotice");
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("CreditsButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                UserData = Tab.Credits,
                OnClicked = SelectTab
            };
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("QuitButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = ForceUpperCase.Yes,
                OnClicked = QuitClicked
            };

            //debug button for quickly starting a new round
#if DEBUG
            new GUIButton(new RectTransform(new Point(300, 30), Frame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(40, 80) },
                "Quickstart (dev)", style: "GUIButtonLarge", color: GUIStyle.Red)
            {
                IgnoreLayoutGroups = true,
                UserData = Tab.Empty,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);

                    QuickStart();

                    return true;
                }
            };
            new GUIButton(new RectTransform(new Point(300, 30), Frame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(40, 130) },
                "Profiling", style: "GUIButtonLarge", color: GUIStyle.Red)
            {
                IgnoreLayoutGroups = true,
                UserData = Tab.Empty,
                ToolTip = "Enables performance indicators and starts the game with a fixed sub, crew and level to make it easier to compare the performance between sessions.",
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);

                    QuickStart(fixedSeed: true);
                    GameMain.ShowPerf = true;
                    GameMain.ShowFPS = true;

                    return true;
                }
            };
            new GUIButton(new RectTransform(new Point(300, 30), Frame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(40, 180) },
                "Join Localhost", style: "GUIButtonLarge", color: GUIStyle.Red)
            {
                IgnoreLayoutGroups = true,
                UserData = Tab.Empty,
                ToolTip = "Connects to a locally hosted dedicated server, assuming default port.",
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);

                    GameMain.Client = new GameClient(MultiplayerPreferences.Instance.PlayerName.FallbackNullOrEmpty(SteamManager.GetUsername()),
                                                     new LidgrenEndpoint(IPAddress.Loopback, NetConfig.DefaultPort), "localhost", Option<int>.None());

                    return true;
                }
            };
#endif
            var minButtonSize = new Point(120, 20);
            var maxButtonSize = new Point(480, 80);

            var relativeSize = new Vector2(0.6f, 0.65f);
            var minSize = new Point(600, 400);
            var maxSize = new Point(2000, 1500);
            var anchor = Anchor.CenterRight;
            var pivot = Pivot.CenterRight;
            Vector2 relativeSpacing = new Vector2(0.05f, 0.0f);
            
            menuTabs = new Dictionary<Tab, GUIFrame>();

            menuTabs[Tab.Settings] = new GUIFrame(new RectTransform(new Vector2(relativeSize.X, 0.8f), GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing },
                style: null);
            menuTabs[Tab.Settings].CanBeFocused = false;

            menuTabs[Tab.NewGame] = new GUIFrame(new RectTransform(relativeSize * new Vector2(1.0f, 1.15f), GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });
            menuTabs[Tab.LoadGame] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });

            CreateCampaignSetupUI();

            var hostServerScale = new Vector2(0.7f, 1.2f);
            menuTabs[Tab.HostServer] = new GUIFrame(new RectTransform(
                Vector2.Multiply(relativeSize, hostServerScale), GUI.Canvas, anchor, pivot, minSize.Multiply(hostServerScale), maxSize.Multiply(hostServerScale))
            { RelativeOffset = relativeSpacing });

            CreateHostServerFields();

            //----------------------------------------------------------------------

            menuTabs[Tab.Tutorials] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });
            CreateTutorialTab();

            this.game = game;

            menuTabs[Tab.Credits] = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null)
            {
                CanBeFocused = false
            };
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, menuTabs[Tab.Credits].RectTransform, Anchor.Center), style: "GUIBackgroundBlocker")
            {
                CanBeFocused = false
            };

            var creditsContainer = new GUIFrame(new RectTransform(new Vector2(0.75f, 1.5f), menuTabs[Tab.Credits].RectTransform, Anchor.CenterRight), style: "OuterGlow", color: Color.Black * 0.8f);
            creditsPlayer = new CreditsPlayer(new RectTransform(Vector2.One, creditsContainer.RectTransform), "Content/Texts/Credits.xml");
        }

        private void CreateTutorialTab()
        {
            var tutorialInnerFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), menuTabs[Tab.Tutorials].RectTransform, Anchor.Center), style: "InnerFrame");
            var tutorialContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), tutorialInnerFrame.RectTransform, Anchor.Center), isHorizontal: true) { RelativeSpacing = 0.02f, Stretch = true };

            tutorialList = new GUIListBox(new RectTransform(new Vector2(0.4f, 1.0f), tutorialContent.RectTransform))
            {
                PlaySoundOnSelect = true,
                OnSelected = (component, obj) =>
                {
                    SelectTutorial(obj as Tutorial);
                    return true;
                }
            };
            var tutorialPreview = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), tutorialContent.RectTransform)) { RelativeSpacing = 0.05f, Stretch = true };
            var imageContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), tutorialPreview.RectTransform), style: "InnerFrame");
            tutorialBanner = new GUIImage(new RectTransform(Vector2.One, imageContainer.RectTransform), style: null, scaleToFit: true);

            var infoContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), tutorialPreview.RectTransform), style: "GUIFrameListBox");
            var infoContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), infoContainer.RectTransform, Anchor.Center), childAnchor: Anchor.TopLeft)
            {
                AbsoluteSpacing = GUI.IntScale(10)
            };

            tutorialHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoContent.RectTransform), string.Empty, font: GUIStyle.SubHeadingFont);
            tutorialDescription = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), infoContent.RectTransform), string.Empty, wrap: true);

            var startButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.0f), infoContent.RectTransform, Anchor.BottomRight), text: TextManager.Get("startgamebutton")) 
            { 
                IgnoreLayoutGroups = true,
                OnClicked = (component, obj) =>
                {
                    (tutorialList.SelectedData as Tutorial)?.Start();
                    return true;
                }
            };

            Tutorial firstTutorial = null;
            foreach (var tutorialPrefab in TutorialPrefab.Prefabs.OrderBy(p => p.Order))
            {
                var tutorial = new Tutorial(tutorialPrefab);
                firstTutorial ??= tutorial;
                var tutorialText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), tutorialList.Content.RectTransform), tutorial.DisplayName)
                {
                    Padding = new Vector4(30.0f * GUI.Scale, 0,0,0),
                    UserData = tutorial
                };
                tutorialText.RectTransform.MinSize = new Point(0, (int)(tutorialText.TextSize.Y * 2));
            }
            GUITextBlock.AutoScaleAndNormalize(tutorialList.Content.Children.Select(c => c as GUITextBlock));
            tutorialList.Select(firstTutorial);
        }

        private void SelectTutorial(Tutorial tutorial)
        {
            tutorialHeader.Text = tutorial.DisplayName;
            tutorialHeader.CalculateHeightFromText();
            tutorialDescription.Text = tutorial.Description;
            tutorialDescription.CalculateHeightFromText();
            (tutorialDescription.Parent as GUILayoutGroup)?.Recalculate();
            tutorial.TutorialPrefab.Banner?.EnsureLazyLoaded();
            tutorialBanner.Sprite = tutorial.TutorialPrefab.Banner;
            tutorialBanner.Color = tutorial.TutorialPrefab.Banner == null ? Color.Black : Color.White;
        }

        public static void UpdateInstanceTutorialButtons()
        {
            if (GameMain.MainMenuScreen is not MainMenuScreen menuScreen) { return; }
            menuScreen.tutorialList.ClearChildren();
            menuScreen.CreateTutorialTab();
        }

        #endregion

        #region Selection
        public override void Select()
        {
            ResetModUpdateButton();
            
            if (WorkshopItemsToUpdate.Any())
            {
                while (WorkshopItemsToUpdate.TryDequeue(out ulong workshopId))
                {
                    SteamManager.Workshop.OnItemDownloadComplete(workshopId, forceInstall: true);
                }
            }
            
            GUI.PreventPauseMenuToggle = false;

            base.Select();

            if (GameMain.Client != null)
            {
                GameMain.Client.Quit();
                GameMain.Client = null;
            }

            GameMain.SubEditorScreen?.ClearBackedUpSubInfo();
            Submarine.Unload();
            
            ResetButtonStates(null);
        }

        public override void Deselect()
        {
            base.Deselect();
            SelectTab(null, 0);
        }

        private bool SelectTab(GUIButton button, object obj)
        {
            titleText.Visible = true;
            if (obj is Tab tab)
            {
                SelectTab(tab);
            }
            else
            {
                SelectTab(Tab.Empty);
            }
            return true;
        }

        private bool SelectTab(Tab tab)
        {
            titleText.Visible = true;
            SettingsMenu.Instance?.Close();
            #warning TODO: reimplement settings confirmation dialog

            switch (tab)
            {
                case Tab.NewGame:
                    if (GameSettings.CurrentConfig.TutorialSkipWarning)
                    {
                        selectedTab = Tab.Empty;
                        ShowTutorialSkipWarning(Tab.NewGame);
                        return true;
                    }
                    campaignSetupUI.RandomizeCrew();
                    campaignSetupUI.SetPage(0);
                    campaignSetupUI.CreateDefaultSaveName();
                    campaignSetupUI.RandomizeSeed();
                    campaignSetupUI.UpdateSubList(SubmarineInfo.SavedSubmarines);
                    break;
                case Tab.LoadGame:
                    campaignSetupUI.UpdateLoadMenu();
                    break;
                case Tab.Settings:
                    SettingsMenu.Create(menuTabs[Tab.Settings].RectTransform);
                    break;
                case Tab.JoinServer:
                    if (GameSettings.CurrentConfig.TutorialSkipWarning)
                    {
                        selectedTab = Tab.Empty;
                        ShowTutorialSkipWarning(Tab.JoinServer);
                        return true;
                    }
                    GameMain.ServerListScreen.Select();
                    break;
                case Tab.HostServer:
                    if (GameSettings.CurrentConfig.TutorialSkipWarning)
                    {
                        selectedTab = Tab.Empty;
                        ShowTutorialSkipWarning(tab);
                        return true;
                    }
                    serverExecutableDropdown.ListBox.Content.Children.ToArray()
                        .Where(c => c.UserData is ServerExecutableFile f && !ContentPackageManager.EnabledPackages.All.Contains(f.ContentPackage))
                        .ForEach(serverExecutableDropdown.ListBox.RemoveChild);
                    var newServerExes
                        = ContentPackageManager.EnabledPackages.All.SelectMany(p => p.GetFiles<ServerExecutableFile>())
                            .Where(f => serverExecutableDropdown.ListBox.Content.Children.None(c => c.UserData == f))
                            .ToArray();
                    foreach (var newServerExe in newServerExes)
                    {
                        serverExecutableDropdown.AddItem($"{newServerExe.ContentPackage.Name} - {Path.GetFileNameWithoutExtension(newServerExe.Path.Value)}", userData: newServerExe);
                    }
                    serverExecutableDropdown.ListBox.Content.Children.ForEach(c =>
                    {
                        c.RectTransform.RelativeSize = (1.0f, c.RectTransform.RelativeSize.Y);
                        c.ForceLayoutRecalculation();
                    });
                    bool serverExePickable = serverExecutableDropdown.ListBox.Content.CountChildren > 1;
                    bool wasPickable = serverExecutableDropdown.Parent.Visible;
                    if (wasPickable != serverExePickable)
                    {
                        serverExecutableDropdown.Parent.Visible = serverExePickable;
                        serverExecutableDropdown.Parent.IgnoreLayoutGroups = !serverExePickable;
                        (serverExecutableDropdown.Parent.Parent as GUILayoutGroup)?.Recalculate();
                        if (serverExecutableDropdown.SelectedComponent is null)
                        {
                            serverExecutableDropdown.Select(0);
                        }
                    }
                    break;
                case Tab.Tutorials:
                    UpdateTutorialList();
                    break;
                case Tab.CharacterEditor:
                    Submarine.MainSub = null;
                    CoroutineManager.StartCoroutine(SelectScreenWithWaitCursor(GameMain.CharacterEditorScreen));
                    break;
                case Tab.SubmarineEditor:
                    CoroutineManager.StartCoroutine(SelectScreenWithWaitCursor(GameMain.SubEditorScreen));
                    break;
                case Tab.SteamWorkshop:
                    var settings = SettingsMenu.Create(menuTabs[Tab.Settings].RectTransform);
                    settings.SelectTab(SettingsMenu.Tab.Mods);
                    tab = Tab.Settings;
                    break;
                case Tab.Credits:
                    titleText.Visible = false;
                    creditsPlayer.Restart();
                    break;
                case Tab.Empty:
                    titleText.Visible = true;
                    selectedTab = Tab.Empty;
                    break;
            }

            selectedTab = tab;

            return true;
        }

        private IEnumerable<CoroutineStatus> SelectScreenWithWaitCursor(Screen screen)
        {
            GUI.SetCursorWaiting();
            //tiny delay to get the cursor to render
            yield return new WaitForSeconds(0.02f);
            GUI.ClearCursorWait();
            screen.Select();
            yield return CoroutineStatus.Success;
        }

        public bool ReturnToMainMenu(GUIButton button, object obj)
        {
            GUI.PreventPauseMenuToggle = false;

            if (Selected != this)
            {
                Select();
            }
            else
            {
                ResetButtonStates(button);
            }

            SelectTab(null, 0);

            return true;
        }

        private void ResetButtonStates(GUIButton button)
        {
            foreach (GUIComponent child in buttonsParent.Children)
            {
                GUIButton otherButton = child as GUIButton;
                if (otherButton == null || otherButton == button) continue;

                otherButton.Selected = false;
            }
        }
#endregion

        public void ResetModUpdateButton()
        {
            modUpdateTask = null;
            modUpdateTimer = 0;
            modUpdatesButton.Visible = false;
        }

        public void QuickStart(bool fixedSeed = false, Identifier sub = default, float difficulty = 50, LevelGenerationParams levelGenerationParams = null)
        {
            if (fixedSeed)
            {
                Rand.SetSyncedSeed(1);
                Rand.SetLocalRandom(1);
            }

            SubmarineInfo selectedSub = null;
            Identifier subName = sub.IfEmpty(GameSettings.CurrentConfig.QuickStartSub);
            if (!subName.IsEmpty)
            {
                DebugConsole.NewMessage($"Loading the predefined quick start sub \"{subName}\"", Color.White);

                selectedSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
                if (selectedSub == null)
                {
                    DebugConsole.NewMessage($"Cannot find a sub that matches the name \"{subName}\".", Color.Red);
                }
            }
            if (selectedSub == null)
            {
                DebugConsole.NewMessage("Loading a random sub.", Color.White);
                var subs = SubmarineInfo.SavedSubmarines.Where(s => s.Type == SubmarineType.Player && !s.HasTag(SubmarineTag.Shuttle) && !s.HasTag(SubmarineTag.HideInMenus));
                selectedSub = subs.ElementAt(Rand.Int(subs.Count()));
            }
            var gamesession = new GameSession(
                selectedSub,
                GameModePreset.DevSandbox,
                missionPrefabs: null);
            //(gamesession.GameMode as SinglePlayerCampaign).GenerateMap(ToolBox.RandomSeed(8));
            gamesession.StartRound(fixedSeed ? "abcd" : ToolBox.RandomSeed(8), difficulty, levelGenerationParams);
            GameMain.GameScreen.Select();
            // TODO: modding support
            Identifier[] jobIdentifiers = new Identifier[] { 
                "captain".ToIdentifier(), 
                "engineer".ToIdentifier(), 
                "mechanic".ToIdentifier(), 
                "securityofficer".ToIdentifier(), 
                "medicaldoctor".ToIdentifier() };
            foreach (Identifier job in jobIdentifiers)
            {
                var jobPrefab = JobPrefab.Get(job);
                var variant = Rand.Range(0, jobPrefab.Variants);
                var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: jobPrefab, variant: variant);
                if (characterInfo.Job == null)
                {
                    DebugConsole.ThrowError("Failed to find the job \"" + job + "\"!");
                }
                gamesession.CrewManager.AddCharacterInfo(characterInfo);
            }
            gamesession.CrewManager.InitSinglePlayerRound();
        }

        private void ShowTutorialSkipWarning(Tab tabToContinueTo)
        {
            var tutorialSkipWarning = new GUIMessageBox("", TextManager.Get("tutorialskipwarning"), new LocalizedString[] { TextManager.Get("tutorialwarningskiptutorials"), TextManager.Get("tutorialwarningplaytutorials") });
            
            GUIButton.OnClickedHandler proceedToTab(Tab tab)
                => (btn, userdata) =>
                {
                    var config = GameSettings.CurrentConfig;
                    config.TutorialSkipWarning = false;
                    GameSettings.SetCurrentConfig(config);
                    GameSettings.SaveCurrentConfig();
                    tutorialSkipWarning.Close();
                    SelectTab(tab);
                    return true;
                };

            tutorialSkipWarning.Buttons[0].OnClicked += proceedToTab(tabToContinueTo);
            tutorialSkipWarning.Buttons[1].OnClicked += proceedToTab(Tab.Tutorials);
        }

        private void UpdateTutorialList()
        {
            foreach (GUITextBlock tutorialText in tutorialList.Content.Children)
            {
                var tutorial = (Tutorial)tutorialText.UserData;
                if (CompletedTutorials.Instance.Contains(tutorial.Identifier) && tutorialText.GetChild<GUIImage>() == null)
                {
                    new GUIImage(new RectTransform(new Point((int)(tutorialText.Padding.X * 0.8f)), tutorialText.RectTransform, Anchor.CenterLeft), style: "ObjectiveIndicatorCompleted");
                }
            }
        }

        private bool ChangeMaxPlayers(GUIButton button, object obj)
        {
            int.TryParse(maxPlayersBox.Text, out int currMaxPlayers);
            currMaxPlayers = (int)MathHelper.Clamp(currMaxPlayers + (int)button.UserData, 1, NetConfig.MaxPlayers);
            maxPlayersBox.Text = currMaxPlayers.ToString();
            return true;
        }

        private void TryStartServer()
        {
            if (SubmarineInfo.SavedSubmarines.Any(s => s.CalculatingHash))
            {
                var waitBox = new GUIMessageBox(TextManager.Get("pleasewait"), TextManager.Get("waitforsubmarinehashcalculations"), new LocalizedString[] { TextManager.Get("cancel") });
                var waitCoroutine = CoroutineManager.StartCoroutine(WaitForSubmarineHashCalculations(waitBox), "WaitForSubmarineHashCalculations");
                waitBox.Buttons[0].OnClicked += (btn, userdata) =>
                {
                    CoroutineManager.StopCoroutines(waitCoroutine);
                    return true;
                };
            }
            else
            {
                StartServer();
            }
        }

        private IEnumerable<CoroutineStatus> WaitForSubmarineHashCalculations(GUIMessageBox messageBox)
        {
            LocalizedString originalText = messageBox.Text.Text;
            int doneCount = 0;
            do
            {
                doneCount = SubmarineInfo.SavedSubmarines.Count(s => !s.CalculatingHash);
                messageBox.Text.Text = originalText + $" ({doneCount}/{SubmarineInfo.SavedSubmarines.Count()})";
                yield return CoroutineStatus.Running;
            } while (doneCount < SubmarineInfo.SavedSubmarines.Count());
            messageBox.Close();
            StartServer();
            yield return CoroutineStatus.Success;
        }

        private void StartServer()
        {
            string name = serverNameBox.Text;

            GameMain.ResetNetLobbyScreen();
            try
            {
                string exeName = serverExecutableDropdown.SelectedComponent?.UserData is ServerExecutableFile f ? f.Path.Value : "DedicatedServer";

                string arguments = "-name \"" + ToolBox.EscapeCharacters(name) + "\"" +
                                   " -public " + isPublicBox.Selected.ToString() +
                                   " -playstyle " + ((PlayStyle)playstyleBanner.UserData).ToString()  +
                                   " -banafterwrongpassword " + wrongPasswordBanBox.Selected.ToString() +
                                   " -karmaenabled " + (!karmaBox.Selected).ToString() +
                                   " -maxplayers " + maxPlayersBox.Text;

                if (!string.IsNullOrWhiteSpace(passwordBox.Text))
                {
                    arguments += " -password \"" + ToolBox.EscapeCharacters(passwordBox.Text) + "\"";
                }
                else
                {
                    arguments += " -nopassword";
                }

                if (SteamManager.GetSteamId().TryUnwrap(out var steamId1))
                {
                    arguments += " -steamid " + steamId1.Value;
                }
                int ownerKey = Math.Max(CryptoRandom.Instance.Next(), 1);
                arguments += " -ownerkey " + ownerKey;

                string filename = Path.Combine(
                    Path.GetDirectoryName(exeName),
                    Path.GetFileNameWithoutExtension(exeName));
#if WINDOWS
                filename += ".exe";
#else
                filename = "./" + exeName;
#endif
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = arguments,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
#if !DEBUG
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
#endif
                };
                ChildServerRelay.Start(processInfo);
                Thread.Sleep(1000); //wait until the server is ready before connecting

                GameMain.Client = new GameClient(MultiplayerPreferences.Instance.PlayerName.FallbackNullOrEmpty(
                    SteamManager.GetUsername().FallbackNullOrEmpty(name)),
                    SteamManager.GetSteamId().TryUnwrap(out var steamId)
                        ? new SteamP2PEndpoint(steamId)
                        : (Endpoint)new LidgrenEndpoint(IPAddress.Loopback, NetConfig.DefaultPort),
                    name,
                    Option<int>.Some(ownerKey));
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start server", e);
            }
        }

        private bool QuitClicked(GUIButton button, object obj)
        {
            game.Exit();
            return true;
        }

        public override void AddToGUIUpdateList()
        {
            Frame.AddToGUIUpdateList();
            if (selectedTab < Tab.Empty && menuTabs.TryGetValue(selectedTab, out GUIFrame tab) && tab != null)
            {
                tab.AddToGUIUpdateList();
                switch (selectedTab)
                {
                    case Tab.NewGame:
                        campaignSetupUI.CharacterMenus?.ForEach(m => m.AddToGUIUpdateList());
                        break;
                }
            }
        }

        public override void Update(double deltaTime)
        {
            modUpdateTimer -= (float)deltaTime;
            if (modUpdateTimer <= 0.0f && modUpdateTask is not { IsCompleted: false })
            {
                modUpdateTask = BulkDownloader.GetItemsThatNeedUpdating();
                modUpdateTimer = ModUpdateInterval;
            }

#if DEBUG
            hostServerButton.Enabled = true;
#else
            if (GameSettings.CurrentConfig.UseSteamMatchmaking)
            {
                hostServerButton.Enabled = SteamManager.IsInitialized;
            }
#endif

            if (modUpdateTask is { IsCompletedSuccessfully: true })
            {
                modUpdatesButton.Visible = modUpdateTask.Result.Count > 0;
            }

            if (modUpdatesButton.Visible)
            {
                var modButtonLabelSize =
                    modsButton.Font.MeasureString(modsButton.Text).ToPoint()
                    + new Point(GUI.IntScale(25));
                modUpdatesButton.RectTransform.AbsoluteOffset =
                    (modButtonLabelSize.X, modsButton.Rect.Height / 2 - modUpdatesButton.Rect.Height / 2);
            }
            
            switch (selectedTab)
            {
                case Tab.NewGame:
                    campaignSetupUI.Update();
                    break;
            }
        }

        public void DrawBackground(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);

            if (backgroundSprite == null)
            {
#if UNSTABLE
                backgroundSprite = new Sprite("Content/UnstableBackground.png", sourceRectangle: null);
#endif
                backgroundSprite ??= (LocationType.Prefabs.Where(l => l.UseInMainMenu).GetRandomUnsynced())?.GetPortrait(0);
            }

            if (backgroundSprite != null)
            {
                GUI.DrawBackgroundSprite(spriteBatch, backgroundSprite,
                    aberrationStrength: 0.0f);
            }

            var vignette = GUIStyle.GetComponentStyle("mainmenuvignette")?.GetDefaultSprite();
            if (vignette != null)
            {
                spriteBatch.Begin(blendState: BlendState.NonPremultiplied);
                vignette.Draw(spriteBatch, Vector2.Zero, Color.White, Vector2.Zero, 0.0f, 
                    new Vector2(GameMain.GraphicsWidth / vignette.size.X, GameMain.GraphicsHeight / vignette.size.Y));
                spriteBatch.End();
            }
        }

        readonly LocalizedString[] legalCrap = new LocalizedString[]
        {
            TextManager.Get("privacypolicy").Fallback("Privacy policy"),
            "© " + DateTime.Now.Year + " Undertow Games & FakeFish. All rights reserved.",
            "© " + DateTime.Now.Year + " Daedalic Entertainment GmbH. The Daedalic logo is a trademark of Daedalic Entertainment GmbH, Germany. All rights reserved."
        };

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            DrawBackground(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);

            GUI.Draw(Cam, spriteBatch);

            if (selectedTab != Tab.Credits)
            {
#if !UNSTABLE
                string versionString = "Barotrauma v" + GameMain.Version + " (" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")";
                GUIStyle.SmallFont.DrawString(spriteBatch, versionString, new Vector2(HUDLayoutSettings.Padding, GameMain.GraphicsHeight - GUIStyle.SmallFont.MeasureString(versionString).Y - HUDLayoutSettings.Padding * 0.75f), Color.White * 0.7f);
#endif
                LocalizedString gameAnalyticsStatus = TextManager.Get($"GameAnalyticsStatus.{GameAnalyticsManager.UserConsented}");
                Vector2 textSize = GUIStyle.SmallFont.MeasureString(gameAnalyticsStatus).ToPoint().ToVector2();
                GUIStyle.SmallFont.DrawString(spriteBatch, gameAnalyticsStatus, new Vector2(HUDLayoutSettings.Padding, GameMain.GraphicsHeight - GUIStyle.SmallFont.LineHeight * 2 - HUDLayoutSettings.Padding * 0.75f), Color.White * 0.7f);


                Vector2 textPos = new Vector2(GameMain.GraphicsWidth - HUDLayoutSettings.Padding, GameMain.GraphicsHeight - HUDLayoutSettings.Padding * 0.75f);
                for (int i = legalCrap.Length - 1; i >= 0; i--)
                {
                    textSize = GUIStyle.SmallFont.MeasureString(legalCrap[i])
                        .ToPoint().ToVector2();
                    bool mouseOn = i == 0 &&
                                   PlayerInput.MousePosition.X > textPos.X - textSize.X && PlayerInput.MousePosition.X < textPos.X &&
                                   PlayerInput.MousePosition.Y > textPos.Y - textSize.Y && PlayerInput.MousePosition.Y < textPos.Y;

                    GUIStyle.SmallFont.DrawString(spriteBatch,
                        legalCrap[i], textPos - textSize,
                        mouseOn ? Color.White : Color.White * 0.7f);

                    if (i == 0)
                    {
                        GUI.DrawLine(spriteBatch, textPos, textPos - Vector2.UnitX * textSize.X, mouseOn ? Color.White : Color.White * 0.7f);
                        if (mouseOn && PlayerInput.PrimaryMouseButtonClicked())
                        {
                            GameMain.ShowOpenUrlInWebBrowserPrompt("http://privacypolicy.daedalic.com");
                        }
                    }
                    textPos.Y -= textSize.Y;
                }
            }

            spriteBatch.End();
        }

        private void StartGame(SubmarineInfo selectedSub, string savePath, string mapSeed, CampaignSettings settings)
        {
            if (string.IsNullOrEmpty(savePath)) { return; }

            var existingSaveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);
            if (existingSaveFiles.Any(s => s.FilePath == savePath))
            {
                new GUIMessageBox(TextManager.Get("SaveNameInUseHeader"), TextManager.Get("SaveNameInUseText"));
                return;
            }

            if (selectedSub == null)
            {
                new GUIMessageBox(TextManager.Get("SubNotSelected"), TextManager.Get("SelectSubRequest"));
                return;
            }

            if (!Directory.Exists(SaveUtil.TempPath))
            {
                Directory.CreateDirectory(SaveUtil.TempPath);
            }

            try
            {
                File.Copy(selectedSub.FilePath, Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"), true);
            }
            catch (System.IO.IOException e)
            {
                DebugConsole.ThrowError("Copying the file \"" + selectedSub.FilePath + "\" failed. The file may have been deleted or in use by another process. Try again or select another submarine.", e);
                GameAnalyticsManager.AddErrorEventOnce(
                    "MainMenuScreen.StartGame:IOException" + selectedSub.Name,
                    GameAnalyticsManager.ErrorSeverity.Error,
                    "Copying a submarine file failed. " + e.Message + "\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            selectedSub = new SubmarineInfo(Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"));
            
            GameMain.GameSession = new GameSession(selectedSub, savePath, GameModePreset.SinglePlayerCampaign, settings, mapSeed);
            GameMain.GameSession.CrewManager.CharacterInfos.Clear();
            foreach (var characterInfo in campaignSetupUI.CharacterMenus.Select(m => m.CharacterInfo))
            {
                GameMain.GameSession.CrewManager.AddCharacterInfo(characterInfo);
            }
            ((SinglePlayerCampaign)GameMain.GameSession.GameMode).LoadNewLevel();
        }

        private void LoadGame(string saveFile)
        {
            if (string.IsNullOrWhiteSpace(saveFile)) return;

            try
            {
                SaveUtil.LoadGame(saveFile);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading save \"" + saveFile + "\" failed", e);
                return;
            }

            //TODO
            //GameMain.LobbyScreen.Select();
        }

#region UI Methods
        private void CreateCampaignSetupUI()
        {
            menuTabs[Tab.NewGame].ClearChildren();
            menuTabs[Tab.LoadGame].ClearChildren();

            var innerNewGame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), menuTabs[Tab.NewGame].RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var newGameContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.95f), innerNewGame.RectTransform, Anchor.Center),
                style: "InnerFrame");

            var paddedLoadGame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), menuTabs[Tab.LoadGame].RectTransform, Anchor.Center) { AbsoluteOffset = new Point(0, 10) },
                style: null);

            campaignSetupUI = new SinglePlayerCampaignSetupUI(newGameContent, paddedLoadGame, SubmarineInfo.SavedSubmarines)
            {
                LoadGame = LoadGame,
                StartNewGame = StartGame
            };
        }

        private void CreateHostServerFields()
        {
            menuTabs[Tab.HostServer].ClearChildren();

            string name = "";
            string password = "";
            int maxPlayers = 8;
            bool isPublic = true;
            bool banAfterWrongPassword = false;
            bool karmaEnabled = true;
            string selectedKarmaPreset = "";
            PlayStyle selectedPlayStyle = PlayStyle.Casual;
            if (File.Exists(ServerSettings.SettingsFile))
            {
                XDocument settingsDoc = XMLExtensions.TryLoadXml(ServerSettings.SettingsFile);
                if (settingsDoc != null)
                {
                    name = settingsDoc.Root.GetAttributeString("name", name);
                    password = settingsDoc.Root.GetAttributeString("password", password);
                    isPublic = settingsDoc.Root.GetAttributeBool("public", isPublic);
                    banAfterWrongPassword = settingsDoc.Root.GetAttributeBool("banafterwrongpassword", banAfterWrongPassword);

                    int maxPlayersElement = settingsDoc.Root.GetAttributeInt("maxplayers", maxPlayers);
                    if (maxPlayersElement > NetConfig.MaxPlayers)
                    {
                        DebugConsole.IsOpen = true;
                        DebugConsole.NewMessage($"Setting the maximum amount of players to {maxPlayersElement} failed due to exceeding the limit of {NetConfig.MaxPlayers} players per server. Using the maximum of {NetConfig.MaxPlayers} instead.", Color.Red);
                        maxPlayersElement = NetConfig.MaxPlayers;
                    }

                    maxPlayers = maxPlayersElement;
                    karmaEnabled = settingsDoc.Root.GetAttributeBool("karmaenabled", true);
                    selectedKarmaPreset = settingsDoc.Root.GetAttributeString("karmapreset", "default");
                    string playStyleStr = settingsDoc.Root.GetAttributeString("playstyle", "Casual");
                    Enum.TryParse(playStyleStr, out selectedPlayStyle);
                }
            }

            Vector2 textLabelSize = new Vector2(1.0f, 0.05f);
            Alignment textAlignment = Alignment.CenterLeft;
            Vector2 textFieldSize = new Vector2(0.5f, 1.0f);
            Vector2 tickBoxSize = new Vector2(0.4f, 0.04f);
            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 0.9f), menuTabs[Tab.HostServer].RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };
            GUIComponent parent = content;

            var header = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("HostServerButton"), textAlignment: Alignment.Center, font: GUIStyle.LargeFont) { ForceUpperCase = ForceUpperCase.Yes };
            header.RectTransform.IsFixedSize = true;

            //play style -----------------------------------------------------

            var playstyleContainer = new GUIFrame(new RectTransform(new Vector2(1.35f, 0.1f), parent.RectTransform), style: null, color: Color.Black);

            playstyleBanner = new GUIImage(new RectTransform(new Vector2(1.0f, 0.1f), playstyleContainer.RectTransform), 
                GUIStyle.GetComponentStyle($"PlayStyleBanner.{PlayStyle.Serious}").GetSprite(GUIComponent.ComponentState.None), scaleToFit: true)
            {
                UserData = PlayStyle.Serious
            };
            float bannerAspectRatio = (float) playstyleBanner.Sprite.SourceRect.Width / playstyleBanner.Sprite.SourceRect.Height;
            playstyleBanner.RectTransform.NonScaledSize = new Point(playstyleBanner.Rect.Width, (int)(playstyleBanner.Rect.Width / bannerAspectRatio));
            playstyleBanner.RectTransform.IsFixedSize = true;
            new GUIFrame(new RectTransform(Vector2.One, playstyleBanner.RectTransform), "InnerGlow", color: Color.Black);

            new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.05f), playstyleBanner.RectTransform) { RelativeOffset = new Vector2(0.01f, 0.03f) },
                "playstyle name goes here", font: GUIStyle.SmallFont, textAlignment: Alignment.Center, textColor: Color.White, style: "GUISlopedHeader");
            
            new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), playstyleContainer.RectTransform, Anchor.CenterLeft) 
                { RelativeOffset = new Vector2(0.02f, 0.0f), MaxSize = new Point(int.MaxValue, (int)(150 * GUI.Scale)) }, 
                style: "UIToggleButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    int playStyleIndex = (int)playstyleBanner.UserData - 1;
                    if (playStyleIndex < 0) { playStyleIndex = Enum.GetValues(typeof(PlayStyle)).Length - 1; }
                    SetServerPlayStyle((PlayStyle)playStyleIndex);
                    return true;
                }
            }.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);

            new GUIButton(new RectTransform(new Vector2(0.05f, 1.0f), playstyleContainer.RectTransform, Anchor.CenterRight) 
                { RelativeOffset = new Vector2(0.02f, 0.0f), MaxSize = new Point(int.MaxValue, (int)(150 * GUI.Scale)) },
                style: "UIToggleButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    int playStyleIndex = (int)playstyleBanner.UserData + 1;
                    if (playStyleIndex >= Enum.GetValues(typeof(PlayStyle)).Length) { playStyleIndex = 0; }
                    SetServerPlayStyle((PlayStyle)playStyleIndex);
                    return true;
                }
            };

            LocalizedString longestPlayStyleStr = "";
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                LocalizedString playStyleStr = TextManager.Get("servertagdescription." + playStyle);
                if (playStyleStr.Length > longestPlayStyleStr.Length) { longestPlayStyleStr = playStyleStr; }
            }

            playstyleDescription = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), playstyleContainer.RectTransform, Anchor.BottomCenter),
                longestPlayStyleStr, style: null, wrap: true)
            {
                Color = Color.Black * 0.8f,
                TextColor = GUIStyle.GetComponentStyle("GUITextBlock").TextColor
            };
            playstyleDescription.Padding = Vector4.One * 10.0f * GUI.Scale;
            playstyleDescription.CalculateHeightFromText(padding: (int)(15 * GUI.Scale));
            playstyleDescription.RectTransform.NonScaledSize = new Point(playstyleDescription.Rect.Width, playstyleDescription.Rect.Height);
            playstyleDescription.RectTransform.IsFixedSize = true;
            playstyleContainer.RectTransform.NonScaledSize = new Point(playstyleContainer.Rect.Width, playstyleBanner.Rect.Height + playstyleDescription.Rect.Height);
            playstyleContainer.RectTransform.IsFixedSize = true;

            SetServerPlayStyle(selectedPlayStyle);

            //other settings -----------------------------------------------------

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), style: null);

            var label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("ServerName"), textAlignment: textAlignment);
            serverNameBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), text: name, textAlignment: textAlignment)
            { 
                MaxTextLength = NetConfig.ServerNameMaxLength,
                OverflowClip = true
            };
            label.RectTransform.IsFixedSize = true;

            var maxPlayersLabel = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("MaxPlayers"), textAlignment: textAlignment);
            var buttonContainer = new GUILayoutGroup(new RectTransform(textFieldSize, maxPlayersLabel.RectTransform, Anchor.CenterRight), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.1f
            };
            new GUIButton(new RectTransform(Vector2.One, buttonContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIMinusButton", textAlignment: Alignment.Center)
            {
                UserData = -1,
                OnClicked = ChangeMaxPlayers,
                ClickSound = GUISoundType.Decrease
            };
            maxPlayersBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 1.0f), buttonContainer.RectTransform), textAlignment: Alignment.Center)
            {
                Text = maxPlayers.ToString()                
            };
            maxPlayersBox.OnEnterPressed += (GUITextBox sender, string text) =>
            {
                maxPlayersBox.Deselect();
                return true;
            };
            maxPlayersBox.OnDeselected += (GUITextBox sender, Microsoft.Xna.Framework.Input.Keys key) =>
            {
                int.TryParse(maxPlayersBox.Text, out int currMaxPlayers);
                currMaxPlayers = (int)MathHelper.Clamp(currMaxPlayers, 1, NetConfig.MaxPlayers);
                maxPlayersBox.Text = currMaxPlayers.ToString();
            };
            new GUIButton(new RectTransform(Vector2.One, buttonContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIPlusButton", textAlignment: Alignment.Center)
            {
                UserData = 1,
                OnClicked = ChangeMaxPlayers,
                ClickSound = GUISoundType.Increase
            };
            maxPlayersLabel.RectTransform.IsFixedSize = true;

            label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("Password"), textAlignment: textAlignment);
            passwordBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), text: password, textAlignment: textAlignment)
            {
                Censor = true
            };
            label.RectTransform.IsFixedSize = true;

            var serverExecutableLabel = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform),
                TextManager.Get("ServerExecutable"), textAlignment: textAlignment);
            const string vanillaServerOption = "Vanilla";
            serverExecutableDropdown
                = new GUIDropDown(new RectTransform(textFieldSize, serverExecutableLabel.RectTransform, Anchor.CenterRight),
                    vanillaServerOption);
            var listBoxSize = serverExecutableDropdown.ListBox.RectTransform.RelativeSize;
            serverExecutableDropdown.ListBox.RectTransform.RelativeSize = new Vector2(listBoxSize.X * 1.5f, listBoxSize.Y);
            serverExecutableDropdown.AddItem(vanillaServerOption, userData: null);
            serverExecutableDropdown.OnSelected = (selected, userData) =>
            {
                if (userData != null)
                {
                    var warningBox = new GUIMessageBox(headerText: TextManager.Get("Warning"),
                        text: TextManager.GetWithVariable("ModServerExesAtYourOwnRisk", "[exename]", serverExecutableDropdown.Text),
                        new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });
                    warningBox.Buttons[0].OnClicked = (_, __) =>
                    {
                        warningBox.Close();
                        return false;
                    };
                    warningBox.Buttons[1].OnClicked = (_, __) =>
                    {
                        serverExecutableDropdown.Select(0);
                        warningBox.Close();
                        return false;
                    };
                }
                
                serverExecutableDropdown.Text = ToolBox.LimitString(serverExecutableDropdown.Text,
                    serverExecutableDropdown.Font, serverExecutableDropdown.Rect.Width * 8 / 10);

                return true;
            };
            serverExecutableLabel.RectTransform.IsFixedSize = true;

            // tickbox upper ---------------

            var tickboxAreaUpper = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, tickBoxSize.Y), parent.RectTransform), isHorizontal: true);

            isPublicBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaUpper.RectTransform), TextManager.Get("PublicServer"))
            {
                Selected = isPublic,
                ToolTip = TextManager.Get("PublicServerToolTip")
            };

            wrongPasswordBanBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaUpper.RectTransform), TextManager.Get("ServerSettingsBanAfterWrongPassword"))
            {
                Selected = banAfterWrongPassword
            };

            tickboxAreaUpper.RectTransform.IsFixedSize = true;

            // tickbox lower ---------------

            var tickboxAreaLower = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, tickBoxSize.Y), parent.RectTransform), isHorizontal: true);

            karmaBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaLower.RectTransform), TextManager.Get("HostServerKarmaSetting"))
            {
                Selected = !karmaEnabled,
                ToolTip = TextManager.Get("hostserverkarmasettingtooltip")
            };

            tickboxAreaLower.RectTransform.IsFixedSize = true;

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), style: null);

            new GUIButton(new RectTransform(new Vector2(0.4f, 0.07f), content.RectTransform), TextManager.Get("StartServerButton"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, userdata) =>
                {
                    string name = serverNameBox.Text;
                    if (string.IsNullOrEmpty(name))
                    {
                        serverNameBox.Flash();
                        return false;
                    }

                    if (isPublicBox.Selected && ForbiddenWordFilter.IsForbidden(name, out string forbiddenWord))
                    {
                        var msgBox = new GUIMessageBox("", 
                            TextManager.GetWithVariables("forbiddenservernameverification", ("[forbiddenword]", forbiddenWord), ("[servername]", name)), 
                            new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") });
                        msgBox.Buttons[0].OnClicked += (_, __) =>
                        {
                            TryStartServer();
                            msgBox.Close();
                            return true;
                        };
                        msgBox.Buttons[1].OnClicked += msgBox.Close;
                    }
                    else
                    {
                        TryStartServer();
                    }

                    return true;
                }
            };
        }

        private void SetServerPlayStyle(PlayStyle playStyle)
        {
            playstyleBanner.Sprite = GUIStyle
                .GetComponentStyle($"PlayStyleBanner.{playStyle}")
                .GetSprite(GUIComponent.ComponentState.None);
            playstyleBanner.UserData = playStyle;

            var nameText = playstyleBanner.GetChild<GUITextBlock>();
            nameText.Text = TextManager.AddPunctuation(':', TextManager.Get("serverplaystyle"), TextManager.Get("servertag." + playStyle));
            nameText.Color = playstyleBanner.Sprite
                .SourceElement.GetAttributeColor("BannerColor") ?? Color.White;
            nameText.RectTransform.NonScaledSize = (nameText.Font.MeasureString(nameText.Text) + new Vector2(25, 10) * GUI.Scale).ToPoint();

            playstyleDescription.Text = TextManager.Get("servertagdescription." + playStyle);
            playstyleDescription.TextAlignment = playstyleDescription.WrappedText.Contains('\n') ?
               Alignment.CenterLeft : Alignment.Center;
        }
#endregion

        private void FetchRemoteContent()
        {
            if (string.IsNullOrEmpty(RemoteContentUrl)) { return; }
            try
            {
                var client = new RestClient(RemoteContentUrl);
                var request = new RestRequest("MenuContent.xml", Method.GET);
                TaskPool.Add("RequestMainMenuRemoteContent", client.ExecuteAsync(request),
                    RemoteContentReceived);
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Fetching remote content to the main menu failed.", e);
#endif
                GameAnalyticsManager.AddErrorEventOnce("MainMenuScreen.FetchRemoteContent:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                    "Fetching remote content to the main menu failed. " + e.Message);
                return;
            }
        }

        private void RemoteContentReceived(Task t)
        {
            try
            {
                if (!t.TryGetResult(out IRestResponse remoteContentResponse)) { throw new Exception("Task did not return a valid result"); }
                string xml = remoteContentResponse.Content;
                int index = xml.IndexOf('<');
                if (index > 0) { xml = xml.Substring(index, xml.Length - index); }
                if (!string.IsNullOrWhiteSpace(xml))
                {
                    remoteContentDoc = XDocument.Parse(xml);
                    foreach (var subElement in remoteContentDoc?.Root.Elements())
                    {
                        GUIComponent.FromXML(subElement.FromContent(ContentPath.Empty), remoteContentContainer.RectTransform);
                    }
                }
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Reading received remote main menu content failed.", e);
#endif
                GameAnalyticsManager.AddErrorEventOnce("MainMenuScreen.RemoteContentReceived:Exception", GameAnalyticsManager.ErrorSeverity.Error,
                    "Reading received remote main menu content failed. " + e.Message);
            }
        }
    }
}
