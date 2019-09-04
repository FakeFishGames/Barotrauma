using Barotrauma.Tutorials;
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
                OnClicked = (btn, userdata) => { TryEndRound(GetLeavingSub()); return true; }
            };

            foreach (JobPrefab jobPrefab in JobPrefab.List.Values)
            {
                for (int i = 0; i < jobPrefab.InitialCount; i++)
                {
                    CrewManager.AddCharacterInfo(new CharacterInfo(Character.HumanConfigFile, "", jobPrefab));
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

            crewDead = false;
            endTimer = 5.0f;
            isRunning = true;
            CrewManager.InitSinglePlayerRound();
        }

        public bool TryHireCharacter(Location location, CharacterInfo characterInfo)
        {
            if (Money < characterInfo.Salary) { return false; }

            location.RemoveHireableCharacter(characterInfo);
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
            if (Character.Controlled?.Submarine == null)
            {
                return null;
            }

            //allow leaving if inside an outpost, and the submarine is either docked to it or close enough
            return GetLeavingSubAtOutpost(Level.Loaded.StartOutpost) ?? GetLeavingSubAtOutpost(Level.Loaded.EndOutpost);

            Submarine GetLeavingSubAtOutpost(Submarine outpost)
            {
                //controlled character has to be inside the outpost
                if (Character.Controlled.Submarine != outpost) { return null; }
                
                //if there's a sub docked to the outpost, we can leave the level
                if (outpost.DockedTo.Count > 0)
                {
                    var dockedSub = outpost.DockedTo.FirstOrDefault();
                    return dockedSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : dockedSub;
                }

                //nothing docked, check if there's a sub close enough to the outpost
                Submarine closestSub = Submarine.FindClosest(outpost.WorldPosition, ignoreOutposts: true);
                if (closestSub == null) { return null; }
                
                if (outpost == Level.Loaded.StartOutpost)
                {
                    if (!closestSub.AtStartPosition) { return null; }
                }
                else if (outpost == Level.Loaded.EndOutpost)
                {
                    if (!closestSub.AtEndPosition) { return null; }
                }
                return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;                
            }            
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isRunning|| GUI.DisableHUD || GUI.DisableUpperHUD) return;
            
            if (Submarine.MainSub == null) return;

            Submarine leavingSub = GetLeavingSub();
            if (leavingSub == null)
            {
                endRoundButton.Visible = false;
            }
            else if (leavingSub.AtEndPosition)
            {
                endRoundButton.Text = ToolBox.LimitString(TextManager.GetWithVariable("EnterLocation", "[locationname]", Map.SelectedLocation.Name), endRoundButton.Font, endRoundButton.Rect.Width - 5);
                endRoundButton.Visible = true;
            }
            else if (leavingSub.AtStartPosition)
            {
                endRoundButton.Text = ToolBox.LimitString(TextManager.GetWithVariable("EnterLocation", "[locationname]", Map.CurrentLocation.Name), endRoundButton.Font, endRoundButton.Rect.Width - 5);
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

            if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
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
                if (endTimer <= 0.0f) { EndRound(leavingSub: null); }
            }  
        }


        protected override void WatchmanInteract(Character watchman, Character interactor)
        {
            Submarine leavingSub = GetLeavingSub();
            if (leavingSub == null)
            {
                CreateDialog(new List<Character> { watchman }, "WatchmanInteractNoLeavingSub", 5.0f);
                return;
            }


            CreateDialog(new List<Character> { watchman }, "WatchmanInteract", 1.0f);

            if (GUIMessageBox.MessageBoxes.Any(mbox => mbox.UserData as string == "watchmanprompt"))
            {
                return;
            }
            var msgBox = new GUIMessageBox("", TextManager.GetWithVariable("CampaignEnterOutpostPrompt", "[locationname]",
                leavingSub.AtStartPosition ? Map.CurrentLocation.Name : Map.SelectedLocation.Name),
                new string[] { TextManager.Get("Yes"), TextManager.Get("No") })
            {
                UserData = "watchmanprompt"
            };
            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                if (!isRunning) { return true; }
                TryEndRound(GetLeavingSub());
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;
            msgBox.Buttons[1].OnClicked += msgBox.Close;
        }

        public override void End(string endMessage = "")
        {
            isRunning = false;

            bool success = CrewManager.GetCharacters().Any(c => !c.IsDead);
            crewDead = false;

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

                    // Recharge headset batteries
                    var headset = c.Inventory.FindItemByIdentifier("headset");
                    if (headset != null)
                    {
                        var battery = headset.OwnInventory.FindItemByTag("loadable");
                        if (battery != null)
                        {
                            battery.Condition = battery.MaxCondition;
                        }
                    }

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

        private bool TryEndRound(Submarine leavingSub)
        {
            if (leavingSub == null) { return false; }

            this.leavingSub = leavingSub;
            subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
            atEndPosition = leavingSub.AtEndPosition;

            if (subsToLeaveBehind.Any())
            {
                string msg = TextManager.Get(subsToLeaveBehind.Count == 1 ? "LeaveSubBehind" : "LeaveSubsBehind");

                var msgBox = new GUIMessageBox(TextManager.Get("Warning"), msg, new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                msgBox.Buttons[0].OnClicked += (btn, userdata) => { EndRound(leavingSub); return true; } ;
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[0].UserData = Submarine.Loaded.FindAll(s => !subsToLeaveBehind.Contains(s));

                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }
            else
            {
                EndRound(leavingSub);
            }

            return true;
        }

        private bool EndRound(Submarine leavingSub)
        {
            isRunning = false;
            
            //var cinematic = new RoundEndCinematic(leavingSub, GameMain.GameScreen.Cam, 5.0f);

            SoundPlayer.OverrideMusicType = CrewManager.GetCharacters().Any(c => !c.IsDead) ? "endround" : "crewdead";
            SoundPlayer.OverrideMusicDuration = 18.0f;

            //CoroutineManager.StartCoroutine(EndCinematic(cinematic), "EndCinematic");
            End("");
            
            return true;
        }

        /*private IEnumerable<object> EndCinematic(RoundEndCinematic cinematic)
        {
            while (cinematic.Running)
            {
                if (Submarine.MainSub == null) yield return CoroutineStatus.Success;                

                yield return CoroutineStatus.Running;
            }

            if (Submarine.MainSub != null) End("");

            yield return CoroutineStatus.Success;
        }*/

        public static SinglePlayerCampaign Load(XElement element)
        {
            SinglePlayerCampaign campaign = new SinglePlayerCampaign(GameModePreset.List.Find(gm => gm.Identifier == "singleplayercampaign"), null);

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
                if (Steam.SteamManager.USE_STEAM && !SteamAchievementManager.CheatsEnabled)
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
                // Refunds the money when save & quitting from the map if there are items selected in the store
                new XAttribute("money", Money + (CargoManager != null ? CargoManager.GetTotalItemCost() : 0)),
                new XAttribute("cheatsenabled", CheatsEnabled));
            CrewManager.Save(modeElement);
            Map.Save(modeElement);
            element.Add(modeElement);
        }
    }
}
