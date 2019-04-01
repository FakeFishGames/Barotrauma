using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Tutorials;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Barotrauma
{
    class MainMenuScreen : Screen
    {
        public enum Tab { NewGame = 1, LoadGame = 2, HostServer = 3, Settings = 4, Tutorials = 5, JoinServer = 6, CharacterEditor = 7, SubmarineEditor = 8, QuickStartDev = 9, SteamWorkshop = 10 }

        private GUIComponent buttonsParent;

        private GUIFrame[] menuTabs;

        private CampaignSetupUI campaignSetupUI;

        private GUITextBox serverNameBox, portBox, queryPortBox, passwordBox, maxPlayersBox;
        private GUITickBox isPublicBox, useUpnpBox;

        private GUIButton joinServerButton, hostServerButton, steamWorkshopButton;

        private GameMain game;

        private Tab selectedTab;

        private Sprite backgroundSprite;
        private Sprite backgroundVignette;

        #region Creation
        public MainMenuScreen(GameMain game)
        {
            backgroundVignette = new Sprite("Content/UI/MainMenuVignette.png", Vector2.Zero);

            new GUIImage(new RectTransform(new Vector2(0.35f, 0.2f), Frame.RectTransform, Anchor.BottomRight)
            { RelativeOffset = new Vector2(0.05f, 0.05f), AbsoluteOffset = new Point(-5, -5) },
                style: "TitleText")
            {
                Color = Color.Black * 0.5f,
                CanBeFocused = false
            };
            new GUIImage(new RectTransform(new Vector2(0.35f, 0.2f), Frame.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.05f, 0.05f) },
                style: "TitleText");

            buttonsParent = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.85f), parent: Frame.RectTransform, anchor: Anchor.BottomLeft, pivot: Pivot.BottomLeft)
            {
                AbsoluteOffset = new Point(50, 0)
            })
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };
            
            // === CAMPAIGN
            var campaignHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 1.0f), parent: buttonsParent.RectTransform) { RelativeOffset = new Vector2(0.1f, 0.0f) }, isHorizontal: true);

            new GUIImage(new RectTransform(new Vector2(0.2f, 0.7f), campaignHolder.RectTransform), "MainMenuCampaignIcon")
            {
                CanBeFocused = false
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.02f, 0.0f), campaignHolder.RectTransform), style: null);

            var campaignNavigation = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.75f), parent: campaignHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.25f) });

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), campaignNavigation.RectTransform),
                TextManager.Get("CampaignLabel"), textAlignment: Alignment.Left, font: GUI.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = true };

            var campaignButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: campaignNavigation.RectTransform), style: "MainMenuGUIFrame");

            var campaignList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: campaignButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), multiplayerNavigation.RectTransform),
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), customizeNavigation.RectTransform),
                TextManager.Get("CustomizeLabel"), textAlignment: Alignment.Left, font: GUI.LargeFont, textColor: Color.Black, style: "MainMenuGUITextBlock") { ForceUpperCase = true };

            var customizeButtons = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), parent: customizeNavigation.RectTransform), style: "MainMenuGUIFrame");

            var customizeList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.2f), parent: customizeButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

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

            if (Steam.SteamManager.USE_STEAM)
            {
                steamWorkshopButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), customizeList.RectTransform), TextManager.Get("SteamWorkshopButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
                {
                    ForceUpperCase = true,
                    Enabled = false,
                    UserData = Tab.SteamWorkshop,
                    OnClicked = SelectTab
                };
            }

            // === OPTION
            var optionHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.5f), parent: buttonsParent.RectTransform), isHorizontal: true);

            new GUIImage(new RectTransform(new Vector2(0.15f, 0.6f), optionHolder.RectTransform), "MainMenuOptionIcon")
            {
                CanBeFocused = false
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(0.01f, 0.0f), optionHolder.RectTransform), style: null);

            var optionButtons = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), parent: optionHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.15f) });

            var optionList = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.15f), parent: optionButtons.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.035f
            };

            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("SettingsButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                UserData = Tab.Settings,
                OnClicked = SelectTab
            };
            new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), optionList.RectTransform), TextManager.Get("QuitButton"), textAlignment: Alignment.Left, style: "MainMenuGUIButton")
            {
                ForceUpperCase = true,
                OnClicked = QuitClicked
            };

            //debug button for quickly starting a new round
#if DEBUG
            new GUIButton(new RectTransform(new Vector2(0.8f, 0.1f), buttonsParent.RectTransform, Anchor.TopLeft, Pivot.BottomLeft) { AbsoluteOffset = new Point(0, -40) },
                "Quickstart (dev)", style: "GUIButtonLarge", color: Color.Red)
            { 
                IgnoreLayoutGroups = true,
                UserData = Tab.QuickStartDev,
                OnClicked = (tb, userdata) =>
                {
                    SelectTab(tb, userdata);
                    return true;
                }
            };
#endif

            var minButtonSize = new Point(120, 20);
            var maxButtonSize = new Point(480, 80);

            /*new GUIButton(new RectTransform(new Vector2(1.0f, 0.1f), buttonsParent.RectTransform), TextManager.Get("TutorialButton"), style: "GUIButtonLarge")
            {
                UserData = Tab.Tutorials,
                OnClicked = SelectTab,
                Enabled = false
            };*/
            
           /* var buttons = GUI.CreateButtons(9, new Vector2(1, 0.04f), buttonsParent.RectTransform, anchor: Anchor.BottomLeft,
                minSize: minButtonSize, maxSize: maxButtonSize, relativeSpacing: 0.005f, extraSpacing: i => i % 2 == 0 ? 20 : 0);
            buttons.ForEach(b => b.Color *= 0.8f);
            SetupButtons(buttons);
            buttons.ForEach(b => b.TextBlock.SetTextPos());*/

            var relativeSize = new Vector2(0.6f, 0.5f);
            var minSize = new Point(600, 400);
            var maxSize = new Point(2000, 1500);
            var anchor = Anchor.CenterRight;
            var pivot = Pivot.CenterRight;
            Vector2 relativeSpacing = new Vector2(0.05f, 0.0f);
            
            menuTabs = new GUIFrame[Enum.GetValues(typeof(Tab)).Length + 1];

            menuTabs[(int)Tab.Settings] = new GUIFrame(new RectTransform(new Vector2(relativeSize.X, 0.8f), GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing },
                style: null);

            menuTabs[(int)Tab.NewGame] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });
            var paddedNewGame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), menuTabs[(int)Tab.NewGame].RectTransform, Anchor.Center)) { Stretch = true };
            menuTabs[(int)Tab.LoadGame] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });
            var paddedLoadGame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), menuTabs[(int)Tab.LoadGame].RectTransform, Anchor.Center), style: null);
            
            campaignSetupUI = new CampaignSetupUI(false, paddedNewGame, paddedLoadGame, Submarine.SavedSubmarines)
            {
                LoadGame = LoadGame,
                StartNewGame = StartGame
            };

            var hostServerScale = new Vector2(0.7f, 1.0f);
            menuTabs[(int)Tab.HostServer] = new GUIFrame(new RectTransform(
                Vector2.Multiply(relativeSize, hostServerScale), GUI.Canvas, anchor, pivot, minSize.Multiply(hostServerScale), maxSize.Multiply(hostServerScale)) { RelativeOffset = relativeSpacing });

            CreateHostServerFields();

            //----------------------------------------------------------------------

            menuTabs[(int)Tab.Tutorials] = new GUIFrame(new RectTransform(relativeSize, GUI.Canvas, anchor, pivot, minSize, maxSize) { RelativeOffset = relativeSpacing });

            //PLACEHOLDER
            var tutorialList = new GUIListBox(
                new RectTransform(new Vector2(0.95f, 0.85f), menuTabs[(int)Tab.Tutorials].RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.1f) }, 
                false, null, "");
            foreach (Tutorial tutorial in Tutorial.Tutorials)
            {
                var tutorialText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), tutorialList.Content.RectTransform), tutorial.Name, textAlignment: Alignment.Center, font: GUI.LargeFont)
                {
                    UserData = tutorial
                };
            }
            tutorialList.OnSelected += (component, obj) =>
            {
                TutorialMode.StartTutorial(obj as Tutorial);
                return true;
            };

            UpdateTutorialList();

            this.game = game;
        }
        #endregion

        #region Selection
        public override void Select()
        {
            base.Select();

            if (GameMain.Client != null)
            {
                GameMain.Client.Disconnect();
                GameMain.Client = null;
            }

            Submarine.Unload();

            UpdateTutorialList();

            campaignSetupUI.UpdateSubList(Submarine.SavedSubmarines);

            ResetButtonStates(null);

            GameAnalyticsManager.SetCustomDimension01("");
        }

        public override void Deselect()
        {
            base.Deselect();
            SelectTab(null, 0);
        }

        private bool SelectTab(GUIButton button, object obj)
        {
            if (obj is Tab)
            {
                if (GameMain.Config.UnsavedSettings)
                {
                    var applyBox = new GUIMessageBox(
                        TextManager.Get("ApplySettingsLabel"),
                        TextManager.Get("ApplySettingsQuestion"),
                        new string[] { TextManager.Get("ApplySettingsYes"), TextManager.Get("ApplySettingsNo") });
                    applyBox.Buttons[0].UserData = (Tab)obj;
                    applyBox.Buttons[0].OnClicked = (tb, userdata) =>
                    {
                        applyBox.Close(button, userdata);
                        ApplySettings(button, userdata);
                        return true;
                    };

                    applyBox.Buttons[1].UserData = (Tab)obj;
                    applyBox.Buttons[1].OnClicked = (tb, userdata) =>
                    {
                        applyBox.Close(button, userdata);
                        DiscardSettings(button, userdata);
                        return true;
                    };
                    return false;
                }

                GameMain.Config.ResetSettingsFrame();
                selectedTab = (Tab)obj;

                switch (selectedTab)
                {
                    case Tab.NewGame:
                        campaignSetupUI.CreateDefaultSaveName();
                        campaignSetupUI.UpdateTutorialSelection();
                        break;
                    case Tab.LoadGame:
                        campaignSetupUI.UpdateLoadMenu();
                        break;
                    case Tab.Settings:
                        menuTabs[(int)Tab.Settings].RectTransform.ClearChildren();
                        GameMain.Config.SettingsFrame.RectTransform.Parent = menuTabs[(int)Tab.Settings].RectTransform;
                        GameMain.Config.SettingsFrame.RectTransform.RelativeSize = Vector2.One;
                        break;
                    case Tab.JoinServer:
                        GameMain.ServerListScreen.Select();
                        break;
                    case Tab.HostServer:
                        break;
                    case Tab.Tutorials:
                        break;
                    case Tab.CharacterEditor:
                        Submarine.MainSub = null;
                        GameMain.CharacterEditorScreen.Select();
                        break;
                    case Tab.SubmarineEditor:
                        GameMain.SubEditorScreen.Select();
                        break;
                    case Tab.QuickStartDev:
                        QuickStart();
                        break;
                    case Tab.SteamWorkshop:
                        if (!Steam.SteamManager.IsInitialized) return false;
                        GameMain.SteamWorkshopScreen.Select();
                        break;
                }
            }
            else
            { 
                selectedTab = 0;
            }

            if (button != null) button.Selected = true;
            ResetButtonStates(button);

            return true;
        }

        public bool ReturnToMainMenu(GUIButton button, object obj)
        {
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

        private void QuickStart()
        {
            Submarine selectedSub = null;
            string subName = GameMain.Config.QuickStartSubmarineName;
            if (!string.IsNullOrEmpty(subName))
            {
                DebugConsole.NewMessage($"Loading the predefined quick start sub \"{subName}\"", Color.White);
                selectedSub = Submarine.SavedSubmarines.FirstOrDefault(s =>
                    s.Name.ToLower() == subName.ToLower());

                if (selectedSub == null)
                {
                    DebugConsole.NewMessage($"Cannot find a sub that matches the name \"{subName}\".", Color.Red);
                }
            }
            if (selectedSub == null)
            {
                DebugConsole.NewMessage("Loading a random sub.", Color.White);
                var subs = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.Shuttle) && !s.HasTag(SubmarineTag.HideInMenus));
                selectedSub = subs.ElementAt(Rand.Int(subs.Count()));
            }
            var gamesession = new GameSession(
                selectedSub,
                "Data/Saves/test.xml",
                GameModePreset.List.Find(gm => gm.Identifier == "devsandbox"),
                missionPrefab: null);
            //(gamesession.GameMode as SinglePlayerCampaign).GenerateMap(ToolBox.RandomSeed(8));
            gamesession.StartRound(ToolBox.RandomSeed(8));
            GameMain.GameScreen.Select();

            string[] jobIdentifiers = new string[] { "captain", "engineer", "mechanic" };
            for (int i = 0; i < 3; i++)
            {
                var spawnPoint = WayPoint.GetRandom(SpawnType.Human, null, Submarine.MainSub);
                if (spawnPoint == null)
                {
                    DebugConsole.ThrowError("No spawnpoints found in the selected submarine. Quickstart failed.");
                    GameMain.MainMenuScreen.Select();
                    return;
                }
                var characterInfo = new CharacterInfo(
                    Character.HumanConfigFile,
                    jobPrefab: JobPrefab.List.Find(j => j.Identifier == jobIdentifiers[i]));
                if (characterInfo.Job == null)
                {
                    DebugConsole.ThrowError("Failed to find the job \"" + jobIdentifiers[i] + "\"!");
                }

                var newCharacter = Character.Create(Character.HumanConfigFile, spawnPoint.WorldPosition, ToolBox.RandomSeed(8), characterInfo);
                newCharacter.GiveJobItems(spawnPoint);
                gamesession.CrewManager.AddCharacter(newCharacter);
                Character.Controlled = newCharacter;
            }         
        }

        private void UpdateTutorialList()
        {
            var tutorialList = menuTabs[(int)Tab.Tutorials].GetChild<GUIListBox>();
            foreach (GUITextBlock tutorialText in tutorialList.Content.Children)
            {
                if (((Tutorial)tutorialText.UserData).Completed)
                {
                    tutorialText.TextColor = Color.LightGreen;
                }
            }
        }

        private bool ApplySettings(GUIButton button, object userData)
        {
            GameMain.Config.SaveNewPlayerConfig();

            if (userData is Tab) SelectTab(button, (Tab)userData);

            if (GameMain.GraphicsWidth != GameMain.Config.GraphicsWidth || GameMain.GraphicsHeight != GameMain.Config.GraphicsHeight)
            {
                new GUIMessageBox(
                    TextManager.Get("RestartRequiredLabel"),
                    TextManager.Get("RestartRequiredText"));
            }

            return true;
        }

        private bool DiscardSettings(GUIButton button, object userData)
        {
            GameMain.Config.LoadPlayerConfig();
            if (userData is Tab) SelectTab(button, (Tab)userData);

            return true;
        }
        
        private bool JoinServerClicked(GUIButton button, object obj)
        {
            GameMain.ServerListScreen.Select();
            return true;
        }

        private bool SteamWorkshopClicked(GUIButton button, object obj)
        {
            if (!Steam.SteamManager.IsInitialized) { return false; }
            GameMain.SteamWorkshopScreen.Select();
            return true;
        }

        private bool ChangeMaxPlayers(GUIButton button, object obj)
        {
            int.TryParse(maxPlayersBox.Text, out int currMaxPlayers);
            currMaxPlayers = (int)MathHelper.Clamp(currMaxPlayers + (int)button.UserData, 1, NetConfig.MaxPlayers);

            maxPlayersBox.Text = currMaxPlayers.ToString();

            return true;
        }

        private bool HostServerClicked(GUIButton button, object obj)
        {
            string name = serverNameBox.Text;
            if (string.IsNullOrEmpty(name))
            {
                serverNameBox.Flash();
                return false;
            }

            if (!int.TryParse(portBox.Text, out int port) || port < 0 || port > 65535)
            {
                portBox.Text = NetConfig.DefaultPort.ToString();
                portBox.Flash();

                return false;
            }

            int queryPort = 0;
            if (Steam.SteamManager.USE_STEAM)
            {
                if (!int.TryParse(queryPortBox.Text, out queryPort) || queryPort < 0 || queryPort > 65535)
                {
                    portBox.Text = NetConfig.DefaultQueryPort.ToString();
                    portBox.Flash();
                    return false;
                }
            }

            GameMain.NetLobbyScreen = new NetLobbyScreen();
            try
            {
                int ownerKey = Math.Max(CryptoRandom.Instance.Next(),1);

                string arguments = "-name \"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" +
                                   " -port " + port.ToString() +
                                   " -queryport " + queryPort.ToString() +
                                   " -password \"" + passwordBox.Text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" +
                                   " -upnp " + useUpnpBox.Selected +
                                   " -playercount " + maxPlayersBox.Text +
                                    " -ownerkey " + ownerKey.ToString();

                string filename = "DedicatedServer.exe";
#if LINUX || OSX
                filename = "mono";
                arguments = "./DedicatedServer.exe " + arguments;
#endif

                var processInfo = new ProcessStartInfo
                {
                    FileName = filename,
                    Arguments = arguments,
#if !DEBUG
                    WindowStyle = ProcessWindowStyle.Hidden
#endif
                };
                GameMain.ServerChildProcess = Process.Start(processInfo);

                Thread.Sleep(1000); //wait until the server is ready before connecting

                GameMain.Client = new GameClient(name, "127.0.0.1:" + port.ToString(),ownerKey);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start server", e);
            }

            return true;
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
            GameMain.TitleScreen.TitlePosition =
                Vector2.Lerp(GameMain.TitleScreen.TitlePosition, new Vector2(
                    GameMain.TitleScreen.TitleSize.X / 2.0f * GameMain.TitleScreen.Scale + 30.0f,
                    GameMain.TitleScreen.TitleSize.Y / 2.0f * GameMain.TitleScreen.Scale + 30.0f),
                    0.1f);
#if !DEBUG
            if (Steam.SteamManager.USE_STEAM)
            {
                if (GameMain.Config.UseSteamMatchmaking)
                {
                    joinServerButton.Enabled = Steam.SteamManager.IsInitialized;
                    hostServerButton.Enabled = Steam.SteamManager.IsInitialized;
                }
                steamWorkshopButton.Enabled = Steam.SteamManager.IsInitialized;
            }
#else
            joinServerButton.Enabled = true;
            hostServerButton.Enabled = true;
            steamWorkshopButton.Enabled = true;
#endif
        }

        public void DrawBackground(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);

            if (backgroundSprite == null)
            {
                backgroundSprite = (LocationType.List.Where(l => l.UseInMainMenu).GetRandom()).GetPortrait(0);
            }

            if (backgroundSprite != null)
            {
                GUI.DrawBackgroundSprite(spriteBatch, backgroundSprite, 
                    blurAmount: 0.0f, 
                    aberrationStrength: 0.0f);
            }

            spriteBatch.Begin(blendState: BlendState.AlphaBlend);
            backgroundVignette.Draw(spriteBatch, Vector2.Zero, Color.White, Vector2.Zero, 0.0f, 
                new Vector2(GameMain.GraphicsWidth / backgroundVignette.size.X, GameMain.GraphicsHeight / backgroundVignette.size.Y));
            spriteBatch.End();
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            DrawBackground(graphics, spriteBatch);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, GameMain.ScissorTestEnable);

            GUI.Draw(Cam, spriteBatch);
            
#if DEBUG
            GUI.Font.DrawString(spriteBatch, "Barotrauma v" + GameMain.Version + " (debug build)", new Vector2(10, GameMain.GraphicsHeight - 20), Color.White);
#else
            GUI.Font.DrawString(spriteBatch, "Barotrauma v" + GameMain.Version, new Vector2(10, GameMain.GraphicsHeight - 20), Color.White);
#endif

            spriteBatch.End();
        }

        private void StartGame(Submarine selectedSub, string saveName, string mapSeed)
        {
            if (string.IsNullOrEmpty(saveName)) return;

            string[] existingSaveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Singleplayer);

            if (Array.Find(existingSaveFiles, s => s == saveName) != null)
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
            catch (IOException e)
            {
                DebugConsole.ThrowError("Copying the file \"" + selectedSub.FilePath + "\" failed. The file may have been deleted or in use by another process. Try again or select another submarine.", e);
                GameAnalyticsManager.AddErrorEventOnce(
                    "MainMenuScreen.StartGame:IOException" + selectedSub.Name,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Copying the file \"" + selectedSub.FilePath + "\" failed.\n" + e.Message + "\n" + Environment.StackTrace);
                return;
            }

            selectedSub = new Submarine(Path.Combine(SaveUtil.TempPath, selectedSub.Name + ".sub"), "");

            ContextualTutorial.Selected = campaignSetupUI.TutorialSelected;
            GameMain.GameSession = new GameSession(selectedSub, saveName,
                GameModePreset.List.Find(g => g.Identifier == "singleplayercampaign"));
            (GameMain.GameSession.GameMode as CampaignMode).GenerateMap(mapSeed);


            GameMain.LobbyScreen.Select();
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


            GameMain.LobbyScreen.Select();
        }

        #region UI Methods      
        private void CreateHostServerFields()
        {
            Vector2 textLabelSize = new Vector2(1.0f, 0.1f);
            Alignment textAlignment = Alignment.CenterLeft;
            Vector2 textFieldSize = new Vector2(0.5f, 1.0f);
            Vector2 tickBoxSize = new Vector2(0.4f, 0.07f);
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.85f, 0.75f), menuTabs[(int)Tab.HostServer].RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) })
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            }; 
            GUIComponent parent = paddedFrame;
            
            new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("HostServerButton"), textAlignment: Alignment.Center, font: GUI.LargeFont) { ForceUpperCase = true };

            var label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("ServerName"), textAlignment: textAlignment);
            serverNameBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment);

            label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("ServerPort"), textAlignment: textAlignment);
            portBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment)
            {
                Text = NetConfig.DefaultPort.ToString(),
                ToolTip = TextManager.Get("ServerPortToolTip")
            };

            if (Steam.SteamManager.USE_STEAM)
            {
                label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("ServerQueryPort"), textAlignment: textAlignment);
                queryPortBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment)
                {
                    Text = NetConfig.DefaultQueryPort.ToString(),
                    ToolTip = TextManager.Get("ServerQueryPortToolTip")
                };
            }

            var maxPlayersLabel = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("MaxPlayers"), textAlignment: textAlignment);
            var buttonContainer = new GUILayoutGroup(new RectTransform(textFieldSize, maxPlayersLabel.RectTransform, Anchor.CenterRight), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.1f
            };
            new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonContainer.RectTransform), "-", textAlignment: Alignment.Center)
            {
                UserData = -1,
                OnClicked = ChangeMaxPlayers
            };

            maxPlayersBox = new GUITextBox(new RectTransform(new Vector2(0.6f, 1.0f), buttonContainer.RectTransform), textAlignment: Alignment.Center)
            {
                Text = "8",
                Enabled = false
            };
            new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonContainer.RectTransform), "+", textAlignment: Alignment.Center)
            {
                UserData = 1,
                OnClicked = ChangeMaxPlayers
            };
            
            label = new GUITextBlock(new RectTransform(textLabelSize, parent.RectTransform), TextManager.Get("Password"), textAlignment: textAlignment);
            passwordBox = new GUITextBox(new RectTransform(textFieldSize, label.RectTransform, Anchor.CenterRight), textAlignment: textAlignment);
            
            isPublicBox = new GUITickBox(new RectTransform(tickBoxSize, parent.RectTransform), TextManager.Get("PublicServer"))
            {
                ToolTip = TextManager.Get("PublicServerToolTip")
            };
            
            useUpnpBox = new GUITickBox(new RectTransform(tickBoxSize, parent.RectTransform), TextManager.Get("AttemptUPnP"))
            {
                ToolTip = TextManager.Get("AttemptUPnPToolTip")
            };

            new GUIButton(new RectTransform(new Vector2(0.4f, 0.1f), menuTabs[(int)Tab.HostServer].RectTransform, Anchor.BottomRight)
            {
                RelativeOffset = new Vector2(0.05f, 0.05f)
            }, TextManager.Get("StartServerButton"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                OnClicked = HostServerClicked
            };
        }
        #endregion

    }
}
