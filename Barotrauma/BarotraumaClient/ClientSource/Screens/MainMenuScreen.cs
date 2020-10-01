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
using System.Diagnostics;
using Barotrauma.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma
{
    class MainMenuScreen : Screen
    {
        public enum Tab { NewGame = 1, LoadGame = 2, HostServer = 3, Settings = 4, Tutorials = 5, JoinServer = 6, CharacterEditor = 7, SubmarineEditor = 8, QuickStartDev = 9, ProfilingTestBench = 10, SteamWorkshop = 11, Credits = 12, Empty = 13 }

        private readonly GUIComponent buttonsParent;

        private readonly GUIFrame[] menuTabs;

        private CampaignSetupUI campaignSetupUI;

        private GUITextBox serverNameBox, /*portBox, queryPortBox,*/ passwordBox, maxPlayersBox;
        private GUITickBox isPublicBox, wrongPasswordBanBox, karmaEnabledBox;
        private GUIDropDown karmaPresetDD;
        private readonly GUIFrame downloadingModsContainer, enableModsContainer;
        private readonly GUIButton joinServerButton, hostServerButton, steamWorkshopButton;
        private readonly GameMain game;

        private GUIImage playstyleBanner;
        private GUITextBlock playstyleDescription;

        private Tab selectedTab;

        private Sprite backgroundSprite;

        private readonly GUIComponent titleText;

        private readonly CreditsPlayer creditsPlayer;

#if OSX
        private bool firstLoadOnMac = true;
#endif

#region Creation
        public MainMenuScreen(GameMain game)
        {
            GameMain.Instance.ResolutionChanged += () =>
            {
                if (Selected == this && selectedTab == Tab.Settings)
                {
                    GameMain.Config.ResetSettingsFrame();
                    SelectTab(Tab.Settings);
                }
                CreateHostServerFields();
                CreateCampaignSetupUI();
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

#if TEST_REMOTE_CONTENT

            var doc = XMLExtensions.TryLoadXml("Content/UI/MenuTextTest.xml");
            if (doc?.Root != null)
            {
                foreach (XElement subElement in doc?.Root.Elements())
                {
                    GUIComponent.FromXML(subElement, Frame.RectTransform);
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
                TextManager.Get("CampaignLabel"), textAlignment: Alignment.Left, font: GUI.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = true };

            var campaignButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: campaignNavigation.RectTransform), style: "MainMenuGUIFrame");

            var campaignList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: campaignButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), campaignList.RectTransform), TextManager.Get("TutorialButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                UserData = Tab.Tutorials,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), campaignList.RectTransform), TextManager.Get("LoadGameButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                UserData = Tab.LoadGame,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), campaignList.RectTransform), TextManager.Get("NewGameButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
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
                TextManager.Get("MultiplayerLabel"), textAlignment: Alignment.Left, font: GUI.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = true };

            var multiplayerButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: multiplayerNavigation.RectTransform), style: "MainMenuGUIFrame");

            var multiplayerList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: multiplayerButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            joinServerButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), multiplayerList.RectTransform), TextManager.Get("JoinServerButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                UserData = Tab.JoinServer,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };
            hostServerButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), multiplayerList.RectTransform), TextManager.Get("HostServerButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
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
                TextManager.Get("CustomizeLabel"), textAlignment: Alignment.Left, font: GUI.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = true };

            var customizeButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: customizeNavigation.RectTransform), style: "MainMenuGUIFrame");

            var customizeList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: customizeButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

#if USE_STEAM
            var steamWorkshopButtonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), customizeList.RectTransform), style: null);

            steamWorkshopButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), steamWorkshopButtonContainer.RectTransform), TextManager.Get("SteamWorkshopButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                Enabled = false,
                UserData = Tab.SteamWorkshop,
                OnClicked = SelectTab
            };

            downloadingModsContainer = new GUIFrame(new RectTransform(new Vector2(1.4f, 0.9f), steamWorkshopButtonContainer.RectTransform,
                Anchor.CenterRight, Pivot.CenterLeft)
            { RelativeOffset = new Vector2(0.3f, 0.0f) },
                "MainMenuNotifBackground", Color.Yellow)
            {
                CanBeFocused = false,
                UserData = "workshopnotif",
                Visible = false
            };
            new GUITextBlock(new RectTransform(Vector2.One * 0.9f, downloadingModsContainer.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft) { RelativeOffset = new Vector2(0.05f, 0.0f) },
                TextManager.Get("ModsDownloadingNotif"), Color.Black)
            {
                CanBeFocused = false,
            };

#endif

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), customizeList.RectTransform), TextManager.Get("SubEditorButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                UserData = Tab.SubmarineEditor,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), customizeList.RectTransform), TextManager.Get("CharacterEditorButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
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
                ForceUpperCase = true,
                UserData = Tab.Settings,
                OnClicked = SelectTab
            };
            
            enableModsContainer = new GUIFrame(new RectTransform(new Vector2(1.4f, 0.9f), settingsButtonContainer.RectTransform,
                Anchor.CenterRight, Pivot.CenterLeft) { RelativeOffset = new Vector2(0.5f, 0.0f) },
                "MainMenuNotifBackground", Color.Yellow)
            {
                CanBeFocused = false,
                UserData = "settingsnotif",
                Visible = false
            };
            new GUITextBlock(new RectTransform(Vector2.One * 0.9f, enableModsContainer.RectTransform, Anchor.CenterLeft, Pivot.CenterLeft) { RelativeOffset = new Vector2(0.05f, 0.0f) },
                TextManager.Get("ModsInstalledNotif"), Color.Black)
            {
                CanBeFocused = false
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("CreditsButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                UserData = Tab.Credits,
                OnClicked = SelectTab
            };
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("QuitButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                OnClicked = QuitClicked
            };

            //debug button for quickly starting a new round
#if DEBUG
            new GUIButton(new RectTransform(new Point(300, 30), Frame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(40, 80) },
                "Quickstart (dev)", style: "GUIButtonLarge", color: GUI.Style.Red)
            {
                IgnoreLayoutGroups = true,
                UserData = Tab.QuickStartDev,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Point(300, 30), Frame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(40, 130) },
                "Profiling", style: "GUIButtonLarge", color: GUI.Style.Red)
            {
                IgnoreLayoutGroups = true,
                UserData = Tab.ProfilingTestBench,
                ToolTip = "Enables performance indicators and starts the game with a fixed sub, crew and level to make it easier to compare the performance between sessions.",
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
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
            
            menuTabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length + 1];

            menuTabs[(int)Tab.Settings] = new GUIFrame(new RectTransform(new Vector2(relativeSize.X, 0.8f), GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing },
                style: null);

            menuTabs[(int)Tab.NewGame] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });
            menuTabs[(int)Tab.LoadGame] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });

            CreateCampaignSetupUI();

            var hostServerScale = new Vector2(0.7f, 1.2f);
            menuTabs[(int)Tab.HostServer] = new GUIFrame(new RectTransform(
                Vector2.Multiply(relativeSize, hostServerScale), GUI.Canvas, anchor, pivot, minSize.Multiply(hostServerScale), maxSize.Multiply(hostServerScale))
            { RelativeOffset = relativeSpacing });

            CreateHostServerFields();

            //----------------------------------------------------------------------

            menuTabs[(int)Tab.Tutorials] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });

            //PLACEHOLDER
            var tutorialList = new GUIListBox(
                new RectTransform(new Vector2(0.95f, 0.85f), menuTabs[(int)Tab.Tutorials].RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.1f) });
            foreach (Tutorial tutorial in Tutorial.Tutorials)
            {
                var tutorialText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), tutorialList.Content.RectTransform), tutorial.DisplayName, textAlignment: Alignment.Center, font: GUI.LargeFont)
                {
                    UserData = tutorial
                };
            }
            tutorialList.OnSelected += (component, obj) =>
            {
                TutorialMode.StartTutorial(obj as Tutorial);
                return true;
            };

            this.game = game;

            menuTabs[(int)Tab.Credits] = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, menuTabs[(int)Tab.Credits].RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            var creditsContainer = new GUIFrame(new RectTransform(new Vector2(0.75f, 1.5f), menuTabs[(int)Tab.Credits].RectTransform, Anchor.CenterRight), style: "OuterGlow", color: Color.Black * 0.8f);
            creditsPlayer = new CreditsPlayer(new RectTransform(Vector2.One, creditsContainer.RectTransform), "Content/Texts/Credits.xml");

            new GUIButton(new RectTransform(new Vector2(0.1f, 0.05f), menuTabs[(int)Tab.Credits].RectTransform, Anchor.BottomLeft) { RelativeOffset = new Vector2(0.25f, 0.02f) },
                TextManager.Get("Back"), style: "GUIButtonLarge")
            {
                OnClicked = SelectTab
            };

        }
#endregion

        #region Selection
        public override void Select()
        {
            GUI.PreventPauseMenuToggle = false;

            base.Select();

            if (GameMain.Client != null)
            {
                GameMain.Client.Disconnect();
                GameMain.Client = null;
            }

            GameMain.SubEditorScreen?.ClearBackedUpSubInfo();
            Submarine.Unload();
            
            ResetButtonStates(null);

            GameAnalyticsManager.SetCustomDimension01("");

            if (GameMain.SteamWorkshopScreen != null)
            {
                CoroutineManager.StartCoroutine(GameMain.SteamWorkshopScreen.RefreshDownloadState());
            }

#if OSX
            // Hack for adjusting the viewport properly after splash screens on older Macs
            if (firstLoadOnMac)
            {
                firstLoadOnMac = false;

                menuTabs[(int)Tab.Empty] = new GUIFrame(new RectTransform(new Vector2(1f, 1f), GUI.Canvas), "", Color.Transparent)
                {
                    CanBeFocused = false
                };
                var emptyList = new GUIListBox(new RectTransform(new Vector2(0.0f, 0.0f), menuTabs[(int)Tab.Empty].RectTransform))
                {
                    CanBeFocused = false
                };

                SelectTab(null, Tab.Empty);
            }
#endif
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
            if (GameMain.Config.UnsavedSettings)
            {
                var applyBox = new GUIMessageBox(
                    TextManager.Get("ApplySettingsLabel"),
                    TextManager.Get("ApplySettingsQuestion"),
                    new string[] { TextManager.Get("ApplySettingsYes"), TextManager.Get("ApplySettingsNo") });
                applyBox.Buttons[0].UserData = tab;
                applyBox.Buttons[0].OnClicked = (tb, userdata) =>
                {
                    applyBox.Close();
                    ApplySettings();
                    SelectTab(tab);
                    return true;
                };

                applyBox.Buttons[1].UserData = tab;
                applyBox.Buttons[1].OnClicked = (tb, userdata) =>
                {
                    applyBox.Close();
                    DiscardSettings();
                    SelectTab(tab);
                    return true;
                };
                return false;
            }

            GameMain.Config.ResetSettingsFrame();

            switch (tab)
            {
                case Tab.NewGame:
                    if (GameMain.Config.ShowTutorialSkipWarning)
                    {
                        selectedTab = 0;
                        ShowTutorialSkipWarning(Tab.NewGame);
                        return true;
                    }
                    if (!GameMain.Config.CampaignDisclaimerShown)
                    {
                        selectedTab = 0;
                        GameMain.Instance.ShowCampaignDisclaimer(() => { SelectTab(null, Tab.NewGame); });
                        return true;
                    }
                    campaignSetupUI.CreateDefaultSaveName();
                    campaignSetupUI.RandomizeSeed();
                    campaignSetupUI.UpdateSubList(SubmarineInfo.SavedSubmarines);
                    break;
                case Tab.LoadGame:
                    campaignSetupUI.UpdateLoadMenu();
                    break;
                case Tab.Settings:
                    GameMain.MainMenuScreen?.SetEnableModsNotification(false);
                    menuTabs[(int)Tab.Settings].RectTransform.ClearChildren();
                    GameMain.Config.SettingsFrame.RectTransform.Parent = menuTabs[(int)Tab.Settings].RectTransform;
                    GameMain.Config.SettingsFrame.RectTransform.RelativeSize = Vector2.One;
                    break;
                case Tab.JoinServer:
                    if (GameMain.Config.ShowTutorialSkipWarning)
                    {
                        selectedTab = 0;
                        ShowTutorialSkipWarning(Tab.JoinServer);
                        return true;
                    }
                    if (!GameMain.Config.CampaignDisclaimerShown)
                    {
                        selectedTab = 0;
                        GameMain.Instance.ShowCampaignDisclaimer(() => { SelectTab(null, Tab.JoinServer); });
                        return true;
                    }
                    GameMain.ServerListScreen.Select();
                    break;
                case Tab.HostServer:
                    if (GameMain.Config.ContentPackageSelectionDirty)
                    {
                        new GUIMessageBox(TextManager.Get("RestartRequiredLabel"), TextManager.Get("ServerRestartRequiredContentPackage", fallBackTag: "RestartRequiredGeneric"));
                        selectedTab = 0;
                        return false;
                    }
                    if (GameMain.Config.ShowTutorialSkipWarning)
                    {
                        selectedTab = 0;
                        ShowTutorialSkipWarning(tab);
                        return true;
                    }
                    if (!GameMain.Config.CampaignDisclaimerShown)
                    {
                        selectedTab = 0;
                        GameMain.Instance.ShowCampaignDisclaimer(() => { SelectTab(null, Tab.HostServer); });
                        return true;
                    }
                    break;
                case Tab.Tutorials:
                    if (!GameMain.Config.CampaignDisclaimerShown)
                    {
                        selectedTab = 0;
                        GameMain.Instance.ShowCampaignDisclaimer(() => { SelectTab(null, Tab.Tutorials); });
                        return true;
                    }
                    UpdateTutorialList();
                    break;
                case Tab.CharacterEditor:
                    Submarine.MainSub = null;
                    CoroutineManager.StartCoroutine(SelectScreenWithWaitCursor(GameMain.CharacterEditorScreen));
                    break;
                case Tab.SubmarineEditor:
                    CoroutineManager.StartCoroutine(SelectScreenWithWaitCursor(GameMain.SubEditorScreen));
                    break;
                case Tab.QuickStartDev:
                    QuickStart();
                    break;
                case Tab.ProfilingTestBench:
                    QuickStart(fixedSeed: true);
                    GameMain.ShowPerf = true;
                    GameMain.ShowFPS = true;
                    break;
                case Tab.SteamWorkshop:
                    if (!Steam.SteamManager.IsInitialized) return false;
                    CoroutineManager.StartCoroutine(SelectScreenWithWaitCursor(GameMain.SteamWorkshopScreen));
                    break;
                case Tab.Credits:
                    titleText.Visible = false;
                    creditsPlayer.Restart();
                    break;
                case Tab.Empty:
                    titleText.Visible = true;
                    selectedTab = 0;
                    break;
            }

            selectedTab = tab;

            return true;
        }

        private IEnumerable<object> SelectScreenWithWaitCursor(Screen screen)
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

        public void QuickStart(bool fixedSeed = false)
        {
            if (fixedSeed)
            {
                Rand.SetSyncedSeed(1);
                Rand.SetLocalRandom(1);
            }

            SubmarineInfo selectedSub = null;
            string subName = GameMain.Config.QuickStartSubmarineName;
            if (!string.IsNullOrEmpty(subName))
            {
                DebugConsole.NewMessage($"Loading the predefined quick start sub \"{subName}\"", Color.White);
                selectedSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s =>
                    s.Name.ToLower() == subName.ToLower());

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
                missionPrefab: null);
            //(gamesession.GameMode as SinglePlayerCampaign).GenerateMap(ToolBox.RandomSeed(8));
            gamesession.StartRound(fixedSeed ? "abcd" : ToolBox.RandomSeed(8), difficulty: 40);
            GameMain.GameScreen.Select();
            // TODO: modding support
            string[] jobIdentifiers = new string[] { "captain", "engineer", "mechanic", "securityofficer", "medicaldoctor" };
            foreach (string job in jobIdentifiers)
            {
                var jobPrefab = JobPrefab.Get(job);
                var variant = Rand.Range(0, jobPrefab.Variants);
                var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: jobPrefab, variant: variant);
                if (characterInfo.Job == null)
                {
                    DebugConsole.ThrowError("Failed to find the job \"" + job + "\"!");
                }
                gamesession.CrewManager.AddCharacterInfo(characterInfo);
            }
            gamesession.CrewManager.InitSinglePlayerRound();
        }

        public void SetEnableModsNotification(bool visible)
        {
            if (enableModsContainer != null) { enableModsContainer.Visible = visible; }
        }

        public void SetDownloadingModsNotification(bool visible)
        {
            if (downloadingModsContainer != null) { downloadingModsContainer.Visible = visible; }
        }

        private void ShowTutorialSkipWarning(Tab tabToContinueTo)
        {
            var tutorialSkipWarning = new GUIMessageBox("", TextManager.Get("tutorialskipwarning"), new string[] { TextManager.Get("tutorialwarningskiptutorials"), TextManager.Get("tutorialwarningplaytutorials") });
            tutorialSkipWarning.Buttons[0].OnClicked += (btn, userdata) =>
            {
                GameMain.Config.ShowTutorialSkipWarning = false;
                GameMain.Config.SaveNewPlayerConfig();
                tutorialSkipWarning.Close();
                SelectTab(tabToContinueTo);
                return true;
            };
            tutorialSkipWarning.Buttons[1].OnClicked += (btn, userdata) =>
            {
                GameMain.Config.ShowTutorialSkipWarning = false;
                GameMain.Config.SaveNewPlayerConfig();
                tutorialSkipWarning.Close();
                SelectTab(Tab.Tutorials);
                return true;
            };
        }

        private void UpdateTutorialList()
        {
            var tutorialList = menuTabs[(int)Tab.Tutorials].GetChild<GUIListBox>();

            int completedTutorials = 0;

            foreach (GUITextBlock tutorialText in tutorialList.Content.Children)
            {
                if (((Tutorial)tutorialText.UserData).Completed)
                {
                    completedTutorials++;
                }
            }

            for (int i = 0; i < tutorialList.Content.Children.Count(); i++)
            {
                if (i < completedTutorials + 1)
                {
                    (tutorialList.Content.GetChild(i) as GUITextBlock).TextColor = GUI.Style.Green;
#if !DEBUG
                    (tutorialList.Content.GetChild(i) as GUITextBlock).CanBeFocused = true;
#endif
                }
                else
                {
                    (tutorialList.Content.GetChild(i) as GUITextBlock).TextColor = Color.Gray;
#if !DEBUG
                    (tutorialList.Content.GetChild(i) as GUITextBlock).CanBeFocused = false;
#endif
                }
            }
        }

        public void ResetSettingsFrame(GameSettings.Tab selectedTab = GameSettings.Tab.Graphics)
        {
            menuTabs[(int)Tab.Settings].RectTransform.ClearChildren();
            GameMain.Config.ResetSettingsFrame();
            GameMain.Config.CreateSettingsFrame(selectedTab);
            GameMain.Config.SettingsFrame.RectTransform.Parent = menuTabs[(int)Tab.Settings].RectTransform;
            GameMain.Config.SettingsFrame.RectTransform.RelativeSize = Vector2.One;
        }

        private bool ApplySettings()
        {
            GameMain.Config.SaveNewPlayerConfig();

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || 
                GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
                new GUIMessageBox(
                    TextManager.Get("RestartRequiredLabel"),
                    TextManager.Get("RestartRequiredGeneric"));
            }

            return true;
        }

        private bool DiscardSettings()
        {
            GameMain.Config.LoadPlayerConfig();

            return true;
        }
        
        private bool ChangeMaxPlayers(GUIButton button, object obj)
        {
            int.TryParse(maxPlayersBox.Text, out int currMaxPlayers);
            currMaxPlayers = (int)MathHelper.Clamp(currMaxPlayers + (int)button.UserData, 1, NetConfig.MaxPlayers);

            maxPlayersBox.Text = currMaxPlayers.ToString();

            return true;
        }

        private void StartServer()
        {
            string name = serverNameBox.Text;

            GameMain.NetLobbyScreen?.Release();
            GameMain.NetLobbyScreen = new NetLobbyScreen();
            try
            {
                string exeName = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.ServerExecutable)?.FirstOrDefault()?.Path;
                if (string.IsNullOrEmpty(exeName))
                {
                    DebugConsole.ThrowError("No server executable defined in the selected content packages. Attempting to use the default executable...");
                    exeName = "DedicatedServer.exe";
                }

                string arguments = "-name \"" + ToolBox.EscapeCharacters(name) + "\"" +
                                   " -public " + isPublicBox.Selected.ToString() +
                                   " -playstyle " + ((PlayStyle)playstyleBanner.UserData).ToString()  +
                                   " -banafterwrongpassword " + wrongPasswordBanBox.Selected.ToString() +
                                   " -karmaenabled " + karmaEnabledBox.Selected.ToString() +
                                   " -karmapreset " + (karmaPresetDD.SelectedData?.ToString() ?? "default") +
                                   " -maxplayers " + maxPlayersBox.Text;

                if (!string.IsNullOrWhiteSpace(passwordBox.Text))
                {
                    arguments += " -password \"" + ToolBox.EscapeCharacters(passwordBox.Text) + "\"";
                }
                else
                {
                    arguments += " -nopassword";
                }

                int ownerKey = 0;
                if (Steam.SteamManager.GetSteamID() != 0)
                {
                    arguments += " -steamid " + Steam.SteamManager.GetSteamID();
                }
                else
                {
                    ownerKey = Math.Max(CryptoRandom.Instance.Next(), 1);
                    arguments += " -ownerkey " + ownerKey;
                }

                string filename = exeName;
#if LINUX || OSX
                filename = "./" + Path.GetFileNameWithoutExtension(exeName);
                //arguments = ToolBox.EscapeCharacters(arguments);
#endif
                var processInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = arguments,
#if !DEBUG
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
#endif
                };
                ChildServerRelay.Start(processInfo);
                Thread.Sleep(1000); //wait until the server is ready before connecting

                GameMain.Client = new GameClient(name, System.Net.IPAddress.Loopback.ToString(), Steam.SteamManager.GetSteamID(), name, ownerKey, true);
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
            if (selectedTab > 0 && menuTabs[(int)selectedTab] != null)
            {
                menuTabs[(int)selectedTab].AddToGUIUpdateList();
            }
        }

        public override void Update(double deltaTime)
        {
#if !DEBUG
#if USE_STEAM
            if (GameMain.Config.UseSteamMatchmaking)
            {
                hostServerButton.Enabled =  Steam.SteamManager.IsInitialized;
            }
            steamWorkshopButton.Enabled = Steam.SteamManager.IsInitialized;
#endif
#else
#if USE_STEAM
            steamWorkshopButton.Enabled = true;
#endif
#endif
        }

        public void DrawBackground(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);

            if (backgroundSprite == null)
            {
                backgroundSprite = (LocationType.List.Where(l => l.UseInMainMenu).GetRandom())?.GetPortrait(0);
            }

            if (backgroundSprite != null)
            {
                GUI.DrawBackgroundSprite(spriteBatch, backgroundSprite,
                    aberrationStrength: 0.0f);
            }

            var vignette = GUI.Style.GetComponentStyle("mainmenuvignette")?.GetDefaultSprite();
            if (vignette != null)
            {
                spriteBatch.Begin(blendState: BlendState.NonPremultiplied);
                vignette.Draw(spriteBatch, Vector2.Zero, Color.White, Vector2.Zero, 0.0f, 
                    new Vector2(GameMain.GraphicsWidth / vignette.size.X, GameMain.GraphicsHeight / vignette.size.Y));
                spriteBatch.End();
            }
        }

        readonly string[] legalCrap = new string[]
        {
            TextManager.Get("privacypolicy", returnNull: true) ?? "Privacy policy",
            "© " + DateTime.Now.Year + " Undertow Games & FakeFish. All rights reserved.",
            "© " + DateTime.Now.Year + " Daedalic Entertainment GmbH. The Daedalic logo is a trademark of Daedalic Entertainment GmbH, Germany. All rights reserved."
        };

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            DrawBackground(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);

            GUI.Draw(Cam, spriteBatch);

#if !UNSTABLE
            string versionString = "Barotrauma v" + GameMain.Version + " (" + AssemblyInfo.BuildString + ", branch " + AssemblyInfo.GitBranch + ", revision " + AssemblyInfo.GitRevision + ")";
            GUI.SmallFont.DrawString(spriteBatch, versionString, new Vector2(HUDLayoutSettings.Padding, GameMain.GraphicsHeight - GUI.SmallFont.MeasureString(versionString).Y - HUDLayoutSettings.Padding * 0.75f), Color.White * 0.7f);
#endif
            if (selectedTab != Tab.Credits)
            {
                Vector2 textPos = new Vector2(GameMain.GraphicsWidth - HUDLayoutSettings.Padding, GameMain.GraphicsHeight - HUDLayoutSettings.Padding * 0.75f);
                for (int i = legalCrap.Length - 1; i >= 0; i--)
                {
                    Vector2 textSize = GUI.SmallFont.MeasureString(legalCrap[i]);
                    textSize = new Vector2((int)textSize.X, (int)textSize.Y);
                    bool mouseOn = i == 0 &&
                        PlayerInput.MousePosition.X > textPos.X - textSize.X && PlayerInput.MousePosition.X < textPos.X &&
                        PlayerInput.MousePosition.Y > textPos.Y - textSize.Y && PlayerInput.MousePosition.Y < textPos.Y;

                    GUI.SmallFont.DrawString(spriteBatch,
                        legalCrap[i], textPos - textSize,
                        mouseOn ? Color.White : Color.White * 0.7f);

                    if (i == 0)
                    {
                        GUI.DrawLine(spriteBatch, textPos, textPos - Vector2.UnitX * textSize.X, mouseOn ? Color.White : Color.White * 0.7f);
                        if (mouseOn && PlayerInput.PrimaryMouseButtonClicked())
                        {
                            GameMain.Instance.ShowOpenUrlInWebBrowserPrompt("http://privacypolicy.daedalic.com");
                        }
                    }
                    textPos.Y -= textSize.Y;
                }
            }

            spriteBatch.End();
        }

        private void StartGame(SubmarineInfo selectedSub, string saveName, string mapSeed)
        {
            if (string.IsNullOrEmpty(saveName)) return;

            var existingSaveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);

            if (existingSaveFiles.Any(s => s == saveName))
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
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Copying the file \"" + selectedSub.FilePath + "\" failed.\n" + e.Message + "\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            selectedSub = new SubmarineInfo(Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"));
            
            GameMain.GameSession = new GameSession(selectedSub, saveName, GameModePreset.SinglePlayerCampaign, mapSeed);
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
            menuTabs[(int)Tab.NewGame].ClearChildren();
            menuTabs[(int)Tab.LoadGame].ClearChildren();

            var innerNewGame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), menuTabs[(int)Tab.NewGame].RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, 0.025f) })
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            var newGameContent = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.95f), innerNewGame.RectTransform, Anchor.Center),
                style: "InnerFrame");

            var paddedNewGame = new GUIFrame(new RectTransform(new Vector2(0.95f), newGameContent.RectTransform, Anchor.Center), style: null);
            var paddedLoadGame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), menuTabs[(int)Tab.LoadGame].RectTransform, Anchor.Center) { AbsoluteOffset = new Point(0, 10) },
                style: null);

            campaignSetupUI = new CampaignSetupUI(false, paddedNewGame, paddedLoadGame, SubmarineInfo.SavedSubmarines)
            {
                LoadGame = LoadGame,
                StartNewGame = StartGame
            };

            var startButtonContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), innerNewGame.RectTransform, Anchor.Center), style: null);
            campaignSetupUI.StartButton.RectTransform.Parent = startButtonContainer.RectTransform;
            campaignSetupUI.StartButton.RectTransform.MinSize = new Point(
                (int)(campaignSetupUI.StartButton.TextBlock.TextSize.X * 1.5f),
                campaignSetupUI.StartButton.RectTransform.MinSize.Y);
            startButtonContainer.RectTransform.MinSize = new Point(0, campaignSetupUI.StartButton.RectTransform.MinSize.Y);
        }

        private void CreateHostServerFields()
        {
            menuTabs[(int)Tab.HostServer].ClearChildren();

            int port = NetConfig.DefaultPort;
            int queryPort = NetConfig.DefaultQueryPort;
            int maxPlayers = 8;
            bool karmaEnabled = true;
            string selectedKarmaPreset = "";
            PlayStyle selectedPlayStyle = PlayStyle.Casual;
            if (File.Exists(ServerSettings.SettingsFile))
            {
                XDocument settingsDoc = XMLExtensions.TryLoadXml(ServerSettings.SettingsFile);
                if (settingsDoc != null)
                {
                    port = settingsDoc.Root.GetAttributeInt("port", port);
                    queryPort = settingsDoc.Root.GetAttributeInt("queryport", queryPort);
                    maxPlayers = settingsDoc.Root.GetAttributeInt("maxplayers", maxPlayers);
                    karmaEnabled = settingsDoc.Root.GetAttributeBool("karmaenabled", true);
                    selectedKarmaPreset = settingsDoc.Root.GetAttributeString("karmapreset", "default");
                    string playStyleStr = settingsDoc.Root.GetAttributeString("playstyle", "Casual");
                    Enum.TryParse(playStyleStr, out selectedPlayStyle);
                }
            }

            Vector2 textLabelSize = new Vector2(1.0f, 0.1f);
            Alignment textAlignment = Alignment.CenterLeft;
            Vector2 textFieldSize = new Vector2(0.5f, 1.0f);
            Vector2 tickBoxSize = new Vector2(0.4f, 0.07f);
            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 0.9f), menuTabs[(int)Tab.HostServer].RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };
            GUIComponent parent = content;

            new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("HostServerButton"), textAlignment: Alignment.Center, font: GUI.LargeFont) { ForceUpperCase = true };

            //play style -----------------------------------------------------

            var playstyleContainer = new GUIFrame(new RectTransform(new Vector2(1.35f, 0.1f), parent.RectTransform), style: null, color: Color.Black);

            playstyleBanner = new GUIImage(new RectTransform(new Vector2(1.0f, 0.1f), playstyleContainer.RectTransform), 
                ServerListScreen.PlayStyleBanners[0], scaleToFit: true)
            {
                UserData = PlayStyle.Serious
            };
            float bannerAspectRatio = (float) playstyleBanner.Sprite.SourceRect.Width / playstyleBanner.Sprite.SourceRect.Height;
            playstyleBanner.RectTransform.NonScaledSize = new Point(playstyleBanner.Rect.Width, (int)(playstyleBanner.Rect.Width / bannerAspectRatio));
            playstyleBanner.RectTransform.IsFixedSize = true;
            new GUIFrame(new RectTransform(Vector2.One, playstyleBanner.RectTransform), "InnerGlow", color: Color.Black);

            new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.05f), playstyleBanner.RectTransform) { RelativeOffset = new Vector2(0.01f, 0.03f) },
                "playstyle name goes here", font: GUI.SmallFont, textAlignment: Alignment.Center, textColor: Color.White, style: "GUISlopedHeader");
            
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

            string longestPlayStyleStr = "";
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                string playStyleStr = TextManager.Get("servertagdescription." + playStyle);
                if (playStyleStr.Length > longestPlayStyleStr.Length) { longestPlayStyleStr = playStyleStr; }
            }

            playstyleDescription = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), playstyleContainer.RectTransform, Anchor.BottomCenter),
                longestPlayStyleStr, style: null, wrap: true)
            {
                Color = Color.Black * 0.8f,
                TextColor = GUI.Style.GetComponentStyle("GUITextBlock").TextColor
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
            serverNameBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment)
            { 
                MaxTextLength = NetConfig.ServerNameMaxLength,
                OverflowClip = true
            };
            label.RectTransform.MaxSize = serverNameBox.RectTransform.MaxSize;
 
            /* TODO: allow lidgren servers from client?
            label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("ServerPort"), textAlignment: textAlignment);
            portBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment)
            {
                Text = port.ToString(),
                ToolTip = TextManager.Get("ServerPortToolTip")
            };

#if USE_STEAM
            label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("ServerQueryPort"), textAlignment: textAlignment);
            queryPortBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment)
            {
                Text = queryPort.ToString(),
                ToolTip = TextManager.Get("ServerQueryPortToolTip")
            };
#endif
            */

            var maxPlayersLabel = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("MaxPlayers"), textAlignment: textAlignment);
            var buttonContainer = new GUILayoutGroup(new RectTransform(textFieldSize, maxPlayersLabel.RectTransform, Anchor.CenterRight), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.1f
            };
            new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIMinusButton", textAlignment: Alignment.Center)
            {
                UserData = -1,
                OnClicked = ChangeMaxPlayers
            };
            maxPlayersBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 1.0f), buttonContainer.RectTransform), textAlignment: Alignment.Center)
            {
                Text = maxPlayers.ToString(),
                CanBeFocused = false
            };
            new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "GUIPlusButton", textAlignment: Alignment.Center)
            {
                UserData = 1,
                OnClicked = ChangeMaxPlayers
            };
            maxPlayersLabel.RectTransform.MaxSize = maxPlayersBox.RectTransform.MaxSize;

            label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("Password"), textAlignment: textAlignment);
            passwordBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment)
            {
                Censor = true
            };
            label.RectTransform.MaxSize = passwordBox.RectTransform.MaxSize;

            // tickbox upper ---------------

            var tickboxAreaUpper = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, tickBoxSize.Y), parent.RectTransform), isHorizontal: true);

            isPublicBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaUpper.RectTransform), TextManager.Get("PublicServer"))
            {
                ToolTip = TextManager.Get("PublicServerToolTip")
            };

            wrongPasswordBanBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaUpper.RectTransform), TextManager.Get("ServerSettingsBanAfterWrongPassword"));

            tickboxAreaUpper.RectTransform.MaxSize = isPublicBox.RectTransform.MaxSize;

            // tickbox lower ---------------

            var tickboxAreaLower = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, tickBoxSize.Y), parent.RectTransform), isHorizontal: true);

            karmaEnabledBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaLower.RectTransform), TextManager.Get("ServerSettingsUseKarma"))
            {
                ToolTip = TextManager.Get("karmaexplanation"),
                OnSelected = (tb) =>
                {
                    karmaPresetDD.Enabled = karmaPresetDD.ButtonEnabled = tb.Selected;
                    return true;
                }                
            };
            karmaPresetDD = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1.0f), tickboxAreaLower.RectTransform))
            {
                ButtonEnabled = false,
                Enabled = false
            };
            var tempKarmaManager = new KarmaManager();
            foreach (string karmaPreset in tempKarmaManager.Presets.Keys)
            {
                karmaPresetDD.AddItem(TextManager.Get("KarmaPreset." + karmaPreset), karmaPreset);
                if (karmaPreset == selectedKarmaPreset) { karmaPresetDD.SelectItem(karmaPreset); }
            }
            if (karmaPresetDD.SelectedIndex == -1) { karmaPresetDD.Select(0); }

            karmaEnabledBox.Selected = karmaEnabled;

            tickboxAreaLower.RectTransform.MaxSize = karmaEnabledBox.RectTransform.MaxSize;

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

                    if (ForbiddenWordFilter.IsForbidden(name, out string forbiddenWord))
                    {
                        var msgBox = new GUIMessageBox("", 
                            TextManager.GetWithVariables("forbiddenservernameverification", new string[] { "[forbiddenword]", "[servername]" }, new string[] { forbiddenWord, name }), 
                            new string[] { TextManager.Get("yes"), TextManager.Get("no") });
                        msgBox.Buttons[0].OnClicked += (_, __) =>
                        {
                            StartServer();
                            msgBox.Close();
                            return true;
                        };
                        msgBox.Buttons[1].OnClicked += msgBox.Close;
                    }
                    else
                    {
                        StartServer();
                    }

                    return true;
                }
            };
        }

        private void SetServerPlayStyle(PlayStyle playStyle)
        {
            playstyleBanner.Sprite = ServerListScreen.PlayStyleBanners[(int)playStyle];
            playstyleBanner.UserData = playStyle;

            var nameText = playstyleBanner.GetChild<GUITextBlock>();
            nameText.Text = TextManager.AddPunctuation(':', TextManager.Get("serverplaystyle"), TextManager.Get("servertag." + playStyle));
            nameText.Color = ServerListScreen.PlayStyleColors[(int)playStyle];
            nameText.RectTransform.NonScaledSize = (nameText.Font.MeasureString(nameText.Text) + new Vector2(25, 10) * GUI.Scale).ToPoint();

            playstyleDescription.Text = TextManager.Get("servertagdescription." + playStyle);
            playstyleDescription.TextAlignment = playstyleDescription.WrappedText.Contains('\n') ?
               Alignment.CenterLeft : Alignment.Center;
        }
        #endregion

        private void FetchRemoteContent()
        {
            if (string.IsNullOrEmpty(GameMain.Config.RemoteContentUrl)) { return; }
            try
            {
                var client = new RestClient(GameMain.Config.RemoteContentUrl);
                var request = new RestRequest("MenuContent.xml", Method.GET);
                client.ExecuteAsync(request, RemoteContentReceived);
                CoroutineManager.StartCoroutine(WairForRemoteContentReceived());
            }

            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Fetching remote content to the main menu failed.", e);
#endif
                GameAnalyticsManager.AddErrorEventOnce("MainMenuScreen.FetchRemoteContent:Exception", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Fetching remote content to the main menu failed. " + e.Message);
                return;
            }
        }

        private IEnumerable<object> WairForRemoteContentReceived()
        {
            while (true)
            {
                lock (remoteContentLock)
                {
                    if (remoteContentResponse != null) { break; }
                }
                yield return new WaitForSeconds(0.1f);
            }
            lock (remoteContentLock)
            {
                if (remoteContentResponse.ResponseStatus != ResponseStatus.Completed || remoteContentResponse.StatusCode != HttpStatusCode.OK)
                {
                    yield return CoroutineStatus.Success;
                }

                try
                {
                    string xml = remoteContentResponse.Content;
                    int index = xml.IndexOf('<');
                    if (index > 0) { xml = xml.Substring(index, xml.Length - index); }
                    if (!string.IsNullOrWhiteSpace(xml))
                    {
                        XElement element = XDocument.Parse(xml)?.Root;
                        foreach (XElement subElement in element.Elements())
                        {
                            GUIComponent.FromXML(subElement, Frame.RectTransform);
                        }
                    }
                }

                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Reading received remote main menu content failed.", e);
#endif
                    GameAnalyticsManager.AddErrorEventOnce("MainMenuScreen.WairForRemoteContentReceived:Exception", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Reading received remote main menu content failed. " + e.Message);
                }
            }
            yield return CoroutineStatus.Success;            
        }

        private readonly object remoteContentLock = new object();
        private IRestResponse remoteContentResponse;

        private void RemoteContentReceived(IRestResponse response, RestRequestAsyncHandle handle)
        {
            lock (remoteContentLock)
            {
                remoteContentResponse = response;
            }
        }
    }
}
