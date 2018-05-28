using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        private GUIFrame menu;
        private GUIFrame infoFrame;
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
        private GUIButton campaignViewButton, spectateButton, settingsButton;

        private GUITickBox playYourself;

        private GUIFrame playerInfoContainer;
        private GUIImage playerHeadSprite;
        private GUIFrame jobInfoFrame;
        private GUIFrame playerFrame;

        private GUITickBox autoRestartBox;

        private GUIDropDown shuttleList;
        private GUITickBox shuttleTickBox;

        private CampaignUI campaignUI;
        private GUIComponent campaignSetupUI;

        private Sprite backgroundSprite;

        private GUITextBox serverMessage;

        private float autoRestartTimer;

        //elements that can only be used by the host
        private List<GUIComponent> clientDisabledElements = new List<GUIComponent>();
        //elements that aren't shown client-side
        private List<GUIComponent> clientHiddenElements = new List<GUIComponent>();

        private bool AllowSubSelection
        {
            get
            {
                return GameMain.Server != null || GameMain.NetworkMember.Voting.AllowSubVoting ||
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

        public bool StartButtonEnabled
        {
            get { return StartButton.Enabled; }
            set { StartButton.Enabled = value; }
        }

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
                backgroundSprite = LocationType.Random(levelSeed).Background;
                seedBox.Text = levelSeed;
            }
        }

        public string AutoRestartText()
        {
            if (GameMain.Server != null)
            {
                if (!GameMain.Server.AutoRestart || GameMain.Server.ConnectedClients.Count == 0) return "";
                return TextManager.Get("RestartingIn") + " " + ToolBox.SecondsToReadableTime(Math.Max(GameMain.Server.AutoRestartTimer, 0));
            }

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

            var myPlayerFrame = new GUIFrame(new RectTransform(new Vector2(0.3f - panelSpacing, 0.6f), menu.RectTransform, Anchor.TopRight));
            playerInfoContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), myPlayerFrame.RectTransform, Anchor.Center), style: null);

            playYourself = new GUITickBox(new RectTransform(new Vector2(0.06f, 0.06f), myPlayerFrame.RectTransform) { RelativeOffset = new Vector2(0.05f,0.05f) },
                TextManager.Get("PlayYourself"))
            {
                OnSelected = TogglePlayYourself,
                UserData = "playyourself"
            };
            clientHiddenElements.Add(playYourself);

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

            var rightInfoColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.9f), defaultModeContainer.RectTransform, Anchor.TopRight))
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
                Enabled = GameMain.Server != null,
                OnTextChanged = ChangeServerName
            };
            clientDisabledElements.Add(serverName);

            serverMessage = new GUITextBox(new RectTransform(new Vector2(infoColumnContainer.RectTransform.RelativeSize.X, 0.15f), defaultModeContainer.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.07f) })
            {
                Wrap = true,
                OnTextChanged = UpdateServerMessage
            };
            clientDisabledElements.Add(serverMessage);
            
            settingsButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), topButtonContainer.RectTransform, Anchor.TopRight),
                TextManager.Get("ServerSettingsButton"));
            clientHiddenElements.Add(settingsButton);

            var showLogButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), topButtonContainer.RectTransform, Anchor.TopRight),
                TextManager.Get("ServerLog"))
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (GameMain.Server.ServerLog.LogFrame == null)
                    {
                        GameMain.Server.ServerLog.CreateLogFrame();
                    }
                    else
                    {
                        GameMain.Server.ServerLog.LogFrame = null;
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                    return true;
                }
            };
            clientHiddenElements.Add(showLogButton);

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
                    if (GameMain.Server != null) lastUpdateID++;
                    return true;
                }
            };
            shuttleList = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.05f), midInfoColumn.RectTransform));

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
                    mode.Name, style: "ListBoxElement", textAlignment: Alignment.CenterLeft)
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
                TextManager.Get("Random"), textAlignment: Alignment.Center)
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
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), rightInfoColumn.RectTransform))
            {
                OnTextChanged = SelectSeed
            };
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

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), rightInfoColumn.RectTransform), style: null); //spacing

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
                TextManager.Get("StartGameButton"), style: "GUIButtonLarge");
            clientHiddenElements.Add(StartButton);
            
            campaignViewButton = new GUIButton(new RectTransform(new Vector2(0.3f, 0.1f), defaultModeContainer.RectTransform, Anchor.BottomRight),
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
        }

        public override void Select()
        {
            if (GameMain.NetworkMember == null) return;
            Character.Controlled = null;
            GameMain.LightManager.LosEnabled = false;

            textBox.Select();
            textBox.OnEnterPressed = GameMain.NetworkMember.EnterChatMessage;
            textBox.OnTextChanged = GameMain.NetworkMember.TypingChatMessage;
            
            subList.Enabled = AllowSubSelection || GameMain.Server != null;
            shuttleList.Enabled = AllowSubSelection || GameMain.Server != null;

            modeList.Enabled = 
                GameMain.Server != null || GameMain.NetworkMember.Voting.AllowModeVoting || 
                (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectMode));
            
            ServerName = (GameMain.Server == null) ? ServerName : GameMain.Server.Name;
            
            //disable/hide elements the clients are not supposed to use/see
            clientDisabledElements.ForEach(c => c.Enabled = GameMain.Server != null);
            clientHiddenElements.ForEach(c => c.Visible = GameMain.Server != null);

            spectateButton.Visible = GameMain.Client != null && GameMain.Client.GameStarted;            
            if (GameMain.NetworkMember.CharacterInfo != null)
            {
                TogglePlayYourself(playYourself);
            }            

            if (IsServer && GameMain.Server != null)
            {
                List<Submarine> subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList();

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
                
                if (subList.Selected == null) subList.Select(Math.Max(0, prevSelectedSub));
                if (shuttleList.Selected == null)
                {
                    var shuttles = shuttleList.GetChildren().FindAll(c => c.UserData is Submarine && ((Submarine)c.UserData).HasTag(SubmarineTag.Shuttle));
                    if (prevSelectedShuttle == -1 && shuttles.Any())
                    {
                        shuttleList.SelectItem(shuttles[0].UserData);
                    }
                    else
                    {
                        shuttleList.Select(Math.Max(0, prevSelectedShuttle));
                    }
                }
                if (GameModePreset.list.Count > 0 && modeList.Selected == null) modeList.Select(0);
                GameMain.Server.Voting.ResetVotes(GameMain.Server.ConnectedClients);
            }
            else if (GameMain.Client != null)
            {
                GameMain.Client.Voting.ResetVotes(GameMain.Client.ConnectedClients);
                spectateButton.OnClicked = GameMain.Client.SpectateClicked;
            }

            GameMain.NetworkMember.EndVoteCount = 0;
            GameMain.NetworkMember.EndVoteMax = 1;

            base.Select();
        }

        public void RandomizeSettings()
        {
            if (GameMain.Server == null) return;

            if (GameMain.Server.RandomizeSeed) LevelSeed = ToolBox.RandomSeed(8);
            if (GameMain.Server.SubSelectionMode == SelectionMode.Random)
            {
                var nonShuttles = subList.Content.Children.FindAll(c => c.UserData is Submarine && !((Submarine)c.UserData).HasTag(SubmarineTag.Shuttle));
                subList.Select(nonShuttles[Rand.Range(0, nonShuttles.Count)].UserData);
            }
            if (GameMain.Server.ModeSelectionMode == SelectionMode.Random)
            {
                var allowedGameModes = GameModePreset.list.FindAll(m => !m.IsSinglePlayer && m.Name != "Campaign");
                modeList.Select(allowedGameModes[Rand.Range(0, allowedGameModes.Count)]);
            }
        }
        
        public void ShowSpectateButton()
        {
            if (GameMain.Client == null) return;
            spectateButton.Visible = true;
        }

        private void UpdatePlayerFrame(CharacterInfo characterInfo)
        {
            playerInfoContainer.ClearChildren();

            GUIComponent infoContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.85f), playerInfoContainer.RectTransform, Anchor.BottomCenter), childAnchor: Anchor.TopCenter);                
            GUIComponent headContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 0.2f), infoContainer.RectTransform, Anchor.TopCenter), isHorizontal: true)
            {
                Stretch = true
            };

            GUIButton toggleHead = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), headContainer.RectTransform), "", style: "GUIButtonHorizontalArrow")
            {
                UserData = -1,
                OnClicked = ToggleHead
            };
            toggleHead.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);
            playerHeadSprite = new GUIImage(new RectTransform(new Vector2(0.3f, 1.0f), headContainer.RectTransform), sprite: null, scaleToFit: true)
            {
                UserData = "playerhead"
            };
            toggleHead = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), headContainer.RectTransform), style: "GUIButtonHorizontalArrow")
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
            foreach (string jobName in GameMain.Config.JobNamePreferences)
            {
                JobPrefab job = JobPrefab.List.Find(x => x.Name == jobName);
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

            UpdatePlayerHead(characterInfo);
            
        }
        
        public bool TogglePlayYourself(GUITickBox tickBox)
        {
            if (tickBox.Selected)
            {
                GameMain.NetworkMember.CharacterInfo = new CharacterInfo(Character.HumanConfigFile, GameMain.NetworkMember.Name, Gender.None, null);
                UpdatePlayerFrame(GameMain.NetworkMember.CharacterInfo);
            }
            else
            {
                playerInfoContainer.ClearChildren();
                
                GameMain.NetworkMember.CharacterInfo = null;
                GameMain.NetworkMember.Character = null;

                new GUITextBlock(new RectTransform(Vector2.One, playerInfoContainer.RectTransform, Anchor.Center), 
                    TextManager.Get("PlayingAsSpectator"),
                    textAlignment: Alignment.Center);
            }
            return false;
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
            if (GameMain.Server == null) return false;

            GameMain.Server.AutoRestart = tickBox.Selected;

            lastUpdateID++;

            return true;
        }

        public void SetMissionType(int missionTypeIndex)
        {
            if (missionTypeIndex < 0 || missionTypeIndex >= MissionPrefab.MissionTypes.Count) return;

            ((GUITextBlock)missionTypeContainer.Children[2]).Text = MissionPrefab.MissionTypes[missionTypeIndex];
            missionTypeContainer.UserData = missionTypeIndex;
        }

        public bool ToggleMissionType(GUIButton button, object userData)
        {
            if (GameMain.Server == null) return false;

            int missionTypeIndex = (int)missionTypeContainer.UserData;
            missionTypeIndex += (int)userData;

            if (missionTypeIndex < 0) missionTypeIndex = MissionPrefab.MissionTypes.Count - 1;
            if (missionTypeIndex >= MissionPrefab.MissionTypes.Count) missionTypeIndex = 0;

            SetMissionType(missionTypeIndex);

            lastUpdateID++;

            return true;
        }
        
        public bool ToggleTraitorsEnabled(GUIButton button, object userData)
        {
            ToggleTraitorsEnabled((int)userData);
            return true;
        }

        public bool ChangeBotCount(GUIButton button, object userData)
        {
            if (GameMain.Server == null) return false;
            SetBotCount(GameMain.Server.BotCount + (int)userData);
            return true;
        }

        public bool ChangeBotSpawnMode(GUIButton button, object userData)
        {
            if (GameMain.Server == null) return false;
            SetBotSpawnMode(GameMain.Server.BotSpawnMode == BotSpawnMode.Fill ? BotSpawnMode.Normal : BotSpawnMode.Fill);
            return true;
        }

        private bool SelectSub(GUIComponent component, object obj)
        {
            if (GameMain.Server == null) return false;

            lastUpdateID++;

            var hash = obj is Submarine ? ((Submarine)obj).MD5Hash.Hash : "";

            //hash will be null if opening the sub file failed -> don't select the sub
            if (string.IsNullOrWhiteSpace(hash))
            {
                (component as GUITextBlock).TextColor = Color.DarkRed * 0.8f;
                component.CanBeFocused = false;

                StartButton.Enabled = false;

                return false;
            }

            StartButton.Enabled = true;

            return true;
        }

        public void UpdateSubList(GUIComponent subList, List<Submarine> submarines)
        {
            if (subList == null) return;
            
            if (submarines.Count == 0 && GameMain.Server != null)
            {
                DebugConsole.ThrowError("No submarines found!");
            }
             
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

            var matchingSub = Submarine.SavedSubmarines.Find(s => s.Name == sub.Name && s.MD5Hash.Hash == sub.MD5Hash.Hash);
            if (matchingSub == null) matchingSub = Submarine.SavedSubmarines.Find(s => s.Name == sub.Name);

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
            if (component.Parent == GameMain.NetLobbyScreen.SubList)
            {
                if (!GameMain.Client.Voting.AllowSubVoting)
                {
                    if (GameMain.Client.HasPermission(ClientPermissions.SelectSub))
                    {
                        GameMain.Client.RequestSelectSub(component.Parent.Children.IndexOf(component));
                        return true;
                    }
                    return false;
                }
                voteType = VoteType.Sub;
            }
            else if (component.Parent == GameMain.NetLobbyScreen.ModeList)
            {
                if (!((GameModePreset)userData).Votable) return false;
                if (!GameMain.Client.Voting.AllowModeVoting)
                {
                    if (GameMain.Client.HasPermission(ClientPermissions.SelectMode))
                    {
                        GameMain.Client.RequestSelectMode(component.Parent.Children.IndexOf(component));
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
            if (GameMain.Server == null) return false;
            ServerName = text;
            lastUpdateID++;

            return true;
        }

        public bool UpdateServerMessage(GUITextBox textBox, string text)
        {
            if (GameMain.Server == null) return false;

            lastUpdateID++;

            return true;
        }

        public void AddPlayer(string name)
        {
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), playerList.Content.RectTransform),
                name, textAlignment: Alignment.CenterLeft)
            {
                UserData = name
            };

            if (GameMain.Server != null) lastUpdateID++;
        }

        public void RemovePlayer(string name)
        {
            GUIComponent child = playerList.Content.Children.Find(c => c.UserData as string == name);
            if (child != null) playerList.RemoveChild(child);
            if (GameMain.Server != null) lastUpdateID++;
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
                    !GameMain.Client.Voting.AllowVoteKick)
                {
                    return false;
                }
            }

            playerFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null, color: Color.Black * 0.6f);
            var playerFrameInner = new GUIFrame(new RectTransform(GameMain.Server != null ? new Point(450, 370) : new Point(450, 150), playerFrame.RectTransform, Anchor.Center));
            var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), playerFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), 
                text: obj.ToString(), font: GUI.LargeFont);

            if (GameMain.Server != null)
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

                    FieldInfo fi = typeof(ClientPermissions).GetField(permission.ToString());
                    DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                    string permissionStr = attributes.Length > 0 ? attributes[0].Description : permission.ToString();

                    var permissionTick = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), permissionsBox.Content.RectTransform),
                        permissionStr, font: GUI.SmallFont)
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
            }

            var buttonAreaUpper = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), isHorizontal: true);
            var buttonAreaLower = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), isHorizontal: true);
            
            if (GameMain.Server != null || GameMain.Client.HasPermission(ClientPermissions.Ban))
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


            if (GameMain.Client != null && GameMain.Client.Voting.AllowVoteKick && selectedClient != null)
            {
                var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform),
                    TextManager.Get("VoteToKick"))
                {
                    Enabled = !selectedClient.HasKickVoteFromID(GameMain.Client.ID),
                    OnClicked = GameMain.Client.VoteForKick,
                    UserData = selectedClient
                };
            }

            if (GameMain.Server != null || GameMain.Client.HasPermission(ClientPermissions.Kick))
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
            GameMain.NetworkMember.CreateKickReasonPrompt(userData.ToString(), false);            
            return false;
        }

        public bool BanPlayer(GUIButton button, object userData)
        {
            if (userData == null || GameMain.NetworkMember == null) return false;
            GameMain.NetworkMember.CreateKickReasonPrompt(userData.ToString(), true);
            return false;
        }

        public bool BanPlayerRange(GUIButton button, object userData)
        {
            if (userData == null || GameMain.NetworkMember == null) return false;
            GameMain.NetworkMember.CreateKickReasonPrompt(userData.ToString(), true, true);
            return false;
        }

        public void ClearPlayers()
        {
            playerList.ClearChildren();

            if (GameMain.Server != null) lastUpdateID++;
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
            
            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            if (backgroundSprite != null)
            {
                float scale = Math.Max((float)GameMain.GraphicsWidth / backgroundSprite.SourceRect.Width, (float)GameMain.GraphicsHeight / backgroundSprite.SourceRect.Height) * 1.2f;
                
                float paddingX = backgroundSprite.SourceRect.Width * scale - GameMain.GraphicsWidth;
                float paddingY = backgroundSprite.SourceRect.Height * scale - GameMain.GraphicsHeight;

                //TODO: blur the background

                double noiseT = (Timing.TotalTime * 0.02f);
                Vector2 pos = new Vector2((float)PerlinNoise.Perlin(noiseT, noiseT, 0) - 0.5f, (float)PerlinNoise.Perlin(noiseT, noiseT, 0.5f) - 0.5f);
                pos = new Vector2(pos.X * paddingX, pos.Y * paddingY);

                spriteBatch.Draw(backgroundSprite.Texture, 
                    new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2 + pos, 
                    null, Color.White, 0.0f, backgroundSprite.size / 2,
                    scale, SpriteEffects.None, 0.0f);
            }
            
            GUI.Draw((float)deltaTime, spriteBatch);

            spriteBatch.End();
        }

        public void NewChatMessage(ChatMessage message)
        {
            float prevSize = chatBox.BarSize;

            while (chatBox.Content.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.Content.Children[1]);
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
            int dir = (int)userData;
            if (GameMain.NetworkMember.CharacterInfo == null) return true;
            GameMain.NetworkMember.CharacterInfo.HeadSpriteId += dir;
            UpdatePlayerHead(GameMain.NetworkMember.CharacterInfo);
            return true;
        }

        private bool SwitchGender(GUIButton button, object obj)
        {
            Gender gender = (Gender)obj;
            GameMain.NetworkMember.CharacterInfo.Gender = gender;

            UpdatePlayerHead(GameMain.NetworkMember.CharacterInfo);
            return true;
        }

        public void SelectMode(int modeIndex)
        {
            if (modeIndex < 0 || modeIndex >= modeList.Content.Children.Count || modeList.SelectedIndex == modeIndex) return;

            if (((GameModePreset)modeList.Content.Children[modeIndex].UserData).Name == "Campaign")
            {
                if (GameMain.Server != null)
                {
                    campaignSetupUI = MultiPlayerCampaign.StartCampaignSetup();
                    return;
                }
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
                if (GameMain.Server != null && !campaignContainer.Visible)
                {
                    campaignSetupUI = MultiPlayerCampaign.StartCampaignSetup();
                    return false;
                }
            }
            else
            {
                ToggleCampaignMode(false);
            }

            lastUpdateID++;
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

            subList.Enabled = !enabled && AllowSubSelection;
            shuttleList.Enabled = !enabled && AllowSubSelection;
            seedBox.Enabled = !enabled && GameMain.Server != null;

            if (campaignViewButton != null) campaignViewButton.Visible = enabled;
            if (StartButton != null) StartButton.Visible = !enabled;            

            if (enabled)
            {
                if (campaignUI == null || campaignUI.Campaign != GameMain.GameSession.GameMode)
                {
                    campaignContainer.ClearChildren();

                    campaignUI = new CampaignUI(GameMain.GameSession.GameMode as CampaignMode, campaignContainer)
                    {
                        StartRound = () => { GameMain.Server.StartGame(); }
                    };

                    var backButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.1f), campaignContainer.RectTransform),
                        TextManager.Get("Back"))
                    {
                        ClampMouseRectToParent = false
                    };
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
                        var tabButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), buttonContainer.RectTransform), tab.ToString())
                        {
                            ClampMouseRectToParent = false
                        };
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

            if (GameMain.Server != null)
            {
                lastUpdateID++;
            }
        }

        private bool SelectSeed(GUITextBox textBox, string seed)
        {
            if (GameMain.Server == null) return false;
            if (string.IsNullOrWhiteSpace(seed)) return false;

            LevelSeed = seed;
            lastUpdateID++;

            return true;
        }
        
        private bool ViewJobInfo(GUIButton button, object obj)
        {
            JobPrefab jobPrefab = button.UserData as JobPrefab;
            if (jobPrefab == null) return false;

            jobInfoFrame = jobPrefab.CreateInfoFrame();
            GUIButton closeButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.05f), jobInfoFrame.Children[0].Children[0].RectTransform, Anchor.BottomRight),
                TextManager.Get("Close"))
            {
                OnClicked = CloseJobInfo
            };
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

            int index = jobList.Content.Children.IndexOf(jobText);
            int newIndex = index + (int)obj;
            if (newIndex < 0 || newIndex > jobList.Content.Children.Count - 1) return false;

            jobText.RectTransform.RepositionChildInHierarchy(newIndex);

            UpdateJobPreferences(jobList);

            return true;
        }

        private void UpdateJobPreferences(GUIListBox listBox)
        {
            listBox.Deselect();
            List<string> jobNamePreferences = new List<string>();

            for (int i = 0; i < listBox.Content.Children.Count; i++)
            {
                float a = (float)(i - 1) / 3.0f;
                a = Math.Min(a, 3);
                Color color = new Color(1.0f - a, (1.0f - a) * 0.6f, 0.0f, 0.3f);

                listBox.Content.Children[i].Color = color;
                listBox.Content.Children[i].HoverColor = color;
                listBox.Content.Children[i].SelectedColor = color;

                (listBox.Content.Children[i].GetChild<GUITextBlock>()).Text = (i + 1) + ". " + (listBox.Content.Children[i].UserData as JobPrefab).Name;

                jobNamePreferences.Add((listBox.Content.Children[i].UserData as JobPrefab).Name);
            }

            if (!GameMain.Config.JobNamePreferences.SequenceEqual(jobNamePreferences))
            {
                GameMain.Config.JobNamePreferences = jobNamePreferences;
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

            Submarine sub = Submarine.SavedSubmarines.Find(m => m.Name == subName && m.MD5Hash.Hash == md5Hash);
            if (sub == null) sub = Submarine.SavedSubmarines.Find(m => m.Name == subName);

            var matchingListSub = subList.Content.Children.Find(c => c.UserData == sub);
            if (matchingListSub != null)
            {
                subList.OnSelected -= VotableClicked;
                subList.Select(subList.Content.Children.IndexOf(matchingListSub), true);
                subList.OnSelected += VotableClicked;

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
                    GameMain.Client.RequestFile(FileTransferType.Submarine, fileInfo[0], fileInfo[1]);
                    return true;
                };
                requestFileBox.Buttons[1].OnClicked += requestFileBox.Close;

                return false;
            }

            return true;
        }

    }
}
