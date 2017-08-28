using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class GameSession
    {
        public enum InfoFrameTab { Crew, Mission, ManagePlayers };

        public readonly EventManager EventManager;
        
        public readonly GameMode GameMode;

        //two locations used as the start and end in the MP mode
        private Location[] dummyLocations;

        private string saveFile;

        private Submarine submarine;

#if CLIENT
        public CrewManager CrewManager;
#endif

        private Mission currentMission;

        public Mission Mission
        {
            get
            {
                return currentMission;
            }
        }

        private Level level;

        public Level Level
        {
            get { return level; }
        }
        
        public Location StartLocation
        {
            get 
            {
                if (Map != null) return Map.CurrentLocation;

                if (dummyLocations==null)
                {
                    CreateDummyLocations();
                }

                return dummyLocations[0]; 
            }
        }
         
        public Location EndLocation
        {
            get
            {
                if (Map != null) return Map.SelectedLocation;

                if (dummyLocations == null)
                {
                    CreateDummyLocations();
                }

                return dummyLocations[1];
            }
        }  
     
        public Submarine Submarine
        {
            get { return submarine; }
            set { submarine = value; }
        }

        public string SaveFile
        {
            get { return saveFile; }
        }

        public GameSession(Submarine submarine, string saveFile, GameModePreset gameModePreset = null, string missionType = "")
        {
            Submarine.MainSub = submarine;

            GameMain.GameSession = this;
            
            EventManager = new EventManager(this);
            
            this.saveFile = saveFile;

#if CLIENT
            CrewManager = new CrewManager();

            infoButton = new GUIButton(new Rectangle(10, 10, 100, 20), "Info", "", null);
            infoButton.OnClicked = ToggleInfoFrame;
#endif

            if (gameModePreset != null) GameMode = gameModePreset.Instantiate(missionType);
            this.submarine = submarine;
        }
        
        public GameSession(Submarine selectedSub, string saveFile, XDocument doc)
            : this(selectedSub, saveFile)
        {
            Submarine.MainSub = submarine;

            GameMain.GameSession = this;
            
            selectedSub.Name = ToolBox.GetAttributeString(doc.Root, "submarine", selectedSub.Name);

#if CLIENT
            CrewManager = new CrewManager();

            foreach (XElement subElement in doc.Root.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "gamemode") continue;

                GameMode = SinglePlayerCampaign.Load(subElement);
            }
#endif
        }

        private void CreateDummyLocations()
        {
            dummyLocations = new Location[2];

            string seed = "";
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null)
            {
                seed = GameMain.GameSession.Level.Seed;
            }
            else if (GameMain.NetLobbyScreen != null)
            {
                seed = GameMain.NetLobbyScreen.LevelSeed;
            }

            MTRandom rand = new MTRandom(ToolBox.StringToInt(seed));
            for (int i = 0; i < 2; i++)
            {
                dummyLocations[i] = Location.CreateRandom(new Vector2((float)rand.NextDouble() * 10000.0f, (float)rand.NextDouble() * 10000.0f));
            }
        }

        public void StartRound(string levelSeed, bool loadSecondSub = false)
        {
            Level randomLevel = Level.CreateRandom(levelSeed);

            StartRound(randomLevel, true, loadSecondSub);
        }

        public void StartRound(Level level, bool reloadSub = true, bool loadSecondSub = false)
        {
#if CLIENT
            GameMain.LightManager.LosEnabled = GameMain.NetworkMember == null || GameMain.NetworkMember.CharacterInfo != null;
#endif
                        
            this.level = level;

            if (submarine==null)
            {
                DebugConsole.ThrowError("Couldn't start game session, submarine not selected");
                return;
            }

            if (reloadSub || Submarine.MainSub != submarine) submarine.Load(true);
            Submarine.MainSub = submarine;
            if (loadSecondSub)
            {
                if (Submarine.MainSubs[1] == null)
                {
                    Submarine.MainSubs[1] = new Submarine(Submarine.MainSub.FilePath,Submarine.MainSub.MD5Hash.Hash,true);
                    Submarine.MainSubs[1].Load(false);
                }
                else if (reloadSub)
                {
                    Submarine.MainSubs[1].Load(false);
                }
            }
            
            if (level != null)
            {
                level.Generate();

                submarine.SetPosition(submarine.FindSpawnPos(level.StartPosition - new Vector2(0.0f, 2000.0f)));
            }

            if (GameMode.Mission != null)
            {
                currentMission = GameMode.Mission;
            }

            if (GameMode!=null) GameMode.Start();

            if (GameMode.Mission != null) Mission.Start(Level.Loaded);
            
            EventManager.StartRound(level);

            if (GameMode != null) GameMode.MsgBox();

            Entity.Spawner = new EntitySpawner();

#if CLIENT
            roundSummary = new RoundSummary(this);

            GameMain.GameScreen.ColorFade(Color.Black, Color.TransparentBlack, 5.0f);
            SoundPlayer.SwitchMusic();
#endif
        }

        public void EndRound(string endMessage)
        {
            if (Mission != null) Mission.End();

            if (GameMain.Server != null)
            {
                //CoroutineManager.StartCoroutine(GameMain.Server.EndGame(endMessage));

            }
            else if (GameMain.Client == null)
            {
                //Submarine.Unload();
                GameMain.LobbyScreen.Select();
            }

#if CLIENT
            if (roundSummary != null)
            {
                GUIFrame summaryFrame = roundSummary.CreateSummaryFrame(endMessage);
                GUIMessageBox.MessageBoxes.Add(summaryFrame);
                var okButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Ok", Alignment.BottomRight, "", summaryFrame.children[0]);
                okButton.OnClicked = (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(summaryFrame); return true; };
            }
#endif

            EventManager.EndRound();

            currentMission = null;

            StatusEffect.StopAll();
        }
        
        public void KillCharacter(Character character)
        {
#if CLIENT
            CrewManager.KillCharacter(character);
#endif
        }

        public void ReviveCharacter(Character character)
        {
#if CLIENT
            CrewManager.ReviveCharacter(character);
#endif
        }

    }
}
