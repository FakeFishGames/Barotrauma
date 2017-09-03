using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SinglePlayerCampaign : CampaignMode
    {
        private GUIButton endRoundButton;
                
        private bool crewDead;
        private float endTimer;

        private bool savedOnStart;

        private List<Submarine> subsToLeaveBehind;

        private Submarine leavingSub;
        private bool atEndPosition;

        protected CrewManager CrewManager
        {
            get { return GameMain.GameSession?.CrewManager; }
        }

        public SinglePlayerCampaign(GameModePreset preset, object param)
            : base(preset, param)
        {            
            endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 220, 20, 200, 25), "End round", null, Alignment.TopLeft, Alignment.Center, "");
            endRoundButton.Font = GUI.SmallFont;
            endRoundButton.OnClicked = TryEndRound;

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
                
                CrewManager.CharacterInfos.Add(new CharacterInfo(Character.HumanConfigFile, "", Gender.None, jobPrefab));
            }
        }
              
        public override void Start()
        {
            CargoManager.CreateItems();

            if (!savedOnStart)
            {
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                savedOnStart = true;
            }

            endTimer = 5.0f;

            isRunning = true;

            CrewManager.StartRound();
        }

        public bool TryHireCharacter(HireManager hireManager, CharacterInfo characterInfo)
        {
            if (Money < characterInfo.Salary) return false;

            hireManager.availableCharacters.Remove(characterInfo);
            CrewManager.CharacterInfos.Add(characterInfo);
            Money -= characterInfo.Salary;

            return true;
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

            Submarine closestSub = Submarine.FindClosest(GameMain.GameScreen.Cam.WorldViewCenter);
            if (closestSub != null && (closestSub.AtEndPosition || closestSub.AtStartPosition))
            {
                return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
            }

            return null;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isRunning|| GUI.DisableHUD) return;

            CrewManager.Draw(spriteBatch);

            if (Submarine.MainSub == null) return;

            Submarine leavingSub = GetLeavingSub();

            if (leavingSub == null)
            {
                endRoundButton.Visible = false;
            }
            else if (leavingSub.AtEndPosition)
            {
                endRoundButton.Text = ToolBox.LimitString("Enter " + Map.SelectedLocation.Name, endRoundButton.Font, endRoundButton.Rect.Width - 5);
                endRoundButton.UserData = leavingSub;
                endRoundButton.Visible = true;
            }
            else if (leavingSub.AtStartPosition)
            {
                endRoundButton.Text = ToolBox.LimitString("Enter " + Map.CurrentLocation.Name, endRoundButton.Font, endRoundButton.Rect.Width - 5);
                endRoundButton.UserData = leavingSub;
                endRoundButton.Visible = true;
            }
            else
            {
                endRoundButton.Visible = false;
            }

            endRoundButton.Draw(spriteBatch);
        }

        public override void AddToGUIUpdateList()
        {
            if (!isRunning) return;

            base.AddToGUIUpdateList();
            CrewManager.AddToGUIUpdateList();
            endRoundButton.AddToGUIUpdateList();
        }

        public override void Update(float deltaTime)
        {
            if (!isRunning || GUI.DisableHUD) return;

            base.Update(deltaTime);

            CrewManager.Update(deltaTime);

            endRoundButton.Update(deltaTime);

            if (!crewDead)
            {
                if (!CrewManager.characters.Any(c => !c.IsDead)) crewDead = true;                
            }
            else
            {
                endTimer -= deltaTime;

                if (endTimer <= 0.0f) EndRound(null, null);
            }  
        }

        public override void End(string endMessage = "")
        {
            isRunning = false;

            bool success = CrewManager.characters.Any(c => !c.IsDead);

            if (success)
            {
                if (subsToLeaveBehind == null || leavingSub == null)
                {
                    DebugConsole.ThrowError("Leaving submarine not selected -> selecting the closest one");

                    leavingSub = GetLeavingSub();

                    subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
                }
            }
            
            GameMain.GameSession.EndRound("");

            if (success)
            {
                if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
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

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }


            if (!success)
            {
                var summaryScreen = GUIMessageBox.VisibleBox;

                if (summaryScreen != null)
                {
                    summaryScreen = summaryScreen.children[0];
                    summaryScreen.RemoveChild(summaryScreen.children.Find(c => c is GUIButton));

                    var okButton = new GUIButton(new Rectangle(-120, 0, 100, 30), "Load game", Alignment.BottomRight, "", summaryScreen);
                    okButton.OnClicked += GameMain.GameSession.LoadPrevious;
                    okButton.OnClicked += (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox); return true; };

                    var quitButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Quit", Alignment.BottomRight, "", summaryScreen);
                    quitButton.OnClicked += GameMain.LobbyScreen.QuitToMainMenu;
                    quitButton.OnClicked += (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox); return true; };
                }
            }

            CrewManager.EndRound();
            for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
            {
                Character.CharacterList[i].Remove();
            }

            Submarine.Unload();
        }

        private bool TryEndRound(GUIButton button, object obj)
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
                msgBox.Buttons[0].OnClicked += EndRound;
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[0].UserData = Submarine.Loaded.FindAll(s => !subsToLeaveBehind.Contains(s));

                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
            else
            {
                EndRound(button, obj);
            }

            return true;
        }

        private bool EndRound(GUIButton button, object obj)
        {
            isRunning = false;

            List<Submarine> leavingSubs = obj as List<Submarine>;
            if (leavingSubs == null) leavingSubs = new List<Submarine>() { GetLeavingSub() };

            var cinematic = new TransitionCinematic(leavingSubs, GameMain.GameScreen.Cam, 5.0f);

            SoundPlayer.OverrideMusicType = CrewManager.characters.Any(c => !c.IsDead) ? "endround" : "crewdead";

            CoroutineManager.StartCoroutine(EndCinematic(cinematic), "EndCinematic");

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

        public static SinglePlayerCampaign Load(XElement element)
        {
            SinglePlayerCampaign campaign = new SinglePlayerCampaign(GameModePreset.list.Find(gm => gm.Name == "Single Player"), null);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crew":
                        GameMain.GameSession.CrewManager = new CrewManager(subElement);
                        break;
                    case "map":
                        campaign.map = Map.Load(subElement);
                        break;
                }
            }

            campaign.Money = ToolBox.GetAttributeInt(element, "money", 0);

            //backwards compatibility with older save files
            if (campaign.map == null)
            {
                string mapSeed = ToolBox.GetAttributeString(element, "mapseed", "a");
                campaign.GenerateMap(mapSeed);
                campaign.map.SetLocation(ToolBox.GetAttributeInt(element, "currentlocation", 0));
            }

            campaign.savedOnStart = true;

            return campaign;
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("SinglePlayerCampaign");
            
            modeElement.Add(new XAttribute("money", Money));
            
            CrewManager.Save(modeElement);
            Map.Save(modeElement);

            element.Add(modeElement);
        }
    }
}
