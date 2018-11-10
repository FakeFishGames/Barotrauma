using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        private GUIFrame menu;
        private GUIFrame infoFrame;
        private GUIFrame myCharacterFrame;
        private GUIListBox playerList;

        private GUIListBox subList, modeList, chatBox;
        public GUIListBox ChatBox
        {
            get
            {
                return chatBox;
            }
        }

        private GUIScrollBar levelDifficultyScrollBar;

        private GUIButton[] traitorProbabilityButtons;
        private GUITextBlock traitorProbabilityText;

        private GUIButton[] botCountButtons;
        private GUITextBlock botCountText;

        private GUIButton[] botSpawnModeButtons;
        private GUITextBlock botSpawnModeText;

        private GUIButton[] missionTypeButtons;
        private GUIComponent missionTypeContainer;

        private GUIListBox jobList;

        private GUITextBox textBox, seedBox;
        public GUITextBox TextBox
        {
            get
            {
                return textBox;
            }
        }

        private GUIFrame defaultModeContainer, campaignContainer;
        private GUIButton campaignViewButton, spectateButton;
        public GUIButton SettingsButton { get; private set; }

        private GUITickBox playYourself;
        
        private GUIFrame playerInfoContainer;
        private GUIImage playerHeadSprite;
        private GUIButton jobInfoFrame;
        private GUIButton playerFrame;

        private GUITickBox autoRestartBox;
                
        private GUIDropDown shuttleList;
        private GUITickBox shuttleTickBox;

        private CampaignUI campaignUI;
        private GUIComponent campaignSetupUI;

        private Sprite backgroundSprite;

        private GUITextBox serverMessage;

        private float autoRestartTimer;

        //persistent characterinfo provided by the server
        //(character settings cannot be edited when this is set)
        private CharacterInfo campaignCharacterInfo;
        public bool CampaignCharacterDiscarded
        {
            get;
            private set;
        }

        //elements that can only be used by the host
        private List<GUIComponent> clientDisabledElements = new List<GUIComponent>();
        //elements that aren't shown client-side
        private List<GUIComponent> clientHiddenElements = new List<GUIComponent>();

        private bool AllowSubSelection
        {
            get
            {
                return GameMain.NetworkMember.ServerSettings.Voting.AllowSubVoting ||
                    (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectSub));
            }
        }

        public GUITextBox ServerMessage
        {
            get { return serverMessage; }
        }

        public string ServerMessageText
        {
            get { return serverMessage.Text; }
            set { serverMessage.Text = value; }
        }

        public GUIButton ShowLogButton
        {
            get;
            private set;
        }

        public GUIListBox SubList
        {
            get { return subList; }
        }

        public GUIDropDown ShuttleList
        {
            get { return shuttleList; }
        }

        public GUITickBox ShuttleTickBox
        {
            get { return shuttleTickBox; }
        }

        public GUIListBox ModeList
        {
            get { return modeList; }
        }
        public int SelectedModeIndex
        {
            get { return modeList.SelectedIndex; }
            set { modeList.Select(value); }
        }

        public GUIListBox PlayerList
        {
            get { return playerList; }
        }

        public GUIButton StartButton
        {
            get;
            private set;
        }

        public GUITickBox ReadyToStartBox
        {
            get;
            private set;
        }

        public GUIFrame MyCharacterFrame
        {
            get { return myCharacterFrame; }
        }

        public bool MyCharacterFrameOpen;

        public GUIFrame InfoFrame
        {
            get { return infoFrame; }
        }

        public Submarine SelectedSub
        {
            get { return subList.SelectedData as Submarine; }
            set { subList.Select(value); }
        }

        public Submarine SelectedShuttle
        {
            get { return shuttleList.SelectedData as Submarine; }
        }

        public bool UsingShuttle
        {
            get { return shuttleTickBox.Selected; }
            set { shuttleTickBox.Selected = value; if (GameMain.Client != null) shuttleTickBox.Enabled = false; }
        }

        public GameModePreset SelectedMode
        {
            get { return modeList.SelectedData as GameModePreset; }
        }

        public int MissionTypeIndex
        {
            get { return (int)missionTypeContainer.UserData; }
            set { missionTypeContainer.UserData = value; }
        }
        
        public List<JobPrefab> JobPreferences
        {
            get
            {
                List<JobPrefab> jobPreferences = new List<JobPrefab>();
                foreach (GUIComponent child in jobList.Content.Children)
                {
                    JobPrefab jobPrefab = child.UserData as JobPrefab;
                    if (jobPrefab == null) continue;
                    jobPreferences.Add(jobPrefab);
                }
                return jobPreferences;
            }
        }

        public string LevelSeed
        {
            get
            {
                return levelSeed;
            }
            set
            {
                if (levelSeed == value) return;

                levelSeed = value;
                backgroundSprite = LocationType.Random(levelSeed)?.Background;
                seedBox.Text = levelSeed;

                //lastUpdateID++;
            }
        }

        public string AutoRestartText()
        {
            /*TODO: fix?
            if (GameMain.Server != null)
            {
                if (!GameMain.Server.AutoRestart || GameMain.Server.ConnectedClients.Count == 0) return "";
                return TextManager.Get("RestartingIn") + " " + ToolBox.SecondsToReadableTime(Math.Max(GameMain.Server.AutoRestartTimer, 0));
            }*/

            if (autoRestartTimer == 0.0f) return "";
            return TextManager.Get("RestartingIn") + " " + ToolBox.SecondsToReadableTime(Math.Max(autoRestartTimer, 0));
        }

        public NetLobbyScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 80, 1500);
            int height = Math.Min(GameMain.GraphicsHeight - 80, 800);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), GUI.Canvas, Anchor.Center), style: null);

            float panelSpacing = 0.02f;

            //server info panel ------------------------------------------------------------

            infoFrame = new GUIFrame(new RectTransform(new Vector2(0.7f, 0.6f), menu.RectTransform));

            defaultModeContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), infoFrame.RectTransform, Anchor.Center), style: null);
            campaignContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), infoFrame.RectTransform, Anchor.Center), style: null)
            {
                Visible = false
            };

            //chatbox ----------------------------------------------------------------------
            GUIFrame chatFrame = new GUIFrame(new RectTransform(new Vector2(0.7f, 0.4f - panelSpacing), menu.RectTransform, Anchor.BottomLeft));
            GUIFrame paddedChatFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.85f), chatFrame.RectTransform, Anchor.Center), style: null);

            chatBox = new GUIListBox(new RectTransform(new Point(paddedChatFrame.Rect.Width, paddedChatFrame.Rect.Height - 30), paddedChatFrame.RectTransform) { IsFixedSize = false });
            textBox = new GUITextBox(new RectTransform(new Point(paddedChatFrame.Rect.Width, 20), paddedChatFrame.RectTransform, Anchor.BottomLeft) { IsFixedSize = false })
            {
                MaxTextLength = ChatMessage.MaxLength,
                Font = GUI.SmallFont
            };

            //player info panel ------------------------------------------------------------

            myCharacterFrame = new GUIFrame(new RectTransform(new Vector2(0.3f - panelSpacing, 0.6f), menu.RectTransform, Anchor.TopRight));
            playerInfoContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), myCharacterFrame.RectTransform, Anchor.Center), style: null);

            playYourself = new GUITickBox(new RectTransform(new Vector2(0.06f, 0.06f), myCharacterFrame.RectTransform) { RelativeOffset = new Vector2(0.05f,0.05f) },
                TextManager.Get("PlayYourself"))
            {
                OnSelected = TogglePlayYourself,
                UserData = "playyourself"
            };

            var toggleMyPlayerFrame = new GUIButton(new RectTransform(new Point(25, 70), myCharacterFrame.RectTransform, Anchor.TopLeft, Pivot.TopRight), "", style: "GUIButtonHorizontalArrow");
            toggleMyPlayerFrame.OnClicked += (GUIButton btn, object userdata) =>
            {
                MyCharacterFrameOpen = !MyCharacterFrameOpen;
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = MyCharacterFrameOpen ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                return true;
            };

            //player list ------------------------------------------------------------------

            GUIFrame playerListFrame = new GUIFrame(new RectTransform(new Vector2(0.3f - panelSpacing, 0.4f - panelSpacing), menu.RectTransform, Anchor.BottomRight));
            GUIFrame paddedPlayerListFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.85f), playerListFrame.RectTransform, Anchor.Center), style: null);

            playerList = new GUIListBox(new RectTransform(Vector2.One, paddedPlayerListFrame.RectTransform))
            {
                OnSelected = SelectPlayer
            };

            //--------------------------------------------------------------------------------------------------------------------------------
            //infoframe contents
            //--------------------------------------------------------------------------------------------------------------------------------

            var infoColumnContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.7f - 0.02f, 0.75f), defaultModeContainer.RectTransform, Anchor.BottomLeft), 
                isHorizontal: true, childAnchor: Anchor.BottomLeft)
                { RelativeSpacing = 0.02f, Stretch = true };
            var leftInfoColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.35f, 1.0f), infoColumnContainer.RectTransform, Anchor.BottomLeft))
                { RelativeSpacing = 0.02f, Stretch = true };
            var midInfoColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.35f, 1.0f), infoColumnContainer.RectTransform, Anchor.BottomLeft))
                { RelativeSpacing = 0.02f, Stretch = true };

            var rightInfoColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.85f), defaultModeContainer.RectTransform, Anchor.TopRight))
                { RelativeSpacing = 0.02f, Stretch = true };
            
            var topButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), rightInfoColumn.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), rightInfoColumn.RectTransform), style: null);

            //server info ------------------------------------------------------------------

            var serverName = new GUITextBox(new RectTransform(new Vector2(0.3f, 0.05f), defaultModeContainer.RectTransform))
            {
                TextGetter = GetServerName,
                Enabled = false//GameMain.Client.HasPermission(ClientPermissions.ManageSettings)
            };
            serverName.OnTextChanged += ChangeServerName;
            clientDisabledElements.Add(serverName);

            serverMessage = new GUITextBox(new RectTransform(new Vector2(infoColumnContainer.RectTransform.RelativeSize.X, 0.15f), defaultModeContainer.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.07f) })
            {
                Wrap = true
            };
            serverMessage.OnTextChanged += UpdateServerMessage;
            clientDisabledElements.Add(serverMessage);
            
            SettingsButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), topButtonContainer.RectTransform, Anchor.TopRight),
                TextManager.Get("ServerSettingsButton"));
            clientHiddenElements.Add(SettingsButton);

            ShowLogButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), topButtonContainer.RectTransform, Anchor.TopRight),
                TextManager.Get("ServerLog"))
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (GameMain.NetworkMember.ServerSettings.ServerLog.LogFrame == null)
                    {
                        GameMain.NetworkMember.ServerSettings.ServerLog.CreateLogFrame();
                    }
                    else
                    {
                        GameMain.NetworkMember.ServerSettings.ServerLog.LogFrame = null;
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                    return true;
                }
            };
            clientHiddenElements.Add(ShowLogButton);

            //submarine list ------------------------------------------------------------------
            
            var subLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), leftInfoColumn.RectTransform), TextManager.Get("Submarine"));
            subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.9f), leftInfoColumn.RectTransform))
            {
                OnSelected = VotableClicked
            };

            var voteText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), subLabel.RectTransform, Anchor.TopRight),
                TextManager.Get("Votes"), textAlignment: Alignment.CenterRight)
            {
                UserData = "subvotes",
                Visible = false
            };

            //respawn shuttle ------------------------------------------------------------------

            shuttleTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), midInfoColumn.RectTransform), TextManager.Get("RespawnShuttle"))
            {
                Selected = true,
                OnSelected = (GUITickBox box) =>
                {
                    shuttleList.Enabled = box.Selected;
                    //if (GameMain.Server != null) lastUpdateID++;
                    return true;
                }
            };
            shuttleList = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), midInfoColumn.RectTransform), elementCount: 10);

            //gamemode ------------------------------------------------------------------

            var modeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), midInfoColumn.RectTransform), TextManager.Get("GameMode"));
            modeList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), midInfoColumn.RectTransform))
            {
                OnSelected = VotableClicked
            };

            voteText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), modeLabel.RectTransform, Anchor.TopRight),
                TextManager.Get("Votes"), textAlignment: Alignment.CenterRight)
            {
                UserData = "modevotes",
                Visible = false
            };

            foreach (GameModePreset mode in GameModePreset.list)
            {
                if (mode.IsSinglePlayer) continue;

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), modeList.Content.RectTransform),
                    TextManager.Get("GameMode." + mode.Name), style: "ListBoxElement", textAlignment: Alignment.CenterLeft)
                {
                    ToolTip = mode.Description,
                    UserData = mode
                };
            }

            //mission type ------------------------------------------------------------------

            missionTypeContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), midInfoColumn.RectTransform), isHorizontal: true)
            {
                UserData = 0,
                Visible = false,
                Stretch = true
            };

            var missionTypeText = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1.0f), missionTypeContainer.RectTransform),
                TextManager.Get("MissionType"));
            missionTypeButtons = new GUIButton[2];
            missionTypeButtons[0] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), missionTypeContainer.RectTransform), "<")
            {
                UserData = -1
            };
            new GUITextBlock(new RectTransform(new Vector2(0.4f, 1.0f), missionTypeContainer.RectTransform),
                TextManager.Get("MissionType.Random"), textAlignment: Alignment.Center)
            {
                UserData = 0
            };
            missionTypeButtons[1] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), missionTypeContainer.RectTransform), ">")
            {
                UserData = 1
            };
            clientDisabledElements.AddRange(missionTypeButtons);

            //seed ------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), TextManager.Get("LevelSeed"));
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform));
            seedBox.OnTextChanged += SelectSeed;
            clientDisabledElements.Add(seedBox);
            LevelSeed = ToolBox.RandomSeed(8);

            //level difficulty ------------------------------------------------------------------

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), TextManager.Get("LevelDifficulty"));
            levelDifficultyScrollBar = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), barSize: 0.1f);
            clientDisabledElements.Add(levelDifficultyScrollBar);

            //traitor probability ------------------------------------------------------------------
            
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), rightInfoColumn.RectTransform), style: null); //spacing

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), TextManager.Get("Traitors"));

            var traitorProbContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), isHorizontal: true);
            traitorProbabilityButtons = new GUIButton[2];
            traitorProbabilityButtons[0] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), traitorProbContainer.RectTransform), "<")
            {
                UserData = -1
            };
            traitorProbabilityText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), traitorProbContainer.RectTransform), TextManager.Get("No"), textAlignment: Alignment.Center);
            traitorProbabilityButtons[1] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), traitorProbContainer.RectTransform), ">")
            {
                UserData = 1
            };
            clientDisabledElements.AddRange(traitorProbabilityButtons);

            //bot count ------------------------------------------------------------------
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), TextManager.Get("BotCount"));
            var botCountContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), isHorizontal: true);
            botCountButtons = new GUIButton[2];
            botCountButtons[0] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), botCountContainer.RectTransform), "<")
            {
                UserData = -1
            };
            botCountText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), botCountContainer.RectTransform), "0", textAlignment: Alignment.Center);
            botCountButtons[1] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), botCountContainer.RectTransform), ">")
            {
                UserData = 1
            };
            clientDisabledElements.AddRange(botCountButtons);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), TextManager.Get("BotSpawnMode"));
            var botSpawnModeContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), isHorizontal: true);
            botSpawnModeButtons = new GUIButton[2];
            botSpawnModeButtons[0] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), botSpawnModeContainer.RectTransform), "<")
            {
                UserData = -1
            };
            botSpawnModeText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), botSpawnModeContainer.RectTransform), "", textAlignment: Alignment.Center);
            botSpawnModeButtons[1] = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), botSpawnModeContainer.RectTransform), ">")
            {
                UserData = 1
            };
            clientDisabledElements.AddRange(botSpawnModeButtons);

            //misc buttons ------------------------------------------------------------------
            
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), rightInfoColumn.RectTransform), style: null); //spacing

            autoRestartBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), TextManager.Get("AutoRestart"))
            {
                OnSelected = ToggleAutoRestart
            };
            clientDisabledElements.Add(autoRestartBox);
            var restartText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform), "", font: GUI.SmallFont)
            {
                TextGetter = AutoRestartText
            };

            StartButton = new GUIButton(new RectTransform(new Vector2(0.3f, 0.1f), defaultModeContainer.RectTransform, Anchor.BottomRight),
                TextManager.Get("StartGameButton"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, obj) => { GameMain.Client.RequestStartRound(); return true; }
            };
            clientHiddenElements.Add(StartButton);

            ReadyToStartBox = new GUITickBox(new RectTransform(new Vector2(0.3f, 0.06f), defaultModeContainer.RectTransform, Anchor.BottomRight),
                TextManager.Get("ReadyToStartTickBox"), GUI.SmallFont)
            {
                Visible = false
            };

            campaignViewButton = new GUIButton(new RectTransform(new Vector2(0.3f, 0.1f), defaultModeContainer.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.0f, 0.06f) },
                TextManager.Get("CampaignView"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, obj) => { ToggleCampaignView(true); return true; },
                Visible = false
            };
            
            spectateButton = new GUIButton(new RectTransform(new Vector2(0.3f, 0.1f), defaultModeContainer.RectTransform, Anchor.BottomRight),
                TextManager.Get("SpectateButton"), style: "GUIButtonLarge");
        }

        public override void Deselect()
        {
            textBox.Deselect();
            myCharacterFrame.GetChild<GUIButton>().Visible = true;
            CampaignCharacterDiscarded = false;
        }

        public override void Select()
        {
            if (GameMain.NetworkMember == null) return;
            Character.Controlled = null;
            GameMain.LightManager.LosEnabled = false;

            CampaignCharacterDiscarded = false;

            textBox.Select();

            textBox.OnEnterPressed = GameMain.Client.EnterChatMessage;
            textBox.OnTextChanged += GameMain.Client.TypingChatMessage;

            myCharacterFrame.RectTransform.AbsoluteOffset = new Point(0, 0);
            myCharacterFrame.GetChild<GUIButton>().Visible = false;

            subList.Enabled = AllowSubSelection;// || GameMain.Server != null;
            shuttleList.Enabled = AllowSubSelection;// || GameMain.Server != null;

            modeList.Enabled = 
                GameMain.NetworkMember.ServerSettings.Voting.AllowModeVoting || 
                (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectMode));

            //ServerName = (GameMain.Server == null) ? ServerName : GameMain.Server.Name;

            //disable/hide elements the clients are not supposed to use/see
            //TODO: is this even applicable anymore?
            clientDisabledElements.ForEach(c => c.Enabled = false);//GameMain.Server != null);
            clientHiddenElements.ForEach(c => c.Visible = false);//GameMain.Server != null);

            ShowLogButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ServerLog);

            SettingsButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            StartButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ManageRound);

            if (GameMain.Client != null)
            {
                spectateButton.Visible = GameMain.Client.GameStarted;
                ReadyToStartBox.Visible = !GameMain.Client.GameStarted && !GameMain.Client.HasPermission(ClientPermissions.ManageRound);
                ReadyToStartBox.Selected = false;
                GameMain.Client.SetReadyToStart(ReadyToStartBox);
            }
            else
            {
                spectateButton.Visible = false;
                ReadyToStartBox.Visible = false;
            }
            SetPlayYourself(playYourself.Selected);            

            /*if (IsServer && GameMain.Server != null)
            {
                List<Submarine> subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList();

                ReadyToStartBox.Visible = false;
                StartButton.OnClicked = GameMain.Server.StartGameClicked;
                settingsButton.OnClicked = GameMain.Server.ToggleSettingsFrame;

                int prevSelectedSub = subList.SelectedIndex;
                UpdateSubList(subList, subsToShow);

                int prevSelectedShuttle = shuttleList.SelectedIndex;
                UpdateSubList(shuttleList, subsToShow);
                modeList.OnSelected = VotableClicked;
                modeList.OnSelected = SelectMode;
                subList.OnSelected = VotableClicked;
                subList.OnSelected = SelectSub;
                shuttleList.OnSelected = SelectSub;

                levelDifficultyScrollBar.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    SetLevelDifficulty(barScroll * 100.0f);
                    return true;
                };

                traitorProbabilityButtons[0].OnClicked = traitorProbabilityButtons[1].OnClicked = ToggleTraitorsEnabled;
                botCountButtons[0].OnClicked = botCountButtons[1].OnClicked = ChangeBotCount;
                botSpawnModeButtons[0].OnClicked = botSpawnModeButtons[1].OnClicked = ChangeBotSpawnMode;
                missionTypeButtons[0].OnClicked = missionTypeButtons[1].OnClicked = ToggleMissionType;
                
                if (subList.SelectedComponent == null) subList.Select(Math.Max(0, prevSelectedSub));
                if (shuttleList.Selected == null)
                {
                    var shuttles = shuttleList.GetChildren().Where(c => c.UserData is Submarine && ((Submarine)c.UserData).HasTag(SubmarineTag.Shuttle));
                    if (prevSelectedShuttle == -1 && shuttles.Any())
                    {
                        shuttleList.SelectItem(shuttles.First().UserData);
                    }
                    else
                    {
                        shuttleList.Select(Math.Max(0, prevSelectedShuttle));
                    }
                }

                GameAnalyticsManager.SetCustomDimension01("multiplayer");
                
                if (GameModePreset.list.Count > 0 && modeList.SelectedComponent == null) modeList.Select(0);
                GameMain.Server.Voting.ResetVotes(GameMain.Server.ConnectedClients);
            }
            else */
            if (GameMain.Client != null)
            {
                GameMain.Client.ServerSettings.Voting.ResetVotes(GameMain.Client.ConnectedClients);
                if (!playYourself.Selected)
                {
                    playYourself.Selected = true;
                    TogglePlayYourself(playYourself);
                }
                spectateButton.OnClicked = GameMain.Client.SpectateClicked;
                ReadyToStartBox.OnSelected = GameMain.Client.SetReadyToStart;
            }

            GameMain.NetworkMember.EndVoteCount = 0;
            GameMain.NetworkMember.EndVoteMax = 1;

            base.Select();
        }

        /*TODO: remove?
        public void RandomizeSettings()
        {
            if (GameMain.Server == null) return;

            if (GameMain.Server.RandomizeSeed) LevelSeed = ToolBox.RandomSeed(8);
            if (GameMain.Server.SubSelectionMode == SelectionMode.Random)
            {
                var nonShuttles = subList.Content.Children.Where(c => c.UserData is Submarine && !((Submarine)c.UserData).HasTag(SubmarineTag.Shuttle));
                subList.Select(nonShuttles.GetRandom());
            }
            if (GameMain.Server.ModeSelectionMode == SelectionMode.Random)
            {
                var allowedGameModes = GameModePreset.list.FindAll(m => !m.IsSinglePlayer && m.Name != "Campaign");
                modeList.Select(allowedGameModes[Rand.Range(0, allowedGameModes.Count)]);
            }
        }*/
        
        public void ShowSpectateButton()
        {
            if (GameMain.Client == null) return;
            spectateButton.Visible = true;
        }

        public void SetCampaignCharacterInfo(CharacterInfo characterInfo)
        {
            if (CampaignCharacterDiscarded) return;

            campaignCharacterInfo = characterInfo;
            if (campaignCharacterInfo != null)
            {
                UpdatePlayerFrame(campaignCharacterInfo, false);
            }
            else
            {
                UpdatePlayerFrame(null, true);
            }
        }

        private void UpdatePlayerFrame(CharacterInfo characterInfo, bool allowEditing = true)
        {
            if (characterInfo == null)
            {
                characterInfo =
                    new CharacterInfo(Character.HumanConfigFile, GameMain.NetworkMember.Name, GameMain.Config.CharacterGender, null)
                    {
                        HeadSpriteId = GameMain.Config.CharacterHeadIndex
                    };
                GameMain.Client.CharacterInfo = characterInfo;
            }

            playerInfoContainer.ClearChildren();
            
            GUIComponent infoContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), playerInfoContainer.RectTransform, Anchor.BottomCenter), childAnchor: Anchor.TopCenter)
                { Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), infoContainer.RectTransform), characterInfo.Name, font: GUI.LargeFont, textAlignment: Alignment.Center, wrap: true);

            GUIComponent headContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 0.2f), infoContainer.RectTransform, Anchor.TopCenter), isHorizontal: true)
            {
                Stretch = true
            };

            if (allowEditing)
            {
                new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), headContainer.RectTransform), "", style: "GUIButtonHorizontalArrow")
                {
                    UserData = -1,
                    OnClicked = ToggleHead
                }.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);
            }

            playerHeadSprite = new GUIImage(new RectTransform(new Vector2(0.3f, 1.0f), headContainer.RectTransform), sprite: null, scaleToFit: true)
            {
                UserData = "playerhead"
            };

            if (allowEditing)
            {
                new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), headContainer.RectTransform), style: "GUIButtonHorizontalArrow")
                {
                    UserData = 1,
                    OnClicked = ToggleHead
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), infoContainer.RectTransform),
                    TextManager.Get("Gender"), textAlignment: Alignment.Center);
                GUIComponent genderContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 0.06f), infoContainer.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                GUIButton maleButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), genderContainer.RectTransform),
                    TextManager.Get("Male"))
                {
                    UserData = Gender.Male,
                    OnClicked = SwitchGender
                };

                GUIButton femaleButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), genderContainer.RectTransform),
                    TextManager.Get("Female"))
                {
                    UserData = Gender.Female,
                    OnClicked = SwitchGender
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), infoContainer.RectTransform), 
                    TextManager.Get("JobPreferences"));

                jobList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), infoContainer.RectTransform))
                {
                    Enabled = false
                };

                int i = 1;
                foreach (string jobIdentifier in GameMain.Config.JobPreferences)
                {
                    JobPrefab job = JobPrefab.List.Find(j => j.Identifier == jobIdentifier);
                    if (job == null) continue;

                    var jobFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), jobList.Content.RectTransform), style: "ListBoxElement")
                    {
                        UserData = job
                    };
                    GUITextBlock jobText = new GUITextBlock(new RectTransform(new Vector2(0.66f, 1.0f), jobFrame.RectTransform, Anchor.CenterRight),
                        i + ". " + job.Name + "    ", textAlignment: Alignment.CenterLeft);

                    var jobButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.8f), jobFrame.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.02f, 0.0f) },
                        isHorizontal: true, childAnchor: Anchor.CenterLeft)
                    {
                        RelativeSpacing = 0.03f
                    };

                    int buttonSize = jobButtonContainer.Rect.Height;
                    GUIButton infoButton = new GUIButton(new RectTransform(new Point(buttonSize, buttonSize), jobButtonContainer.RectTransform), "?")
                    {
                        UserData = job,
                        OnClicked = ViewJobInfo
                    };

                    GUIButton upButton = new GUIButton(new RectTransform(new Point(buttonSize, buttonSize), jobButtonContainer.RectTransform), "")
                    {
                        UserData = -1,
                        OnClicked = ChangeJobPreference
                    };
                    new GUIImage(new RectTransform(new Vector2(0.8f, 0.8f), upButton.RectTransform, Anchor.Center), GUI.Arrow, scaleToFit: true);

                    GUIButton downButton = new GUIButton(new RectTransform(new Point(buttonSize, buttonSize), jobButtonContainer.RectTransform), "")
                    {
                        UserData = 1,
                        OnClicked = ChangeJobPreference
                    };
                    new GUIImage(new RectTransform(new Vector2(0.8f, 0.8f), downButton.RectTransform, Anchor.Center), GUI.Arrow, scaleToFit: true)
                    {
                        Rotation = MathHelper.Pi
                    };
                }

                UpdateJobPreferences(jobList);
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), infoContainer.RectTransform), characterInfo.Job.Name, textAlignment: Alignment.Center, wrap: true);

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), infoContainer.RectTransform), TextManager.Get("Skills"));
                foreach (Skill skill in characterInfo.Job.Skills)
                {
                    Color textColor = Color.White * (0.5f + skill.Level / 200.0f);
                    var skillText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), infoContainer.RectTransform),
                        "  - " + TextManager.Get("SkillName." + skill.Identifier) + ": " + (int)skill.Level, textColor);
                }

                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), infoContainer.RectTransform), style: null);

                new GUIButton(new RectTransform(new Vector2(0.8f, 0.1f), infoContainer.RectTransform, Anchor.BottomCenter), "Create new")
                {
                    IgnoreLayoutGroups = true,
                    OnClicked = (btn, userdata) =>
                    {
                        var confirmation = new GUIMessageBox(TextManager.Get("NewCampaignCharacterHeader"), TextManager.Get("NewCampaignCharacterText"),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                        confirmation.Buttons[0].OnClicked += confirmation.Close;
                        confirmation.Buttons[0].OnClicked += (btn2, userdata2) =>
                        {
                            CampaignCharacterDiscarded = true;
                            campaignCharacterInfo = null;
                            UpdatePlayerFrame(null, true);
                            return true;
                        };
                        confirmation.Buttons[1].OnClicked += confirmation.Close;
                        return true;
                    }
                };
            }

            UpdatePlayerHead(characterInfo);            
        }
        
        public bool TogglePlayYourself(GUITickBox tickBox)
        {
            if (tickBox.Selected)
            {
                UpdatePlayerFrame(campaignCharacterInfo, allowEditing: campaignCharacterInfo == null);
            }
            else
            {
                playerInfoContainer.ClearChildren();
                
                GameMain.Client.CharacterInfo = null;
                GameMain.Client.Character = null;

                new GUITextBlock(new RectTransform(Vector2.One, playerInfoContainer.RectTransform, Anchor.Center), 
                    TextManager.Get("PlayingAsSpectator"),
                    textAlignment: Alignment.Center);
            }
            return false;
        }

        public void SetPlayYourself(bool playYourself)
        {
            this.playYourself.Selected = playYourself;
            if (playYourself)
            {
                UpdatePlayerFrame(campaignCharacterInfo, allowEditing: campaignCharacterInfo == null);
            }
            else
            {
                playerInfoContainer.ClearChildren();

                GameMain.Client.CharacterInfo = null;
                GameMain.Client.Character = null;

                new GUITextBlock(new RectTransform(Vector2.One, playerInfoContainer.RectTransform, Anchor.Center),
                    TextManager.Get("PlayingAsSpectator"),
                    textAlignment: Alignment.Center);
            }
        }

        public void SetAllowSpectating(bool allowSpectating)
        {
            //show the player config menu if spectating is not allowed
            if (!playYourself.Selected && !allowSpectating)
            {
                playYourself.Selected = !playYourself.Selected;
                TogglePlayYourself(playYourself);
            }
            //hide "play yourself" tickbox if spectating is not allowed
            playYourself.Visible = allowSpectating;            
        }

        public void SetAutoRestart(bool enabled, float timer = 0.0f)
        {
            autoRestartBox.Selected = enabled;
            autoRestartTimer = timer;
        }

        public void ToggleAutoRestart()
        {
            autoRestartBox.Selected = !autoRestartBox.Selected;
            ToggleAutoRestart(autoRestartBox);
        }

        private bool ToggleAutoRestart(GUITickBox tickBox)
        {
            return false;
            /*if (GameMain.Server == null) return false;

            GameMain.Server.AutoRestart = tickBox.Selected;

            lastUpdateID++;

            return true;*/
        }

        public void SetMissionType(int missionTypeIndex)
        {
            if (missionTypeIndex < 0 || missionTypeIndex >= Enum.GetValues(typeof(MissionType)).Length) return;
            
            ((GUITextBlock)missionTypeContainer.GetChild(2)).Text = TextManager.Get("MissionType." + ((MissionType)missionTypeIndex).ToString());
            missionTypeContainer.UserData = ((MissionType)missionTypeIndex);
        }

        public bool ToggleMissionType(GUIButton button, object userData)
        {
            return false;
            /*if (GameMain.Server == null) return false;

            int missionTypeIndex = (int)missionTypeContainer.UserData;
            missionTypeIndex += (int)userData;

            if (missionTypeIndex < 0) missionTypeIndex = Enum.GetValues(typeof(MissionType)).Length - 1;
            if (missionTypeIndex >= Enum.GetValues(typeof(MissionType)).Length) missionTypeIndex = 0;

            SetMissionType(missionTypeIndex);

            lastUpdateID++;

            return true;*/
        }
        
        public bool ToggleTraitorsEnabled(GUIButton button, object userData)
        {
            ToggleTraitorsEnabled((int)userData);
            return true;
        }

        public bool ChangeBotCount(GUIButton button, object userData)
        {
            return false;
            /*if (GameMain.Server == null) return false;
            SetBotCount(GameMain.Server.BotCount + (int)userData);
            return true;*/
        }

        public bool ChangeBotSpawnMode(GUIButton button, object userData)
        {
            return false;
            /*if (GameMain.Server == null) return false;
            SetBotSpawnMode(GameMain.Server.BotSpawnMode == BotSpawnMode.Fill ? BotSpawnMode.Normal : BotSpawnMode.Fill);
            return true;*/
        }

        private bool SelectSub(GUIComponent component, object obj)
        {
            return false;
            /*if (GameMain.Server == null) return false;

            lastUpdateID++;

            var hash = obj is Submarine ? ((Submarine)obj).MD5Hash.Hash : "";

            //hash will be null if opening the sub file failed -> don't select the sub
            if (string.IsNullOrWhiteSpace(hash))
            {
                GUITextBlock submarineTextBlock = component.GetChild<GUITextBlock>();
                if (submarineTextBlock != null)
                {
                    submarineTextBlock.TextColor = Color.DarkRed * 0.8f;
                    submarineTextBlock.CanBeFocused = false;
                }
                else
                {
                    DebugConsole.ThrowError("Failed to select submarine. Selected GUIComponent was of the type \"" + (component == null ? "null" : component.GetType().ToString()) + "\".");
                    GameAnalyticsManager.AddErrorEventOnce(
                        "NetLobbyScreen.SelectSub:InvalidComponent", 
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Failed to select submarine. Selected GUIComponent was of the type \"" + (component == null ? "null" : component.GetType().ToString()) + "\".");
                }


                StartButton.Enabled = false;

                return false;
            }

            StartButton.Enabled = true;

            return true;*/
        }

        public void UpdateSubList(GUIComponent subList, List<Submarine> submarines)
        {
            if (subList == null) return;

            subList.ClearChildren();
            
            /*if (submarines.Count == 0 && GameMain.Server != null)
            {
                DebugConsole.ThrowError("No submarines found!");
            }*/
             
            foreach (Submarine sub in submarines)
            {
                AddSubmarine(subList, sub);
            }
        }

        private void AddSubmarine(GUIComponent subList, Submarine sub)
        {
            if (subList is GUIListBox)
            {
                subList = ((GUIListBox)subList).Content;
            }
            else if (subList is GUIDropDown)
            {
                subList = ((GUIDropDown)subList).ListBox.Content;
            }

            var frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), subList.RectTransform) { MinSize = new Point(0, 20) },
                style: "ListBoxElement")
            {
                ToolTip = sub.Description,
                UserData = sub
            };

            int buttonSize = (int)(frame.Rect.Height * 0.8f);
            var subTextBlock = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(buttonSize + 5, 0) },
                ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 65), textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };

            var matchingSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == sub.Name && s.MD5Hash.Hash == sub.MD5Hash.Hash);
            if (matchingSub == null) matchingSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == sub.Name);

            if (matchingSub == null)
            {
                subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                frame.ToolTip = TextManager.Get("SubNotFound");
            }
            else if (matchingSub.MD5Hash.Hash != sub.MD5Hash.Hash)
            {
                subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                frame.ToolTip = TextManager.Get("SubDoesntMatch");
            }
            else
            {
                if (subList == shuttleList || subList == shuttleList.ListBox || subList == shuttleList.ListBox.Content)
                {
                    subTextBlock.TextColor = new Color(subTextBlock.TextColor, sub.HasTag(SubmarineTag.Shuttle) ? 1.0f : 0.6f);
                }

                GUIButton infoButton = new GUIButton(new RectTransform(new Point(buttonSize, buttonSize), frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point((int)(buttonSize * 0.2f), 0) }, "?")
                {
                    UserData = sub
                };
                infoButton.OnClicked += (component, userdata) =>
                {
                    ((Submarine)userdata).CreatePreviewWindow(new GUIMessageBox("", "", 550, 400));
                    return true;
                };
            }

            if (sub.HasTag(SubmarineTag.Shuttle))
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), frame.RectTransform, Anchor.CenterRight),
                    TextManager.Get("Shuttle"), textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                {
                    TextColor = subTextBlock.TextColor * 0.8f,
                    ToolTip = subTextBlock.ToolTip,
                    CanBeFocused = false
                };
                //make shuttles more dim in the sub list (selecting a shuttle as the main sub is allowed but not recommended)
                if (subList == this.subList.Content)
                {
                    subTextBlock.TextColor *= 0.5f;
                    foreach (GUIComponent child in frame.Children)
                    {
                        child.Color *= 0.5f;
                    }
                }
            }
        }

        public bool VotableClicked(GUIComponent component, object userData)
        {
            if (GameMain.Client == null) return false;
            
            VoteType voteType;
            if (component.Parent == GameMain.NetLobbyScreen.SubList.Content)
            {
                if (!GameMain.Client.ServerSettings.Voting.AllowSubVoting)
                {
                    if (GameMain.Client.HasPermission(ClientPermissions.SelectSub))
                    {
                        GameMain.Client.RequestSelectSub(component.Parent.GetChildIndex(component));
                        return true;
                    }
                    return false;
                }
                voteType = VoteType.Sub;
            }
            else if (component.Parent == GameMain.NetLobbyScreen.ModeList.Content)
            {
                if (!((GameModePreset)userData).Votable) return false;
                if (!GameMain.Client.ServerSettings.Voting.AllowModeVoting)
                {
                    if (GameMain.Client.HasPermission(ClientPermissions.SelectMode))
                    {
                        GameMain.Client.RequestSelectMode(component.Parent.GetChildIndex(component));
                        return true;
                    }
                    return false;
                }

                voteType = VoteType.Mode;
            }
            else
            {
                return false;
            }

            GameMain.Client.Vote(voteType, userData);

            return true;
        }

        public bool ChangeServerName(GUITextBox textBox, string text)
        {
            return false;
            /*if (GameMain.Server == null) return false;
            ServerName = text;
            lastUpdateID++;

            return true;*/
        }

        public bool UpdateServerMessage(GUITextBox textBox, string text)
        {
            return false;
            /*if (GameMain.Server == null) return false;

            lastUpdateID++;

            return true;*/
        }

        public void AddPlayer(string name)
        {
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), playerList.Content.RectTransform),
                name, textAlignment: Alignment.CenterLeft)
            {
                UserData = name
            };

            //if (GameMain.Server != null) lastUpdateID++;
        }

        public void RemovePlayer(string name)
        {
            GUIComponent child = playerList.Content.GetChildByUserData(name);
            if (child != null) playerList.RemoveChild(child);
            //if (GameMain.Server != null) lastUpdateID++;
        }

        private bool SelectPlayer(GUIComponent component, object obj)
        {
            var selectedClient = GameMain.NetworkMember.ConnectedClients.Find(c => c.Name == obj.ToString());
            if (selectedClient == null) return false;

            if (GameMain.Client != null)
            {
                if (selectedClient.ID == GameMain.Client.ID) return false;

                if (!GameMain.Client.HasPermission(ClientPermissions.Ban) &&
                    !GameMain.Client.HasPermission(ClientPermissions.Kick) &&
                    !GameMain.Client.ServerSettings.Voting.AllowVoteKick)
                {
                    return false;
                }
            }

            playerFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) ClosePlayerFrame(btn, userdata); return true; }
            };
        
            var playerFrameInner = new GUIFrame(new RectTransform(/*GameMain.Server != null ? new Point(450, 370) :*/new Point(450, 150), playerFrame.RectTransform, Anchor.Center));
            var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), playerFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), 
                text: obj.ToString(), font: GUI.LargeFont);

            /*if (GameMain.Server != null)
            {
                playerFrame.UserData = selectedClient;

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform),
                     selectedClient.Connection.RemoteEndPoint.Address.ToString());

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedPlayerFrame.RectTransform), 
                    TextManager.Get("Rank"));
                var rankDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform),
                    TextManager.Get("Rank"))
                {
                    UserData = selectedClient
                };
                foreach (PermissionPreset permissionPreset in PermissionPreset.List)
                {
                    rankDropDown.AddItem(permissionPreset.Name, permissionPreset, permissionPreset.Description);
                }
                rankDropDown.AddItem(TextManager.Get("CustomRank"), null);

                PermissionPreset currentPreset = PermissionPreset.List.Find(p =>
                    p.Permissions == selectedClient.Permissions &&
                    p.PermittedCommands.Count == selectedClient.PermittedConsoleCommands.Count && !p.PermittedCommands.Except(selectedClient.PermittedConsoleCommands).Any());
                rankDropDown.SelectItem(currentPreset);

                rankDropDown.OnSelected += (c, userdata) =>
                {
                    PermissionPreset selectedPreset = (PermissionPreset)userdata;
                    if (selectedPreset != null)
                    {
                        var client = playerFrame.UserData as Client;
                        client.SetPermissions(selectedPreset.Permissions, selectedPreset.PermittedCommands);
                        GameMain.Server.UpdateClientPermissions(client);

                        playerFrame = null;
                        SelectPlayer(null, client.Name);
                    }
                    return true;
                };

                var permissionLabels = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), paddedPlayerFrame.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), permissionLabels.RectTransform), TextManager.Get("Permissions"));
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), permissionLabels.RectTransform), TextManager.Get("PermittedConsoleCommands"));
                
                var permissionContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), paddedPlayerFrame.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var permissionsBox = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), permissionContainer.RectTransform))
                {
                    UserData = selectedClient
                };

                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    if (permission == ClientPermissions.None) continue;

                    var permissionTick = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), permissionsBox.Content.RectTransform),
                        TextManager.Get("ClientPermission." + permission), font: GUI.SmallFont)
                    {
                        UserData = permission,
                        Selected = selectedClient.HasPermission(permission),

                        OnSelected = (tickBox) =>
                        {
                            //reset rank to custom
                            rankDropDown.SelectItem(null);

                            var client = playerFrame.UserData as Client;
                            if (client == null) return false;

                            var thisPermission = (ClientPermissions)tickBox.UserData;

                            if (tickBox.Selected)
                                client.GivePermission(thisPermission);
                            else
                                client.RemovePermission(thisPermission);

                            GameMain.Server.UpdateClientPermissions(client);

                            return true;
                        }
                    };
                }

                var commandList = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), permissionContainer.RectTransform))
                {
                    UserData = selectedClient
                };
                foreach (DebugConsole.Command command in DebugConsole.Commands)
                {
                    var commandTickBox = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), commandList.Content.RectTransform),
                        command.names[0], font: GUI.SmallFont)
                    {
                        Selected = selectedClient.PermittedConsoleCommands.Contains(command),
                        ToolTip = command.help,
                        UserData = command
                    };
                    commandTickBox.OnSelected += (GUITickBox tickBox) =>
                    {
                        //reset rank to custom
                        rankDropDown.SelectItem(null);

                        Client client = playerFrame.UserData as Client;
                        DebugConsole.Command selectedCommand = tickBox.UserData as DebugConsole.Command;
                        if (client == null) return false;

                        if (!tickBox.Selected)
                        {
                            client.PermittedConsoleCommands.Remove(selectedCommand);
                        }
                        else if (!client.PermittedConsoleCommands.Contains(selectedCommand))
                        {
                            client.PermittedConsoleCommands.Add(selectedCommand);
                        }

                        GameMain.Server.UpdateClientPermissions(client);
                        return true;
                    };
                }
            } TODO: this is a lot*/

            var buttonAreaUpper = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), isHorizontal: true);
            var buttonAreaLower = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), isHorizontal: true);
            
            if (GameMain.Client.HasPermission(ClientPermissions.Ban))
            {
                var banButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaUpper.RectTransform),
                    TextManager.Get("Ban"))
                {
                    UserData = obj
                };
                banButton.OnClicked += BanPlayer;
                banButton.OnClicked += ClosePlayerFrame;

                var rangebanButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaUpper.RectTransform),
                    TextManager.Get("BanRange"))
                {
                    UserData = obj
                };
                rangebanButton.OnClicked += BanPlayerRange;
                rangebanButton.OnClicked += ClosePlayerFrame;
            }


            if (GameMain.Client != null && GameMain.Client.ServerSettings.Voting.AllowVoteKick && selectedClient != null)
            {
                var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform),
                    TextManager.Get("VoteToKick"))
                {
                    Enabled = !selectedClient.HasKickVoteFromID(GameMain.Client.ID),
                    OnClicked = GameMain.Client.VoteForKick,
                    UserData = selectedClient
                };
            }

            if (GameMain.Client.HasPermission(ClientPermissions.Kick))
            {
                var kickButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform),
                    TextManager.Get("Kick"))
                {
                    UserData = obj
                };
                kickButton.OnClicked = KickPlayer;
                kickButton.OnClicked += ClosePlayerFrame;
            }

            var closeButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform, Anchor.BottomRight),
                TextManager.Get("Close"))
            {
                IgnoreLayoutGroups = true,
                OnClicked = ClosePlayerFrame
            };

            return false;
        }

        private bool ClosePlayerFrame(GUIButton button, object userData)
        {
            playerFrame = null;

            return true;
        }

        public bool KickPlayer(GUIButton button, object userData)
        {
            if (userData == null || GameMain.NetworkMember == null) return false;
            GameMain.Client.CreateKickReasonPrompt(userData.ToString(), false);            
            return false;
        }

        public bool BanPlayer(GUIButton button, object userData)
        {
            if (userData == null || GameMain.NetworkMember == null) return false;
            GameMain.Client.CreateKickReasonPrompt(userData.ToString(), true);
            return false;
        }

        public bool BanPlayerRange(GUIButton button, object userData)
        {
            if (userData == null || GameMain.NetworkMember == null) return false;
            GameMain.Client.CreateKickReasonPrompt(userData.ToString(), true, true);
            return false;
        }

        public void ClearPlayers()
        {
            playerList.ClearChildren();

            //if (GameMain.Server != null) lastUpdateID++;
        }

        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
              
            menu.AddToGUIUpdateList();              
            playerFrame?.AddToGUIUpdateList();  
            campaignSetupUI?.AddToGUIUpdateList();
            jobInfoFrame?.AddToGUIUpdateList();
        }
        
        public List<Submarine> GetSubList()
        {
            List<Submarine> subs = new List<Submarine>();
            foreach (GUIComponent component in subList.Content.Children)
            {
                if (component.UserData is Submarine) subs.Add((Submarine)component.UserData);
            }

            return subs;
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
                        
            if (campaignSetupUI != null)
            {
                if (!campaignSetupUI.Visible) campaignSetupUI = null;                
            }

            if (autoRestartTimer != 0.0f && autoRestartBox.Selected)
            {
                autoRestartTimer = Math.Max(autoRestartTimer - (float)deltaTime, 0.0f);
            }
        }
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);

            GUI.DrawBackgroundSprite(spriteBatch, backgroundSprite);

            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        public void NewChatMessage(ChatMessage message)
        {
            float prevSize = chatBox.BarSize;

            while (chatBox.Content.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.Content.Children.First());
            }

            GUITextBlock msg = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), chatBox.Content.RectTransform),
                text: (message.Type == ChatMessageType.Private ? TextManager.Get("PrivateMessageTag") + " " : "") + message.TextWithSender,
                textColor: message.Color,
                color: ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f,
                wrap: true, font: GUI.SmallFont)
            {
                UserData = message,
                CanBeFocused = false,
            };
            //chatBox.AddChild(msg);

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;
        }

        private void UpdatePlayerHead(CharacterInfo characterInfo)
        {
            playerHeadSprite.Sprite = characterInfo.HeadSprite;
        }

        private bool ToggleHead(GUIButton button, object userData)
        {
            if (GameMain.Client.CharacterInfo == null) return true;

            int dir = (int)userData;
            GameMain.Client.CharacterInfo.HeadSpriteId += dir;
            GameMain.Config.CharacterHeadIndex = GameMain.Client.CharacterInfo.HeadSpriteId;
            UpdatePlayerHead(GameMain.Client.CharacterInfo);
            return true;
        }

        private bool SwitchGender(GUIButton button, object obj)
        {
            Gender gender = (Gender)obj;
            GameMain.Client.CharacterInfo.Gender = gender;
            GameMain.Config.CharacterGender = GameMain.Client.CharacterInfo.Gender;
            UpdatePlayerHead(GameMain.Client.CharacterInfo);
            return true;
        }

        public void SelectMode(int modeIndex)
        {
            if (modeIndex < 0 || modeIndex >= modeList.Content.CountChildren || modeList.SelectedIndex == modeIndex) return;

            if (((GameModePreset)modeList.Content.GetChild(modeIndex).UserData).Name == "Campaign")
            {
                /*if (GameMain.Server != null)
                {
                    campaignSetupUI = MultiPlayerCampaign.StartCampaignSetup();
                    return;
                }*/
            }
            else
            {
                ToggleCampaignMode(false);
            }

            modeList.Select(modeIndex, true);
            missionTypeContainer.Visible = SelectedMode != null && SelectedMode.Name == "Mission";
        }

        private bool SelectMode(GUIComponent component, object obj)
        {
            if (GameMain.NetworkMember == null || obj == modeList.SelectedData) return false;
            
            GameModePreset modePreset = obj as GameModePreset;
            if (modePreset == null) return false;

            missionTypeContainer.Visible = modePreset.Name == "Mission";

            if (modePreset.Name == "Campaign")
            {
                //campaign selected and the campaign view has not been set up yet
                // -> don't select the mode yet and start campaign setup
                /*if (GameMain.Server != null && !campaignContainer.Visible)
                {
                    campaignSetupUI = MultiPlayerCampaign.StartCampaignSetup();
                    return false;
                }*/
            }
            else
            {
                ToggleCampaignMode(false);
            }

            //lastUpdateID++;
            return true;
        }

        public void ToggleCampaignView(bool enabled)
        {
            campaignContainer.Visible = enabled;
            defaultModeContainer.Visible = !enabled;
        }

        public void ToggleCampaignMode(bool enabled)
        {
            ToggleCampaignView(enabled);

            if (!enabled)
            {
                campaignCharacterInfo = null;
                CampaignCharacterDiscarded = false;
                UpdatePlayerFrame(null);
            }

            subList.Enabled = !enabled && AllowSubSelection;
            shuttleList.Enabled = !enabled && AllowSubSelection;
            seedBox.Enabled = false;//!enabled && GameMain.Server != null;

            if (campaignViewButton != null) campaignViewButton.Visible = enabled;
            
            if (enabled)
            {
                if (campaignUI == null || campaignUI.Campaign != GameMain.GameSession.GameMode)
                {
                    campaignContainer.ClearChildren();

                    campaignUI = new CampaignUI(GameMain.GameSession.GameMode as CampaignMode, campaignContainer)
                    {
                        StartRound = null//TODO: shdkjshdf //() => { GameMain.Server.StartGame(); }
                    };

                    var backButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.1f), campaignContainer.RectTransform),
                        TextManager.Get("Back"));
                    backButton.OnClicked += (btn, obj) => { ToggleCampaignView(false); return true; };

                    var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 0.1f), campaignContainer.RectTransform) { RelativeOffset = new Vector2(0.3f, 0.0f) },
                        isHorizontal: true)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };


                    List<CampaignUI.Tab> tabTypes = new List<CampaignUI.Tab>() { CampaignUI.Tab.Map, CampaignUI.Tab.Store };
                    foreach (CampaignUI.Tab tab in tabTypes)
                    {
                        var tabButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform), tab.ToString());
                        tabButton.OnClicked += (btn, obj) =>
                        {
                            campaignUI.SelectTab(tab);
                            return true;
                        };
                    }

                    var moneyText = new GUITextBlock(new RectTransform(new Vector2(0.25f, 0.1f), campaignContainer.RectTransform, Anchor.BottomLeft),
                        TextManager.Get("Credit"))
                    {
                        TextGetter = campaignUI.GetMoney
                    };

                    var restartText = new GUITextBlock(new RectTransform(new Vector2(0.25f, 0.1f), campaignContainer.RectTransform, Anchor.BottomRight), "", font: GUI.SmallFont)
                    {
                        TextGetter = AutoRestartText
                    };
                }
                modeList.Select(2, true);
            }

            /*if (GameMain.Server != null)
            {
                lastUpdateID++;
            }*/
        }

        private bool SelectSeed(GUITextBox textBox, string seed)
        {
            return false;
            //if (GameMain.Server == null) return false;
            if (string.IsNullOrWhiteSpace(seed)) return false;
            LevelSeed = seed;
            return true;
        }
        
        private bool ViewJobInfo(GUIButton button, object obj)
        {
            JobPrefab jobPrefab = button.UserData as JobPrefab;
            if (jobPrefab == null) return false;

            jobInfoFrame = jobPrefab.CreateInfoFrame();
            GUIButton closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), jobInfoFrame.GetChild(2).GetChild(0).RectTransform, Anchor.BottomRight),
                TextManager.Get("Close"))
            {
                OnClicked = CloseJobInfo
            };
            jobInfoFrame.OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) CloseJobInfo(btn, userdata); return true; };
            
            return true;
        }

        private bool CloseJobInfo(GUIButton button, object obj)
        {
            jobInfoFrame = null;
            return true;
        }

        private bool ChangeJobPreference(GUIButton button, object obj)
        {
            GUIComponent jobText = button.Parent.Parent;

            int index = jobList.Content.GetChildIndex(jobText);
            int newIndex = index + (int)obj;
            if (newIndex < 0 || newIndex > jobList.Content.CountChildren - 1) return false;

            jobText.RectTransform.RepositionChildInHierarchy(newIndex);

            UpdateJobPreferences(jobList);

            return true;
        }

        private void UpdateJobPreferences(GUIListBox listBox)
        {
            listBox.Deselect();
            List<string> jobNamePreferences = new List<string>();

            for (int i = 0; i < listBox.Content.CountChildren; i++)
            {
                float a = (float)(i - 1) / 3.0f;
                a = Math.Min(a, 3);
                Color color = new Color(1.0f - a, (1.0f - a) * 0.6f, 0.0f, 0.3f);

                GUIComponent child = listBox.Content.GetChild(i);

                child.Color = color;
                child.HoverColor = color;
                child.SelectedColor = color;

                (child.GetChild<GUITextBlock>()).Text = (i + 1) + ". " + (child.UserData as JobPrefab).Name;

                jobNamePreferences.Add((child.UserData as JobPrefab).Identifier);
            }

            if (!GameMain.Config.JobPreferences.SequenceEqual(jobNamePreferences))
            {
                GameMain.Config.JobPreferences = jobNamePreferences;
                GameMain.Config.Save();
            }
        }

        public Pair<string, string> FailedSelectedSub;
        public Pair<string, string> FailedSelectedShuttle;

        public bool TrySelectSub(string subName, string md5Hash, GUIListBox subList)
        {
            if (GameMain.Client == null) return false;

            //already downloading the selected sub file
            if (GameMain.Client.FileReceiver.ActiveTransfers.Any(t => t.FileName == subName + ".sub"))
            {
                return false;
            }

            Submarine sub = Submarine.SavedSubmarines.FirstOrDefault(m => m.Name == subName && m.MD5Hash.Hash == md5Hash);
            if (sub == null) sub = Submarine.SavedSubmarines.FirstOrDefault(m => m.Name == subName);

            var matchingListSub = subList.Content.GetChildByUserData(sub);
            if (matchingListSub != null)
            {
                if (subList.Parent is GUIDropDown subDropDown)
                {
                    subDropDown.SelectItem(sub);
                }
                else
                {
                    subList.OnSelected -= VotableClicked;
                    subList.Select(subList.Content.GetChildIndex(matchingListSub), true);
                    subList.OnSelected += VotableClicked;
                }

                if (subList == SubList)
                    FailedSelectedSub = null;
                else
                    FailedSelectedShuttle = null;
            }

            if (sub == null || sub.MD5Hash.Hash != md5Hash)
            {
                if (subList == SubList)
                    FailedSelectedSub = new Pair<string, string>(subName, md5Hash);
                else
                    FailedSelectedShuttle = new Pair<string, string>(subName, md5Hash);

                string errorMsg = "";
                if (sub == null)
                {
                    errorMsg = TextManager.Get("SubNotFoundError").Replace("[subname]", subName) + " ";
                }
                else if (sub.MD5Hash.Hash == null)
                {
                    errorMsg = TextManager.Get("SubLoadError").Replace("[subname]", subName) + " ";
                    if (matchingListSub != null) matchingListSub.GetChild<GUITextBox>().TextColor = Color.Red;
                }
                else
                {
                    errorMsg = TextManager.Get("SubDoesntMatchError").Replace("[subname]", sub.Name).Replace("[myhash]", sub.MD5Hash.ShortHash).Replace("[serverhash]", Md5Hash.GetShortHash(md5Hash)) + " ";
                }

                errorMsg += TextManager.Get("DownloadSubQuestion");

                //already showing a message about the same sub
                if (GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "request" + subName))
                {
                    return false;
                }

                var requestFileBox = new GUIMessageBox(TextManager.Get("DownloadSubLabel"), errorMsg, new string[] { TextManager.Get("Yes"), TextManager.Get("No") }, 400, 300);
                requestFileBox.UserData = "request" + subName;
                requestFileBox.Buttons[0].UserData = new string[] { subName, md5Hash };
                requestFileBox.Buttons[0].OnClicked += requestFileBox.Close;
                requestFileBox.Buttons[0].OnClicked += (GUIButton button, object userdata) =>
                {
                    string[] fileInfo = (string[])userdata;
                    GameMain.Client?.RequestFile(FileTransferType.Submarine, fileInfo[0], fileInfo[1]);
                    return true;
                };
                requestFileBox.Buttons[1].OnClicked += requestFileBox.Close;

                return false;
            }

            return true;
        }

    }
}
