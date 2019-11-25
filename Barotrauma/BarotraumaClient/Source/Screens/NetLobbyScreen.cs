using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        private readonly List<Sprite> characterSprites = new List<Sprite>();
        private readonly List<Sprite> jobPreferenceSprites = new List<Sprite>();

        private GUIFrame infoFrame, modeFrame;
        private GUIFrame myCharacterFrame;

        private GUIListBox subList, modeList;

        private GUIListBox chatBox, playerList;
        private GUIListBox serverLogBox, serverLogFilterTicks;

        private GUITextBox chatInput;
        private GUITextBox serverLogFilter;
        public GUITextBox ChatInput
        {
            get
            {
                return chatInput;
            }
        }

        private readonly GUIImage micIcon;

        private readonly GUIScrollBar levelDifficultyScrollBar;

        private readonly GUIButton[] traitorProbabilityButtons;
        private readonly GUITextBlock traitorProbabilityText;

        private readonly GUIButton[] botCountButtons;
        private readonly GUITextBlock botCountText;

        private readonly GUIButton[] botSpawnModeButtons;
        private readonly GUITextBlock botSpawnModeText;

        public readonly GUIFrame MissionTypeFrame;
        public readonly GUIFrame CampaignSetupFrame;

        private readonly GUITickBox[] missionTypeTickBoxes;
        private readonly GUIListBox missionTypeList;

        
        public GUITextBox SeedBox
        {
            get; private set;
        }

        private readonly GUIComponent gameModeContainer, campaignContainer;
        private readonly GUIButton gameModeViewButton, campaignViewButton, spectateButton;
        private readonly GUILayoutGroup roundControlsHolder;
        public GUIButton SettingsButton { get; private set; }

        private readonly GUITickBox spectateBox;
        
        private readonly GUIFrame playerInfoContainer;
        private GUIButton jobInfoFrame;
        private GUIButton playerFrame;

        private readonly GUIComponent subPreviewContainer;

        private readonly GUITickBox autoRestartBox;
        private readonly GUITextBlock autoRestartText;
                
        private GUIDropDown shuttleList;
        private GUITickBox shuttleTickBox;

        private CampaignUI campaignUI;

        private Sprite backgroundSprite;

        private GUIButton jobPreferencesButton;
        private GUIButton appearanceButton;

        private GUIFrame characterInfoFrame;
        private GUIFrame appearanceFrame;

        public GUIListBox HeadSelectionList;
        public GUIFrame JobSelectionFrame;

        public GUIListBox JobList;
        
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
        private readonly List<GUIComponent> clientDisabledElements = new List<GUIComponent>();
        //elements that aren't shown client-side
        private readonly List<GUIComponent> clientHiddenElements = new List<GUIComponent>();

        public GUIComponent FileTransferFrame { get; private set; }
        public GUITextBlock FileTransferTitle { get; private set; }
        public GUIProgressBar FileTransferProgressBar { get; private set; }
        public GUITextBlock FileTransferProgressText { get; private set; }


        private bool AllowSubSelection
        {
            get
            {
                return GameMain.NetworkMember.ServerSettings.Voting.AllowSubVoting ||
                    (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectSub));
            }
        }
        
        public GUITextBox ServerName
        {
            get;
            private set;
        }
        
        public GUITextBox ServerMessage
        {
            get;
            private set;
        }

        public GUILayoutGroup LogButtons
        {
            get;
            private set;
        }

        private GUIButton showChatButton;
        private GUIButton showLogButton;

        public GUIListBox SubList
        {
            get { return subList; }
        }

        public GUIDropDown ShuttleList
        {
            get { return shuttleList; }
        }

        public GUIListBox ModeList
        {
            get { return modeList; }
        }

        private int selectedModeIndex;
        public int SelectedModeIndex
        {
            get { return selectedModeIndex; }
            set
            {
                if (HighlightedModeIndex == selectedModeIndex)
                {
                    modeList.Select(value);
                }
                selectedModeIndex = value;
            }
        }

        public int HighlightedModeIndex
        {
            get { return modeList.SelectedIndex; }
            set
            {
                modeList.Select(value, true);
            }
        }

        public GUIListBox PlayerList
        {
            get { return playerList; }
        }

        public GUITextBox CharacterNameBox
        {
            get;
            private set;
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
            set { shuttleTickBox.Selected = value; }
        }

        public GameModePreset SelectedMode
        {
            get { return modeList.SelectedData as GameModePreset; }
        }

        public MissionType MissionType
        {
            get
            {
                MissionType retVal = MissionType.None;
                int index = 0;
                foreach (MissionType type in Enum.GetValues(typeof(MissionType)))
                {
                    if (type == MissionType.None || type == MissionType.All) { continue; }

                    if (missionTypeTickBoxes[index].Selected)
                    {
                        retVal = (MissionType)((int)retVal | (int)type);
                    }

                    index++;
                }

                return retVal;
            }
            set
            {
                int index = 0;
                foreach (MissionType type in Enum.GetValues(typeof(MissionType)))
                {
                    if (type == MissionType.None || type == MissionType.All) { continue; }

                    missionTypeTickBoxes[index].Selected = (((int)type & (int)value) != 0);

                    index++;
                }
            }
        }

        public List<Pair<JobPrefab, int>> JobPreferences
        {
            get
            {
                //joblist if the server has already assigned the player a job 
                //(e.g. the player has a pre-existing campaign character)
                if (JobList?.Content == null)
                {
                    return new List<Pair<JobPrefab, int>>();
                }

                List<Pair<JobPrefab, int>> jobPreferences = new List<Pair<JobPrefab, int>>();
                foreach (GUIComponent child in JobList.Content.Children)
                {
                    var jobPrefab = child.UserData as Pair<JobPrefab, int>;
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

                int intSeed = ToolBox.StringToInt(levelSeed);
                backgroundSprite = LocationType.Random(new MTRandom(intSeed))?.GetPortrait(intSeed);
                SeedBox.Text = levelSeed;
            }
        }


        public CampaignUI CampaignUI
        {
            get { return campaignUI; }
        }

        public NetLobbyScreen()
        {
            float panelSpacing = 0.005f;
            var innerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), Frame.RectTransform, Anchor.Center) { MaxSize = new Point(int.MaxValue, GameMain.GraphicsHeight - 50) }, isHorizontal: false)
            {
                Stretch = true,
                RelativeSpacing = panelSpacing
            };

            var panelContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), innerFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = panelSpacing
            };
            
            GUILayoutGroup panelHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.7f, 1.0f), panelContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = panelSpacing
            };

            GUILayoutGroup bottomBar = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), innerFrame.RectTransform))
            {
                Stretch = true,
                IsHorizontal = true,
                RelativeSpacing = panelSpacing
            };
            GUILayoutGroup bottomBarLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), bottomBar.RectTransform))
            {
                Stretch = true,
                IsHorizontal = true,
                RelativeSpacing = panelSpacing
            };
            GUILayoutGroup bottomBarMid = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), bottomBar.RectTransform))
            {
                Stretch = true,
                IsHorizontal = true,
                RelativeSpacing = panelSpacing
            };
            GUILayoutGroup bottomBarRight = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), bottomBar.RectTransform))
            {
                Stretch = true,
                IsHorizontal = true,
                RelativeSpacing = panelSpacing
            };

            //server info panel ------------------------------------------------------------

            infoFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), panelHolder.RectTransform));
            var infoFrameContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), infoFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.025f
            };

            //gamemode tab buttons ------------------------------------------------------------

            var gameModeTabButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.03f), panelHolder.RectTransform), isHorizontal: true)
            {
                RelativeSpacing = 0.01f
            };
            gameModeViewButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.4f), gameModeTabButtonContainer.RectTransform),
                TextManager.Get("GameMode"), style: "GUITabButton")
            {
                Selected = true,
                OnClicked = (bt, userData) => { ToggleCampaignView(false); return true; }
            };
            campaignViewButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.4f), gameModeTabButtonContainer.RectTransform),
                TextManager.Get("CampaignLabel"), style: "GUITabButton")
            {
                Visible = false,
                OnClicked = (bt, userData) => { ToggleCampaignView(true); return true; }
            };

            //server game panel ------------------------------------------------------------

            modeFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), panelHolder.RectTransform))
            {
                CanBeFocused = false
            };

            gameModeContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), modeFrame.RectTransform, Anchor.Center))
            {
                RelativeSpacing = panelSpacing * 2.0f,
                Stretch = true
            };

            campaignContainer = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), modeFrame.RectTransform, Anchor.Center), style: null)
            {
                Visible = false
            };

            new GUIButton(new RectTransform(new Vector2(0.5f, 1.0f), bottomBarLeft.RectTransform), TextManager.Get("disconnect"), style: "GUIButtonLarge")
            {
                OnClicked = (bt, userdata) => { GameMain.QuitToMainMenu(save: false, showVerificationPrompt: true); return true; }
            };

            // file transfers  ------------------------------------------------------------
            FileTransferFrame = new GUIFrame(new RectTransform(Vector2.One, bottomBarLeft.RectTransform), style: "TextFrame");
            var fileTransferContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), FileTransferFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            FileTransferTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), fileTransferContent.RectTransform), "", font: GUI.SmallFont);
            var fileTransferBottom = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), fileTransferContent.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };
            FileTransferProgressBar = new GUIProgressBar(new RectTransform(new Vector2(0.6f, 1.0f), fileTransferBottom.RectTransform), 0.0f, Color.DarkGreen);
            FileTransferProgressText = new GUITextBlock(new RectTransform(Vector2.One, FileTransferProgressBar.RectTransform), "", 
                font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
            new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), fileTransferBottom.RectTransform), TextManager.Get("cancel"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (!(userdata is FileReceiver.FileTransferIn transfer)) { return false; }
                    GameMain.Client?.CancelFileTransfer(transfer);
                    GameMain.Client.FileReceiver.StopTransfer(transfer);
                    return true;
                }
            };

            // Sidebar area (Character customization/Chat)

            GUILayoutGroup sideBar = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1.0f), panelContainer.RectTransform, maxSize: new Point(650, panelContainer.RectTransform.Rect.Height)))
            {
                Stretch = true
            };

            //player info panel ------------------------------------------------------------

            myCharacterFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), sideBar.RectTransform));
            playerInfoContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), myCharacterFrame.RectTransform, Anchor.Center), style: null);

            spectateBox = new GUITickBox(new RectTransform(new Vector2(0.06f, 0.06f), myCharacterFrame.RectTransform) { RelativeOffset = new Vector2(0.05f,0.05f) },
                TextManager.Get("spectatebutton"))
            {
                Selected = false,
                OnSelected = ToggleSpectate,
                UserData = "spectate"
            };

            // Social area

            GUIFrame logBackground = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.5f), sideBar.RectTransform));
            GUILayoutGroup logHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), logBackground.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            GUILayoutGroup socialHolder = null; GUILayoutGroup serverLogHolder = null;

            LogButtons = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), logHolder.RectTransform), true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            clientHiddenElements.Add(LogButtons);

            // Show chat button
            showChatButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.25f), LogButtons.RectTransform),
               TextManager.Get("Chat"), style: "GUITabButton")
            {
                Selected = true,
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (socialHolder != null) { socialHolder.Visible = true; }
                    if (serverLogHolder != null) { serverLogHolder.Visible = false; }
                    showChatButton.Selected = true;
                    showLogButton.Selected = false;
                    return true;
                }
            };

            // Server log button
            showLogButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.25f), LogButtons.RectTransform),
                TextManager.Get("ServerLog"), style: "GUITabButton")
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (socialHolder != null) { socialHolder.Visible = false; }
                    if (!(serverLogHolder?.Visible ?? true))
                    {
                        serverLogHolder.Visible = true;
                        GameMain.Client.ServerSettings.ServerLog.AssignLogFrame(serverLogBox, serverLogFilterTicks.Content, serverLogFilter);
                    }
                    showChatButton.Selected = false;
                    showLogButton.Selected = true;
                    return true;
                }
            };

            GUITextBlock.AutoScaleAndNormalize(showChatButton.TextBlock, showLogButton.TextBlock);

            GUIFrame logHolderBottom = new GUIFrame(new RectTransform(Vector2.One, logHolder.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            socialHolder = new GUILayoutGroup(new RectTransform(Vector2.One, logHolderBottom.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), socialHolder.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            GUILayoutGroup socialHolderHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), socialHolder.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            //chatbox ----------------------------------------------------------------------

            chatBox = new GUIListBox(new RectTransform(new Vector2(0.6f, 1.0f), socialHolderHorizontal.RectTransform));

            //player list ------------------------------------------------------------------

            playerList = new GUIListBox(new RectTransform(new Vector2(0.4f, 1.0f), socialHolderHorizontal.RectTransform))
            {
                OnSelected = (component, userdata) => { SelectPlayer(userdata as Client); return true; }
            };

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), socialHolder.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            // Chat input

            var chatRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), socialHolder.RectTransform), true)
            {
                Stretch = true
            };

            chatInput = new GUITextBox(new RectTransform(new Vector2(0.95f, 1.0f), chatRow.RectTransform))
            {
                MaxTextLength = ChatMessage.MaxLength,
                Font = GUI.SmallFont,
                DeselectAfterMessage = false
            };

            micIcon = new GUIImage(new RectTransform(new Vector2(0.05f, 1.0f), chatRow.RectTransform), style: "GUIMicrophoneUnavailable");

            serverLogHolder = new GUILayoutGroup(new RectTransform(Vector2.One, logHolderBottom.RectTransform, Anchor.Center))
            {
                Stretch = true,
                Visible = false
            };

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), serverLogHolder.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            GUILayoutGroup serverLogHolderHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), serverLogHolder.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            //server log ----------------------------------------------------------------------

            serverLogBox = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), serverLogHolderHorizontal.RectTransform));

            //filter tickbox list ------------------------------------------------------------------

            serverLogFilterTicks = new GUIListBox(new RectTransform(new Vector2(0.5f, 1.0f), serverLogHolderHorizontal.RectTransform) { MinSize = new Point(150, 0) })
            {
                OnSelected = (component, userdata) => { return false; }
            };

            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), serverLogHolder.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            // Filter text input

            serverLogFilter = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.07f), serverLogHolder.RectTransform))
            {
                MaxTextLength = ChatMessage.MaxLength,
                Font = GUI.SmallFont
            };

            roundControlsHolder = new GUILayoutGroup(new RectTransform(Vector2.One, bottomBarRight.RectTransform), 
                isHorizontal: true)
            {
                Stretch = true
            };

            GUIFrame readyToStartContainer = new GUIFrame(new RectTransform(Vector2.One, roundControlsHolder.RectTransform), style: "TextFrame")
            {
                Visible = false
            };

            // Ready to start tickbox
            ReadyToStartBox = new GUITickBox(new RectTransform(new Vector2(0.95f, 0.75f), readyToStartContainer.RectTransform, anchor: Anchor.Center),
                TextManager.Get("ReadyToStartTickBox"));

            // Spectate button
            spectateButton = new GUIButton(new RectTransform(Vector2.One, roundControlsHolder.RectTransform),
                TextManager.Get("SpectateButton"), style: "GUIButtonLarge");

            // Start button
            StartButton = new GUIButton(new RectTransform(Vector2.One, roundControlsHolder.RectTransform),
                TextManager.Get("StartGameButton"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, obj) =>
                {
                    GameMain.Client.RequestStartRound();
                    CoroutineManager.StartCoroutine(WaitForStartRound(StartButton, allowCancel: true), "WaitForStartRound");
                    return true;
                }
            };
            clientHiddenElements.Add(StartButton);

            //autorestart ------------------------------------------------------------------

            autoRestartText = new GUITextBlock(new RectTransform(Vector2.One, bottomBarMid.RectTransform), "", font: GUI.SmallFont, style: "TextFrame", textAlignment: Alignment.Center);
            GUIFrame autoRestartBoxContainer = new GUIFrame(new RectTransform(Vector2.One, bottomBarMid.RectTransform), style: "TextFrame");
            autoRestartBox = new GUITickBox(new RectTransform(new Vector2(0.95f, 0.75f), autoRestartBoxContainer.RectTransform, Anchor.Center), TextManager.Get("AutoRestart"))
            {
                OnSelected = (tickBox) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, autoRestart: tickBox.Selected);
                    return true;
                }
            };
            clientDisabledElements.Add(autoRestartBoxContainer);            

            //--------------------------------------------------------------------------------------------------------------------------------
            //infoframe contents
            //--------------------------------------------------------------------------------------------------------------------------------

            //server info ------------------------------------------------------------------

            // Server Info Header
            GUILayoutGroup lobbyHeader = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), infoFrameContent.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            ServerName = new GUITextBox(new RectTransform(Vector2.One, lobbyHeader.RectTransform))
            {
                MaxTextLength = NetConfig.ServerNameMaxLength,
                OverflowClip = true
            };
            ServerName.OnDeselected += (textBox, key) =>
            {
                GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Name);
            };
            clientDisabledElements.Add(ServerName);

            SettingsButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), lobbyHeader.RectTransform, Anchor.TopRight),
                TextManager.Get("ServerSettingsButton"), style: "GUIButtonLarge");
            clientHiddenElements.Add(SettingsButton);

            GUILayoutGroup lobbyContent = new GUILayoutGroup(new RectTransform(Vector2.One, infoFrameContent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.025f
            };

            GUILayoutGroup serverInfoHolder = new GUILayoutGroup(new RectTransform(Vector2.One, lobbyContent.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.025f
            };

            var serverBanner = new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.25f), serverInfoHolder.RectTransform), DrawServerBanner)
            {
                HideElementsOutsideFrame = true
            };
            new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.05f), serverBanner.RectTransform) { RelativeOffset = new Vector2(0.01f, 0.04f) },
                "", font: GUI.SmallFont, textAlignment: Alignment.Center, textColor: Color.White, style: "GUISlopedHeader")
            {
                CanBeFocused = false
            };

            var serverMessageContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.75f), serverInfoHolder.RectTransform));
            ServerMessage = new GUITextBox(new RectTransform(Vector2.One, serverMessageContainer.Content.RectTransform))
            {
                Wrap = true
            };
            ServerMessage.OnTextChanged += (textBox, text) =>
            {
                Vector2 textSize = textBox.Font.MeasureString(textBox.WrappedText);
                textBox.RectTransform.NonScaledSize = new Point(textBox.RectTransform.NonScaledSize.X, Math.Max(serverMessageContainer.Rect.Height, (int)textSize.Y + 10));
                serverMessageContainer.UpdateScrollBarSize();
                serverMessageContainer.BarScroll = 1.0f;
                return true;
            };
            ServerMessage.OnDeselected += (textBox, key) =>
            {
                GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Message);
            };
            clientDisabledElements.Add(ServerMessage);

            //submarine list ------------------------------------------------------------------

            GUILayoutGroup subHolder = new GUILayoutGroup(new RectTransform(Vector2.One, lobbyContent.RectTransform))
            {
                RelativeSpacing = panelSpacing,
                Stretch = true
            };

            var subLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.055f), subHolder.RectTransform) { MinSize = new Point(0, 25) }, TextManager.Get("Submarine"));
            subList = new GUIListBox(new RectTransform(Vector2.One, subHolder.RectTransform))
            {
                OnSelected = VotableClicked
            };

            var voteText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), subLabel.RectTransform, Anchor.TopRight),
                TextManager.Get("Votes"), textAlignment: Alignment.CenterRight)
            {
                UserData = "subvotes",
                Visible = false
            };

            //respawn shuttle / submarine preview ------------------------------------------------------------------

            GUILayoutGroup rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), lobbyContent.RectTransform))
            {
                RelativeSpacing = panelSpacing,
                Stretch = true
            };

            GUILayoutGroup shuttleHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), rightColumn.RectTransform) { MinSize = new Point(0, 25) }, isHorizontal: true)
            {
                Stretch = true
            };

            shuttleTickBox = new GUITickBox(new RectTransform(Vector2.One, shuttleHolder.RectTransform), TextManager.Get("RespawnShuttle"))
            {
                Selected = true,                
                OnSelected = (GUITickBox box) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, useRespawnShuttle: box.Selected);
                    return true;
                }
            };
            shuttleTickBox.TextBlock.RectTransform.SizeChanged += () =>
            {
                shuttleTickBox.TextBlock.AutoScale = true;
                shuttleTickBox.TextBlock.TextScale = 1.0f;
                if (shuttleTickBox.TextBlock.TextScale < 0.75f)
                {
                    shuttleTickBox.TextBlock.Wrap = true;
                    shuttleTickBox.TextBlock.AutoScale = true;
                    shuttleTickBox.TextBlock.TextScale = 1.0f;
                }
            };
            shuttleList = new GUIDropDown(new RectTransform(Vector2.One, shuttleHolder.RectTransform), elementCount: 10)
            {
                OnSelected = (component, obj) =>
                {
                    GameMain.Client.RequestSelectSub(component.Parent.GetChildIndex(component), isShuttle: true);
                    return true;
                }
            };
            shuttleList.ListBox.RectTransform.MinSize = new Point(250, 0);

            subPreviewContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), rightColumn.RectTransform), style: null);
            subPreviewContainer.RectTransform.SizeChanged += () =>
            {
                if (SelectedSub != null)
                {
                    subPreviewContainer.ClearChildren();
                    SelectedSub.CreatePreviewWindow(subPreviewContainer);
                }
            };

            //------------------------------------------------------------------------------------------------------------------
            //   Gamemode panel
            //------------------------------------------------------------------------------------------------------------------

            GUILayoutGroup miscSettingsHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.075f), gameModeContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            
            miscSettingsHolder.RectTransform.SizeChanged += () =>
            {
                miscSettingsHolder.Recalculate();
                foreach (GUIComponent child in miscSettingsHolder.Children)
                {
                    if (child is GUITextBlock textBlock)
                    {
                        textBlock.TextScale = 1;
                        textBlock.AutoScale = true;
                        textBlock.SetTextPos();
                    }
                    else if (child is GUITickBox tickBox)
                    {
                        tickBox.TextBlock.TextScale = 1;
                        tickBox.TextBlock.AutoScale = true;
                        tickBox.TextBlock.SetTextPos();
                    }
                }
            };

            //seed ------------------------------------------------------------------

            var seedLabel = new GUITextBlock(new RectTransform(Vector2.One, miscSettingsHolder.RectTransform), TextManager.Get("LevelSeed"));
            seedLabel.RectTransform.MaxSize = new Point((int)(seedLabel.TextSize.X + 30 * GUI.Scale), int.MaxValue);
            SeedBox = new GUITextBox(new RectTransform(new Vector2(0.25f, 1.0f), miscSettingsHolder.RectTransform));
            SeedBox.OnDeselected += (textBox, key) =>
            {
                GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.LevelSeed);
            };
            clientDisabledElements.Add(SeedBox);
            LevelSeed = ToolBox.RandomSeed(8);

            //level difficulty ------------------------------------------------------------------

            var difficultyLabel = new GUITextBlock(new RectTransform(Vector2.One, miscSettingsHolder.RectTransform), TextManager.Get("LevelDifficulty"))
            {
                ToolTip = TextManager.Get("leveldifficultyexplanation")
            };
            levelDifficultyScrollBar = new GUIScrollBar(new RectTransform(new Vector2(0.25f, 1.0f), miscSettingsHolder.RectTransform), barSize: 0.2f)
            {
                Step = 0.05f,
                Range = new Vector2(0.0f, 100.0f),
                ToolTip = TextManager.Get("leveldifficultyexplanation"),
                OnReleased = (scrollbar, value) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, levelDifficulty: scrollbar.BarScrollValue);
                    return true;
                }
            };
            difficultyLabel.RectTransform.MaxSize = new Point((int)(difficultyLabel.TextSize.X + 30 * GUI.Scale), int.MaxValue);
            var difficultyName = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), miscSettingsHolder.RectTransform), "")
            {
                ToolTip = TextManager.Get("leveldifficultyexplanation")
            };
            levelDifficultyScrollBar.OnMoved = (scrollbar, value) =>
            {
                if (EventManagerSettings.List.Count == 0) { return true; }
                difficultyName.Text = EventManagerSettings.List[Math.Min((int)Math.Floor(value * EventManagerSettings.List.Count), EventManagerSettings.List.Count - 1)].Name;
                difficultyName.TextColor = Color.Lerp(ToolBox.GradientLerp(scrollbar.BarScroll, Color.LightGreen, Color.Orange, Color.Red), difficultyLabel.TextColor, 0.5f);
                return true;
            };

            clientDisabledElements.Add(levelDifficultyScrollBar);
            
            //gamemode ------------------------------------------------------------------

            GUILayoutGroup gameModeBackground = new GUILayoutGroup(new RectTransform(Vector2.One, gameModeContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };
            
            GUILayoutGroup gameModeHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.333f, 1.0f), gameModeBackground.RectTransform))
            {
                Stretch = true
            };

            var modeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.055f), gameModeHolder.RectTransform) { MinSize = new Point(0, 25) }, TextManager.Get("GameMode"));
            voteText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), modeLabel.RectTransform, Anchor.TopRight),
                TextManager.Get("Votes"), textAlignment: Alignment.CenterRight)
            {
                UserData = "modevotes",
                Visible = false
            };
            modeList = new GUIListBox(new RectTransform(Vector2.One, gameModeHolder.RectTransform))
            {
                OnSelected = VotableClicked
            };
            
            foreach (GameModePreset mode in GameModePreset.List)
            {
                if (mode.IsSinglePlayer) { continue; }

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), modeList.Content.RectTransform) { MinSize = new Point(0, (int)(30 * GUI.Scale)) },
                    mode.Name, style: "ListBoxElement", textAlignment: Alignment.CenterLeft)
                {
                    UserData = mode,
                };
                textBlock.ToolTip = mode.Description;                
            }

            var gameModeSpecificFrame = new GUIFrame(new RectTransform(new Vector2(0.333f, 1.0f), gameModeBackground.RectTransform), style: null);
            CampaignSetupFrame = new GUIFrame(new RectTransform(Vector2.One, gameModeSpecificFrame.RectTransform), style: null)
            {
                Visible = false
            };

            //mission type ------------------------------------------------------------------
            MissionTypeFrame = new GUIFrame(new RectTransform(Vector2.One, gameModeSpecificFrame.RectTransform), style: null);

            GUILayoutGroup missionHolder = new GUILayoutGroup(new RectTransform(Vector2.One, MissionTypeFrame.RectTransform))
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.055f), missionHolder.RectTransform) { MinSize = new Point(0, 25) }, TextManager.Get("MissionType"));
            missionTypeList = new GUIListBox(new RectTransform(Vector2.One, missionHolder.RectTransform))
            {
                OnSelected = (component, obj) =>
                {
                    return false;
                }
            };

            missionTypeTickBoxes = new GUITickBox[Enum.GetValues(typeof(MissionType)).Length - 2];
            int index = 0;
            foreach (MissionType missionType in Enum.GetValues(typeof(MissionType)))
            {
                if (missionType == MissionType.None || missionType == MissionType.All) { continue; }

                GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.05f), missionTypeList.Content.RectTransform) { MinSize = new Point(0, (int)(30 * GUI.Scale)) }, style: "ListBoxElement")
                {
                    UserData = index,
                };

                missionTypeTickBoxes[index] = new GUITickBox(new RectTransform(Vector2.One, frame.RectTransform),
                    TextManager.Get("MissionType." + missionType.ToString()))
                {
                    UserData = (int)missionType,
                    OnSelected = (tickbox) =>
                    {
                        int missionTypeOr = tickbox.Selected ? (int)tickbox.UserData : (int)MissionType.None;
                        int missionTypeAnd = (int)MissionType.All & (!tickbox.Selected ? (~(int)tickbox.UserData) : (int)MissionType.All);
                        GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, (int)missionTypeOr, (int)missionTypeAnd);
                        return true;
                    }
                };

                index++;
            }

            clientDisabledElements.AddRange(missionTypeTickBoxes);

            //traitor probability ------------------------------------------------------------------

            GUILayoutGroup settingsHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.333f, 1.0f), gameModeBackground.RectTransform))
            {
                Stretch = true
            };

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.055f), settingsHolder.RectTransform) { MinSize = new Point(0, 25) }, style: null);
            var settingsContent = new GUILayoutGroup(new RectTransform(Vector2.One, settingsHolder.RectTransform))
            {
                RelativeSpacing = 0.025f
            };
            new GUIFrame(new RectTransform(Vector2.One, settingsContent.RectTransform), style: "InnerFrame")
            {
                IgnoreLayoutGroups = true
            };

            var traitorsSettingHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), settingsContent.RectTransform), isHorizontal: true) { Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), traitorsSettingHolder.RectTransform), TextManager.Get("Traitors"));

            var traitorProbContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), traitorsSettingHolder.RectTransform), isHorizontal: true) { Stretch = true };
            traitorProbabilityButtons = new GUIButton[2];
            traitorProbabilityButtons[0] = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), traitorProbContainer.RectTransform), "<")
            {
                OnClicked = (button, obj) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, traitorSetting: -1);

                    return true;
                }
            };

            traitorProbabilityText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), traitorProbContainer.RectTransform), TextManager.Get("No"), textAlignment: Alignment.Center);
            traitorProbabilityButtons[1] = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), traitorProbContainer.RectTransform), ">")
            {
                OnClicked = (button, obj) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, traitorSetting: 1);

                    return true;
                }
            };

            clientDisabledElements.AddRange(traitorProbabilityButtons);

            //bot count ------------------------------------------------------------------

            var botCountSettingHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), settingsContent.RectTransform), isHorizontal: true) { Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), botCountSettingHolder.RectTransform), TextManager.Get("BotCount"));
            var botCountContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), botCountSettingHolder.RectTransform), isHorizontal: true) { Stretch = true };
            botCountButtons = new GUIButton[2];
            botCountButtons[0] = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), botCountContainer.RectTransform), "<")
            {
                OnClicked = (button, obj) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, botCount: -1);
                    return true;
                }
            };

            botCountText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), botCountContainer.RectTransform), "0", textAlignment: Alignment.Center);
            botCountButtons[1] = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), botCountContainer.RectTransform), ">")
            {
                OnClicked = (button, obj) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, botCount: 1);
                    return true;
                }
            };

            clientDisabledElements.AddRange(botCountButtons);

            var botSpawnModeSettingHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), settingsContent.RectTransform), isHorizontal: true) { Stretch = true };

            new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), botSpawnModeSettingHolder.RectTransform), TextManager.Get("BotSpawnMode"));
            var botSpawnModeContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), botSpawnModeSettingHolder.RectTransform), isHorizontal: true) { Stretch = true };
            botSpawnModeButtons = new GUIButton[2];
            botSpawnModeButtons[0] = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), botSpawnModeContainer.RectTransform), "<")
            {
                OnClicked = (button, obj) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, botSpawnMode: -1);
                    return true;
                }
            };

            botSpawnModeText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), botSpawnModeContainer.RectTransform), "", textAlignment: Alignment.Center);
            botSpawnModeButtons[1] = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), botSpawnModeContainer.RectTransform), ">")
            {
                OnClicked = (button, obj) =>
                {
                    GameMain.Client.ServerSettings.ClientAdminWrite(ServerSettings.NetFlags.Misc, botSpawnMode: 1);
                    return true;
                }
            };

            List<GUIComponent> settingsElements = settingsContent.Children.ToList();
            int spacingElementCount = 0;
            for (int i = 1; i < settingsElements.Count; i++)
            {
                settingsElements[i].RectTransform.MinSize = new Point(0, (int)(20 * GUI.Scale));
                if (settingsElements[i] is GUITextBlock)
                {
                    var spacing = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.03f), settingsContent.RectTransform), style: null);
                    spacing.RectTransform.RepositionChildInHierarchy(i + spacingElementCount);
                    spacingElementCount++;
                }
            }

            clientDisabledElements.AddRange(botSpawnModeButtons);
        }

        public IEnumerable<object> WaitForStartRound(GUIButton startButton, bool allowCancel)
        {
            string headerText = TextManager.Get("RoundStartingPleaseWait");
            var msgBox = new GUIMessageBox(headerText, TextManager.Get("RoundStarting"),
                allowCancel ? new string[] { TextManager.Get("Cancel") } : new string[0]);

            if (allowCancel)
            {
                msgBox.Buttons[0].OnClicked = (btn, userdata) =>
                {
                    startButton.Enabled = true;
                    GameMain.Client.RequestRoundEnd();
                    CoroutineManager.StopCoroutines("WaitForStartRound");
                    return true;
                };
                msgBox.Buttons[0].OnClicked += msgBox.Close;
            }

            if (startButton != null)
            {
                startButton.Enabled = false;
            }

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 10);
            while (Selected == GameMain.NetLobbyScreen && DateTime.Now < timeOut)
            {
                msgBox.Header.Text = headerText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                yield return CoroutineStatus.Running;
            }

            msgBox.Close();
            if (startButton != null)
            {
                startButton.Enabled = true;
            }

            yield return CoroutineStatus.Success;
        }

        public override void Deselect()
        {
            chatInput.Deselect();
            CampaignCharacterDiscarded = false;
            HeadSelectionList = null;
            JobSelectionFrame = null;

            foreach (Sprite sprite in characterSprites) { sprite.Remove(); }
            characterSprites.Clear();

            foreach (Sprite sprite in jobPreferenceSprites) { sprite.Remove(); }
            jobPreferenceSprites.Clear();
        }

        public override void Select()
        {
            if (GameMain.NetworkMember == null) { return; }

            if (HeadSelectionList != null) { HeadSelectionList.Visible = false; }
            if (JobSelectionFrame != null) { JobSelectionFrame.Visible = false; }

            Character.Controlled = null;
            GameMain.LightManager.LosEnabled = false;

            CampaignCharacterDiscarded = false;

            chatInput.Select();
            chatInput.OnEnterPressed = GameMain.Client.EnterChatMessage;
            chatInput.OnTextChanged += GameMain.Client.TypingChatMessage;

            //disable/hide elements the clients are not supposed to use/see
            clientDisabledElements.ForEach(c => c.Enabled = false);
            clientHiddenElements.ForEach(c => c.Visible = false);

            UpdatePermissions();

            if (GameMain.Client != null)
            {
                spectateButton.Visible = GameMain.Client.GameStarted;
                ReadyToStartBox.Parent.Visible = !GameMain.Client.GameStarted;
                ReadyToStartBox.Selected = false;
                if (campaignUI != null)
                {
                    campaignUI.SelectTab(CampaignUI.Tab.Map);
                    if (campaignUI.StartButton != null)
                    {
                        campaignUI.StartButton.Visible = !GameMain.Client.GameStarted &&
                            (GameMain.Client.HasPermission(ClientPermissions.ManageRound) ||
                            GameMain.Client.HasPermission(ClientPermissions.ManageCampaign));
                    }
                }
                GameMain.Client.SetReadyToStart(ReadyToStartBox);
            }
            else
            {
                spectateButton.Visible = false;
                ReadyToStartBox.Parent.Visible = false;
            }
            SetSpectate(spectateBox.Selected);            

            if (GameMain.Client != null)
            {
                GameMain.Client.ServerSettings.Voting.ResetVotes(GameMain.Client.ConnectedClients);
                spectateButton.OnClicked = GameMain.Client.SpectateClicked;
                ReadyToStartBox.OnSelected = GameMain.Client.SetReadyToStart;
            }

            roundControlsHolder.Children.ForEach(c => c.IgnoreLayoutGroups = !c.Visible);
            roundControlsHolder.Recalculate();

            GameMain.NetworkMember.EndVoteCount = 0;
            GameMain.NetworkMember.EndVoteMax = 1;

            base.Select();
        }

        
        public void UpdatePermissions()
        {
            ServerName.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            ServerMessage.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            missionTypeList.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            foreach (var tickBox in missionTypeTickBoxes)
            {
                tickBox.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            }
            traitorProbabilityButtons[0].Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            traitorProbabilityButtons[1].Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            botCountButtons[0].Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            botCountButtons[1].Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            botSpawnModeButtons[0].Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            botSpawnModeButtons[1].Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            levelDifficultyScrollBar.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            autoRestartBox.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            SeedBox.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);

            SettingsButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            SettingsButton.OnClicked = GameMain.Client.ServerSettings.ToggleSettingsFrame;
            StartButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ManageRound) && !GameMain.Client.GameStarted && !campaignContainer.Visible;
            ServerName.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            ServerMessage.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            shuttleTickBox.Enabled = GameMain.Client.HasPermission(ClientPermissions.ManageSettings);
            SubList.Enabled = GameMain.Client.ServerSettings.Voting.AllowSubVoting || GameMain.Client.HasPermission(ClientPermissions.SelectSub);
            shuttleList.Enabled = GameMain.Client.HasPermission(ClientPermissions.SelectSub);
            ModeList.Enabled = GameMain.Client.ServerSettings.Voting.AllowModeVoting || GameMain.Client.HasPermission(ClientPermissions.SelectMode);
            LogButtons.Visible = GameMain.Client.HasPermission(ClientPermissions.ServerLog);
            GameMain.Client.ShowLogButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ServerLog);

            GameMain.Client.EndRoundButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ManageRound);

            if (campaignUI?.StartButton != null)
            {
                campaignUI.StartButton.Visible = !GameMain.Client.GameStarted &&
                    (GameMain.Client.HasPermission(ClientPermissions.ManageRound) || 
                    GameMain.Client.HasPermission(ClientPermissions.ManageCampaign));
            }

            roundControlsHolder.Children.ForEach(c => c.IgnoreLayoutGroups = !c.Visible);
            roundControlsHolder.Recalculate();
        }

        public void ShowSpectateButton()
        {
            if (GameMain.Client == null) return;
            spectateButton.Visible = true;
            spectateButton.Enabled = true;
        }

        public void SetCampaignCharacterInfo(CharacterInfo newCampaignCharacterInfo)
        {            
            if (newCampaignCharacterInfo != null)
            {
                if (CampaignCharacterDiscarded) { return; }
                if (campaignCharacterInfo != newCampaignCharacterInfo)
                {
                    campaignCharacterInfo = newCampaignCharacterInfo;
                    UpdatePlayerFrame(campaignCharacterInfo, false);
                }
            }
            else if (campaignCharacterInfo != null)
            {
                campaignCharacterInfo = null;
                UpdatePlayerFrame(campaignCharacterInfo, false);                
            }
        }

        private void UpdatePlayerFrame(CharacterInfo characterInfo, bool allowEditing = true)
        {
            UpdatePlayerFrame(characterInfo, allowEditing, playerInfoContainer);
        }


        public void CreatePlayerFrame(GUIComponent parent)
        {
            UpdatePlayerFrame(
                playerInfoContainer.Children?.First().UserData as CharacterInfo, 
                allowEditing: campaignCharacterInfo == null,
                parent: parent);
        }

        private void UpdatePlayerFrame(CharacterInfo characterInfo, bool allowEditing, GUIComponent parent)
        {
            if (characterInfo == null)
            {
                characterInfo = new CharacterInfo(Character.HumanSpeciesName, GameMain.Client.Name, null);
                characterInfo.RecreateHead(
                    GameMain.Config.CharacterHeadIndex,
                    GameMain.Config.CharacterRace,
                    GameMain.Config.CharacterGender,
                    GameMain.Config.CharacterHairIndex,
                    GameMain.Config.CharacterBeardIndex,
                    GameMain.Config.CharacterMoustacheIndex,
                    GameMain.Config.CharacterFaceAttachmentIndex);
                GameMain.Client.CharacterInfo = characterInfo;
                characterInfo.OmitJobInPortraitClothing = true;
            }

            parent.ClearChildren();

            GUILayoutGroup infoContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), parent.RectTransform, Anchor.BottomCenter), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.015f,
                Stretch = true,
                UserData = characterInfo
            };

            CharacterNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.065f), infoContainer.RectTransform), characterInfo.Name, textAlignment: Alignment.Center)
            {
                MaxTextLength = Client.MaxNameLength,
                OverflowClip = true
            };
            CharacterNameBox.OnEnterPressed += (tb, text) => { CharacterNameBox.Deselect(); return true; };
            CharacterNameBox.OnDeselected += (tb, key) =>
            {
                if (GameMain.Client == null) { return; }
                string newName = Client.SanitizeName(tb.Text);
                newName = newName.Replace(":", "").Replace(";", "");
                if (string.IsNullOrWhiteSpace(newName))
                {
                    tb.Text = GameMain.Client.Name;
                }
                else
                {
                    ReadyToStartBox.Selected = false;
                    GameMain.Client.SetName(tb.Text);
                };
            };
            
            new GUICustomComponent(new RectTransform(new Vector2(0.6f, 0.18f), infoContainer.RectTransform, Anchor.TopCenter),
                onDraw: (sb, component) => characterInfo.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()));
            
            if (allowEditing)
            {
                GUILayoutGroup characterInfoTabs = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.04f), infoContainer.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };

                jobPreferencesButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.33f), characterInfoTabs.RectTransform),
                    TextManager.Get("JobPreferences"), style: "GUITabButton")
                {
                    Selected = true,
                    OnClicked = SelectJobPreferencesTab
                };
                appearanceButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1.33f), characterInfoTabs.RectTransform),
                    TextManager.Get("CharacterAppearance"), style: "GUITabButton")
                {
                    OnClicked = SelectAppearanceTab
                };

                GUITextBlock.AutoScaleAndNormalize(jobPreferencesButton.TextBlock, appearanceButton.TextBlock);

                characterInfoFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), infoContainer.RectTransform), style: null);

                JobList = new GUIListBox(new RectTransform(Vector2.One, characterInfoFrame.RectTransform), true)
                {
                    Enabled = true,
                    OnSelected = (child, obj) =>
                    {
                        if (child.IsParentOf(GUI.MouseOn)) return false;
                        return OpenJobSelection(child, obj);
                    }
                };

                for (int i = 0; i < 3; i++)
                {
                    Pair<JobPrefab, int> jobPrefab = null;
                    while (i < GameMain.Config.JobPreferences.Count)
                    {
                        var jobIdent = GameMain.Config.JobPreferences[i];
                        if (!JobPrefab.List.ContainsKey(jobIdent.First))
                        {
                            GameMain.Config.JobPreferences.RemoveAt(i);
                            continue;
                        }
                        jobPrefab = new Pair<JobPrefab, int>(JobPrefab.List[jobIdent.First], jobIdent.Second);
                        break;
                    }

                    var slot = new GUIFrame(new RectTransform(new Vector2(0.333f, 1.0f), JobList.Content.RectTransform), style: "ListBoxElement")
                    {
                        CanBeFocused = true,
                        UserData = jobPrefab
                    };
                }

                UpdateJobPreferences(JobList);

                appearanceFrame = new GUIFrame(new RectTransform(Vector2.One, characterInfoFrame.RectTransform), style: "GUIFrameListBox")
                {
                    Visible = false,
                    Color = Color.White
                };
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), infoContainer.RectTransform), characterInfo.Job.Name, textAlignment: Alignment.Center, wrap: true);

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), infoContainer.RectTransform), TextManager.Get("Skills"));
                foreach (Skill skill in characterInfo.Job.Skills)
                {
                    Color textColor = Color.White * (0.5f + skill.Level / 200.0f);
                    var skillText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.08f), infoContainer.RectTransform),
                        "  - " + TextManager.AddPunctuation(':', TextManager.Get("SkillName." + skill.Identifier), ((int)skill.Level).ToString()), 
                        textColor);
                }

                //spacing
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), infoContainer.RectTransform), style: null);

                new GUIButton(new RectTransform(new Vector2(0.8f, 0.1f), infoContainer.RectTransform, Anchor.BottomCenter), TextManager.Get("CreateNew"))
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
        }
        
        public bool ToggleSpectate(GUITickBox tickBox)
        {
            SetSpectate(tickBox.Selected);
            return false;
        }

        public void SetSpectate(bool spectate)
        {
            this.spectateBox.Selected = spectate;
            if (spectate)
            {
                playerInfoContainer.ClearChildren();

                GameMain.Client.CharacterInfo?.Remove();
                GameMain.Client.CharacterInfo = null;
                GameMain.Client.Character?.Remove();
                GameMain.Client.Character = null;
                new GUITextBlock(new RectTransform(Vector2.One, playerInfoContainer.RectTransform, Anchor.Center),
                    TextManager.Get("PlayingAsSpectator"),
                    textAlignment: Alignment.Center);
            }
            else
            {
                UpdatePlayerFrame(campaignCharacterInfo, allowEditing: campaignCharacterInfo == null);
            }
        }

        public void SetAllowSpectating(bool allowSpectating)
        {
            //server owner is allowed to spectate regardless of the server settings
            if (GameMain.Client != null && GameMain.Client.IsServerOwner)
            {
                return;
            }

            //show the player config menu if spectating is not allowed
            if (spectateBox.Selected && !allowSpectating)
            {
                spectateBox.Selected = false;
            }
            //hide spectate tickbox if spectating is not allowed
            spectateBox.Visible = allowSpectating;            
        }

        public void SetAutoRestart(bool enabled, float timer = 0.0f)
        {
            autoRestartBox.Selected = enabled;
            autoRestartTimer = timer;
        }

        public void SetMissionType(MissionType missionType)
        {
            MissionType = missionType;
        }

        public void UpdateSubList(GUIComponent subList, List<Submarine> submarines)
        {
            if (subList == null) { return; }

            subList.ClearChildren();
            
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
            var subTextBlock = new GUITextBlock(new RectTransform(new Vector2(0.8f, 1.0f), frame.RectTransform, Anchor.CenterLeft) /*{ AbsoluteOffset = new Point(buttonSize + 5, 0) }*/,
                ToolBox.LimitString(sub.DisplayName, GUI.Font, subList.Rect.Width - 65), textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };

            var matchingSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == sub.Name && s.MD5Hash?.Hash == sub.MD5Hash?.Hash);
            if (matchingSub == null) matchingSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == sub.Name);

            if (matchingSub == null)
            {
                subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                frame.ToolTip = TextManager.Get("SubNotFound");
            }
            else if (matchingSub?.MD5Hash == null || matchingSub.MD5Hash?.Hash != sub.MD5Hash?.Hash)
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

                /*GUIButton infoButton = new GUIButton(new RectTransform(new Point(buttonSize, buttonSize), frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point((int)(buttonSize * 0.2f), 0) }, "?")
                {
                    UserData = sub
                };
                infoButton.OnClicked += (component, userdata) =>
                {
                    ((Submarine)userdata).CreatePreviewWindow(new GUIMessageBox("", "", new Vector2(0.25f, 0.25f), new Point(500, 400)));
                    return true;
                };*/
            }

            if (!sub.RequiredContentPackagesInstalled)
            {
                subTextBlock.TextColor = Color.Lerp(subTextBlock.TextColor, Color.DarkRed, 0.5f);
                frame.ToolTip = TextManager.Get("ContentPackageMismatch") + "\n\n" + frame.RawToolTip;
            }

            if (sub.HasTag(SubmarineTag.Shuttle))
            {
                var shuttleText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), frame.RectTransform, Anchor.CenterRight),
                    TextManager.Get("Shuttle", fallBackTag: "RespawnShuttle"), textAlignment: Alignment.CenterRight, font: GUI.SmallFont)
                {
                    TextColor = subTextBlock.TextColor * 0.8f,
                    ToolTip = subTextBlock.RawToolTip,
                    CanBeFocused = false
                };
                //make shuttles more dim in the sub list (selecting a shuttle as the main sub is allowed but not recommended)
                if (subList == this.subList.Content)
                {
                    shuttleText.RectTransform.RelativeOffset = new Vector2(0.1f, 0.0f);
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
            if (GameMain.Client == null) { return false; }
            
            VoteType voteType;
            if (component.Parent == GameMain.NetLobbyScreen.SubList.Content)
            {
                if (!GameMain.Client.ServerSettings.Voting.AllowSubVoting)
                {
                    var selectedSub = component.UserData as Submarine;
                    if (!selectedSub.RequiredContentPackagesInstalled)
                    {
                        var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                            selectedSub.RequiredContentPackages.Any() ?
                            TextManager.GetWithVariable("ContentPackageMismatchWarning", "[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)) :
                            TextManager.Get("ContentPackageMismatchWarningGeneric"),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[0].OnClicked = msgBox.Close;
                        msgBox.Buttons[0].OnClicked += (button, obj) =>
                        {
                            GameMain.Client.RequestSelectSub(component.Parent.GetChildIndex(component), isShuttle: false);
                            return true;
                        };
                        msgBox.Buttons[1].OnClicked = msgBox.Close;
                        return false;
                    }
                    else if (GameMain.Client.HasPermission(ClientPermissions.SelectSub))
                    {
                        GameMain.Client.RequestSelectSub(component.Parent.GetChildIndex(component), isShuttle: false);
                        return true;
                    }
                    return false;
                }
                if (component.UserData is Submarine sub)
                {
                    subPreviewContainer.ClearChildren();
                    sub.CreatePreviewWindow(subPreviewContainer);
                }
                voteType = VoteType.Sub;
            }
            else if (component.Parent == GameMain.NetLobbyScreen.ModeList.Content)
            {
                if (!GameMain.Client.ServerSettings.Voting.AllowModeVoting)
                {
                    if (GameMain.Client.HasPermission(ClientPermissions.SelectMode))
                    {
                        string presetName = ((GameModePreset)(component.UserData)).Identifier;

                        //display a verification prompt when switching away from the campaign
                        if (HighlightedModeIndex == SelectedModeIndex &&
                            (GameMain.NetLobbyScreen.ModeList.SelectedData as GameModePreset)?.Identifier == "multiplayercampaign" &&
                            presetName != "multiplayercampaign")
                        {
                            var verificationBox = new GUIMessageBox("", TextManager.Get("endcampaignverification"), new string[] { TextManager.Get("yes"), TextManager.Get("no") });
                            verificationBox.Buttons[0].OnClicked += (btn, userdata) =>
                            {
                                GameMain.Client.RequestSelectMode(component.Parent.GetChildIndex(component));
                                HighlightMode(SelectedModeIndex);
                                verificationBox.Close(btn, userdata);
                                return true;
                            };
                            verificationBox.Buttons[1].OnClicked = verificationBox.Close;
                            return false;
                        }
                        GameMain.Client.RequestSelectMode(component.Parent.GetChildIndex(component));
                        HighlightMode(SelectedModeIndex);
                        return (presetName.ToLowerInvariant() != "multiplayercampaign");
                    }
                    return false;
                }
                else if (!((GameModePreset)userData).Votable)
                {
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
        
        public void AddPlayer(Client client)
        {
            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), playerList.Content.RectTransform) { MinSize = new Point(0, (int)(30 * GUI.Scale)) },
                client.Name, textAlignment: Alignment.CenterLeft, font: GUI.SmallFont, style: null)
            {
                Padding = Vector4.One * 10.0f * GUI.Scale,
                Color = Color.White * 0.25f,
                HoverColor = Color.White * 0.5f,
                SelectedColor = Color.White * 0.85f,
                OutlineColor = Color.White * 0.5f,
                TextColor = Color.White,
                UserData = client
            };
            var soundIcon = new GUIImage(new RectTransform(new Point((int)(textBlock.Rect.Height * 0.8f)), textBlock.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(5, 0) },
                sprite: GUI.Style.GetComponentStyle("GUISoundIcon").Sprites[GUIComponent.ComponentState.None].FirstOrDefault().Sprite, scaleToFit: true)
            {
                UserData = new Pair<string, float>("soundicon", 0.0f),
                CanBeFocused = false,
                Visible = true,
                OverrideState = GUIComponent.ComponentState.None,
                HoverColor = Color.White
            };
            
            new GUIImage(new RectTransform(new Point((int)(textBlock.Rect.Height * 0.8f)), textBlock.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(5, 0) }, 
                "GUISoundIconDisabled")
            {
                UserData = "soundicondisabled",
                CanBeFocused = true,
                Visible = false,
                OverrideState = GUIComponent.ComponentState.None,
                HoverColor = Color.White
            };
            new GUIFrame(new RectTransform(new Vector2(0.6f, 0.6f), textBlock.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.BothHeight) { AbsoluteOffset = new Point(10 + soundIcon.Rect.Width, 0) }, style: "GUIReadyToStart")
            {
                Visible = false,
                CanBeFocused = false,
                ToolTip = TextManager.Get("ReadyToStartTickBox"),
                UserData = "clientready"
            };
        }

        public void SetPlayerNameAndJobPreference(Client client)
        {
            var playerFrame = (GUITextBlock)PlayerList.Content.FindChild(client);
            if (playerFrame == null) { return; }
            playerFrame.Text = client.Name;
            
            Color color = Color.White;
            if (JobPrefab.List.ContainsKey(client.PreferredJob))
            {
                color = JobPrefab.List[client.PreferredJob].UIColor;
            }
            playerFrame.Color = color * 0.4f;
            playerFrame.HoverColor = color * 0.6f;
            playerFrame.SelectedColor = color * 0.8f;
            playerFrame.OutlineColor = color * 0.5f;
            playerFrame.TextColor = color;
        }

        public void SetPlayerVoiceIconState(Client client, bool muted, bool mutedLocally)
        {
            var playerFrame = PlayerList.Content.FindChild(client);
            if (playerFrame == null) { return; }
            var soundIcon = playerFrame.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
            var soundIconDisabled = playerFrame.FindChild("soundicondisabled");

            Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;

            if (!soundIcon.Visible)
            {
                userdata.Second = 0.0f;
            }
            soundIcon.Visible = !muted && !mutedLocally;
            soundIconDisabled.Visible = muted || mutedLocally;
            soundIconDisabled.ToolTip = TextManager.Get(mutedLocally ? "MutedLocally" : "MutedGlobally");
        }

        public void SetPlayerSpeaking(Client client)
        {
            var playerFrame = PlayerList.Content.FindChild(client);
            if (playerFrame == null) { return; }
            var soundIcon = playerFrame.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
            Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;
            userdata.Second = Math.Max(userdata.Second,   0.18f);
            soundIcon.Visible = true;
        }

        public void RemovePlayer(Client client)
        {
            GUIComponent child = playerList.Content.GetChildByUserData(client);
            if (child != null) { playerList.RemoveChild(child); }
        }

        private bool SelectPlayer(Client selectedClient)
        {
            bool myClient = selectedClient.ID == GameMain.Client.ID;

            playerFrame = new GUIButton(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker")
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) ClosePlayerFrame(btn, userdata); return true; }
            };

            Vector2 frameSize = GameMain.Client.HasPermission(ClientPermissions.ManagePermissions) ? new Vector2(.24f, .5f) : new Vector2(.24f, .24f);

            var playerFrameInner = new GUIFrame(new RectTransform(frameSize, playerFrame.RectTransform, Anchor.Center) { MinSize = new Point(550, 0) });
            var paddedPlayerFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.88f), playerFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var headerContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true
            };
            
            var nameText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), headerContainer.RectTransform), 
                text: selectedClient.Name, font: GUI.LargeFont);
            nameText.Text = ToolBox.LimitString(nameText.Text, nameText.Font, nameText.Rect.Width);

            if (selectedClient.SteamID != 0 && Steam.SteamManager.IsInitialized)
            {
                var viewSteamProfileButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), headerContainer.RectTransform, Anchor.TopCenter) { MaxSize = new Point(int.MaxValue, (int)(40 * GUI.Scale)) },
                        TextManager.Get("ViewSteamProfile"))
                {
                    UserData = selectedClient
                };
                viewSteamProfileButton.TextBlock.AutoScale = true;
                viewSteamProfileButton.OnClicked = (bt, userdata) =>
                {
                    Steam.SteamManager.Instance.Overlay.OpenUrl("https://steamcommunity.com/profiles/" + selectedClient.SteamID.ToString());
                    return true;
                };
            }

            if (GameMain.Client.HasPermission(ClientPermissions.ManagePermissions))
            {
                playerFrame.UserData = selectedClient;
                
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), paddedPlayerFrame.RectTransform), 
                    TextManager.Get("Rank"));
                var rankDropDown = new GUIDropDown(new RectTransform(new Vector2(1.0f, 0.1f), paddedPlayerFrame.RectTransform),
                    TextManager.Get("Rank"))
                {
                    UserData = selectedClient,
                    Enabled = !myClient
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
                        GameMain.Client.UpdateClientPermissions(client);

                        playerFrame = null;
                        SelectPlayer(client);
                    }
                    return true;
                };

                var permissionLabels = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), paddedPlayerFrame.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };
                var permissionLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), permissionLabels.RectTransform), TextManager.Get("Permissions"));
                var consoleCommandLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), permissionLabels.RectTransform), TextManager.Get("PermittedConsoleCommands"));
                GUITextBlock.AutoScaleAndNormalize(permissionLabel, consoleCommandLabel);

                var permissionContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), paddedPlayerFrame.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var listBoxContainerLeft = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), permissionContainer.RectTransform))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), listBoxContainerLeft.RectTransform), TextManager.Get("all", fallBackTag: "clientpermission.all"))
                {
                    Enabled = !myClient,
                    OnSelected = (tickbox) =>
                    {
                        //reset rank to custom
                        rankDropDown.SelectItem(null);

                        if (!(playerFrame.UserData is Client client)) { return false; }

                        foreach (GUIComponent child in tickbox.Parent.GetChild<GUIListBox>().Content.Children)
                        {
                            var permissionTickBox = child as GUITickBox;
                            permissionTickBox.Enabled = false;
                            permissionTickBox.Selected = tickbox.Selected;
                            permissionTickBox.Enabled = true;
                        }
                        GameMain.Client.UpdateClientPermissions(client);
                        return true;
                    }
                };
                var permissionsBox = new GUIListBox(new RectTransform(Vector2.One, listBoxContainerLeft.RectTransform))
                {
                    UserData = selectedClient
                };

                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    if (permission == ClientPermissions.None || permission == ClientPermissions.All) continue;

                    var permissionTick = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), permissionsBox.Content.RectTransform),
                        TextManager.Get("ClientPermission." + permission), font: GUI.SmallFont)
                    {
                        UserData = permission,
                        Selected = selectedClient.HasPermission(permission),
                        Enabled = !myClient,
                        OnSelected = (tickBox) =>
                        {
                            //reset rank to custom
                            rankDropDown.SelectItem(null);

                            var client = playerFrame.UserData as Client;
                            if (client == null) { return false; }

                            var thisPermission = (ClientPermissions)tickBox.UserData;
                            if (tickBox.Selected)
                            {
                                client.GivePermission(thisPermission);
                            }
                            else
                            {
                                client.RemovePermission(thisPermission);
                            }
                            if (tickBox.Enabled)
                            {
                                GameMain.Client.UpdateClientPermissions(client);
                            }
                            return true;
                        }
                    };
                }

                var listBoxContainerRight = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), permissionContainer.RectTransform))
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), listBoxContainerRight.RectTransform), TextManager.Get("all", fallBackTag: "clientpermission.all"))
                {
                    Enabled = !myClient,
                    OnSelected = (tickbox) =>
                    {
                        //reset rank to custom
                        rankDropDown.SelectItem(null);

                        var client = playerFrame.UserData as Client;
                        if (client == null) { return false; }

                        foreach (GUIComponent child in tickbox.Parent.GetChild<GUIListBox>().Content.Children)
                        {
                            var commandTickBox = child as GUITickBox;
                            commandTickBox.Enabled = false;
                            commandTickBox.Selected = tickbox.Selected;
                            commandTickBox.Enabled = true;
                        }
                        GameMain.Client.UpdateClientPermissions(client);
                        return true;
                    }
                };
                var commandList = new GUIListBox(new RectTransform(Vector2.One, listBoxContainerRight.RectTransform))
                {
                    UserData = selectedClient
                };
                foreach (DebugConsole.Command command in DebugConsole.Commands)
                {
                    var commandTickBox = new GUITickBox(new RectTransform(new Vector2(0.15f, 0.15f), commandList.Content.RectTransform),
                        command.names[0], font: GUI.SmallFont)
                    {
                        Selected = selectedClient.PermittedConsoleCommands.Contains(command),
                        Enabled = !myClient,
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
                        if (tickBox.Enabled)
                        {
                            GameMain.Client.UpdateClientPermissions(client);
                        }
                        return true;
                    };
                }
            }

            var buttonAreaTop = myClient ? null : new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), paddedPlayerFrame.RectTransform), isHorizontal: true);
            var buttonAreaLower = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.08f), paddedPlayerFrame.RectTransform), isHorizontal: true);
            
            if (!myClient)
            {
                if (GameMain.Client.HasPermission(ClientPermissions.Ban))
                {
                    var banButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaTop.RectTransform),
                        TextManager.Get("Ban"))
                    {
                        UserData = selectedClient
                    };
                    banButton.OnClicked = (bt, userdata) => { BanPlayer(selectedClient); return true; };
                    banButton.OnClicked += ClosePlayerFrame;

                    var rangebanButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaTop.RectTransform),
                        TextManager.Get("BanRange"))
                    {
                        UserData = selectedClient
                    };
                    rangebanButton.OnClicked = (bt, userdata) => { BanPlayerRange(selectedClient); return true; };
                    rangebanButton.OnClicked += ClosePlayerFrame;
                }


                if (GameMain.Client != null && GameMain.Client.ServerSettings.Voting.AllowVoteKick && 
                    selectedClient != null && selectedClient.AllowKicking)
                {
                    var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform),
                        TextManager.Get("VoteToKick"))
                    {
                        Enabled = !selectedClient.HasKickVoteFromID(GameMain.Client.ID),
                        OnClicked = (btn, userdata) => { GameMain.Client.VoteForKick(selectedClient); btn.Enabled = false; return true; },
                        UserData = selectedClient
                    };
                }

                if (GameMain.Client.HasPermission(ClientPermissions.Kick) &&
                    selectedClient != null && selectedClient.AllowKicking)
                {
                    var kickButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform),
                        TextManager.Get("Kick"))
                    {
                        UserData = selectedClient
                    };
                    kickButton.OnClicked = (bt, userdata) => { KickPlayer(selectedClient); return true; };
                    kickButton.OnClicked += ClosePlayerFrame;
                }

                new GUITickBox(new RectTransform(new Vector2(0.25f, 1.0f), buttonAreaTop.RectTransform, Anchor.TopRight),
                    TextManager.Get("Mute"))
                {
                    IgnoreLayoutGroups = true,
                    Selected = selectedClient.MutedLocally,
                    OnSelected = (tickBox) => { selectedClient.MutedLocally = tickBox.Selected; return true; }
                };
            }

            var closeButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonAreaLower.RectTransform, Anchor.BottomRight),
                TextManager.Get("Close"), style: "GUIButtonLarge")
            {
                IgnoreLayoutGroups = true,
                OnClicked = ClosePlayerFrame
            };

            return false;
        }

        private bool ClosePlayerFrame(GUIButton button, object userData)
        {
            playerFrame = null;
            playerList.Deselect();
            return true;
        }

        public void KickPlayer(Client client)
        {
            if (GameMain.NetworkMember == null || client == null) { return; }
            GameMain.Client.CreateKickReasonPrompt(client.Name, false);    
        }

        public void BanPlayer(Client client)
        {
            if (GameMain.NetworkMember == null || client == null) { return; }
            GameMain.Client.CreateKickReasonPrompt(client.Name, ban: true, rangeBan: false);
        }

        public void BanPlayerRange(Client client)
        {
            if (GameMain.NetworkMember == null || client == null) { return; }
            GameMain.Client.CreateKickReasonPrompt(client.Name, ban: true, rangeBan: true);
        }
        
        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();
            
            playerFrame?.AddToGUIUpdateList();  
            //CampaignSetupUI?.AddToGUIUpdateList();
            jobInfoFrame?.AddToGUIUpdateList();

            HeadSelectionList?.AddToGUIUpdateList();
            JobSelectionFrame?.AddToGUIUpdateList();
        }
        

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            string currMicStyle = micIcon.Style.Element.Name.LocalName;

            string targetMicStyle = "GUIMicrophoneEnabled";
            if (GameMain.Config.CaptureDeviceNames == null)
            {
                GameMain.Config.CaptureDeviceNames = OpenAL.Alc.GetStringList(IntPtr.Zero, OpenAL.Alc.CaptureDeviceSpecifier);
            }

            if (GameMain.Config.CaptureDeviceNames.Count == 0)
            {
                targetMicStyle = "GUIMicrophoneUnavailable";
            }
            else if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.Disabled)
            {
                targetMicStyle = "GUIMicrophoneDisabled";
            }

            if (targetMicStyle.ToLowerInvariant() != currMicStyle.ToLowerInvariant())
            {
                GUI.Style.Apply(micIcon, targetMicStyle);
            }
            
            foreach (GUIComponent child in playerList.Content.Children)
            {
                if (child.UserData is Client client)
                {
                    if (child.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") is GUIImage soundIcon)
                    {
                        double voipAmplitude = 0.0f;
                        if (client.ID != GameMain.Client.ID)
                        {
                            voipAmplitude = client.VoipSound?.CurrentAmplitude ?? 0.0f;
                        }
                        else if (VoipCapture.Instance.LastEnqueueAudio > DateTime.Now - new TimeSpan(0, 0, 0, 0, milliseconds: 100))
                        {
                            voipAmplitude = VoipCapture.Instance?.LastAmplitude ?? 0.0;
                        }
                        VoipClient.UpdateVoiceIndicator(soundIcon, (float)voipAmplitude, (float)deltaTime);
                    }
                }
            }

            autoRestartText.Visible = autoRestartTimer > 0.0f && autoRestartBox.Selected;
            if (!MathUtils.NearlyEqual(autoRestartTimer, 0.0f) && autoRestartBox.Selected)
            {
                autoRestartTimer = Math.Max(autoRestartTimer - (float)deltaTime, 0.0f);
                if (autoRestartTimer > 0.0f)
                {
                    autoRestartText.Text = TextManager.Get("RestartingIn") + " " + ToolBox.SecondsToReadableTime(Math.Max(autoRestartTimer, 0));
                }
            }

            if (HeadSelectionList != null && PlayerInput.LeftButtonDown() && !GUI.IsMouseOn(HeadSelectionList))
            {
                HeadSelectionList.Visible = false;                
            }
            if (JobSelectionFrame != null && PlayerInput.LeftButtonDown() && !GUI.IsMouseOn(JobSelectionFrame))
            {
                JobList.Deselect();
                JobSelectionFrame.Visible = false;                
            }
        }
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);

            GUI.DrawBackgroundSprite(spriteBatch, backgroundSprite);

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }


        private PlayStyle? prevPlayStyle = null;
        private void DrawServerBanner(SpriteBatch spriteBatch, GUICustomComponent component)
        {
            if (GameMain.NetworkMember?.ServerSettings == null) { return; }

            PlayStyle playStyle = GameMain.NetworkMember.ServerSettings.PlayStyle;
            if ((int)playStyle < 0 || 
                (int)playStyle >= GameMain.ServerListScreen.PlayStyleBanners.Length)
            {
                return;
            }

            Sprite sprite = GameMain.ServerListScreen.PlayStyleBanners[(int)playStyle];
            float scale = component.Rect.Width / sprite.size.X;
            sprite.Draw(spriteBatch, component.Center, scale: scale);

            if (!prevPlayStyle.HasValue || playStyle != prevPlayStyle.Value)
            {
                var nameText = component.GetChild<GUITextBlock>();
                nameText.Text = TextManager.Get("servertag." + playStyle);
                nameText.Color = GameMain.ServerListScreen.PlayStyleColors[(int)playStyle];
                nameText.RectTransform.NonScaledSize = (nameText.Font.MeasureString(nameText.Text) + new Vector2(25, 10) * GUI.Scale).ToPoint();
                prevPlayStyle = playStyle;

                component.ToolTip = TextManager.Get("servertagdescription." + playStyle);
            }
        }

        public void NewChatMessage(ChatMessage message)
        {
            float prevSize = chatBox.BarSize;

            while (chatBox.Content.CountChildren > 60)
            {
                chatBox.RemoveChild(chatBox.Content.Children.First());
            }

            GUITextBlock msg = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), chatBox.Content.RectTransform),
                text: ChatMessage.GetTimeStamp() + (message.Type == ChatMessageType.Private ? TextManager.Get("PrivateMessageTag") + " " : "") + message.TextWithSender,
                textColor: message.Color,
                color: ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f,
                wrap: true, font: GUI.SmallFont)
            {
                UserData = message,
                CanBeFocused = false
            };
            msg.RectTransform.SizeChanged += Recalculate;
            void Recalculate()
            {
                msg.RectTransform.SizeChanged -= Recalculate;
                msg.CalculateHeightFromText();
                msg.RectTransform.SizeChanged += Recalculate;
            }

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) 
            { 
                chatBox.BarScroll = 1.0f; 
            }
        }

        private bool SelectJobPreferencesTab(GUIButton button, object userData)
        {
            jobPreferencesButton.Selected = true;
            appearanceButton.Selected = false;

            JobList.Visible = true;
            appearanceFrame.Visible = false;

            return false;
        }

        private bool SelectAppearanceTab(GUIButton button, object userData)
        {
            jobPreferencesButton.Selected = false;
            appearanceButton.Selected = true;

            JobList.Visible = false;
            appearanceFrame.Visible = true;

            appearanceFrame.ClearChildren();
            if (HeadSelectionList != null) { HeadSelectionList.Visible = false; }

            GUIButton maleButton = null;
            GUIButton femaleButton = null;

            var info = GameMain.Client.CharacterInfo;

            GUILayoutGroup columnLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), appearanceFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            
            //left column
            GUILayoutGroup leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), columnLayout.RectTransform))
            {
                RelativeSpacing = 0.05f
            };

            GUILayoutGroup genderContainer = new GUILayoutGroup(new RectTransform(new Vector2(2.0f, 0.2f), leftColumn.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), genderContainer.RectTransform), TextManager.Get("Gender"));
            maleButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), genderContainer.RectTransform),
                TextManager.Get("Male"), style: "ListBoxElement")
            {
                UserData = Gender.Male,
                OnClicked = OpenHeadSelection,
                Selected = info.Gender == Gender.Male
            };
            femaleButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), genderContainer.RectTransform),
                TextManager.Get("Female"), style: "ListBoxElement")
            {
                UserData = Gender.Female,
                OnClicked = OpenHeadSelection,
                Selected = info.Gender == Gender.Female
            };

            int hairCount = info.FilterByTypeAndHeadID(info.FilterElementsByGenderAndRace(info.Wearables), WearableType.Hair).Count();
            if (hairCount > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), leftColumn.RectTransform), TextManager.Get("FaceAttachment.Hair"));
                var hairSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.15f), leftColumn.RectTransform))
                {
                    Range = new Vector2(0, hairCount),
                    StepValue = 1,
                    BarScrollValue = info.HairIndex,
                    OnMoved = SwitchHair,
                    BarSize = 1.0f / (float)(hairCount + 1)
                };
            }

            int beardCount = info.FilterByTypeAndHeadID(info.FilterElementsByGenderAndRace(info.Wearables), WearableType.Beard).Count();
            if (beardCount > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), leftColumn.RectTransform), TextManager.Get("FaceAttachment.Beard"));
                var beardSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.15f), leftColumn.RectTransform))
                {
                    Range = new Vector2(0, beardCount),
                    StepValue = 1,
                    BarScrollValue = info.BeardIndex,
                    OnMoved = SwitchBeard,
                    BarSize = 1.0f / (float)(beardCount + 1)
                };
            }

            //right column
            GUILayoutGroup rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), columnLayout.RectTransform))
            {
                RelativeSpacing = 0.05f
            };

            //spacing to account for the gender selection in the left column
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), rightColumn.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            int moustacheCount = info.FilterByTypeAndHeadID(info.FilterElementsByGenderAndRace(info.Wearables), WearableType.Moustache).Count();
            if (moustacheCount > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), rightColumn.RectTransform), TextManager.Get("FaceAttachment.Moustache"));
                var moustacheSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.15f), rightColumn.RectTransform))
                {
                    Range = new Vector2(0, moustacheCount),
                    StepValue = 1,
                    BarScrollValue = info.MoustacheIndex,
                    OnMoved = SwitchMoustache,
                    BarSize = 1.0f / (float)(moustacheCount + 1)
                };
            }

            int faceAttachmentCount = info.FilterByTypeAndHeadID(info.FilterElementsByGenderAndRace(info.Wearables), WearableType.FaceAttachment).Count();
            if (faceAttachmentCount > 0)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), rightColumn.RectTransform), TextManager.Get("FaceAttachment.Accessories"));
                var faceAttachmentSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.15f), rightColumn.RectTransform))
                {
                    Range = new Vector2(0, faceAttachmentCount),
                    StepValue = 1,
                    BarScrollValue = info.FaceAttachmentIndex,
                    OnMoved = SwitchFaceAttachment,
                    BarSize = 1.0f / (float)(faceAttachmentCount + 1)
                };
            }
            
            return false;
        }

        private bool OpenHeadSelection(GUIButton button, object userData)
        {
            Gender selectedGender = (Gender)userData;
            if (HeadSelectionList != null)
            {
                HeadSelectionList.Visible = true;
                foreach (GUIComponent child in HeadSelectionList.Content.Children)
                {
                    child.Visible = (Gender)child.UserData == selectedGender;
                    child.Children.ForEach(c => c.Visible = ((Tuple<Gender, Race, int>)c.UserData).Item1 == selectedGender);
                }
                return true;
            }

            var info = GameMain.Client.CharacterInfo;

            HeadSelectionList = new GUIListBox(
                new RectTransform(new Point(characterInfoFrame.Rect.Width, (characterInfoFrame.Rect.Bottom - button.Rect.Bottom) + characterInfoFrame.Rect.Height * 2), GUI.Canvas)
                {
                    AbsoluteOffset = new Point(characterInfoFrame.Rect.Right - characterInfoFrame.Rect.Width, button.Rect.Bottom)
                });

            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), HeadSelectionList.RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black)
            {
                UserData = "outerglow",
                CanBeFocused = false
            };

            GUILayoutGroup row = null;
            int itemsInRow = 0;

            XElement headElement = info.Ragdoll.MainElement.Elements().FirstOrDefault(e => e.GetAttributeString("type", "").ToLowerInvariant() == "head");
            XElement headSpriteElement = headElement.Element("sprite");
            string spritePathWithTags = headSpriteElement.Attribute("texture").Value;

            var characterConfigElement = info.CharacterConfigElement;

            var heads = info.Heads;
            if (heads != null)
            {
                row = null;
                itemsInRow = 0;
                foreach (var head in heads)
                {
                    var headPreset = head.Key;
                    Gender gender = headPreset.Gender;
                    Race race = headPreset.Race;
                    int headIndex = headPreset.ID;

                    string spritePath = spritePathWithTags
                        .Replace("[GENDER]", gender.ToString().ToLowerInvariant())
                        .Replace("[RACE]", race.ToString().ToLowerInvariant());

                    if (!File.Exists(spritePath)) { continue; }

                    Sprite headSprite = new Sprite(headSpriteElement, "", spritePath);
                    headSprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(headSprite, head.Value.ToPoint()), headSprite.SourceRect.Size);
                    characterSprites.Add(headSprite);

                    if (row == null || itemsInRow >= 4)
                    {
                        row = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.333f), HeadSelectionList.Content.RectTransform), true)
                        {
                            UserData = gender,
                            Visible = gender == selectedGender
                        };
                        itemsInRow = 0;
                    }

                    var btn = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), row.RectTransform), style: "ListBoxElement")
                    {
                        OutlineColor = Color.White * 0.5f,
                        PressedColor = Color.White * 0.5f,
                        UserData = new Tuple<Gender, Race, int>(gender, race, headIndex),
                        OnClicked = SwitchHead,
                        Selected = gender == info.Gender && race == info.Race && headIndex == info.HeadSpriteId,
                        Visible = gender == selectedGender
                    };

                    new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), headSprite, scaleToFit: true);
                    itemsInRow++;
                }
            }

            return false;
        }

        private bool SwitchJob(GUIButton button, object obj)
        {
            int childIndex = JobList.SelectedIndex;
            var child = JobList.SelectedComponent;

            bool moveToNext = obj != null;

            var jobPrefab = (obj as Pair<JobPrefab, int>)?.First;

            var prevObj = child.UserData;

            var existingChild = JobList.Content.FindChild(d => (d.UserData is Pair<JobPrefab, int> prefab) && (prefab.First == jobPrefab));
            if (existingChild != null && obj != null)
            {
                existingChild.UserData = prevObj;
            }
            child.UserData = obj;

            for (int i = 0; i < 2; i++)
            {
                if (i < 2 && JobList.Content.GetChild(i).UserData == null)
                {
                    JobList.Content.GetChild(i).UserData = JobList.Content.GetChild(i + 1).UserData;
                    JobList.Content.GetChild(i + 1).UserData = null;
                }
            }

            UpdateJobPreferences(JobList);

            if (moveToNext)
            {
                var emptyChild = JobList.Content.FindChild(c => c.UserData == null && c.CanBeFocused);
                if (emptyChild != null)
                {
                    JobList.Select(JobList.Content.GetChildIndex(emptyChild));
                }
                else
                {
                    JobList.Deselect();
                    if (JobSelectionFrame != null) { JobSelectionFrame.Visible = false; }
                }
            }
            else
            {
                OpenJobSelection(child, child.UserData);
            }

            return false;
        }

        private bool OpenJobSelection(GUIComponent child, object userData)
        {
            if (JobSelectionFrame != null)
            {
                JobSelectionFrame.Visible = true;
                return true;
            }

            Point frameSize = new Point(characterInfoFrame.Rect.Width, characterInfoFrame.Rect.Height * 2);
            JobSelectionFrame = new GUIFrame(new RectTransform(frameSize, GUI.Canvas, Anchor.TopLeft)
                { AbsoluteOffset = new Point(characterInfoFrame.Rect.Right - frameSize.X, characterInfoFrame.Rect.Bottom) }, "GUIFrameListBox");

            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), JobSelectionFrame.RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black)
            {
                UserData = "outerglow",
                CanBeFocused = false
            };

            var rows = new GUILayoutGroup(new RectTransform(Vector2.One, JobSelectionFrame.RectTransform)) { Stretch = true };
            var row = new GUILayoutGroup(new RectTransform(Vector2.One, rows.RectTransform), true);

            GUIButton jobButton = null;

            var availableJobs = JobPrefab.List.Values.Where(jobPrefab =>
                    jobPrefab.MaxNumber > 0 && JobList.Content.Children.All(c => !(c.UserData is Pair<JobPrefab, int> prefab) || prefab.First != jobPrefab)
            ).Select(j => new Pair<JobPrefab, int>(j, 1));
            availableJobs = availableJobs.Concat(
                JobPrefab.List.Values.Where(jobPrefab =>
                    jobPrefab.MaxNumber > 0 && JobList.Content.Children.Any(c => (c.UserData is Pair<JobPrefab, int> prefab) && prefab.First == jobPrefab)
            ).Select(j => JobList.Content.FindChild(c => (c.UserData is Pair<JobPrefab, int> prefab) && prefab.First == j).UserData as Pair<JobPrefab, int>));
            availableJobs = availableJobs.ToList();

            int itemsInRow = 1;

            foreach (var jobPrefab in availableJobs)
            {
                if (itemsInRow >= 4)
                {
                    row = new GUILayoutGroup(new RectTransform(Vector2.One, rows.RectTransform), true);
                    itemsInRow = 0;
                }

                jobButton = new GUIButton(new RectTransform(new Vector2(1.0f / 3.0f, 1.0f), row.RectTransform), style: "ListBoxElement")
                {
                    PressedColor = Color.White,
                    OutlineColor = Color.White * 0.5f,
                    UserData = jobPrefab,
                    OnClicked = (btn, usdt) =>
                    {
                        if (btn.IsParentOf(GUI.MouseOn)) return false;
                        return SwitchJob(btn, usdt);
                    }
                };
                itemsInRow++;

                var images = AddJobSpritesToGUIComponent(jobButton, jobPrefab.First);
                for (int variantIndex = 0; variantIndex < images.Length; variantIndex++)
                {
                    foreach (GUIImage image in images[variantIndex])
                    {
                        characterSprites.Add(image.Sprite);
                    }
                }

                if (images != null && images.Length > 1)
                {
                    jobPrefab.Second = Math.Min(jobPrefab.Second, images.Length);
                    int currVisible = jobPrefab.Second;
                    GUIButton currSelected = null;
                    for (int variantIndex = 0; variantIndex < images.Length; variantIndex++)
                    {
                        foreach (GUIImage image in images[variantIndex])
                        {
                            image.Visible = currVisible == (variantIndex + 1);
                        }

                        var variantButton = new GUIButton(new RectTransform(new Vector2(0.15f), jobButton.RectTransform, scaleBasis: ScaleBasis.BothWidth) { RelativeOffset = new Vector2(0.05f, 0.05f + 0.2f * variantIndex) }, (variantIndex + 1).ToString(), style: null)
                        {
                            Color = new Color(50, 50, 50, 200),
                            HoverColor = Color.Gray * 0.75f,
                            PressedColor = Color.Black * 0.75f,
                            SelectedColor = new Color(45, 70, 100, 200),
                            UserData = new Pair<JobPrefab, int>(jobPrefab.First, variantIndex+1),
                            OnClicked = (btn, obj) =>
                            {
                                currSelected.Selected = false;
                                int k = ((Pair<JobPrefab, int>)obj).Second;
                                btn.Parent.UserData = obj;
                                for (int j = 0; j < images.Length; j++)
                                {
                                    foreach (GUIImage image in images[j])
                                    {
                                        image.Visible = k == (j + 1);
                                    }
                                }
                                currSelected = btn;
                                currSelected.Selected = true;

                                return false;
                            }
                        };

                        if (currVisible == (variantIndex + 1))
                        {
                            currSelected = variantButton;
                        }
                    }
                    if (currSelected != null)
                    {
                        currSelected.Selected = true;
                    }
                }
            }

            return true;
        }

        private GUIImage[][] AddJobSpritesToGUIComponent(GUIComponent parent, JobPrefab jobPrefab)
        {
            GUIFrame innerFrame = null;
            List<JobPrefab.OutfitPreview> outfitPreviews = jobPrefab.GetJobOutfitSprites(Gender.Male, out Vector2 dimensions);

            innerFrame = new GUIFrame(new RectTransform(Vector2.One * 0.8f, parent.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(-0.07f, -0.06f) }, style: null)
            {
                CanBeFocused = false
            };

            void recalculateInnerFrame()
            {
                float buttonWidth = parent.Rect.Width;
                float buttonHeight = parent.Rect.Height;

                Vector2 innerFrameSize;
                if (buttonWidth / dimensions.X > buttonHeight / dimensions.Y)
                {
                    innerFrameSize = new Vector2((dimensions.X / dimensions.Y) * (buttonHeight / buttonWidth), 1.0f);
                }
                else
                {
                    innerFrameSize = new Vector2(1.0f, (dimensions.Y / dimensions.X) * (buttonWidth / buttonHeight));
                }

                innerFrame.RectTransform.RelativeSize = innerFrameSize * 0.8f;
            }

            GUIImage[][] retVal = new GUIImage[0][];
            if (outfitPreviews != null && outfitPreviews.Any())
            {
                parent.RectTransform.SizeChanged += recalculateInnerFrame;

                retVal = new GUIImage[outfitPreviews.Count][];
                for (int i = 0; i < outfitPreviews.Count; i++)
                {
                    JobPrefab.OutfitPreview outfitPreview = outfitPreviews[i];
                    retVal[i] = new GUIImage[outfitPreview.Sprites.Count];
                    for (int j = 0; j < outfitPreview.Sprites.Count; j++)
                    {
                        Pair<Sprite, Vector2> sprite = outfitPreview.Sprites[j];
                        retVal[i][j] = new GUIImage(new RectTransform(sprite.First.SourceRect.Size.ToVector2() / dimensions, innerFrame.RectTransform, Anchor.Center) { RelativeOffset = sprite.Second / dimensions }, sprite.First, scaleToFit: true)
                        {
                            PressedColor = Color.White,
                            CanBeFocused = false
                        };
                    }
                }

                recalculateInnerFrame();
            }

            var textBlock = new GUITextBlock(
              innerFrame.CountChildren == 0 ?
                  new RectTransform(Vector2.One, parent.RectTransform, Anchor.Center) :
                  new RectTransform(new Vector2(1.0f, 0.2f), parent.RectTransform, Anchor.BottomCenter),
              jobPrefab.Name, textAlignment: Alignment.Center)
            {
                TextColor = jobPrefab.UIColor,
                CanBeFocused = false,
                AutoScale = true
            };
            textBlock.RectTransform.SizeChanged += () => { textBlock.TextScale = 1.0f; };

            return retVal;
        }

        private bool SwitchHead(GUIButton button, object obj)
        {
            var info = GameMain.Client.CharacterInfo;

            Gender gender = ((Tuple<Gender, Race, int>)obj).Item1;
            Race race = ((Tuple<Gender, Race, int>)obj).Item2;
            int id = ((Tuple<Gender, Race, int>)obj).Item3;

            if (gender != info.Gender || race != info.Race || id != info.HeadSpriteId)
            {
                info.Head = new CharacterInfo.HeadInfo(id, gender, race);
                info.ReloadHeadAttachments();
            }
            StoreHead();

            UpdateJobPreferences(JobList);

            SelectAppearanceTab(button, obj);

            return true;
        }

        private bool SwitchHair(GUIScrollBar scrollBar, float barScroll) => SwitchAttachment(scrollBar, WearableType.Hair);
        private bool SwitchBeard(GUIScrollBar scrollBar, float barScroll) => SwitchAttachment(scrollBar, WearableType.Beard);
        private bool SwitchMoustache(GUIScrollBar scrollBar, float barScroll) => SwitchAttachment(scrollBar, WearableType.Moustache);
        private bool SwitchFaceAttachment(GUIScrollBar scrollBar, float barScroll) => SwitchAttachment(scrollBar, WearableType.FaceAttachment);
        private bool SwitchAttachment(GUIScrollBar scrollBar, WearableType type)
        {
            var info = GameMain.Client.CharacterInfo;
            int index = (int)scrollBar.BarScrollValue;
            switch (type)
            {
                case WearableType.Beard:
                    info.Head = new CharacterInfo.HeadInfo(info.HeadSpriteId, info.Gender, info.Race, info.HairIndex, index, info.MoustacheIndex, info.FaceAttachmentIndex);
                    break;
                case WearableType.FaceAttachment:
                    info.Head = new CharacterInfo.HeadInfo(info.HeadSpriteId, info.Gender, info.Race, info.HairIndex, info.BeardIndex, info.MoustacheIndex, index);
                    break;
                case WearableType.Hair:
                    info.Head = new CharacterInfo.HeadInfo(info.HeadSpriteId, info.Gender, info.Race, index, info.BeardIndex, info.MoustacheIndex, info.FaceAttachmentIndex);
                    break;
                case WearableType.Moustache:
                    info.Head = new CharacterInfo.HeadInfo(info.HeadSpriteId, info.Gender, info.Race, info.HairIndex, info.BeardIndex, index, info.FaceAttachmentIndex);
                    break;
                default:
                    DebugConsole.ThrowError($"Wearable type not implemented: {type.ToString()}");
                    return false;
            }
            info.ReloadHeadAttachments();
            StoreHead();
            return true;
        }

        private void StoreHead()
        {
            var info = GameMain.Client.CharacterInfo;
            var config = GameMain.Config;
            config.CharacterRace = info.Race;
            config.CharacterGender = info.Gender;
            config.CharacterHeadIndex = info.HeadSpriteId;
            config.CharacterHairIndex = info.HairIndex;
            config.CharacterBeardIndex = info.BeardIndex;
            config.CharacterMoustacheIndex = info.MoustacheIndex;
            config.CharacterFaceAttachmentIndex = info.FaceAttachmentIndex;
        }

        public void SelectMode(int modeIndex)
        {
            if (modeIndex < 0 || modeIndex >= modeList.Content.CountChildren) { return; }
            
            if (campaignUI != null &&
                ((GameModePreset)modeList.Content.GetChild(modeIndex).UserData).Identifier != "multiplayercampaign")
            {
                ToggleCampaignMode(false);
            }
            
            if ((HighlightedModeIndex == selectedModeIndex || HighlightedModeIndex<0) && modeList.SelectedIndex != modeIndex) { modeList.Select(modeIndex, true); }
            selectedModeIndex = modeIndex;

            MissionTypeFrame.Visible = SelectedMode != null && SelectedMode.Identifier == "mission" && HighlightedModeIndex == SelectedModeIndex;
            CampaignSetupFrame.Visible = false;
        }

        public void HighlightMode(int modeIndex)
        {
            if (modeIndex < 0 || modeIndex >= modeList.Content.CountChildren) { return; }

            HighlightedModeIndex = modeIndex;
            MissionTypeFrame.Visible = SelectedMode != null && SelectedMode.Identifier == "mission" && HighlightedModeIndex == SelectedModeIndex;
            CampaignSetupFrame.Visible = SelectedMode != null && SelectedMode.Identifier == "multiplayercampaign";
        }

        public void ToggleCampaignView(bool enabled)
        {
            campaignContainer.Visible = enabled;
            gameModeContainer.Visible = !enabled;

            campaignViewButton.Selected = enabled;
            gameModeViewButton.Selected = !enabled;
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
            shuttleList.Enabled = !enabled && GameMain.Client.HasPermission(ClientPermissions.SelectSub);
            StartButton.Visible = GameMain.Client.HasPermission(ClientPermissions.ManageRound) && !GameMain.Client.GameStarted && !enabled;

            if (campaignViewButton != null) { campaignViewButton.Visible = enabled; }
            
            if (enabled)
            {
                if (campaignUI == null || campaignUI.Campaign != GameMain.GameSession.GameMode)
                {
                    campaignContainer.ClearChildren();

                    campaignUI = new CampaignUI(GameMain.GameSession.GameMode as CampaignMode, campaignContainer)
                    {
                        StartRound = () => 
                        {
                            GameMain.Client.RequestStartRound();
                            CoroutineManager.StartCoroutine(WaitForStartRound(campaignUI.StartButton, allowCancel: true), "WaitForStartRound");
                        }
                    };

                    var campaignMenuContainer = new GUIFrame(new RectTransform(new Vector2(0.4f, 1.0f), campaignContainer.RectTransform, Anchor.TopRight), style: null)
                    {
                        Color = Color.Black
                    };
                    CampaignUI.SetMenuPanelParent(campaignMenuContainer.RectTransform);
                    CampaignUI.SetMissionPanelParent(campaignMenuContainer.RectTransform);
                    GameMain.GameSession.Map.CenterOffset = new Vector2(-campaignContainer.Rect.Width / 5, 0);
                }
                modeList.Select(2, true);
            }
            else
            {
                campaignUI = null;
            }

            /*if (GameMain.Server != null)
            {
                lastUpdateID++;
            }*/
        }

        public void TryDisplayCampaignSubmarine(Submarine submarine)
        {
            string name = submarine?.Name;
            bool displayed = false;
            subList.OnSelected -= VotableClicked;
            subList.Deselect();
            subPreviewContainer.ClearChildren();
            foreach (GUIComponent child in subList.Content.Children)
            {
                Submarine sub = child.UserData as Submarine;
                if (sub == null) { continue; }
                //just check the name, even though the campaign sub may not be the exact same version
                //we're selecting the sub just for show, the selection is not actually used for anything
                if (sub.Name == name)
                {
                    subList.Select(sub);
                    if (Submarine.SavedSubmarines.Contains(sub))
                    {
                        sub.CreatePreviewWindow(subPreviewContainer);
                        displayed = true;
                    }
                    break;
                }
            }
            subList.OnSelected += VotableClicked;
            if (!displayed)
            {
                submarine.CreatePreviewWindow(subPreviewContainer);
            }
        }

        private bool ViewJobInfo(GUIButton button, object obj)
        {
            if (!(button.UserData is JobPrefab jobPrefab)) { return false; }

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

        private void UpdateJobPreferences(GUIListBox listBox)
        {
            foreach (Sprite sprite in jobPreferenceSprites) { sprite.Remove(); }
            jobPreferenceSprites.Clear();

            List<Pair<string, int>> jobNamePreferences = new List<Pair<string, int>>();

            bool disableNext = false;
            for (int i = 0; i < listBox.Content.CountChildren; i++)
            {
                GUIComponent slot = listBox.Content.GetChild(i);

                slot.OutlineColor = Color.White * 0.4f;
                slot.Color = Color.Gray;
                slot.HoverColor = Color.White;
                slot.SelectedColor = Color.White;
                
                slot.ClearChildren();

                slot.CanBeFocused = !disableNext;
                if (slot.UserData is Pair<JobPrefab, int> jobPrefab)
                {
                    var images = AddJobSpritesToGUIComponent(slot, jobPrefab.First);
                    for (int variantIndex = 0; variantIndex < images.Length; variantIndex++)
                    {
                        foreach (GUIImage image in images[variantIndex])
                        {
                            jobPreferenceSprites.Add(image.Sprite);
                            int selectedVariantIndex = Math.Min(jobPrefab.Second, images.Length);
                            image.Visible = images.Length == 1 || selectedVariantIndex == (variantIndex + 1);
                        }
                        if (images.Length > 1)
                        {
                            var variantButton = new GUIButton(new RectTransform(new Vector2(0.15f), slot.RectTransform, scaleBasis: ScaleBasis.BothWidth) { RelativeOffset = new Vector2(0.05f, 0.25f + 0.2f * variantIndex) }, (variantIndex + 1).ToString(), style: null)
                            {
                                Color = new Color(50, 50, 50, 200),
                                HoverColor = Color.Gray * 0.75f,
                                PressedColor = Color.Black * 0.75f,
                                SelectedColor = new Color(45, 70, 100, 200),
                                Selected = jobPrefab.Second == (variantIndex + 1),
                                UserData = new Pair<JobPrefab, int>(jobPrefab.First, variantIndex + 1),
                                OnClicked = (btn, obj) =>
                                {
                                    int k = ((Pair<JobPrefab, int>)obj).Second;
                                    btn.Parent.UserData = obj;
                                    UpdateJobPreferences(listBox);
                                    return false;
                                }
                            };
                        }
                    }

                    //info button
                    new GUIButton(new RectTransform(new Vector2(0.15f), slot.RectTransform, Anchor.TopLeft, scaleBasis: ScaleBasis.BothWidth) { RelativeOffset = new Vector2(0.05f) }, style: "GUIButtonInfo")
                    {
                        UserData = jobPrefab.First,
                        OnClicked = ViewJobInfo                    
                    };

                    //remove button
                    new GUIButton(new RectTransform(new Vector2(0.15f), slot.RectTransform, Anchor.TopRight, scaleBasis: ScaleBasis.BothWidth) { RelativeOffset = new Vector2(0.05f) }, style: "GUICancelButton")
                    {
                        UserData = i,
                        OnClicked = (btn, obj) =>
                        {
                            JobList.Select((int)obj, true);
                            SwitchJob(btn, null);
                            if (JobSelectionFrame != null) { JobSelectionFrame.Visible = false; }
                            JobList.Deselect();

                            return false;
                        }
                    };

                    jobNamePreferences.Add(new Pair<string, int>(jobPrefab.First.Identifier, jobPrefab.Second));
                }
                else
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.6f), slot.RectTransform), (i + 1).ToString(), textColor: Color.White * (disableNext ? 0.15f : 0.5f), textAlignment: Alignment.Center, font: GUI.LargeFont)
                    {
                        CanBeFocused = false
                    };

                    if (!disableNext)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), slot.RectTransform, Anchor.BottomCenter), TextManager.Get("clicktoselectjob"), font: GUI.SmallFont, wrap: true, textAlignment: Alignment.Center)
                        {
                            CanBeFocused = false
                        };
                    }

                    disableNext = true;
                }
            }
            GameMain.Client.ForceNameAndJobUpdate();

            if (!GameMain.Config.JobPreferences.SequenceEqual(jobNamePreferences))
            {
                GameMain.Config.JobPreferences = jobNamePreferences;
                GameMain.Config.SaveNewPlayerConfig();
            }
        }

        public Pair<string, string> FailedSelectedSub;
        public Pair<string, string> FailedSelectedShuttle;

        public bool TrySelectSub(string subName, string md5Hash, GUIListBox subList)
        {
            if (GameMain.Client == null) { return false; }

            //already downloading the selected sub file
            if (GameMain.Client.FileReceiver.ActiveTransfers.Any(t => t.FileName == subName + ".sub"))
            {
                return false;
            }
            
            Submarine sub = subList.Content.Children
                .FirstOrDefault(c => c.UserData is Submarine s && s.Name == subName && s.MD5Hash?.Hash == md5Hash)?
                .UserData as Submarine;

            //matching sub found and already selected, all good
            if (sub != null)   
            {
                if (subList == this.subList)
                {
                    subPreviewContainer.ClearChildren();
                    sub.CreatePreviewWindow(subPreviewContainer);
                }
                if (subList.SelectedData is Submarine selectedSub && selectedSub.MD5Hash?.Hash == md5Hash && System.IO.File.Exists(sub.FilePath))
                {
                    return true;
                }
            }

            //sub not found, see if we have a sub with the same name
            if (sub == null)
            {
                sub = subList.Content.Children
                    .FirstOrDefault(c => c.UserData is Submarine s && s.Name == subName)?
                    .UserData as Submarine;
            }

            //found a sub that at least has the same name, select it
            if (sub != null)
            {
                if (subList.Parent is GUIDropDown subDropDown)
                {
                    subDropDown.SelectItem(sub);
                }
                else
                {
                    subList.OnSelected -= VotableClicked;
                    subList.Select(sub, force: true);
                    subList.OnSelected += VotableClicked;
                }

                if (subList == SubList)
                    FailedSelectedSub = null;
                else
                    FailedSelectedShuttle = null;
                
                //hashes match, all good
                if (sub.MD5Hash?.Hash == md5Hash && Submarine.SavedSubmarines.Contains(sub))
                {
                    return true;
                }
            }

            //-------------------------------------------------------------------------------------
            //if we get to this point, a matching sub was not found or it has an incorrect MD5 hash
            
            if (subList == SubList)
                FailedSelectedSub = new Pair<string, string>(subName, md5Hash);
            else
                FailedSelectedShuttle = new Pair<string, string>(subName, md5Hash);

            string errorMsg = "";
            if (sub == null || !Submarine.SavedSubmarines.Contains(sub))
            {
                errorMsg = TextManager.GetWithVariable("SubNotFoundError", "[subname]", subName) + " ";
            }
            else if (sub.MD5Hash?.Hash == null)
            {
                errorMsg = TextManager.GetWithVariable("SubLoadError", "[subname]", subName) + " ";
                GUITextBlock textBlock = subList.Content.GetChildByUserData(sub)?.GetChild<GUITextBlock>();
                if (textBlock != null) { textBlock.TextColor = Color.Red; }
            }
            else
            {
                errorMsg = TextManager.GetWithVariables("SubDoesntMatchError", new string[3] { "[subname]" , "[myhash]", "[serverhash]" }, 
                    new string[3] { sub.Name, sub.MD5Hash.ShortHash, Md5Hash.GetShortHash(md5Hash) }) + " ";
            }

            errorMsg += TextManager.Get("DownloadSubQuestion");

            //already showing a message about the same sub
            if (GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "request" + subName))
            {
                return false;
            }

            var requestFileBox = new GUIMessageBox(TextManager.Get("DownloadSubLabel"), errorMsg, 
                new string[] { TextManager.Get("Yes"), TextManager.Get("No") })
            {
                UserData = "request" + subName
            };
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

    }
}
