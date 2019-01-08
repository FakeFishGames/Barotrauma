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

        public SinglePlayerCampaign(GameModePreset preset, object param)
            : base(preset, param)
        {
            int buttonHeight = (int)(HUDLayoutSettings.ButtonAreaTop.Height * 0.7f);
            endRoundButton = new GUIButton(HUDLayoutSettings.ToRectTransform(new Rectangle(HUDLayoutSettings.ButtonAreaTop.Right - 200, HUDLayoutSettings.ButtonAreaTop.Center.Y - buttonHeight / 2, 200, buttonHeight), GUICanvas.Instance),
                TextManager.Get("EndRound"), textAlignment: Alignment.Center)
            {
                Font = GUI.SmallFont,
                OnClicked = TryEndRound
            };

            foreach (JobPrefab jobPrefab in JobPrefab.List)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    CrewManager.AddCharacterInfo(new CharacterInfo(Character.HumanConfigFile, "", Gender.None, jobPrefab));
                }
            }
        }

        public override void Start()
        {
            base.Start();
            CargoManager.CreateItems();

            if (!savedOnStart)
            {
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                savedOnStart = true;
            }

            endTimer = 5.0f;
            isRunning = true;
            CrewManager.InitSinglePlayerRound();
        }

        public bool TryHireCharacter(HireManager hireManager, CharacterInfo characterInfo)
        {
            if (Money < characterInfo.Salary) return false;

            hireManager.availableCharacters.Remove(characterInfo);
            CrewManager.AddCharacterInfo(characterInfo);
            Money -= characterInfo.Salary;

            return true;
        }

        public void FireCharacter(CharacterInfo characterInfo)
        {
            CrewManager.RemoveCharacterInfo(characterInfo);
        }

        private Submarine GetLeavingSub()
        {
            if (Character.Controlled != null && Character.Controlled.Submarine != null)
            {
                if (Character.Controlled.Submarine == Level.Loaded.StartOutpost)
                {
                    return Level.Loaded.StartOutpost.DockedTo.FirstOrDefault();
                }
                else if (Character.Controlled.Submarine == Level.Loaded.EndOutpost)
                {
                    return Level.Loaded.StartOutpost.DockedTo.FirstOrDefault();
                }

                if (Character.Controlled.Submarine.AtEndPosition || Character.Controlled.Submarine.AtStartPosition)
                {
                    return Character.Controlled.Submarine;
                }
                return null;
            }

            Submarine closestSub = Submarine.FindClosest(GameMain.GameScreen.Cam.WorldViewCenter, ignoreOutposts: true);
            if (closestSub != null && (closestSub.AtEndPosition || closestSub.AtStartPosition))
            {
                return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
            }

            return null;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isRunning|| GUI.DisableHUD) return;
            
            if (Submarine.MainSub == null) return;

            Submarine leavingSub = GetLeavingSub();

            if (leavingSub == null)
            {
                endRoundButton.Visible = false;
            }
            else if (leavingSub.AtEndPosition)
            {
                endRoundButton.Text = ToolBox.LimitString(TextManager.Get("EnterLocation").Replace("[locationname]", Map.SelectedLocation.Name), endRoundButton.Font, endRoundButton.Rect.Width - 5);
                endRoundButton.UserData = leavingSub;
                endRoundButton.Visible = true;
            }
            else if (leavingSub.AtStartPosition)
            {
                endRoundButton.Text = ToolBox.LimitString(TextManager.Get("EnterLocation").Replace("[locationname]", Map.CurrentLocation.Name), endRoundButton.Font, endRoundButton.Rect.Width - 5);
                endRoundButton.UserData = leavingSub;
                endRoundButton.Visible = true;
            }
            else
            {
                endRoundButton.Visible = false;
            }

            endRoundButton.DrawManually(spriteBatch);
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
            if (!isRunning) { return; }

            base.Update(deltaTime);

            if (!GUI.DisableHUD)
            {
                endRoundButton.UpdateManually(deltaTime);
            }

            if (!crewDead)
            {
                if (!CrewManager.GetCharacters().Any(c => !c.IsDead)) crewDead = true;                
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

            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);

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
                else
                {
                    Map.SelectLocation(-1);
                }
                Map.ProgressWorld();

                //save and remove all items that are in someone's inventory
                foreach (Character c in Character.CharacterList)
                {
                    if (c.Info == null || c.Inventory == null) { continue; }
                    var inventoryElement = new XElement("inventory");
                    c.SaveInventory(c.Inventory, inventoryElement);
                    c.Info.InventoryData = inventoryElement;
                    c.Inventory?.DeleteAllItems();
                }
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }

            if (!success)
            {
                var summaryScreen = GUIMessageBox.VisibleBox;

                if (summaryScreen != null)
                {
                    summaryScreen = summaryScreen.Children.First();
                    var buttonArea = summaryScreen.Children.First().FindChild("buttonarea");
                    buttonArea.ClearChildren();


                    summaryScreen.RemoveChild(summaryScreen.Children.FirstOrDefault(c => c is GUIButton));

                    var okButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform),
                        TextManager.Get("LoadGameButton"))
                    {
                        OnClicked = (GUIButton button, object obj) =>
                        {
                            GameMain.GameSession.LoadPrevious();
                            GameMain.LobbyScreen.Select();
                            GUIMessageBox.MessageBoxes.Remove(GUIMessageBox.VisibleBox);
                            return true;
                        }
                    };

                    var quitButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), buttonArea.RectTransform),
                        TextManager.Get("QuitButton"));
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
            
            GameMain.LobbyScreen.Select();
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
                string msg = TextManager.Get(subsToLeaveBehind.Count == 1 ? "LeaveSubBehind" : "LeaveSubsBehind");

                var msgBox = new GUIMessageBox(TextManager.Get("Warning"), msg, new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
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

            SoundPlayer.OverrideMusicType = CrewManager.GetCharacters().Any(c => !c.IsDead) ? "endround" : "crewdead";
            SoundPlayer.OverrideMusicDuration = 18.0f;

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

            if (Submarine.MainSub != null) End("");

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
                        GameMain.GameSession.CrewManager = new CrewManager(subElement, true);
                        break;
                    case "map":
                        campaign.map = Map.LoadNew(subElement);
                        break;
                }
            }

            campaign.Money = element.GetAttributeInt("money", 0);
            campaign.CheatsEnabled = element.GetAttributeBool("cheatsenabled", false);
            if (campaign.CheatsEnabled)
            {
                DebugConsole.CheatsEnabled = true;
                if (GameMain.Config.UseSteam && !SteamAchievementManager.CheatsEnabled)
                {
                    SteamAchievementManager.CheatsEnabled = true;
                    new GUIMessageBox("Cheats enabled", "Cheat commands have been enabled on the campaign. You will not receive Steam Achievements until you restart the game.");
                }
            }

            //backwards compatibility with older save files
            if (campaign.map == null)
            {
                string mapSeed = element.GetAttributeString("mapseed", "a");
                campaign.GenerateMap(mapSeed);
                campaign.map.SetLocation(element.GetAttributeInt("currentlocation", 0));
            }

            campaign.savedOnStart = true;

            return campaign;
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("SinglePlayerCampaign",
                new XAttribute("money", Money),
                new XAttribute("cheatsenabled", CheatsEnabled));
            CrewManager.Save(modeElement);
            Map.Save(modeElement);

            element.Add(modeElement);
        }
    }
}
