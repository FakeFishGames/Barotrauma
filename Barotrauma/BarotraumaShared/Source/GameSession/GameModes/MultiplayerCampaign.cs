using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;
using Lidgren.Network;
using System.Collections.Generic;

namespace Barotrauma
{
    class MultiplayerCampaign : CampaignMode
    {
        private UInt16 lastUpdateID;
        public UInt16 LastUpdateID
        {
            get { if (GameMain.Server != null && lastUpdateID < 1) lastUpdateID++; return lastUpdateID; }
            set { lastUpdateID = value; }
        }

        private UInt16 lastSaveID;
        public UInt16 LastSaveID
        {
            get { if (GameMain.Server != null && lastSaveID < 1) lastSaveID++; return lastSaveID; }
            set { lastSaveID = value; }
        }
        
        public UInt16 PendingSaveID
        {
            get;
            set;
        }

        public MultiplayerCampaign(GameModePreset preset, object param) : 
            base(preset, param)
        {
        }

#if CLIENT
        public static void StartCampaignSetup(Boolean AutoSetup = false)
        {
            if (!AutoSetup)
            {
                var setupBox = new GUIMessageBox("Campaign Setup", "", new string[0], 500, 500);
                setupBox.InnerFrame.Padding = new Vector4(20.0f, 80.0f, 20.0f, 20.0f);

                var newCampaignContainer = new GUIFrame(new Rectangle(0, 40, 0, 0), null, setupBox.InnerFrame);
                var loadCampaignContainer = new GUIFrame(new Rectangle(0, 40, 0, 0), null, setupBox.InnerFrame);

                var campaignSetupUI = new CampaignSetupUI(true, newCampaignContainer, loadCampaignContainer);

                var newCampaignButton = new GUIButton(new Rectangle(0, 0, 120, 20), "New campaign", "", setupBox.InnerFrame);
                newCampaignButton.OnClicked += (btn, obj) =>
                {
                    newCampaignContainer.Visible = true;
                    loadCampaignContainer.Visible = false;
                    return true;
                };

                var loadCampaignButton = new GUIButton(new Rectangle(130, 0, 120, 20), "Load campaign", "", setupBox.InnerFrame);
                loadCampaignButton.OnClicked += (btn, obj) =>
                {
                    newCampaignContainer.Visible = false;
                    loadCampaignContainer.Visible = true;
                    return true;
                };

                loadCampaignContainer.Visible = false;

                campaignSetupUI.StartNewGame = (Submarine sub, string saveName, string mapSeed) =>
                {
                    GameMain.GameSession = new GameSession(new Submarine(sub.FilePath, ""), saveName, GameModePreset.list.Find(g => g.Name == "Campaign"));
                    var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                    campaign.GenerateMap(mapSeed);
                    campaign.SetDelegates();

                    setupBox.Close();

                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    campaign.Map.SelectRandomLocation(true);
                    SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                    campaign.LastSaveID++;

                    if (GameMain.NilMod.CampaignAutoPurchase)
                    {
                        int totalcost = 0;
                        int totalitems = 0;
                        foreach (CampaignPurchase cp in GameMain.NilMod.ServerNewCampaignAutobuy)
                        {
                            if (!cp.isvalid) continue;

                            ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                            for (int i = 0; i < cp.count; i++)
                            {
                                if (campaign.Money >= prefab.Price)

                                    totalitems += 1;
                                totalcost += prefab.Price;
                                campaign.CargoManager.PurchaseItem(prefab);
                            }
                        }

                        GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                            + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                        campaign.LastUpdateID++;
                    }
                };

                campaignSetupUI.LoadGame = (string fileName) =>
                {
                    SaveUtil.LoadGame(fileName);
                    var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                    campaign.LastSaveID++;

                    setupBox.Close();

                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    campaign.Map.SelectRandomLocation(true);

                    if (GameMain.NilMod.CampaignAutoPurchase)
                    {
                        //If money is exactly the same as what we start as, assume its actually a new game that was saved and reloaded!
                        if (campaign.Money == GameMain.NilMod.CampaignInitialMoney)
                        {
                            int totalcost = 0;
                            int totalitems = 0;
                            foreach (CampaignPurchase cp in GameMain.NilMod.ServerNewCampaignAutobuy)
                            {
                                if (!cp.isvalid) continue;

                                ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                                for (int i = 0; i < cp.count; i++)
                                {
                                    if (campaign.Money >= prefab.Price)

                                        totalitems += 1;
                                    totalcost += prefab.Price;
                                    campaign.CargoManager.PurchaseItem(prefab);
                                }
                            }

                            GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                                + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                            campaign.LastUpdateID++;
                        }
                    }
                    //Money is not the default amount on loading, so its likely a game in progress
                    else
                    {
                        int totalcost = 0;
                        int totalitems = 0;
                        foreach (CampaignPurchase cp in GameMain.NilMod.ServerExistingCampaignAutobuy)
                        {
                            if (!cp.isvalid) continue;

                            ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                            for (int i = 0; i < cp.count; i++)
                            {
                                if (campaign.Money >= prefab.Price)
                                {
                                    totalitems += 1;
                                    totalcost += prefab.Price;
                                    campaign.CargoManager.PurchaseItem(prefab);
                                }
                            }
                        }
                        GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                            + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                        campaign.LastUpdateID++;
                    }
                };

                var cancelButton = new GUIButton(new Rectangle(0, 0, 120, 30), "Cancel", Alignment.BottomLeft, "", setupBox.InnerFrame);
                cancelButton.OnClicked += (btn, obj) =>
                {
                    setupBox.Close();
                    int otherModeIndex = 0;
                    for (otherModeIndex = 0; otherModeIndex < GameMain.NetLobbyScreen.ModeList.children.Count; otherModeIndex++)
                    {
                        if (GameMain.NetLobbyScreen.ModeList.children[otherModeIndex].UserData is MultiplayerCampaign) continue;
                        break;
                    }

                    GameMain.NetLobbyScreen.SelectMode(otherModeIndex);
                    return true;
                };
            }
            else
            {
                string[] saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer);
                string Savepath = "Data" + System.IO.Path.DirectorySeparatorChar + "Saves" + System.IO.Path.DirectorySeparatorChar + "Multiplayer" + System.IO.Path.DirectorySeparatorChar + GameMain.NilMod.CampaignDefaultSaveName + ".save";
                if (saveFiles.Contains(Savepath))
                {
                    SaveUtil.LoadGame(Savepath);
                    var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                    campaign.LastSaveID++;
                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    GameMain.GameSession.Map.SelectRandomLocation(true);

                    if (GameMain.Server != null)
                    {
                        if (GameMain.NilMod.CampaignAutoPurchase)
                        {
                            //If money is exactly the same as what we start as, assume its actually a new game that was saved and reloaded!
                            if (campaign.Money == GameMain.NilMod.CampaignInitialMoney)
                            {
                                GameMain.NilMod.CampaignStart = true;
                                int totalcost = 0;
                                int totalitems = 0;
                                foreach (CampaignPurchase cp in GameMain.NilMod.ServerNewCampaignAutobuy)
                                {
                                    if (!cp.isvalid) continue;

                                    ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                                    for (int i = 0; i < cp.count; i++)
                                    {
                                        if (campaign.Money >= prefab.Price)

                                            totalitems += 1;
                                        totalcost += prefab.Price;
                                        campaign.CargoManager.PurchaseItem(prefab);
                                    }
                                }

                                GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                                    + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                                campaign.LastUpdateID++;
                            }
                        }
                        //Money is not the default amount on loading, so its likely a game in progress
                        else
                        {
                            GameMain.NilMod.CampaignStart = false;
                            int totalcost = 0;
                            int totalitems = 0;
                            foreach (CampaignPurchase cp in GameMain.NilMod.ServerExistingCampaignAutobuy)
                            {
                                if (!cp.isvalid) continue;

                                ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                                for (int i = 0; i < cp.count; i++)
                                {
                                    if (campaign.Money >= prefab.Price)
                                    {
                                        totalitems += 1;
                                        totalcost += prefab.Price;
                                        campaign.CargoManager.PurchaseItem(prefab);
                                    }
                                }
                            }
                            GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                                + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                            campaign.LastUpdateID++;
                        }
                    }

                    DebugConsole.NewMessage(@"Campaign save """ + GameMain.NilMod.CampaignDefaultSaveName + @""" automatically loaded!", Color.Cyan);
                    DebugConsole.NewMessage("On Submarine: " + GameMain.GameSession.Submarine.Name, Color.Cyan);
                    DebugConsole.NewMessage("Using Level Seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);
                    DebugConsole.NewMessage(GameMain.NetLobbyScreen.SelectedMode.Name,Color.Cyan);
                }
                else
                {
                    string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, GameMain.NilMod.CampaignDefaultSaveName);
                    Submarine CampaignSub = null;

                    var subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList<Submarine>();

                    if (!GameMain.NilMod.CampaignUseRandomSubmarine)
                    {
                        if (GameMain.NilMod.DefaultSubmarine != "" && subsToShow.Count >= 0)
                        {
                            CampaignSub = subsToShow.Find(s => s.Name.ToLowerInvariant() == GameMain.NilMod.DefaultSubmarine.ToLowerInvariant());

                            if (CampaignSub == null)
                            {
                                subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus) && !s.HasTag(SubmarineTag.Shuttle)).ToList<Submarine>();
                                if (subsToShow.Count > 0)
                                {
                                    CampaignSub = subsToShow[Rand.Range((int)0, subsToShow.Count())];
                                }
                                else
                                {
                                    subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList<Submarine>();
                                    if (subsToShow.Count > 0)
                                    {
                                        DebugConsole.NewMessage("Error - No default submarine found in nilmodsettings, a random submarine has been chosen", Color.Red);
                                        CampaignSub = subsToShow[Rand.Range((int)0, subsToShow.Count())];
                                    }
                                    else
                                    {
                                        DebugConsole.NewMessage("Error - No saved submarines to initialize campaign with.", Color.Red);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus) && !s.HasTag(SubmarineTag.Shuttle)).ToList<Submarine>();

                        if (subsToShow.Count >= 0)
                        {
                            CampaignSub = subsToShow[Rand.Range((int)0, subsToShow.Count())];
                        }
                        else
                        {
                            subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus)).ToList<Submarine>();
                            if (subsToShow.Count >= 0) CampaignSub = subsToShow[Rand.Range((int)0, subsToShow.Count())];
                        }
                    }

                    if (CampaignSub != null)
                    {
                        GameMain.NilMod.CampaignFails = 0;
                        GameMain.GameSession = new GameSession(new Submarine(CampaignSub.FilePath, ""), savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));
                        var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                        campaign.GenerateMap(GameMain.NetLobbyScreen.LevelSeed);
                        campaign.SetDelegates();

                        GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                        GameMain.GameSession.Map.SelectRandomLocation(true);

                        GameMain.NilMod.CampaignStart = true;

                        SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                        campaign.LastSaveID++;

                        if (GameMain.Server != null)
                        {
                            if (GameMain.NilMod.CampaignAutoPurchase)
                            {
                                int totalitems = 0;
                                int totalcost = 0;
                                foreach (CampaignPurchase cp in GameMain.NilMod.ServerNewCampaignAutobuy)
                                {
                                    if (!cp.isvalid) continue;

                                    ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                                    for (int i = 0; i < cp.count; i++)
                                    {
                                        if (campaign.Money >= prefab.Price)
                                        {
                                            totalitems += 1;
                                            totalcost += prefab.Price;
                                            campaign.CargoManager.PurchaseItem(prefab);
                                        }
                                    }
                                }
                                GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                                + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                                campaign.LastUpdateID++;
                            }
                        }



                        DebugConsole.NewMessage(@"New campaign """ + GameMain.NilMod.CampaignDefaultSaveName + @""" automatically started!", Color.Cyan);
                        DebugConsole.NewMessage("On Submarine: " + CampaignSub.Name, Color.Cyan);
                        DebugConsole.NewMessage("Using Level Seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.ToggleCampaignMode(false);
                        GameMain.NetLobbyScreen.SelectedModeIndex = 0;
                        GameMain.NetLobbyScreen.SelectMode(0);
                        //Cancel it here
                    }
                }
            }
        }
#elif SERVER
        public static void StartCampaignSetup(Boolean AutoSetup = false)
        {
            DebugConsole.NewMessage("********* CAMPAIGN SETUP *********", Color.White);
            if(!AutoSetup)
            {
                DebugConsole.ShowQuestionPrompt("Do you want to start a new campaign? Y Yes/N No/C Cancel", (string arg) =>
                {
                    if (arg.ToLowerInvariant() == "y" || arg.ToLowerInvariant() == "yes")
                    {
                        DebugConsole.ShowQuestionPrompt("Enter a save name for the campaign:", (string saveName) =>
                        {
                            if (string.IsNullOrWhiteSpace(saveName)) return;

                            string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveName);
                            GameMain.GameSession = new GameSession(new Submarine(GameMain.NetLobbyScreen.SelectedSub.FilePath, ""), savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));
                            var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                            campaign.GenerateMap(GameMain.NetLobbyScreen.LevelSeed);
                            campaign.SetDelegates();

                            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                            GameMain.GameSession.Map.SelectRandomLocation(true);
                            SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                            campaign.LastSaveID++;

                            DebugConsole.NewMessage("Campaign started!", Color.Cyan);
                        });
                    }
                    else if (arg.ToLowerInvariant() == "n" || arg.ToLowerInvariant() == "no")
                    {
                        string[] saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer);
                        DebugConsole.NewMessage("Saved campaigns:", Color.White);
                        for (int i = 0; i < saveFiles.Length; i++)
                        {
                            DebugConsole.NewMessage("   " + i + ". " + saveFiles[i], Color.White);
                        }
                        DebugConsole.ShowQuestionPrompt("Select a save file to load (0 - " + (saveFiles.Length - 1) + "):", (string selectedSave) =>
                        {
                            int saveIndex = -1;
                            if (!int.TryParse(selectedSave, out saveIndex)) return;

                            SaveUtil.LoadGame(saveFiles[saveIndex]);
                            ((MultiplayerCampaign)GameMain.GameSession.GameMode).LastSaveID++;
                            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                            GameMain.GameSession.Map.SelectRandomLocation(true);

                            DebugConsole.NewMessage("Campaign loaded!", Color.Cyan);
                        });
                    }
                    else
                    {
                        DebugConsole.NewMessage("Campaign cancelled!", Color.Cyan);
                    }
                });
            }
            else
            {
                string[] saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer);
                if(saveFiles.Contains(GameMain.NilMod.CampaignDefaultSaveName))
                {
                    SaveUtil.LoadGame(GameMain.NilMod.CampaignDefaultSaveName);
                    ((MultiplayerCampaign)GameMain.GameSession.GameMode).LastSaveID++;
                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    GameMain.GameSession.Map.SelectRandomLocation(true);

                    DebugConsole.NewMessage(@"Campaign save """ + GameMain.NilMod.CampaignDefaultSaveName + @""" automatically loaded!", Color.Cyan);
                    DebugConsole.NewMessage("On Submarine: " + GameMain.NetLobbyScreen.SelectedSub.Name, Color.Cyan);
                    DebugConsole.NewMessage("Using Level Seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);
                }
                else
                {
                    string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, GameMain.NilMod.CampaignDefaultSaveName);
                    GameMain.GameSession = new GameSession(new Submarine(GameMain.NetLobbyScreen.SelectedSub.FilePath, ""), savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));
                    var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                    campaign.GenerateMap(GameMain.NetLobbyScreen.LevelSeed);
                    campaign.SetDelegates();

                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    GameMain.GameSession.Map.SelectRandomLocation(true);
                    SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                    campaign.LastSaveID++;

                    DebugConsole.NewMessage(@"New campaign """ + GameMain.NilMod.CampaignDefaultSaveName + @""" automatically started!", Color.Cyan);
                    DebugConsole.NewMessage("On Submarine: " + GameMain.NetLobbyScreen.SelectedSub.Name, Color.Cyan);
                    DebugConsole.NewMessage("Using Level Seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);
                }
            }
        }
#endif

        private void SetDelegates()
        {
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
            }
        }

        public override void Start()
        {
            base.Start();

            if (GameMain.Server != null)
            {
                CargoManager.CreateItems();
            }

            lastUpdateID++;
        }


        public override void End(string endMessage = "")
        {
            isRunning = false;

            if (GameMain.Client != null)
            {
                GameMain.GameSession.EndRound("");
#if CLIENT
                GameMain.GameSession.CrewManager.EndRound();
#endif
                return;                
            }
            
            lastUpdateID++;

            bool success = 
                (GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead) ||
                (GameMain.Server.Character != null && !GameMain.Server.Character.IsDead)) && (!GameMain.NilMod.RoundEnded || Submarine.MainSub.AtEndPosition);

            /*if (success)
            {
                if (subsToLeaveBehind == null || leavingSub == null)
                {
                    DebugConsole.ThrowError("Leaving submarine not selected -> selecting the closest one");

                    leavingSub = GetLeavingSub();

                    subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
                }
            }*/

            GameMain.GameSession.EndRound("");

            //TODO: save player inventories between mp campaign rounds

            //remove all items that are in someone's inventory
            foreach (Character c in Character.CharacterList)
            {
                if (c.Inventory == null) continue;
                //Character is inside of a submarine and still alive in some form
                if (c.Submarine != null)
                {
                    CheckSubInventory(c.Inventory.Items);
                }
                //Not on the submarine or dead, just remove everything.
                else
                {
                    foreach (Item item in c.Inventory.Items)
                    {
                        if (item != null)
                        {
                            item.Remove();
                        }
                    }
                }
            }

            //Code for removing items from the level which started in a players inventory, makes a bit of a mess though.
            for (int i = Item.ItemList.ToArray().Length - 1; i >= 0; i--)
            {
                if (Item.ItemList[i] == null) continue;
                if (Item.ItemList[i].Submarine == null) continue;
                if (Item.ItemList[i] != null)
                {
                    if (Item.ItemList[i].HasTag("Starter_Item") && Item.ItemList[i].ContainedItems != null)
                    {
                        CheckSubInventory(Item.ItemList[i].ContainedItems);
                        Item.ItemList[i].Remove();
                    }
                    else if (Item.ItemList[i].HasTag("Starter_Item"))
                    {
                        Item.ItemList[i].Remove();
                    }
                }
            }

#if CLIENT
            GameMain.GameSession.CrewManager.EndRound();
            if(GameSession.inGameInfo != null) GameSession.inGameInfo.ResetGUIListData();
#endif

            if (success)
            {
                GameMain.NilMod.CampaignStart = false;
                //Make the save last longer if its being successful.
                if (GameMain.NilMod.CampaignFails > 0)
                {
                    GameMain.NilMod.CampaignFails -= GameMain.NilMod.CampaignSuccessFailReduction;
                    if (GameMain.NilMod.CampaignFails < 0) GameMain.NilMod.CampaignFails = 0;
                }
                bool atEndPosition = Submarine.MainSub.AtEndPosition;

                /*if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
                    Submarine.MainSub = leavingSub;

                    GameMain.GameSession.Submarine = leavingSub;

                    foreach (Submarine sub in subsToLeaveBehind)
                    {
                        MapEntity.mapEntityList.RemoveAll(e => e.Submarine == sub && e is LinkedSubmarine);
                        LinkedSubmarine.CreateDummy(leavingSub, sub);
                    }
                }*/

                if (atEndPosition)
                {
                    Map.MoveToNextLocation();

                    //select a random location to make sure we've got some destination
                    //to head towards even if the host/clients don't select anything
                    map.SelectRandomLocation(true);
                }

                //Repair submarine walls
                foreach (Structure w in Structure.WallList)
                {
                    for (int i = 0; i < w.SectionCount; i++)
                    {
                        w.AddDamage(i, -100000.0f);
                    }
                }

                //Remove water, replenish oxygen, Extinguish fires
                foreach (Hull hull in Hull.hullList)
                {
                    hull.OxygenPercentage = 100.0f;
                    hull.WaterVolume = 0f;

                    for (int i = hull.FireSources.Count - 1; i >= 0; i--)
                    {
                        hull.FireSources[i].Remove();
                    }
                }

                //Repair devices, electricals and shutdown reactors.
                foreach (Item it in Item.ItemList)
                {
                    if (it.GetComponent<Barotrauma.Items.Components.Powered>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Reactor>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Engine>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Steering>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Radar>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.MiniMap>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.Door>() != null ||
                        it.GetComponent<Barotrauma.Items.Components.RelayComponent>() != null) it.Condition = it.Prefab.Health;

                    if (it.GetComponent<Barotrauma.Items.Components.Reactor>() != null)
                    {
                        //Compatability for BTE.
                        if (it.Prefab.Name == "Diesel-Electric Generator") continue;
                        Barotrauma.Items.Components.Reactor reactor = it.GetComponent<Barotrauma.Items.Components.Reactor>();
                        reactor.AutoTemp = false;
                        reactor.FissionRate = 0;
                        reactor.CoolingRate = 100;
                        reactor.Temperature = 0;
                    }

                    if(it.GetComponent<Barotrauma.Items.Components.PowerContainer>() != null)
                    {
                        var powerContainer = it.GetComponent<Barotrauma.Items.Components.PowerContainer>();
                        powerContainer.Charge = Math.Min(powerContainer.Capacity * 0.9f, powerContainer.Charge);
                    }
                }

                Money += GameMain.NilMod.CampaignSurvivalReward;

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
            else
            {
                GameMain.NilMod.CampaignFails += 1;

                if (GameMain.NilMod.CampaignFails > GameMain.NilMod.CampaignMaxFails)
                {
                    GameMain.NilMod.CampaignFails = 0;
                    CoroutineManager.StartCoroutine(ResetCampaignMode(), "ResetCampaign");

                    foreach (Client c in GameMain.Server.ConnectedClients)
                    {
                        NilMod.NilModEventChatter.SendServerMessage("Campaign Info: No more chances remain, the campaign is lost!", c);
                        NilMod.NilModEventChatter.SendServerMessage("Starting a new campaign...", c);
                    }
                    GameMain.NetworkMember.AddChatMessage("Campaign Info: No more chances remain, the campaign is lost!", ChatMessageType.Server, "", null);
                    GameMain.NetworkMember.AddChatMessage("Starting a new campaign...", ChatMessageType.Server, "", null);
                }
                else
                {
                    if((GameMain.NilMod.CampaignMaxFails - GameMain.NilMod.CampaignFails) < 3)
                    {
                        foreach(Client c in GameMain.Server.ConnectedClients)
                        {
                            NilMod.NilModEventChatter.SendServerMessage("Campaign Info: There are " + (GameMain.NilMod.CampaignMaxFails - GameMain.NilMod.CampaignFails) + " Attempts remaining on this save unless you start pulling off some success!", c);
                        }
                        GameMain.NetworkMember.AddChatMessage("Campaign: There are " + (GameMain.NilMod.CampaignMaxFails - GameMain.NilMod.CampaignFails) + " Attempts remaining on this save unless you start pulling off some success!", ChatMessageType.Server, "", null);
                    }
                    //Reload the game and such
                    SaveUtil.LoadGame(GameMain.GameSession.SavePath);
#if CLIENT
                    GameMain.NetLobbyScreen.modeList.Select(2, true);
#endif
                    GameMain.GameSession.Map.SelectRandomLocation(true);
                    LastSaveID += 1;
                }
            }

            //If its campaign start, add the starter items to the buy menu
            if (GameMain.NilMod.CampaignAutoPurchase)
            {
                var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);

                if (GameMain.NilMod.CampaignStart)
                {
                    int totalitems = 0;
                    int totalcost = 0;
                    foreach (CampaignPurchase cp in GameMain.NilMod.ServerNewCampaignAutobuy)
                    {
                        if (!cp.isvalid) continue;

                        ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                        for (int i = 0; i < cp.count; i++)
                        {
                            if (campaign.Money >= prefab.Price)
                            {
                                totalitems += 1;
                                totalcost += prefab.Price;
                                campaign.CargoManager.PurchaseItem(prefab);
                            }
                        }
                    }
                    GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                                + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                    LastUpdateID++;
                }
                //If its a round that wasn't the first, buy the mid-round items!
                else
                {
                    int totalitems = 0;
                    int totalcost = 0;
                    foreach (CampaignPurchase cp in GameMain.NilMod.ServerExistingCampaignAutobuy)
                    {
                        if (!cp.isvalid) continue;

                        ItemPrefab prefab = (ItemPrefab)ItemPrefab.Find(cp.itemprefab);
                        for (int i = 0; i < cp.count; i++)
                        {
                            if (campaign.Money >= prefab.Price)
                            {
                                totalitems += 1;
                                totalcost += prefab.Price;
                                campaign.CargoManager.PurchaseItem(prefab);
                            }
                        }
                    }
                    GameMain.Server.ServerLog.WriteLine("AUTOBUY: Added " + totalitems + " Items costing "
                                + totalcost + " money.", ServerLog.MessageType.ServerMessage);
                    LastUpdateID++;
                }
            }
        }

        public void CheckSubInventory(Item[] items)
        {
            foreach (Item item in items)
            {
                if (item != null)
                {
                    if (!item.HasTag("Starter_Item") && item.ContainedItems != null)
                    {
                        CheckSubInventory(item.ContainedItems);
                        item.Drop();
                        item.FindHull();
                    }
                    else if(!item.HasTag("Starter_Item"))
                    {
                        item.Drop();
                        item.FindHull();
                    }
                    else if(item.HasTag("Starter_Item") && item.ContainedItems != null)
                    {
                        CheckSubInventory(item.ContainedItems);
                        item.Remove();
                    }
                    else
                    {
                        item.Remove();
                    }
                }
            }
        }

        private IEnumerable<object> ResetCampaignMode()
        {
            ushort newsaveid = LastSaveID +=1;
            float waittime = 0f;
            GameMain.NetLobbyScreen.SelectedModeIndex = 0;
#if Client
            GameMain.NetLobbyScreen.modeList.Select(0, true);
#endif

            GameMain.NetLobbyScreen.ToggleCampaignMode(false);
            SaveUtil.DeleteSave(GameMain.GameSession.SavePath);

            GameMain.NilMod.CampaignStart = true;

            if (GameMain.Server.AutoRestart)
            {
                GameMain.Server.AutoRestartTimer += 11f;
            }

            while (waittime < 11f)
            {
                waittime += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            StartCampaignSetup(true);
            GameMain.NetLobbyScreen.SelectedModeIndex = 2;
            SetDelegates();
            LastSaveID = newsaveid;

            if (GameMain.Server.AutoRestart)
            {
                GameMain.Server.AutoRestartTimer = GameMain.Server.AutoRestartInterval;
            }

            yield return CoroutineStatus.Success;
        }

            public static MultiplayerCampaign LoadNew(XElement element)
        {
            MultiplayerCampaign campaign = new MultiplayerCampaign(GameModePreset.list.Find(gm => gm.Name == "Campaign"), null);
            campaign.Load(element);            
            campaign.SetDelegates();

            return campaign;
        }

        public void Load(XElement element)
        {
            Money = element.GetAttributeInt("money", 0);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "map":
                        if (map == null)
                        {
                            map = Map.LoadNew(subElement);
                        }
                        else
                        {
                            map.Load(subElement);
                        }
                        break;
                }
            }
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("MultiPlayerCampaign");
            modeElement.Add(new XAttribute("money", Money));            
            Map.Save(modeElement);
            element.Add(modeElement);

            lastSaveID++;
        }

        public void ServerWrite(NetBuffer msg, Client c)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.Write(lastUpdateID);
            msg.Write(lastSaveID);
            msg.Write(map.Seed);
            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);

            msg.Write(Money);

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (ItemPrefab ip in CargoManager.PurchasedItems)
            {
                msg.Write((UInt16)MapEntityPrefab.List.IndexOf(ip));
            }
        }
        
#if CLIENT
        public static void ClientRead(NetBuffer msg)
        {
            //static because we may need to instantiate the campaign if it hasn't been done yet

            UInt16 updateID         = msg.ReadUInt16();
            UInt16 saveID           = msg.ReadUInt16();
            string mapSeed          = msg.ReadString();
            UInt16 currentLocIndex  = msg.ReadUInt16();
            UInt16 selectedLocIndex = msg.ReadUInt16();

            int money = msg.ReadInt32();

            UInt16 purchasedItemCount = msg.ReadUInt16();
            List<ItemPrefab> purchasedItems = new List<ItemPrefab>();
            for (int i = 0; i<purchasedItemCount; i++)
            {
                UInt16 itemPrefabIndex = msg.ReadUInt16();
                purchasedItems.Add(MapEntityPrefab.List[itemPrefabIndex] as ItemPrefab);
            }

            MultiplayerCampaign campaign = GameMain.GameSession?.GameMode as MultiplayerCampaign;
            if (campaign == null || mapSeed != campaign.Map.Seed)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);
                
                GameMain.GameSession = new GameSession(null, savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));

                campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                campaign.GenerateMap(mapSeed);
            }

            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            if (NetIdUtils.IdMoreRecent(campaign.lastUpdateID, updateID)) return;
            
            //server has a newer save file
            if (NetIdUtils.IdMoreRecent(saveID, campaign.PendingSaveID))
            {
                /*//stop any active campaign save transfers, they're outdated now
                List<FileReceiver.FileTransferIn> saveTransfers = 
                    GameMain.Client.FileReceiver.ActiveTransfers.FindAll(t => t.FileType == FileTransferType.CampaignSave);

                foreach (var transfer in saveTransfers)
                {
                    GameMain.Client.FileReceiver.StopTransfer(transfer);                    
                }

                GameMain.Client.RequestFile(FileTransferType.CampaignSave, null, null);*/
                campaign.PendingSaveID = saveID;
            }
            //we've got the latest save file
            else if (!NetIdUtils.IdMoreRecent(saveID, campaign.lastSaveID))
            {
                campaign.Map.SetLocation(currentLocIndex == UInt16.MaxValue ? -1 : currentLocIndex);
                campaign.Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);

                campaign.Money = money;
                campaign.CargoManager.SetPurchasedItems(purchasedItems);

                campaign.lastUpdateID = updateID;
            }
        }
#endif

        public void ClientWrite(NetBuffer msg)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);
            
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (ItemPrefab ip in CargoManager.PurchasedItems)
            {
                msg.Write((UInt16)MapEntityPrefab.List.IndexOf(ip));
            }
        }

        public void ServerRead(NetBuffer msg, Client sender)
        {
            UInt16 selectedLocIndex = msg.ReadUInt16();
            UInt16 purchasedItemCount = msg.ReadUInt16();

            List<ItemPrefab> purchasedItems = new List<ItemPrefab>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                UInt16 itemPrefabIndex = msg.ReadUInt16();
                purchasedItems.Add(MapEntityPrefab.List[itemPrefabIndex] as ItemPrefab);
            }

            if (!sender.HasPermission(ClientPermissions.ManageCampaign))
            {
                DebugConsole.ThrowError("Client \""+sender.Name+"\" does not have a permission to manage the campaign");
                return;
            }

            Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);

            List<ItemPrefab> currentItems = new List<ItemPrefab>(CargoManager.PurchasedItems);
            foreach (ItemPrefab ip in currentItems)
            {
                CargoManager.SellItem(ip);
            }

            foreach (ItemPrefab ip in purchasedItems)
            {
                CargoManager.PurchaseItem(ip);
            }
        }
    }
}
