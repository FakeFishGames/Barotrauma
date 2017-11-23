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

        private GUIButton[] traitorProbabilityButtons;
        private GUITextBlock traitorProbabilityText;

        private GUIButton[] missionTypeButtons;
        private GUIComponent missionTypeBlock;

        private GUIListBox jobList;

        private GUITextBox textBox, seedBox;

        private GUIFrame defaultModeContainer, campaignContainer;

        private GUIButton campaignViewButton;

        private GUIFrame myPlayerFrame;

        private GUIFrame jobInfoFrame;

        private GUIFrame playerFrame;

        private GUITickBox autoRestartBox;

        private GUIDropDown shuttleList;

        private CampaignUI campaignUI;

        private Sprite backgroundSprite;

        private GUITextBox serverMessage;

        private float autoRestartTimer;

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

        public GameModePreset SelectedMode
        {
            get { return modeList.SelectedData as GameModePreset; }
        }

        public int MissionTypeIndex
        {
            get { return (int)missionTypeBlock.UserData; }
            set { missionTypeBlock.UserData = value; }
        }
        
        public List<JobPrefab> JobPreferences
        {
            get
            {
                List<JobPrefab> jobPreferences = new List<JobPrefab>();
                foreach (GUIComponent child in jobList.children)
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
                return "Restarting in " + ToolBox.SecondsToReadableTime(Math.Max(GameMain.Server.AutoRestartTimer, 0));
            }

            if (autoRestartTimer == 0.0f) return "";
            return "Restarting in " + ToolBox.SecondsToReadableTime(Math.Max(autoRestartTimer, 0));
        }

        public NetLobbyScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 80, 1500);
            int height = Math.Min(GameMain.GraphicsHeight - 80, 800);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(panelRect, Color.Transparent, Alignment.Center, null);
            //menu.Padding = GUI.style.smallPadding;

            //server info panel ------------------------------------------------------------

            infoFrame = new GUIFrame(new Rectangle(0, 0, (int)(panelRect.Width * 0.7f), (int)(panelRect.Height * 0.6f)), "", menu);
            infoFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            //chatbox ----------------------------------------------------------------------
            GUIFrame chatFrame = new GUIFrame(
                new Rectangle(0, (int)(panelRect.Height * 0.6f + 20),
                    (int)(panelRect.Width * 0.7f),
                    (int)(panelRect.Height * 0.4f - 20)),
                "", menu);
            chatFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 40.0f);

            chatBox = new GUIListBox(new Rectangle(0, 0, 0, chatFrame.Rect.Height - 80), Color.White, "", chatFrame);
            textBox = new GUITextBox(new Rectangle(0, 25, 0, 25), Alignment.Bottom, "", chatFrame);
            textBox.MaxTextLength = ChatMessage.MaxLength;
            textBox.Font = GUI.SmallFont;

            //player info panel ------------------------------------------------------------

            myPlayerFrame = new GUIFrame(
                new Rectangle((int)(panelRect.Width * 0.7f + 20), 0,
                    (int)(panelRect.Width * 0.3f - 20), (int)(panelRect.Height * 0.6f)),
                "", menu);
            myPlayerFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            //player list ------------------------------------------------------------------

            GUIFrame playerListFrame = new GUIFrame(
                new Rectangle((int)(panelRect.Width * 0.7f + 20), (int)(panelRect.Height * 0.6f + 20),
                    (int)(panelRect.Width * 0.3f - 20), (int)(panelRect.Height * 0.4f - 20)),
                "", menu);

            playerListFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 40.0f);

            playerList = new GUIListBox(new Rectangle(0, 0, 0, 0), null, "", playerListFrame);
            playerList.OnSelected = SelectPlayer;

            defaultModeContainer = new GUIFrame(new Rectangle(0, 10, 0, 0), null, infoFrame);

            campaignContainer = new GUIFrame(new Rectangle(0, 20, 0, 0), null, infoFrame);
            campaignContainer.Visible = false;

            //submarine list ------------------------------------------------------------------

            int columnWidth = infoFrame.Rect.Width / 3 - 5;
            int columnX = 0;

            new GUITextBlock(new Rectangle(columnX, 110, columnWidth, 30), "Submarine:", "", defaultModeContainer);
            subList = new GUIListBox(new Rectangle(columnX, 140, columnWidth, defaultModeContainer.Rect.Height - 170), Color.White, "", defaultModeContainer);
            subList.OnSelected = VotableClicked;

            var voteText = new GUITextBlock(new Rectangle(columnX, 110, columnWidth, 30), "Votes: ", "", Alignment.TopLeft, Alignment.TopRight, defaultModeContainer);
            voteText.UserData = "subvotes";
            voteText.Visible = false;
            
            columnX += columnWidth + 20;


            //respawn shuttle ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 110, 20, 20), "Respawn shuttle:", "", defaultModeContainer);
            shuttleList = new GUIDropDown(new Rectangle(columnX, 140, 200, 20), "", "", defaultModeContainer);


            //gamemode ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 170, 0, 30), "Game mode: ", "", defaultModeContainer);
            modeList = new GUIListBox(new Rectangle(columnX, 200, columnWidth, defaultModeContainer.Rect.Height - 230), "", defaultModeContainer);
            modeList.OnSelected = VotableClicked;

            voteText = new GUITextBlock(new Rectangle(columnX, 170, columnWidth, 30), "Votes: ", "", Alignment.TopLeft, Alignment.TopRight, defaultModeContainer);
            voteText.UserData = "modevotes";
            voteText.Visible = false;

            foreach (GameModePreset mode in GameModePreset.list)
            {
                if (mode.IsSinglePlayer) continue;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    mode.Name, "ListBoxElement",
                    Alignment.TopLeft, Alignment.CenterLeft,
                    modeList);
                textBlock.ToolTip = mode.Description;
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = mode;
            }

            //mission type ------------------------------------------------------------------

            missionTypeBlock = new GUITextBlock(new Rectangle(columnX, -10, 300, 20), "Mission type:", "", Alignment.BottomLeft, Alignment.CenterLeft, defaultModeContainer);
            missionTypeBlock.Padding = Vector4.Zero;
            missionTypeBlock.UserData = 0;

            missionTypeButtons = new GUIButton[2];

            missionTypeButtons[0] = new GUIButton(new Rectangle(100, 0, 20, 20), "<", Alignment.BottomLeft, "", missionTypeBlock);
            missionTypeButtons[0].UserData = -1;

            new GUITextBlock(new Rectangle(120, 0, 80, 20), "Random", "", Alignment.BottomLeft, Alignment.Center, missionTypeBlock).UserData = 0;

            missionTypeButtons[1] = new GUIButton(new Rectangle(200, 0, 20, 20), ">", Alignment.BottomLeft, "", missionTypeBlock);
            missionTypeButtons[1].UserData = 1;

            missionTypeBlock.Visible = false;

            columnX += columnWidth + 20;

            //gamemode description ------------------------------------------------------------------

            //var modeDescription = new GUITextBlock(
            //    new Rectangle(columnX, 150, (int)(columnWidth * 1.2f), infoFrame.Rect.Height - 150 - 80), 
            //    "", "", Alignment.TopLeft, Alignment.TopLeft, infoFrame, true, GUI.SmallFont);
            //modeDescription.Color = Color.Black * 0.3f;

            //modeList.UserData = modeDescription;

            //columnX += modeDescription.Rect.Width + 20;

            //seed ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 110, 180, 20),
                "Level Seed: ", "", Alignment.Left, Alignment.TopLeft, defaultModeContainer);

            seedBox = new GUITextBox(new Rectangle(columnX, 140, columnWidth / 2, 20),
                Alignment.TopLeft, "", defaultModeContainer);
            seedBox.OnTextChanged = SelectSeed;
            LevelSeed = ToolBox.RandomSeed(8);

            //traitor probability ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 170, 20, 20), "Traitors:", "", defaultModeContainer);

            traitorProbabilityButtons = new GUIButton[2];

            traitorProbabilityButtons[0] = new GUIButton(new Rectangle(columnX, 195, 20, 20), "<", "", defaultModeContainer);
            traitorProbabilityButtons[0].UserData = -1;

            traitorProbabilityText = new GUITextBlock(new Rectangle(columnX + 20, 195, 80, 20), "No", null, null, Alignment.Center, "", defaultModeContainer);

            traitorProbabilityButtons[1] = new GUIButton(new Rectangle(columnX + 100, 195, 20, 20), ">", "", defaultModeContainer);
            traitorProbabilityButtons[1].UserData = 1;


            //automatic restart ------------------------------------------------------------------

            autoRestartBox = new GUITickBox(new Rectangle(columnX, 230, 20, 20), "Automatic restart", Alignment.TopLeft, defaultModeContainer);
            autoRestartBox.OnSelected = ToggleAutoRestart;

            var restartText = new GUITextBlock(new Rectangle(columnX, 255, 20, 20), "", "", defaultModeContainer);
            restartText.Font = GUI.SmallFont;
            restartText.TextGetter = AutoRestartText;

            //server info ------------------------------------------------------------------

            var serverName = new GUITextBox(new Rectangle(0, 0, 200, 20), null, null, Alignment.TopLeft, Alignment.TopLeft, "", defaultModeContainer);
            serverName.TextGetter = GetServerName;
            serverName.Enabled = GameMain.Server != null;
            serverName.OnTextChanged = ChangeServerName;

            serverMessage = new GUITextBox(new Rectangle(0, 30, 360, 70), null, null, Alignment.TopLeft, Alignment.TopLeft, "", defaultModeContainer);
            serverMessage.Wrap = true;
            serverMessage.OnTextChanged = UpdateServerMessage;

            var showLogButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Server Log", Alignment.TopRight, "", infoFrame);
            showLogButton.UserData = "showlog";
            showLogButton.OnClicked = (GUIButton button, object userData) =>
            {
                if (GameMain.Server.ServerLog.LogFrame == null)
                {
                    GameMain.Server.ServerLog.CreateLogFrame();
                }
                else
                {
                    GameMain.Server.ServerLog.LogFrame = null;
                    GUIComponent.KeyboardDispatcher.Subscriber = null;
                }
                return true;
            };
        }

        public override void Deselect()
        {
            textBox.Deselect();
        }

        public override void Select()
        {
            if (GameMain.NetworkMember == null) return;

            GameMain.LightManager.LosEnabled = false;

            textBox.Select();

            textBox.OnEnterPressed = GameMain.NetworkMember.EnterChatMessage;
            textBox.OnTextChanged = GameMain.NetworkMember.TypingChatMessage;

            Character.Controlled = null;
            //GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            subList.Enabled = GameMain.Server != null || GameMain.NetworkMember.Voting.AllowSubVoting ||
                (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectSub));
            shuttleList.Enabled = subList.Enabled;

            modeList.Enabled = 
                GameMain.Server != null || GameMain.NetworkMember.Voting.AllowModeVoting || 
                (GameMain.Client != null && GameMain.Client.HasPermission(ClientPermissions.SelectMode));

            seedBox.Enabled = GameMain.Server != null;
            serverMessage.Enabled = GameMain.Server != null;
            autoRestartBox.Enabled = GameMain.Server != null;

            traitorProbabilityButtons[0].Enabled = GameMain.Server != null;
            traitorProbabilityButtons[1].Enabled = GameMain.Server != null;

            missionTypeButtons[0].Enabled = GameMain.Server != null;
            missionTypeButtons[1].Enabled = GameMain.Server != null;

            ServerName = (GameMain.Server == null) ? ServerName : GameMain.Server.Name;

            infoFrame.RemoveChild(StartButton);
            infoFrame.RemoveChild(infoFrame.children.Find(c => c.UserData as string == "settingsButton"));
            infoFrame.RemoveChild(infoFrame.children.Find(c => c.UserData as string == "spectateButton"));

            InfoFrame.FindChild("showlog").Visible = GameMain.Server != null;
            
            campaignViewButton = new GUIButton(new Rectangle(0, 0, 130, 30), "Campaign view", Alignment.BottomRight, "", defaultModeContainer);
            campaignViewButton.OnClicked = (btn, obj) => { ToggleCampaignView(true); return true; };
            campaignViewButton.Visible = false;

            if (myPlayerFrame.children.Find(c => c.UserData as string == "playyourself") == null)
            {
                var playYourself = new GUITickBox(new Rectangle(0, 0, 20, 20), "Play yourself", Alignment.TopLeft, myPlayerFrame);
                playYourself.Selected = GameMain.NetworkMember.CharacterInfo != null;
                playYourself.OnSelected = TogglePlayYourself;
                playYourself.UserData = "playyourself";

                if (GameMain.NetworkMember.CharacterInfo != null)
                {
                    TogglePlayYourself(playYourself);
                }
            }

            if (IsServer && GameMain.Server != null)
            {
                List<Submarine> subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList();

                int prevSelectedSub = subList.SelectedIndex;
                UpdateSubList(subList, subsToShow);

                int prevSelectedShuttle = shuttleList.SelectedIndex;
                UpdateSubList(shuttleList, subsToShow);

                modeList.OnSelected = VotableClicked;
                modeList.OnSelected = SelectMode;
                subList.OnSelected = VotableClicked;
                subList.OnSelected = SelectSub;

                shuttleList.OnSelected = SelectSub;

                traitorProbabilityButtons[0].OnClicked = ToggleTraitorsEnabled;
                traitorProbabilityButtons[1].OnClicked = ToggleTraitorsEnabled;

                missionTypeButtons[0].OnClicked = ToggleMissionType;
                missionTypeButtons[1].OnClicked = ToggleMissionType;

                StartButton = new GUIButton(new Rectangle(0, 0, 80, 30), "Start", Alignment.BottomRight, "", defaultModeContainer);
                StartButton.OnClicked = GameMain.Server.StartGameClicked;

                GUIButton settingsButton = new GUIButton(new Rectangle(-110, 0, 80, 20), "Settings", Alignment.TopRight, "", infoFrame);
                settingsButton.OnClicked = GameMain.Server.ToggleSettingsFrame;
                settingsButton.UserData = "settingsButton";

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

                if (GameMain.Server.RandomizeSeed) LevelSeed = ToolBox.RandomSeed(8);
                if (GameMain.Server.SubSelectionMode == SelectionMode.Random)
                {
                    var nonShuttles = subList.children.FindAll(c => c.UserData is Submarine && !((Submarine)c.UserData).HasTag(SubmarineTag.Shuttle));
                    subList.Select(nonShuttles[Rand.Range(0, nonShuttles.Count)].UserData);
                }
                if (GameMain.Server.ModeSelectionMode == SelectionMode.Random) modeList.Select(Rand.Range(0, modeList.CountChildren));
            }
            else if (GameMain.Client != null)
            {
                if (GameMain.Client.GameStarted)
                {
                    GUIButton spectateButton = new GUIButton(new Rectangle(0, 0, 80, 30), "Spectate", Alignment.BottomRight, "", infoFrame);
                    spectateButton.OnClicked = GameMain.Client.SpectateClicked;
                    spectateButton.UserData = "spectateButton";
                }

                GameMain.Client.Voting.ResetVotes(GameMain.Client.ConnectedClients);
            }

            GameMain.NetworkMember.EndVoteCount = 0;
            GameMain.NetworkMember.EndVoteMax = 1;

            base.Select();
        }
        
        public void ShowSpectateButton()
        {
            if (GameMain.Client == null) return;

            infoFrame.RemoveChild(infoFrame.children.Find(c => c.UserData as string == "spectateButton"));
            GUIButton spectateButton = new GUIButton(new Rectangle(0, 0, 80, 30), "Spectate", Alignment.BottomRight, "", infoFrame);
            spectateButton.OnClicked = GameMain.Client.SpectateClicked;
            spectateButton.UserData = "spectateButton";
        }

        private void UpdatePlayerFrame(CharacterInfo characterInfo)
        {
            if (myPlayerFrame.children.Count <= 2)
            {
                myPlayerFrame.ClearChildren();
                
                var playYourself = new GUITickBox(new Rectangle(0, 0, 20, 20), "Play yourself", Alignment.TopLeft, myPlayerFrame);
                playYourself.Selected = GameMain.NetworkMember.CharacterInfo != null;
                playYourself.OnSelected = TogglePlayYourself;
                playYourself.UserData = "playyourself";                
                
                GUIButton toggleHead = new GUIButton(new Rectangle(0, 50, 15, 15), "<", "", myPlayerFrame);
                toggleHead.UserData = -1;
                toggleHead.OnClicked = ToggleHead;
                toggleHead = new GUIButton(new Rectangle(60, 50, 15, 15), ">", "", myPlayerFrame);
                toggleHead.UserData = 1;
                toggleHead.OnClicked = ToggleHead;

                new GUITextBlock(new Rectangle(100, 30, 200, 30), "Gender: ", "", myPlayerFrame);

                GUIButton maleButton = new GUIButton(new Rectangle(100, 50, 60, 20), "Male",
                    Alignment.TopLeft, "", myPlayerFrame);
                maleButton.UserData = Gender.Male;
                maleButton.OnClicked += SwitchGender;

                GUIButton femaleButton = new GUIButton(new Rectangle(170, 50, 60, 20), "Female",
                    Alignment.TopLeft, "", myPlayerFrame);
                femaleButton.UserData = Gender.Female;
                femaleButton.OnClicked += SwitchGender;

                new GUITextBlock(new Rectangle(0, 120, 20, 30), "Job preferences:", "", myPlayerFrame);

                jobList = new GUIListBox(new Rectangle(0, 150, 0, 0), "", myPlayerFrame);
                jobList.Enabled = false;

                int i = 1;
                foreach (string jobName in GameMain.Config.JobNamePreferences)
                {
                    JobPrefab job = JobPrefab.List.Find(x => x.Name == jobName);
                    if (job == null)
                    {
                        continue;
                    }

                    GUITextBlock jobText = new GUITextBlock(new Rectangle(0, 0, 0, 20),  i + ". " + job.Name + "    ", 
                        "", Alignment.Left, Alignment.Right, jobList, false,
                        GameMain.GraphicsWidth<1000 ? GUI.SmallFont : GUI.Font);
                    jobText.UserData = job;

                    GUIButton infoButton = new GUIButton(new Rectangle(0, 2, 15, 15), "?", "", jobText);
                    infoButton.UserData = -1;
                    infoButton.OnClicked += ViewJobInfo;

                    GUIButton upButton = new GUIButton(new Rectangle(30, 2, 15, 15), "", "", jobText);
                    //TODO: make GUIImages align correctly when scaled/rotated 
                    //so there's no need to do this ↓
                    new GUIImage(new Rectangle(3, 2, 0, 0), GUI.Arrow, Alignment.Center, upButton).Scale = 0.6f;
                    upButton.UserData = -1;
                    upButton.OnClicked += ChangeJobPreference;

                    GUIButton downButton = new GUIButton(new Rectangle(50, 2, 15, 15), "", "", jobText);
                    var downArrow = new GUIImage(new Rectangle(13, 14, 0, 0), GUI.Arrow, Alignment.Center, downButton);
                    downArrow.Rotation = MathHelper.Pi;
                    downArrow.Scale = 0.6f;

                    downButton.UserData = 1;
                    downButton.OnClicked += ChangeJobPreference;
                }

                UpdateJobPreferences(jobList);

                UpdatePlayerHead(characterInfo);
            }
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
                myPlayerFrame.ClearChildren();
                
                GameMain.NetworkMember.CharacterInfo = null;
                GameMain.NetworkMember.Character = null;

                new GUITextBlock(Rectangle.Empty, "Playing as a spectator", "", Alignment.Center, Alignment.Center, myPlayerFrame, true);

                var playYourself = new GUITickBox(new Rectangle(0, 0, 20, 20), "Play yourself", Alignment.TopLeft, myPlayerFrame);
                playYourself.OnSelected = TogglePlayYourself;
                playYourself.UserData = "playyourself";
            }
            return false;
        }

        public void SetAllowSpectating(bool allowSpectating)
        {
            GUITickBox playYourselfTickBox = myPlayerFrame?.FindChild("playyourself") as GUITickBox;
            if (playYourselfTickBox == null) return;
            
            //show the player config menu if spectating is not allowed
            if (!playYourselfTickBox.Selected && !allowSpectating)
            {
                playYourselfTickBox.Selected = !playYourselfTickBox.Selected;
                TogglePlayYourself(playYourselfTickBox);
            }
            //hide "play yourself" tickbox if spectating is not allowed
            playYourselfTickBox.Visible = allowSpectating;            
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
            if (missionTypeIndex < 0 || missionTypeIndex >= Mission.MissionTypes.Count) return;

            missionTypeBlock.GetChild<GUITextBlock>().Text = Mission.MissionTypes[missionTypeIndex];
            missionTypeBlock.UserData = missionTypeIndex;
        }

        public bool ToggleMissionType(GUIButton button, object userData)
        {
            if (GameMain.Server == null) return false;

            int missionTypeIndex = (int)missionTypeBlock.UserData;
            missionTypeIndex += (int)userData;

            if (missionTypeIndex < 0) missionTypeIndex = Mission.MissionTypes.Count - 1;
            if (missionTypeIndex >= Mission.MissionTypes.Count) missionTypeIndex = 0;

            SetMissionType(missionTypeIndex);

            lastUpdateID++;

            return true;
        }

        public bool ToggleTraitorsEnabled(GUIButton button, object userData)
        {
            ToggleTraitorsEnabled((int)userData);
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

            subList.ClearChildren();

            if (submarines.Count == 0 && GameMain.Server != null)
            {
                DebugConsole.ThrowError("No submarines found!");
            }

            foreach (Submarine sub in submarines)
            {
                AddSubmarine(subList, sub);
            }
        }

        public void AddSubmarine(GUIComponent subList, Submarine sub)
        {
            var subTextBlock = new GUITextBlock(
                new Rectangle(0, 0, 0, 25), ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 65), "ListBoxElement",
                Alignment.TopLeft, Alignment.CenterLeft, subList)
            {
                Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f),
                ToolTip = sub.Description,
                UserData = sub
            };


            var matchingSub = Submarine.SavedSubmarines.Find(s => s.Name == sub.Name && s.MD5Hash.Hash == sub.MD5Hash.Hash);
            if (matchingSub == null) matchingSub = Submarine.SavedSubmarines.Find(s => s.Name == sub.Name);

            if (matchingSub == null)
            {
                subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                subTextBlock.ToolTip = "Submarine not found in your submarine folder";
            }
            else if (matchingSub.MD5Hash.Hash != sub.MD5Hash.Hash)
            {
                subTextBlock.TextColor = new Color(subTextBlock.TextColor, 0.5f);
                subTextBlock.ToolTip = "Your version of the submarine doesn't match the servers version";
            }
            else
            {
                if (subList == shuttleList || subList == shuttleList.ListBox)
                {
                    subTextBlock.TextColor = new Color(subTextBlock.TextColor, sub.HasTag(SubmarineTag.Shuttle) ? 1.0f : 0.6f);
                }
            }

            if (sub.HasTag(SubmarineTag.Shuttle))
            {
                var shuttleText = new GUITextBlock(new Rectangle(0, 0, 0, 25), "Shuttle", "", Alignment.Left, Alignment.CenterY | Alignment.Right, subTextBlock, false, GUI.SmallFont);
                shuttleText.TextColor = subTextBlock.TextColor * 0.8f;
                shuttleText.ToolTip = subTextBlock.ToolTip;
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
                        GameMain.Client.RequestSelectSub(component.Parent.children.IndexOf(component));
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
                        GameMain.Client.RequestSelectMode(component.Parent.children.IndexOf(component));
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
            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(0, 0, playerList.Rect.Width - 20, 25), name,
                 "", Alignment.Left, Alignment.Left,
                playerList);

            textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
            textBlock.UserData = name;

            if (GameMain.Server != null) lastUpdateID++;
        }

        public void RemovePlayer(string name)
        {
            GUIComponent child = playerList.children.Find(c => c.UserData as string == name);

            if (child != null) playerList.RemoveChild(child);

            if (GameMain.Server != null) lastUpdateID++;
        }

        private bool SelectPlayer(GUIComponent component, object obj)
        {
            if (GameMain.Client != null)
            {
                if (!GameMain.Client.HasPermission(ClientPermissions.Ban) &&
                    !GameMain.Client.HasPermission(ClientPermissions.Kick))
                {
                    return false;
                }
            }

            playerFrame = new GUIFrame(new Rectangle(0, 0, 0, 0), Color.Black * 0.6f);

            var playerFrameInner = new GUIFrame(new Rectangle(0, 0, 300, 280), null, Alignment.Center, "", playerFrame);
            playerFrameInner.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0, 0, 200, 20), component.UserData.ToString(),
                "", Alignment.TopLeft, Alignment.TopLeft,
                playerFrameInner, false, GUI.LargeFont);

            if (GameMain.Server != null)
            {
                var selectedClient = GameMain.Server.ConnectedClients.Find(c => c.Name == component.UserData.ToString());

                new GUITextBlock(new Rectangle(0, 25, 150, 15), selectedClient.Connection.RemoteEndPoint.Address.ToString(), "", playerFrameInner);

                var permissionsBox = new GUIFrame(new Rectangle(0, 60, 0, 90), null, playerFrameInner);
                permissionsBox.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                permissionsBox.UserData = selectedClient;

                new GUITextBlock(new Rectangle(0, 0, 0, 15), "Permissions:", "", permissionsBox);
                int x = 0, y = 0;
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    if (permission == ClientPermissions.None) continue;

                    FieldInfo fi = typeof(ClientPermissions).GetField(permission.ToString());
                    DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                    string permissionStr = attributes.Length > 0 ? attributes[0].Description : permission.ToString();

                    var permissionTick = new GUITickBox(new Rectangle(x, y + 25, 15, 15), permissionStr, Alignment.TopLeft, GUI.SmallFont, permissionsBox);
                    permissionTick.UserData = permission;
                    permissionTick.Selected = selectedClient.HasPermission(permission);

                    permissionTick.OnSelected = (tickBox) =>
                    {
                        var client = tickBox.Parent.UserData as Client;
                        if (client == null) return false;

                        var thisPermission = (ClientPermissions)tickBox.UserData;

                        if (tickBox.Selected)
                            client.GivePermission(thisPermission);
                        else
                            client.RemovePermission(thisPermission);

                        GameMain.Server.UpdateClientPermissions(client);

                        return true;
                    };


                    y += 20;
                    if (y >= permissionsBox.Rect.Height - 40)
                    {
                        y = 0;
                        x += 100;
                    }
                }
            }

            if (GameMain.Server != null || GameMain.Client.HasPermission(ClientPermissions.Kick))
            {
                var kickButton = new GUIButton(new Rectangle(0, -50, 100, 20), "Kick", Alignment.BottomLeft, "", playerFrameInner);
                kickButton.UserData = obj;
                kickButton.OnClicked += KickPlayer;
                kickButton.OnClicked += ClosePlayerFrame;
            }

            if (GameMain.Server != null || GameMain.Client.HasPermission(ClientPermissions.Ban))
            {
                var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomLeft, "", playerFrameInner);
                banButton.UserData = obj;
                banButton.OnClicked += BanPlayer;
                banButton.OnClicked += ClosePlayerFrame;

                var rangebanButton = new GUIButton(new Rectangle(0, -25, 100, 20), "Ban range", Alignment.BottomLeft, "", playerFrameInner);
                rangebanButton.UserData = obj;
                rangebanButton.OnClicked += BanPlayerRange;
                rangebanButton.OnClicked += ClosePlayerFrame;
            }

            var closeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Close", Alignment.BottomRight, "", playerFrameInner);
            closeButton.OnClicked = ClosePlayerFrame;

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

            if (jobInfoFrame != null)
            {
                jobInfoFrame.AddToGUIUpdateList();
            }
            else if (playerFrame != null)
            {
                playerFrame.AddToGUIUpdateList();
            }
            else
            {
                menu.AddToGUIUpdateList();
            }
        }
        
        public List<Submarine> GetSubList()
        {
            List<Submarine> subs = new List<Submarine>();
            foreach (GUIComponent component in subList.children)
            {
                if (component.UserData is Submarine) subs.Add((Submarine)component.UserData);
            }

            return subs;
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            if (jobInfoFrame != null)
            {
                jobInfoFrame.Update((float)deltaTime);
            }
            else if (playerFrame != null)
            {
                playerFrame.Update((float)deltaTime);
            }
            else
            {
                menu.Update((float)deltaTime);
            }

            if (campaignContainer.Visible && campaignUI != null)
            {
                //campaignContainer.Update((float)deltaTime);
                campaignUI.Update((float)deltaTime);
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
                spriteBatch.Draw(backgroundSprite.Texture, Vector2.Zero, null, Color.White, 0.0f, Vector2.Zero,
                    Math.Max((float)GameMain.GraphicsWidth / backgroundSprite.SourceRect.Width, (float)GameMain.GraphicsHeight / backgroundSprite.SourceRect.Height),
                    SpriteEffects.None, 0.0f);
            }

            menu.Draw(spriteBatch);

            if (jobInfoFrame != null) jobInfoFrame.Draw(spriteBatch);
            
            if (campaignContainer.Visible && campaignUI != null)
            {
                campaignUI.Draw(spriteBatch);
            }

            if (playerFrame != null) playerFrame.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        public void NewChatMessage(ChatMessage message)
        {
            float prevSize = chatBox.BarSize;

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[1]);
            }

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, chatBox.Rect.Width - 20, 0),
                message.TextWithSender,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, message.Color,
                Alignment.Left, Alignment.TopLeft, "", null, true, GUI.SmallFont);
            msg.UserData = message;
            msg.CanBeFocused = false;

            msg.Padding = new Vector4(20, 0, 0, 0);
            chatBox.AddChild(msg);

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;
        }

        private void UpdatePlayerHead(CharacterInfo characterInfo)
        {
            GUIComponent existing = myPlayerFrame.FindChild("playerhead");
            if (existing != null) myPlayerFrame.RemoveChild(existing);

            GUIImage image = new GUIImage(new Rectangle(20, 40, 30, 30), characterInfo.HeadSprite, Alignment.TopLeft, myPlayerFrame);
            image.UserData = "playerhead";
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
            if (modeIndex < 0 || modeIndex >= modeList.children.Count || modeList.SelectedIndex == modeIndex) return;

            if (((GameModePreset)modeList.children[modeIndex].UserData).Name == "Campaign")
            {
                if (GameMain.Server != null)
                {
                    MultiplayerCampaign.StartCampaignSetup();
                    return;
                }
            }
            else
            {
                ToggleCampaignMode(false);
            }

            modeList.Select(modeIndex, true);            
            missionTypeBlock.Visible = SelectedMode != null && SelectedMode.Name == "Mission";
        }

        private bool SelectMode(GUIComponent component, object obj)
        {
            if (GameMain.NetworkMember == null || obj == modeList.SelectedData) return false;
            
            GameModePreset modePreset = obj as GameModePreset;
            if (modePreset == null) return false;
            
            missionTypeBlock.Visible = modePreset.Name == "Mission";

            if (modePreset.Name == "Campaign")
            {
                //campaign selected and the campaign view has not been set up yet
                // -> don't select the mode yet and start campaign setup
                if (GameMain.Server != null && !campaignContainer.Visible)
                {
                    MultiplayerCampaign.StartCampaignSetup();
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

            subList.Enabled = !enabled;
            shuttleList.Enabled = !enabled;
            seedBox.Enabled = !enabled;

            if (campaignViewButton != null) campaignViewButton.Visible = enabled;
            if (StartButton != null) StartButton.Visible = !enabled;            

            if (enabled)
            {
                if (campaignUI == null || campaignUI.Campaign != GameMain.GameSession.GameMode)
                {
                    campaignContainer.ClearChildren();                    

                    campaignUI = new CampaignUI(GameMain.GameSession.GameMode as CampaignMode, campaignContainer);
                    campaignUI.StartRound = () => { GameMain.Server.StartGame(); };

                    var backButton = new GUIButton(new Rectangle(0, -20, 100, 30), "Back", "", campaignContainer);
                    backButton.OnClicked += (btn, obj) => { ToggleCampaignView(false); return true; };

                    int buttonX = backButton.Rect.Width + 50;
                    List<CampaignUI.Tab> tabTypes = new List<CampaignUI.Tab>() { CampaignUI.Tab.Map, CampaignUI.Tab.Store };
                    foreach (CampaignUI.Tab tab in tabTypes)
                    {
                        var tabButton = new GUIButton(new Rectangle(buttonX, -10, 100, 20), tab.ToString(), "", campaignContainer);
                        tabButton.OnClicked += (btn, obj) =>
                        {
                            campaignUI.SelectTab(tab);
                            return true;
                        };
                        buttonX += 110;
                    }

                    var moneyText = new GUITextBlock(new Rectangle(120,0,200,20), "Money", "", Alignment.BottomLeft, Alignment.TopLeft, campaignContainer);
                    moneyText.TextGetter = campaignUI.GetMoney;

                    var restartText = new GUITextBlock(new Rectangle(-250, -20, 100, 30), "", "", Alignment.BottomRight, Alignment.BottomLeft, campaignContainer);
                    restartText.Font = GUI.SmallFont;
                    restartText.TextGetter = AutoRestartText;
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
            GUIComponent jobText = button.Parent;

            JobPrefab jobPrefab = jobText.UserData as JobPrefab;
            if (jobPrefab == null) return false;

            jobInfoFrame = jobPrefab.CreateInfoFrame();
            GUIButton closeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Close", Alignment.BottomRight, "", jobInfoFrame.children[0]);
            closeButton.OnClicked = CloseJobInfo;
            return true;
        }

        private bool CloseJobInfo(GUIButton button, object obj)
        {
            jobInfoFrame = null;
            return true;
        }

        private bool ChangeJobPreference(GUIButton button, object obj)
        {
            GUIComponent jobText = button.Parent;
            GUIListBox jobList = jobText.Parent as GUIListBox;

            int index = jobList.children.IndexOf(jobText);
            int newIndex = index + (int)obj;
            if (newIndex < 0 || newIndex > jobList.children.Count - 1) return false;

            GUIComponent temp = jobList.children[newIndex];
            jobList.children[newIndex] = jobText;
            jobList.children[index] = temp;

            UpdateJobPreferences(jobList);

            return true;
        }

        private void UpdateJobPreferences(GUIListBox listBox)
        {
            listBox.Deselect();
            List<string> jobNamePreferences = new List<string>();

            for (int i = 0; i < listBox.children.Count; i++)
            {
                float a = (float)(i - 1) / 3.0f;
                a = Math.Min(a, 3);
                Color color = new Color(1.0f - a, (1.0f - a) * 0.6f, 0.0f, 0.3f);

                listBox.children[i].Color = color;
                listBox.children[i].HoverColor = color;
                listBox.children[i].SelectedColor = color;
                
                (listBox.children[i] as GUITextBlock).Text = (i+1) + ". " + (listBox.children[i].UserData as JobPrefab).Name;

                jobNamePreferences.Add((listBox.children[i].UserData as JobPrefab).Name);
            }

            GameMain.Config.JobNamePreferences = jobNamePreferences;
        }

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

            var matchingListSub = subList.children.Find(c => c.UserData == sub) as GUITextBlock;
            if (matchingListSub != null)
            {
                subList.OnSelected -= VotableClicked;
                subList.Select(subList.children.IndexOf(matchingListSub), true);
                subList.OnSelected += VotableClicked;
            }

            if (sub == null || sub.MD5Hash.Hash != md5Hash)
            {
                string errorMsg = "";
                if (sub == null)
                {
                    errorMsg = "Submarine \"" + subName + "\" was selected by the server. Matching file not found in your submarine folder. ";
                }
                else if (sub.MD5Hash.Hash == null)
                {
                    errorMsg = "Couldn't load submarine \"" + subName + "\". The file may be corrupted. ";

                    if (matchingListSub != null) matchingListSub.TextColor = Color.Red;
                }
                else
                {
                    errorMsg = "Your version of the submarine file \"" + sub.Name + "\" doesn't match the server's version!\n"
                    + "Your MD5 hash: " + sub.MD5Hash.ShortHash + "\n"
                    + "Server's MD5 hash: " + Md5Hash.GetShortHash(md5Hash) + "\n";
                }

                errorMsg += "Do you want to download the file from the server host?";

                //already showing a message about the same sub
                if (GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "request" + subName))
                {
                    return false;
                }

                var requestFileBox = new GUIMessageBox("Submarine not found!", errorMsg, new string[] { "Yes", "No" }, 400, 300);
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
