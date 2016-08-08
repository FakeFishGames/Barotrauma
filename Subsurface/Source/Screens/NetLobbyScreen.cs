using System;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Factories;
using FarseerPhysics.Dynamics;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class NetLobbyScreen : Screen
    {
        private GUIFrame menu;
        private GUIFrame infoFrame;
        private GUIListBox playerList;

        private GUIListBox subList, modeList, chatBox;

        private GUIButton[] traitorProbabilityButtons;
        private GUITextBlock traitorProbabilityText;

        private GUIButton[] missionTypeButtons;
        private GUIComponent missionTypeBlock;
        
        private GUIListBox jobList;

        private GUITextBox textBox, seedBox;

        private GUIFrame myPlayerFrame;

        private GUIFrame jobInfoFrame;

        private GUIFrame playerFrame;

        private GUITickBox autoRestartBox;

        private GUIDropDown shuttleList;

        public bool IsServer;
        public string ServerName;

        const float NetworkUpdateInterval = 1.0f;
        private float networkUpdateTimer;
        private bool valueChanged;

        private Sprite backgroundSprite;

        private GUITextBox serverMessage;

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

        public GUIListBox PlayerList
        {
            get { return playerList; }
        }

        public GUIButton StartButton
        {
            get;
            private set;
        }

        public GUIFrame InfoFrame
        {
            get { return infoFrame; }
        }

        public Submarine SelectedSub
        {
            get { return subList.SelectedData as Submarine; }
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

        //for guitextblock delegate
        public string GetServerName()
        {
            return ServerName;
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

        private string levelSeed;

        public string LevelSeed
        {
            get
            {
                return levelSeed;
            }
            private set
            {
                if (levelSeed == value) return;

                levelSeed = value;
                backgroundSprite = LocationType.Random(levelSeed).Background;
                seedBox.Text = levelSeed;
            }
        }

        private float autoRestartTimer;

        public string AutoRestartText()
        {
            if (GameMain.Server != null)
            {
                if (!GameMain.Server.AutoRestart) return "";
                return "Restarting in " + ToolBox.SecondsToReadableTime(GameMain.Server.AutoRestartTimer);
            }
            if (autoRestartTimer == 0.0f) return "";            
            return "Restarting in " + ToolBox.SecondsToReadableTime(autoRestartTimer);
        }
               
        public NetLobbyScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 80, 1500);
            int height = Math.Min(GameMain.GraphicsHeight - 80, 800);

            Rectangle panelRect = new Rectangle(0,0,width,height);

            menu = new GUIFrame(panelRect, Color.Transparent, Alignment.Center);
            //menu.Padding = GUI.style.smallPadding;

            //server info panel ------------------------------------------------------------

            infoFrame = new GUIFrame(new Rectangle(0, 0, (int)(panelRect.Width * 0.7f), (int)(panelRect.Height * 0.6f)), GUI.Style, menu);
            infoFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            
            //chatbox ----------------------------------------------------------------------
            GUIFrame chatFrame = new GUIFrame(
                new Rectangle(0, (int)(panelRect.Height * 0.6f + 20),
                    (int)(panelRect.Width * 0.7f),
                    (int)(panelRect.Height * 0.4f - 20)),
                GUI.Style, menu);
            chatFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 40.0f);

            chatBox = new GUIListBox(new Rectangle(0,0,0,chatFrame.Rect.Height-80), Color.White, GUI.Style, chatFrame);            
            textBox = new GUITextBox(new Rectangle(0, 25, 0, 25), Alignment.Bottom, GUI.Style, chatFrame);
            textBox.Font = GUI.SmallFont;

            //player info panel ------------------------------------------------------------

            myPlayerFrame = new GUIFrame(
                new Rectangle((int)(panelRect.Width * 0.7f + 20), 0,
                    (int)(panelRect.Width * 0.3f - 20), (int)(panelRect.Height * 0.6f)),
                GUI.Style, menu);
            myPlayerFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            //player list ------------------------------------------------------------------

            GUIFrame playerListFrame = new GUIFrame(
                new Rectangle((int)(panelRect.Width * 0.7f + 20), (int)(panelRect.Height * 0.6f + 20),
                    (int)(panelRect.Width * 0.3f - 20), (int)(panelRect.Height * 0.4f - 20)),
                GUI.Style, menu);

            playerListFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 40.0f);

            playerList = new GUIListBox(new Rectangle(0,0,0,0), null, GUI.Style, playerListFrame);
            playerList.OnSelected = SelectPlayer;

            //submarine list ------------------------------------------------------------------

            int columnWidth = infoFrame.Rect.Width / 3 - 5;
            int columnX = 0;

            new GUITextBlock(new Rectangle(columnX, 120, columnWidth, 30), "Submarine:", GUI.Style, infoFrame);
            subList = new GUIListBox(new Rectangle(columnX, 150, columnWidth, infoFrame.Rect.Height - 150 - 80), Color.White, GUI.Style, infoFrame);
            subList.OnSelected = VotableClicked;

            var voteText = new GUITextBlock(new Rectangle(columnX, 120, columnWidth, 30), "Votes: ", GUI.Style, Alignment.TopLeft, Alignment.TopRight, infoFrame);
            voteText.UserData = "subvotes";
            voteText.Visible = false;

            //UpdateSubList(Submarine.SavedSubmarines);

            columnX += columnWidth + 20;


            //respawn shuttle ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 120, 20, 20), "Respawn shuttle:", GUI.Style, infoFrame);
            shuttleList = new GUIDropDown(new Rectangle(columnX, 150, 200, 20), "", GUI.Style, infoFrame);
            

            //gamemode ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 180, 0, 30), "Game mode: ", GUI.Style, infoFrame);
            modeList = new GUIListBox(new Rectangle(columnX, 200, columnWidth, infoFrame.Rect.Height - 300), GUI.Style, infoFrame);
            modeList.OnSelected = VotableClicked;
            
            voteText = new GUITextBlock(new Rectangle(columnX, 120, columnWidth, 30), "Votes: ", GUI.Style, Alignment.TopLeft, Alignment.TopRight, infoFrame);
            voteText.UserData = "modevotes";
            voteText.Visible = false;

            foreach (GameModePreset mode in GameModePreset.list)
            {
                if (mode.IsSinglePlayer) continue;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    mode.Name, GUI.Style,
                    Alignment.Left, Alignment.Left,
                    modeList);
                textBlock.ToolTip = mode.Description;
                textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                textBlock.UserData = mode;
            }
                        
            //mission type ------------------------------------------------------------------

            missionTypeBlock = new GUITextBlock(new Rectangle(columnX, 0, 300, 20), "Mission type:", GUI.Style, Alignment.BottomLeft, Alignment.TopLeft, infoFrame);
            missionTypeBlock.UserData = 0;
                       
            missionTypeButtons = new GUIButton[2];

            missionTypeButtons[0] = new GUIButton(new Rectangle(100, 0, 20, 20), "<", Alignment.BottomLeft, GUI.Style, missionTypeBlock);
            missionTypeButtons[0].UserData = -1;

            new GUITextBlock(new Rectangle(120, 0, 80, 20), "Random", GUI.Style, Alignment.BottomLeft, Alignment.TopCenter, missionTypeBlock).UserData = 0;

            missionTypeButtons[1] = new GUIButton(new Rectangle(200, 0, 20, 20), ">", Alignment.BottomLeft, GUI.Style, missionTypeBlock);
            missionTypeButtons[1].UserData = 1;
            
            missionTypeBlock.Visible = false;

            columnX += columnWidth + 20;

            //gamemode description ------------------------------------------------------------------
            
            //var modeDescription = new GUITextBlock(
            //    new Rectangle(columnX, 150, (int)(columnWidth * 1.2f), infoFrame.Rect.Height - 150 - 80), 
            //    "", GUI.Style, Alignment.TopLeft, Alignment.TopLeft, infoFrame, true, GUI.SmallFont);
            //modeDescription.Color = Color.Black * 0.3f;

            //modeList.UserData = modeDescription;

            //columnX += modeDescription.Rect.Width + 20;

            //seed ------------------------------------------------------------------
            
            new GUITextBlock(new Rectangle(columnX, 120, 180, 20),
                "Level Seed: ", GUI.Style, Alignment.Left, Alignment.TopLeft, infoFrame);

            seedBox = new GUITextBox(new Rectangle(columnX, 150, columnWidth/2, 20),
                Alignment.TopLeft, GUI.Style, infoFrame);
            seedBox.OnTextChanged = SelectSeed;
            LevelSeed = ToolBox.RandomSeed(8);

            //traitor probability ------------------------------------------------------------------

            new GUITextBlock(new Rectangle(columnX, 180, 20, 20), "Traitors:", GUI.Style, infoFrame);

            traitorProbabilityButtons = new GUIButton[2];

            traitorProbabilityButtons[0] = new GUIButton(new Rectangle(columnX, 205, 20, 20), "<", GUI.Style, infoFrame);
            traitorProbabilityButtons[0].UserData = -1;

            traitorProbabilityText = new GUITextBlock(new Rectangle(columnX+20, 205, 80, 20), "No", null,null, Alignment.TopCenter, GUI.Style, infoFrame);

            traitorProbabilityButtons[1] = new GUIButton(new Rectangle(columnX + 100, 205, 20, 20), ">", GUI.Style, infoFrame);
            traitorProbabilityButtons[1].UserData = 1;


            //automatic restart ------------------------------------------------------------------

            autoRestartBox = new GUITickBox(new Rectangle(columnX, 240, 20, 20), "Automatic restart", Alignment.TopLeft, infoFrame);
            autoRestartBox.OnSelected = ToggleAutoRestart;

            var restartText = new GUITextBlock(new Rectangle(columnX, 265, 20, 20), "", GUI.Style, infoFrame);
            restartText.Font = GUI.SmallFont;
            restartText.TextGetter = AutoRestartText;

            //server info ------------------------------------------------------------------
            
            var serverName = new GUITextBox(new Rectangle(0, 0, 200, 20), null, null, Alignment.TopLeft, Alignment.TopLeft, GUI.Style, infoFrame);
            serverName.TextGetter = GetServerName;
            serverName.Enabled = GameMain.Server != null;
            serverName.OnTextChanged = ChangeServerName;

            serverMessage = new GUITextBox(new Rectangle(0, 30, 360, 70), null, null, Alignment.TopLeft, Alignment.TopLeft, GUI.Style, infoFrame);
            serverMessage.Wrap = true;
            serverMessage.OnTextChanged = UpdateServerMessage;

        }

        public override void Deselect()
        {
            textBox.Deselect();
        }

        public override void Select()
        {
            GameMain.LightManager.LosEnabled = false;
            
            textBox.Select();
            
            textBox.OnEnterPressed = GameMain.NetworkMember.EnterChatMessage;
            textBox.OnTextChanged = GameMain.NetworkMember.TypingChatMessage;

            Character.Controlled = null;
            //GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            subList.Enabled         = GameMain.Server != null || GameMain.NetworkMember.Voting.AllowSubVoting;
            shuttleList.Enabled     = subList.Enabled;
            playerList.Enabled      = GameMain.Server != null;
            modeList.Enabled        = GameMain.Server != null || GameMain.NetworkMember.Voting.AllowModeVoting;                  
            seedBox.Enabled         = GameMain.Server != null;                       
            serverMessage.Enabled   = GameMain.Server != null;
            autoRestartBox.Enabled  = GameMain.Server != null;

            traitorProbabilityButtons[0].Enabled = GameMain.Server != null;
            traitorProbabilityButtons[1].Enabled = GameMain.Server != null;

            missionTypeButtons[0].Enabled = GameMain.Server != null;
            missionTypeButtons[1].Enabled = GameMain.Server != null;

            ServerName = (GameMain.Server==null) ? "Server" : GameMain.Server.Name;
            
            infoFrame.RemoveChild(StartButton);
            infoFrame.RemoveChild(infoFrame.children.Find(c => c.UserData as string == "settingsButton"));
            infoFrame.RemoveChild(infoFrame.children.Find(c => c.UserData as string == "spectateButton"));

            //playerList.Parent.RemoveChild(playerList.Parent.children.Find(c => c.UserData as string == "banListButton"));

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

                StartButton = new GUIButton(new Rectangle(0, 0, 80, 30), "Start", Alignment.BottomRight, GUI.Style, infoFrame);
                StartButton.OnClicked = GameMain.Server.StartGameClicked;

                GUIButton settingsButton = new GUIButton(new Rectangle(-100, 0, 80, 30), "Settings", Alignment.BottomRight, GUI.Style, infoFrame);
                settingsButton.OnClicked = GameMain.Server.ToggleSettingsFrame;
                settingsButton.UserData = "settingsButton";
                
                if (subList.Selected == null) subList.Select(Math.Max(0, prevSelectedSub));
                if (shuttleList.Selected == null)
                {
                    var shuttles = shuttleList.GetChildren().FindAll(c => c.UserData is Submarine && ((Submarine)c.UserData).HasTag(SubmarineTag.Shuttle));
                    if (prevSelectedShuttle==-1 && shuttles.Any())
                    {
                        shuttleList.SelectItem(shuttles[0].UserData);
                    }
                    else
                    {
                        shuttleList.Select(Math.Max(0, prevSelectedShuttle));
                    }
                }

                if (GameModePreset.list.Count > 0 && modeList.Selected == null) modeList.Select(0);

                if (myPlayerFrame.children.Find(c => c.UserData as string == "playyourself") == null)
                {
                    var playYourself = new GUITickBox(new Rectangle(0, -10, 20, 20), "Play yourself", Alignment.TopLeft, myPlayerFrame);
                    playYourself.Selected = GameMain.Server.CharacterInfo != null;
                    playYourself.OnSelected = TogglePlayYourself;
                    playYourself.UserData = "playyourself";
                }

                GameMain.Server.Voting.ResetVotes(GameMain.Server.ConnectedClients);

                if (GameMain.Server.RandomizeSeed) LevelSeed = ToolBox.RandomSeed(8);
                if (GameMain.Server.SubSelectionMode == SelectionMode.Random) subList.Select(Rand.Range(0,subList.CountChildren));
                if (GameMain.Server.ModeSelectionMode == SelectionMode.Random) modeList.Select(Rand.Range(0, modeList.CountChildren));
            }
            else if (GameMain.Client != null)
            {
                if (GameMain.Client.GameStarted)
                {
                    GUIButton spectateButton = new GUIButton(new Rectangle(0, 0, 80, 30), "Spectate", Alignment.BottomRight, GUI.Style, infoFrame);
                    spectateButton.OnClicked = GameMain.Client.SpectateClicked;
                    spectateButton.UserData = "spectateButton";
                }

                GameMain.Client.Voting.ResetVotes(GameMain.Client.ConnectedClients);

                UpdatePlayerFrame(GameMain.Client.CharacterInfo);
            }

            GameMain.NetworkMember.EndVoteCount = 0;
            GameMain.NetworkMember.EndVoteMax = 1;

            base.Select();
        }

        private void UpdatePlayerFrame(CharacterInfo characterInfo)
        {
            if (myPlayerFrame.children.Count <= 1)
            {
                myPlayerFrame.ClearChildren();

                if (IsServer && GameMain.Server != null)
                {
                    var playYourself = new GUITickBox(new Rectangle(0, -10, 20, 20), "Play yourself", Alignment.TopLeft, myPlayerFrame);
                    playYourself.Selected = GameMain.Server.CharacterInfo != null;
                    playYourself.OnSelected = TogglePlayYourself;
                    playYourself.UserData = "playyourself";
                }

                //new GUITextBlock(new Rectangle(100, 30, 200, 30), "Name: ", GUI.Style, myPlayerFrame);

                //GUITextBox playerName = new GUITextBox(new Rectangle(100, 55, 0, 20), Alignment.TopLeft, GUI.Style, myPlayerFrame);
                //playerName.Text = characterInfo.Name;
                //playerName.OnEnterPressed += ChangeCharacterName;

                GUIButton toggleHead = new GUIButton(new Rectangle(0, 50, 15, 15), "<", GUI.Style, myPlayerFrame);
                toggleHead.UserData = -1;
                toggleHead.OnClicked = ToggleHead;
                toggleHead = new GUIButton(new Rectangle(60, 50, 15, 15), ">", GUI.Style, myPlayerFrame);
                toggleHead.UserData = 1;
                toggleHead.OnClicked = ToggleHead;

                new GUITextBlock(new Rectangle(100, 30, 200, 30), "Gender: ", GUI.Style, myPlayerFrame);

                GUIButton maleButton = new GUIButton(new Rectangle(100, 50, 60, 20), "Male",
                    Alignment.TopLeft, GUI.Style, myPlayerFrame);
                maleButton.UserData = Gender.Male;
                maleButton.OnClicked += SwitchGender;

                GUIButton femaleButton = new GUIButton(new Rectangle(170, 50, 60, 20), "Female",
                    Alignment.TopLeft, GUI.Style, myPlayerFrame);
                femaleButton.UserData = Gender.Female;
                femaleButton.OnClicked += SwitchGender;

                new GUITextBlock(new Rectangle(0, 120, 20, 30), "Job preferences:", GUI.Style, myPlayerFrame);

                jobList = new GUIListBox(new Rectangle(0, 150, 0, 0), GUI.Style, myPlayerFrame);
                jobList.Enabled = false;


                int i = 1;
                foreach (JobPrefab job in JobPrefab.List)
                {
                    GUITextBlock jobText = new GUITextBlock(new Rectangle(0, 0, 0, 20),  i + ". " + job.Name+"    ", 
                        GUI.Style,Alignment.Left, Alignment.Right, jobList, false,
                        GameMain.GraphicsWidth<1000 ? GUI.SmallFont : GUI.Font);
                    jobText.UserData = job;

                    GUIButton infoButton = new GUIButton(new Rectangle(0, 2, 15, 15), "?", GUI.Style, jobText);
                    infoButton.UserData = -1;
                    infoButton.OnClicked += ViewJobInfo;

                    GUIButton upButton = new GUIButton(new Rectangle(30, 2, 15, 15), "^", GUI.Style, jobText);
                    upButton.UserData = -1;
                    upButton.OnClicked += ChangeJobPreference;

                    GUIButton downButton = new GUIButton(new Rectangle(50, 2, 15, 15), "˅", GUI.Style, jobText);
                    downButton.UserData = 1;
                    downButton.OnClicked += ChangeJobPreference;
                }

                UpdateJobPreferences(jobList);

                UpdatePreviewPlayer(characterInfo);
            }
        }

        private bool TogglePlayYourself(GUITickBox tickBox)
        {
            if (tickBox.Selected)
            {
                GameMain.Server.CharacterInfo = new CharacterInfo(Character.HumanConfigFile, GameMain.Server.Name);
                UpdatePlayerFrame(GameMain.Server.CharacterInfo);
            }
            else
            {
                myPlayerFrame.ClearChildren();

                if (IsServer && GameMain.Server != null)
                {
                    GameMain.Server.CharacterInfo = null;
                    GameMain.Server.Character = null;

                    var playYourself = new GUITickBox(new Rectangle(0, 0, 20, 20), "Play yourself", Alignment.TopLeft, myPlayerFrame);
                    playYourself.OnSelected = TogglePlayYourself;
                    playYourself.UserData = "playyourself";
                }
            }
            return false;
        }

        private bool ToggleAutoRestart(GUITickBox tickBox)
        {
            if (GameMain.Server == null) return false;
            
            GameMain.Server.AutoRestart = tickBox.Selected;
            
            valueChanged = true;
                       
            return true;
        }

        private void SetMissionType(int missionTypeIndex)
        {
            if (missionTypeIndex < 0 || missionTypeIndex >= Mission.MissionTypes.Count) return;

            missionTypeBlock.GetChild<GUITextBlock>().Text = Mission.MissionTypes[missionTypeIndex];
            missionTypeBlock.UserData = missionTypeIndex;

            valueChanged = true;
        }

        public bool ToggleMissionType(GUIButton button, object userData)
        {
            int missionTypeIndex = (int)missionTypeBlock.UserData;
            missionTypeIndex += (int)userData;

            if (missionTypeIndex<0) missionTypeIndex = Mission.MissionTypes.Count-1;
            if (missionTypeIndex>=Mission.MissionTypes.Count) missionTypeIndex=0;

            SetMissionType(missionTypeIndex);

            valueChanged = true;

            return true;
        }

        public bool ToggleTraitorsEnabled(GUIButton button, object userData)
        {
            if (GameMain.Server == null) return false;

            int dir = (int)userData;

            int index = (int)GameMain.Server.TraitorsEnabled + dir;
            if (index < 0) index = 2;
            if (index > 2) index = 0;

            SetTraitorsEnabled((YesNoMaybe)index);
            
            valueChanged = true;

            return true;
        }

        private void SetTraitorsEnabled(YesNoMaybe enabled)
        {

            if (GameMain.Server != null) GameMain.Server.TraitorsEnabled = enabled;
            (traitorProbabilityText as GUITextBlock).Text = enabled.ToString();
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

        private bool SelectSub(GUIComponent component, object obj)
        {
            valueChanged = true;

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

            if (submarines.Count == 0)
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
                new Rectangle(0, 0, 0, 25), sub.Name, GUI.Style,
                Alignment.Left, Alignment.Left, subList)
            {
                Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f),
                ToolTip = sub.Description,
                UserData = sub
            };

            var matchingSub = Submarine.SavedSubmarines.Find(s => s.Name == sub.Name);
            if (matchingSub == null)
            {
                subTextBlock.TextColor = Color.Gray;
                subTextBlock.ToolTip = "Submarine not found in your submarine folder";
            }
            else if (matchingSub.MD5Hash.Hash != sub.MD5Hash.Hash)
            {
                subTextBlock.TextColor = Color.LightGray;
                subTextBlock.ToolTip = "Your version of the submarine doesn't match the servers version";
            }
            else
            {
                if (subList == shuttleList || subList == shuttleList.ListBox)
                {
                    subTextBlock.TextColor = sub.HasTag(SubmarineTag.Shuttle) ? Color.White : Color.DarkGray;
                }
            }
        }
        
        
        public bool VotableClicked(GUIComponent component, object userData)
        {
            if (GameMain.Client == null) return false;

            VoteType voteType = VoteType.Unknown;
            if (component.Parent == GameMain.NetLobbyScreen.SubList)
            {
                if (!GameMain.Client.Voting.AllowSubVoting) return false;
                voteType = VoteType.Sub;
            }
            else if (component.Parent == GameMain.NetLobbyScreen.ModeList)
            {
                if (!GameMain.Client.Voting.AllowModeVoting) return false;
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
            valueChanged = true;

            return true;
        }

        public bool UpdateServerMessage(GUITextBox textBox, string text)
        {
            if (GameMain.Server == null) return false;
            valueChanged = true;

            return true;
        }

        public void AddPlayer(string name)
        {
            GUITextBlock textBlock = new GUITextBlock(
                new Rectangle(0, 0, playerList.Rect.Width-20, 25), name, 
                 GUI.Style, Alignment.Left, Alignment.Left,
                playerList);

            textBlock.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
            textBlock.UserData = name;          
        }

        public void RemovePlayer(string name)
        {
            GUIComponent child = playerList.children.Find(c => c.UserData as string == name);

            if (child != null) playerList.RemoveChild(child);
        }

        private bool SelectPlayer(GUIComponent component, object obj)
        {
            playerFrame = new GUIFrame(new Rectangle(0, 0, 0, 0), Color.Black * 0.3f);

            var playerFrameInner = new GUIFrame(new Rectangle(0,0,300,150), null, Alignment.Center, GUI.Style, playerFrame);
            playerFrameInner.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            new GUITextBlock(new Rectangle(0,0,200,20), component.UserData.ToString(), 
                GUI.Style, Alignment.TopLeft, Alignment.TopLeft,
                playerFrameInner, false, GUI.LargeFont);

            var kickButton = new GUIButton(new Rectangle(0, -30, 100, 20), "Kick", Alignment.BottomLeft, GUI.Style, playerFrameInner);
            kickButton.UserData = obj;
            kickButton.OnClicked += KickPlayer;
            kickButton.OnClicked += ClosePlayerFrame;

            var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomLeft, GUI.Style, playerFrameInner);
            banButton.UserData = obj;
            banButton.OnClicked += BanPlayer;
            banButton.OnClicked += ClosePlayerFrame;

            var closeButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Close", Alignment.BottomRight, GUI.Style, playerFrameInner);
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
            if (GameMain.Server == null || userData == null) return false;

            GameMain.Server.KickPlayer(userData.ToString());
            
            return false;
        }

        public bool BanPlayer(GUIButton button, object userData)
        {
            if (GameMain.Server == null || userData == null) return false;

            GameMain.Server.KickPlayer(userData.ToString(), true);

            return false;
        }

        public void ClearPlayers()
        {
            playerList.ClearChildren();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            
            //Vector2 pos = new Vector2(
            //    Submarine.Borders.X + Submarine.Borders.Width / 2,
            //    Submarine.Borders.Y - Submarine.Borders.Height / 2);

            //camAngle += (float)deltaTime / 10.0f;
            //Vector2 offset = (new Vector2(
            //    (float)Math.Cos(camAngle) * (Submarine.Borders.Width / 2.0f),
            //    (float)Math.Sin(camAngle) * (Submarine.Borders.Height / 2.0f)));
            
            //pos += offset * 0.8f;
            
            //GameMain.GameScreen.Cam.TargetPos = pos;
            //GameMain.GameScreen.Cam.MoveCamera((float)deltaTime);

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

            if (autoRestartTimer != 0.0f && autoRestartBox.Selected)
            {
                autoRestartTimer = Math.Max(autoRestartTimer - (float)deltaTime, 0.0f);
            }
            
            if (valueChanged && GameMain.Server != null)
            {
                networkUpdateTimer -= (float)deltaTime;
                if (networkUpdateTimer <= 0.0f)
                {
                    GameMain.Server.UpdateNetLobby(null);

                    valueChanged = false;
                    networkUpdateTimer = NetworkUpdateInterval;
                }
            }
                        
            //durationBar.BarScroll = Math.Max(durationBar.BarScroll, 1.0f / 60.0f);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);
            
            spriteBatch.Begin();

            if (backgroundSprite!=null)
            {
                spriteBatch.Draw(backgroundSprite.Texture, Vector2.Zero, null, Color.White, 0.0f, Vector2.Zero,
                    Math.Max((float)GameMain.GraphicsWidth / backgroundSprite.SourceRect.Width, (float)GameMain.GraphicsHeight / backgroundSprite.SourceRect.Height), 
                    SpriteEffects.None, 0.0f);
            }

            menu.Draw(spriteBatch);
            
            if (jobInfoFrame != null) jobInfoFrame.Draw(spriteBatch);

            //if (previewPlayer!=null) previewPlayer.Draw(spriteBatch);

            if (playerFrame != null) playerFrame.Draw(spriteBatch);
            
            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        public void NewChatMessage(ChatMessage message)
        {
            float prevSize = chatBox.BarSize;

            while (chatBox.CountChildren>20)
            {
                chatBox.RemoveChild(chatBox.children[1]);
            }

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20),
                message.TextWithSender, 
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black*0.1f, message.Color, 
                Alignment.Left, GUI.Style, null, true);
            msg.Font = GUI.SmallFont;
            msg.CanBeFocused = false;

            msg.Padding = new Vector4(20, 0, 0, 0);
            chatBox.AddChild(msg);

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;
        }
        
        //public bool EnterChatMessage(GUITextBox textBox, string message)
        //{
        //    if (String.IsNullOrEmpty(message)) return false;

        //    GameMain.NetworkMember.SendChatMessage(ChatMessage.Create(message, ChatMessageType.Default, null));
            
        //    return true;
        //}

        private void UpdatePreviewPlayer(CharacterInfo characterInfo)
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
            if (GameMain.Client != null) GameMain.Client.SendCharacterData();

            UpdatePreviewPlayer(GameMain.NetworkMember.CharacterInfo);

            return true;
        }

        private bool SwitchGender(GUIButton button, object obj)
        {
            Gender gender = (Gender)obj;
            GameMain.NetworkMember.CharacterInfo.Gender = gender;
            if (GameMain.Client != null) GameMain.Client.SendCharacterData();
                
            UpdatePreviewPlayer(GameMain.NetworkMember.CharacterInfo);
            return true;
        }

        private bool SelectMode(GUIComponent component, object obj)
        {
            if (GameMain.NetworkMember == null) return false;

            //if (GameMain.Server==null)
            //{
            //    return VotableClicked(component, obj);
            //}

            GameModePreset modePreset = obj as GameModePreset;
            missionTypeBlock.Visible = modePreset.Name == "Mission";

            if (modePreset == null) return false;

            valueChanged = true;

            return true;
        }


        private bool SelectSeed(GUITextBox textBox, string seed)
        {
            if (!string.IsNullOrWhiteSpace(seed))
            {
                LevelSeed = seed;
            }

            //textBox.Text = LevelSeed;
            //textBox.Selected = false;
            
            valueChanged = true;

            return true;
        }

        //private bool ChangeCharacterName(GUITextBox textBox, string newName)
        //{
        //    if (string.IsNullOrEmpty(newName)) return false;

        //    if (GameMain.NetworkMember == null || GameMain.NetworkMember.CharacterInfo == null) return true;

        //    GameMain.NetworkMember.CharacterInfo.Name = newName;
        //    if (GameMain.Client != null)
        //    {
        //        GameMain.Client.Name = newName;
        //        GameMain.Client.SendCharacterData();
        //    }

        //    textBox.Text = newName;
        //    textBox.Selected = false;

        //    return true;
        //}

        private bool ViewJobInfo(GUIButton button, object obj)
        {
            GUIComponent jobText = button.Parent;

            JobPrefab jobPrefab = jobText.UserData as JobPrefab;
            if (jobPrefab == null) return false;

            jobInfoFrame = jobPrefab.CreateInfoFrame();
            GUIButton closeButton = new GUIButton(new Rectangle(0,0,100,20), "Close", Alignment.BottomRight, GUI.Style, jobInfoFrame.children[0]);
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
            for (int i = 0; i < listBox.children.Count; i++)
            {
                float a = (float)(i - 1) / 3.0f;
                a = Math.Min(a, 3);
                Color color = new Color(1.0f - a, (1.0f - a) * 0.6f, 0.0f, 0.3f);

                listBox.children[i].Color = color;
                listBox.children[i].HoverColor = color;
                listBox.children[i].SelectedColor = color;

                (listBox.children[i] as GUITextBlock).Text = (i+1) + ". " + (listBox.children[i].UserData as JobPrefab).Name;
            }

            if (GameMain.Client!=null) GameMain.Client.SendCharacterData();
        }

        public bool TrySelectSub(string subName, string md5Hash, GUIListBox subList)
        {
            //already downloading the selected sub file
            if (GameMain.Client.ActiveFileTransferName == subName+".sub") return false;

            var matchingListSub = subList.children.Find(c => c.UserData != null && (c.UserData as Submarine).Name == subName) as GUITextBlock;
            if (matchingListSub != null)
            {
                subList.Select(subList.children.IndexOf(matchingListSub), true);
            }

            Submarine sub = Submarine.SavedSubmarines.Find(m => m.Name == subName);
            if (sub == null || sub.MD5Hash.Hash != md5Hash)
            {
                string errorMsg = "";
                if (sub == null)
                {
                    errorMsg = "Submarine ''" + subName + "'' was selected by the server. Matching file not found in your submarine folder. ";
                }
                else if (sub.MD5Hash.Hash == null)
                {
                    errorMsg = "Couldn't load submarine ''" + subName + "''. The file may be corrupted. ";

                    if (matchingListSub != null) matchingListSub.TextColor = Color.Red;
                }
                else
                {
                    errorMsg = "Your version of the submarine file ''" + sub.Name + "'' doesn't match the server's version! "
                    + "Your MD5 hash: " + sub.MD5Hash.Hash + " \n"
                    + "Server's MD5 hash: " + md5Hash + ". ";
                }
                    
                string downloadMsg = string.IsNullOrEmpty(GameMain.Client.ActiveFileTransferName) ?
                    "Do you want to download the file from the server host?" :
                    "Do you want to download the file and cancel downloading ''" + GameMain.Client.ActiveFileTransferName + "''?";

                if (GUIMessageBox.MessageBoxes.Count>0)
                {
                    var currentMessageBox = GUIMessageBox.MessageBoxes.Peek();
                    if (currentMessageBox != null && currentMessageBox.UserData as string == subName)
                    {
                        return false;
                    }
                }

                var requestFileBox = new GUIMessageBox("Submarine not found!", errorMsg+downloadMsg, new string[] { "Yes", "No" }, 400, 300);
                requestFileBox.Buttons[0].UserData = subName;
                requestFileBox.Buttons[0].OnClicked += requestFileBox.Close;
                requestFileBox.Buttons[0].OnClicked += (GUIButton button, object userdata) =>
                    {
                        GameMain.Client.RequestFile(userdata.ToString(), FileTransferMessageType.Submarine);
                        return true;
                    };
                requestFileBox.Buttons[1].OnClicked += requestFileBox.Close;
                
                return false;
            }

            return true;
        }
                
        public void WriteData(NetOutgoingMessage msg)
        {
            Submarine selectedSub = subList.SelectedData as Submarine;

            if (selectedSub==null)
            {
                msg.Write("");
                msg.Write("");
            }
            else
            {
                msg.Write(Path.GetFileName(selectedSub.Name));
                msg.Write(selectedSub.MD5Hash.Hash);
            }

            if (SelectedShuttle == null)
            {
                msg.Write("");
                msg.Write("");
            }
            else
            {
                msg.Write(Path.GetFileName(SelectedShuttle.Name));
                msg.Write(SelectedShuttle.MD5Hash.Hash);
            }

            msg.Write(ServerName);
            msg.Write(serverMessage.Text);

            msg.WriteRangedInteger(0, 2, (int)GameMain.Server.TraitorsEnabled);

            msg.WriteRangedInteger(0, Mission.MissionTypes.Count-1, MissionTypeIndex);

            msg.Write((byte)modeList.SelectedIndex);
            msg.Write(LevelSeed);

            msg.Write(GameMain.Server != null && GameMain.Server.AutoRestart);
            msg.Write(GameMain.Server == null ? 0.0f : GameMain.Server.AutoRestartTimer);

            msg.Write((byte)(playerList.CountChildren));
            for (int i = 0; i < playerList.CountChildren; i++)
            {
                string clientName = playerList.children[i].UserData as string;
                msg.Write(clientName==null ? "" : clientName);
            }
        }

        public void ReadData(NetIncomingMessage msg)
        {
            string subName = "", subHash = "";
            string shuttleName = "", shuttleHash = "";
            
            int modeIndex = 0;
            //float durationScroll = 0.0f;
            string newSeed = "";

            bool autoRestart = false;

            float restartTimer = 0.0f;

            YesNoMaybe traitorsEnabled;

            try
            {
                subName         = msg.ReadString();
                subHash         = msg.ReadString();

                shuttleName     = msg.ReadString();
                shuttleHash     = msg.ReadString();

                ServerName      = msg.ReadString();
                serverMessage.Text   = msg.ReadString();

                traitorsEnabled = (YesNoMaybe)msg.ReadRangedInteger(0, 2);

                SetMissionType(msg.ReadRangedInteger(0, Mission.MissionTypes.Count - 1));

                modeIndex       = msg.ReadByte();

                newSeed         = msg.ReadString();

                autoRestart     = msg.ReadBoolean();
                restartTimer    = msg.ReadFloat();

                int playerCount = msg.ReadByte();

                playerList.ClearChildren();
                for (int i = 0; i < playerCount; i++)
                {
                    AddPlayer(msg.ReadString());
                }
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to read lobby update message ("+e.Message+")");
                return;
            }

            if (!string.IsNullOrWhiteSpace(subName) && !GameMain.NetworkMember.Voting.AllowSubVoting) TrySelectSub(subName, subHash, subList);

            if (!string.IsNullOrWhiteSpace(shuttleName)) TrySelectSub(shuttleName, shuttleHash, shuttleList.ListBox);

            if (!GameMain.NetworkMember.Voting.AllowModeVoting)
            {
                //SelectMode(modeList.children[modeIndex], modeList.children[modeIndex].UserData);
                modeList.Select(modeIndex, true);

                GameModePreset modePreset = modeList.children[modeIndex].UserData as GameModePreset;
                missionTypeBlock.Visible = modePreset.Name == "Mission";
            }

            SetTraitorsEnabled(traitorsEnabled);

            autoRestartBox.Selected = autoRestart;
            autoRestartTimer = restartTimer;

            //durationBar.BarScroll = durationScroll;

            LevelSeed = newSeed;
        }

    }
}
