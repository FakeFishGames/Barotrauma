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
        public static void StartCampaignSetup()
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
                campaign.map.OnLocationSelected += (loc, connection) => { campaign.LastUpdateID++; };

                setupBox.Close();

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                
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

#endif

        public override void Start()
        {
            base.Start();
            
            lastUpdateID++;
        }


        public override void End(string endMessage = "")
        {
            isRunning = false;

            if (GameMain.Server != null)
            {
                lastUpdateID++;

                bool success = 
                    GameMain.Server.ConnectedClients.Any(c => c.inGame && c.Character != null && !c.Character.IsDead) ||
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
            }
            else
            {
                GameMain.GameSession.EndRound("");
            }
        }

        public static MultiplayerCampaign Load(XElement element)
        {
            MultiplayerCampaign campaign = new MultiplayerCampaign(GameModePreset.list.Find(gm => gm.Name == "Campaign"), null);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
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

            campaign.map.OnLocationSelected += (loc, connection) => { campaign.LastUpdateID++; };

            //campaign.savedOnStart = true;
            return campaign;
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
                //stop any active campaign save transfers, they're outdated now
                List<FileReceiver.FileTransferIn> saveTransfers = 
                    GameMain.Client.FileReceiver.ActiveTransfers.FindAll(t => t.FileType == FileTransferType.CampaignSave);

                foreach (var transfer in saveTransfers)
                {
                    GameMain.Client.FileReceiver.StopTransfer(transfer);                    
                }

                GameMain.Client.RequestFile(FileTransferType.CampaignSave, null, null);
                campaign.PendingSaveID = saveID;
            }
            //we've got the latest save file
            else if (!NetIdUtils.IdMoreRecent(saveID, campaign.lastSaveID))
            {
                campaign.Map.SetLocation(currentLocIndex == UInt16.MaxValue ? -1 : currentLocIndex);
                campaign.Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
                campaign.lastUpdateID = updateID;
            }
        }

#endif
    }
}
