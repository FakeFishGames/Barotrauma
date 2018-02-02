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
            var setupBox = new GUIMessageBox("Campaign Setup", "", new string [0], 500, 500);
            setupBox.InnerFrame.Padding = new Vector4(20.0f, 80.0f, 20.0f, 20.0f);

            var newCampaignContainer = new GUIFrame(new Rectangle(0,40,0,0), null, setupBox.InnerFrame);
            var loadCampaignContainer = new GUIFrame(new Rectangle(0, 40, 0, 0), null, setupBox.InnerFrame);

            var campaignSetupUI = new CampaignSetupUI(true, newCampaignContainer, loadCampaignContainer);

            var newCampaignButton = new GUIButton(new Rectangle(0,0,120,20), "New campaign", "", setupBox.InnerFrame);
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
            };

            campaignSetupUI.LoadGame = (string fileName) =>
            {
                SaveUtil.LoadGame(fileName);
                var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                campaign.LastSaveID++;

                setupBox.Close();

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);        
                campaign.Map.SelectRandomLocation(true);
            };

            var cancelButton = new GUIButton(new Rectangle(0,0,120,30), "Cancel", Alignment.BottomLeft, "", setupBox.InnerFrame);
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
                if(saveFiles.Contains(GameMain.NilMod.CampaignSaveName))
                {
                    SaveUtil.LoadGame(GameMain.NilMod.CampaignSaveName);
                    ((MultiplayerCampaign)GameMain.GameSession.GameMode).LastSaveID++;
                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    GameMain.GameSession.Map.SelectRandomLocation(true);

                    DebugConsole.NewMessage(@"Campaign save """ + GameMain.NilMod.CampaignSaveName + @""" automatically loaded!", Color.Cyan);
                    DebugConsole.NewMessage("On Submarine: " + GameMain.NetLobbyScreen.SelectedSub.Name, Color.Cyan);
                    DebugConsole.NewMessage("Using Level Seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);
                }
                else
                {
                    string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, GameMain.NilMod.CampaignSaveName);
                    GameMain.GameSession = new GameSession(new Submarine(GameMain.NetLobbyScreen.SelectedSub.FilePath, ""), savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));
                    var campaign = ((MultiplayerCampaign)GameMain.GameSession.GameMode);
                    campaign.GenerateMap(GameMain.NetLobbyScreen.LevelSeed);
                    campaign.SetDelegates();

                    GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    GameMain.GameSession.Map.SelectRandomLocation(true);
                    SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                    campaign.LastSaveID++;

                    DebugConsole.NewMessage(@"New campaign """ + GameMain.NilMod.CampaignSaveName + @""" automatically started!", Color.Cyan);
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
                GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead) ||
                (GameMain.Server.Character != null && !GameMain.Server.Character.IsDead);

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
                foreach (Item item in c.Inventory.Items)
                {
                    if (item != null) item.Remove();
                }
            }

#if CLIENT
            GameMain.GameSession.CrewManager.EndRound();
#endif

            if (success)
            {
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

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
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
