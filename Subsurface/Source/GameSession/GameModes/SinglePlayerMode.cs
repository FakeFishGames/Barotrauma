using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class SinglePlayerMode : GameMode
    {
        //private const int StartCharacterAmount = 3;

        //public readonly CrewManager CrewManager;
        //public readonly HireManager hireManager;

        private GUIButton endShiftButton;

        public readonly CargoManager CargoManager;
        
        public Map Map;

        private bool crewDead;
        private float endTimer;

        private bool savedOnStart;

        private List<Submarine> subsToLeaveBehind;

        private Submarine leavingSub;
        private bool atEndPosition;

        public override Mission Mission
        {
            get
            {
                return Map.SelectedConnection.Mission;
            }
        }

        public int Money
        {
            get { return GameMain.GameSession.CrewManager.Money; }
            set { GameMain.GameSession.CrewManager.Money = value; }
        }

        private CrewManager CrewManager
        {
            get { return GameMain.GameSession.CrewManager; }
        }

        public SinglePlayerMode(GameModePreset preset, object param)
            : base(preset, param)
        {

            CargoManager = new CargoManager();

            endShiftButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 220, 20, 200, 25), "End shift", Alignment.TopLeft, GUI.Style);
            endShiftButton.Font = GUI.SmallFont;
            endShiftButton.OnClicked = TryEndShift;

            for (int i = 0; i < 3; i++)
            {
                JobPrefab jobPrefab = null;
                switch (i)
                {
                    case 0:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Captain");
                        break;
                    case 1:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Engineer");
                        break;
                    case 2:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Mechanic");
                        break;
                }

                CharacterInfo characterInfo =
                    new CharacterInfo(Character.HumanConfigFile, "", Gender.None, jobPrefab);
                CrewManager.characterInfos.Add(characterInfo);
            }
  
        }

        public SinglePlayerMode(XElement element)
            : this(GameModePreset.list.Find(gm => gm.Name == "Single Player"), null)
        {            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crew":
                        GameMain.GameSession.CrewManager = new CrewManager(subElement); 
                        break;
                    case "map":
                        Map = Map.Load(subElement);
                        break;
                }               
            }

            //backwards compatibility with older save files
            if (Map==null)
            {
                string mapSeed = ToolBox.GetAttributeString(element, "mapseed", "a");

                GenerateMap(mapSeed);

                Map.SetLocation(ToolBox.GetAttributeInt(element, "currentlocation", 0));
            }

            savedOnStart = true;
        }

        public void GenerateMap(string seed)
        {
            Map = new Map(seed, 500);
        }

        public override void Start()
        {
            CargoManager.CreateItems();

            if (!savedOnStart)
            {
                SaveUtil.SaveGame(GameMain.GameSession.SaveFile);
                savedOnStart = true;
            }

            endTimer = 5.0f;

            isRunning = true;

            CrewManager.StartShift();
        }

        public bool TryHireCharacter(HireManager hireManager, CharacterInfo characterInfo)
        {
            if (CrewManager.Money < characterInfo.Salary) return false;

            hireManager.availableCharacters.Remove(characterInfo);
            CrewManager.characterInfos.Add(characterInfo);

            CrewManager.Money -= characterInfo.Salary;

            return true;
        }

        public string GetMoney()
        {
            return "Money: " + CrewManager.Money;
        }


        private Submarine GetLeavingSub()
        {
            if (Character.Controlled != null && Character.Controlled.Submarine != null)
            {
                if (Character.Controlled.Submarine.AtEndPosition || Character.Controlled.Submarine.AtStartPosition)
                {
                    return Character.Controlled.Submarine;
                }
                return null;
            }

            Submarine closestSub = Submarine.GetClosest(GameMain.GameScreen.Cam.WorldViewCenter);
            if (closestSub != null && (closestSub.AtEndPosition || closestSub.AtStartPosition))
            {
                return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
            }

            return null;
        }

        private List<Submarine> GetSubsToLeaveBehind(Submarine leavingSub)
        {
            //leave subs behind if they're not docked to the leaving sub and not at the same exit
            return Submarine.Loaded.FindAll(s =>
                s != leavingSub &&
                !leavingSub.DockedTo.Contains(s) &&
                (s.AtEndPosition != leavingSub.AtEndPosition || s.AtStartPosition != leavingSub.AtStartPosition));
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isRunning) return;

            CrewManager.Draw(spriteBatch);

            if (Submarine.MainSub == null) return;

            Submarine leavingSub = GetLeavingSub();

            if (leavingSub == null)
            {
                endShiftButton.Visible = false;
            }
            else if (leavingSub.AtEndPosition)
            {
                endShiftButton.Text = "Enter " + Map.SelectedLocation.Name;
                endShiftButton.UserData = leavingSub;
                endShiftButton.Visible = true;
            }
            else if (leavingSub.AtStartPosition)
            {
                endShiftButton.Text = "Enter " + Map.CurrentLocation.Name;
                endShiftButton.UserData = leavingSub;
                endShiftButton.Visible = true;
            }
            else
            {
                endShiftButton.Visible = false;
            }

            endShiftButton.Draw(spriteBatch);
        }

        public override void Update(float deltaTime)
        {
            if (!isRunning) return;

            base.Update(deltaTime);

            CrewManager.Update(deltaTime);

            endShiftButton.Update(deltaTime);

            if (!crewDead)
            {
                if (!CrewManager.characters.Any(c => !c.IsDead)) crewDead = true;                
            }
            else
            {
                endTimer -= deltaTime;

                if (endTimer <= 0.0f) EndShift(null, null);
            }  
        }

        public override void End(string endMessage = "")
        {
            isRunning = false;

            if (subsToLeaveBehind == null || leavingSub == null)
            {
                DebugConsole.ThrowError("Leaving submarine not selected -> selecting the closest one");

                leavingSub = GetLeavingSub();

                subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
            }


            bool success = CrewManager.characters.Any(c => !c.IsDead);
            
            GameMain.GameSession.EndShift("");

            if (success)
            {
                if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
                    Submarine oldMainSub = Submarine.MainSub;
                    Submarine.MainSub = leavingSub;

                    GameMain.GameSession.Submarine = leavingSub;
                    
                    foreach (Submarine sub in subsToLeaveBehind)
                    {
                        MapEntity.mapEntityList.RemoveAll(e => e.Submarine == sub && e is LinkedSubmarine);
                        LinkedSubmarine.CreateDummy(leavingSub, sub);
                    }
                }

                if (atEndPosition)
                {
                    Map.MoveToNextLocation();
                }

                SaveUtil.SaveGame(GameMain.GameSession.SaveFile);
            }


            if (!success)
            {
                var summaryScreen = GUIMessageBox.MessageBoxes.Peek();

                if (summaryScreen != null)
                {
                    summaryScreen = summaryScreen.children[0];
                    summaryScreen.RemoveChild(summaryScreen.children.Find(c => c is GUIButton));

                    var okButton = new GUIButton(new Rectangle(-120, 0, 100, 30), "Load game", Alignment.BottomRight, GUI.Style, summaryScreen);
                    okButton.OnClicked += GameMain.GameSession.LoadPrevious;
                    okButton.OnClicked += (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Dequeue(); return true; };

                    var quitButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Quit", Alignment.BottomRight, GUI.Style, summaryScreen);
                    quitButton.OnClicked += GameMain.LobbyScreen.QuitToMainMenu;
                    quitButton.OnClicked += (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Dequeue(); return true; };
                }
            }

            CrewManager.EndShift();
            for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
            {
                Character.CharacterList[i].Remove();
            }

            Submarine.Unload();
        }

        private bool TryEndShift(GUIButton button, object obj)
        {
            leavingSub = obj as Submarine;
            if (leavingSub != null)
            {
                subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
            }

            atEndPosition = leavingSub.AtEndPosition;

            if (subsToLeaveBehind.Any())
            {
                string msg = "";
                if (subsToLeaveBehind.Count == 1)
                {
                    msg = "One of your vessels isn't at the exit yet. Do you want to leave it behind?";
                }
                else
                {
                    msg = "Some of your vessels aren't at the exit yet. Do you want to leave them behind?";
                }

                var msgBox = new GUIMessageBox("Warning", msg, new string[] {"Yes", "No"});
                msgBox.Buttons[0].OnClicked += EndShift;
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[0].UserData = Submarine.Loaded.FindAll(s => !subsToLeaveBehind.Contains(s));

                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
            else
            {
                EndShift(button, obj);
            }

            return true;
        }

        private bool EndShift(GUIButton button, object obj)
        {
            isRunning = false;

            List<Submarine> leavingSubs = obj as List<Submarine>;
            if (leavingSubs == null) leavingSubs = new List<Submarine>() { GetLeavingSub() };
            
            var cinematic = new TransitionCinematic(leavingSubs, GameMain.GameScreen.Cam, 5.0f);

            SoundPlayer.OverrideMusicType = CrewManager.characters.Any(c => !c.IsDead) ? "endshift" : "crewdead";

            CoroutineManager.StartCoroutine(EndCinematic(cinematic));

            return true;
        }

        private IEnumerable<object> EndCinematic(TransitionCinematic cinematic)
        {
            while (cinematic.Running)
            {
                if (Submarine.MainSub == null) yield return CoroutineStatus.Success;

                yield return CoroutineStatus.Running;
            }

            if (Submarine.MainSub == null) yield return CoroutineStatus.Success;

            End("");

            yield return new WaitForSeconds(18.0f);
            
            SoundPlayer.OverrideMusicType = null;

            yield return CoroutineStatus.Success;
        }

        public void Save(XElement element)
        {
            //element.Add(new XAttribute("day", day));
            XElement modeElement = new XElement("gamemode");

            //modeElement.Add(new XAttribute("currentlocation", Map.CurrentLocationIndex));
            //modeElement.Add(new XAttribute("mapseed", Map.Seed));
            
            
            CrewManager.Save(modeElement);
            Map.Save(modeElement);

            element.Add(modeElement);
        }
    }
}
