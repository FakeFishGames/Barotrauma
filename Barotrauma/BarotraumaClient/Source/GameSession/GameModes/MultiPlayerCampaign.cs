using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        public static GUIComponent StartCampaignSetup()
        {
            GUIFrame background = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f, null);

            GUIFrame setupBox = new GUIFrame(new Rectangle(0, 0, 500, 500), null, Alignment.Center, "", background);
            setupBox.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            new GUITextBlock(new Rectangle(0, 0, 10, 10), "Campaign Setup", "", setupBox, GUI.LargeFont);
            setupBox.Padding = new Vector4(20.0f, 80.0f, 20.0f, 20.0f);

            var newCampaignContainer = new GUIFrame(new Rectangle(0, 40, 0, 0), null, setupBox);
            var loadCampaignContainer = new GUIFrame(new Rectangle(0, 40, 0, 0), null, setupBox);

            var campaignSetupUI = new CampaignSetupUI(true, newCampaignContainer, loadCampaignContainer);

            var newCampaignButton = new GUIButton(new Rectangle(0, 0, 120, 20), "New campaign", "", setupBox);
            newCampaignButton.OnClicked += (btn, obj) =>
            {
                newCampaignContainer.Visible = true;
                loadCampaignContainer.Visible = false;
                return true;
            };

            var loadCampaignButton = new GUIButton(new Rectangle(130, 0, 120, 20), "Load campaign", "", setupBox);
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
                var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);
                campaign.GenerateMap(mapSeed);
                campaign.SetDelegates();

                background.Visible = false;

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                campaign.Map.SelectRandomLocation(true);
                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                campaign.LastSaveID++;
            };

            campaignSetupUI.LoadGame = (string fileName) =>
            {
                SaveUtil.LoadGame(fileName);
                var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);
                campaign.LastSaveID++;

                background.Visible = false;

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                campaign.Map.SelectRandomLocation(true);
            };

            var cancelButton = new GUIButton(new Rectangle(0, 0, 120, 30), "Cancel", Alignment.BottomLeft, "", setupBox);
            cancelButton.OnClicked += (btn, obj) =>
            {
                background.Visible = false;
                int otherModeIndex = 0;
                for (otherModeIndex = 0; otherModeIndex < GameMain.NetLobbyScreen.ModeList.children.Count; otherModeIndex++)
                {
                    if (GameMain.NetLobbyScreen.ModeList.children[otherModeIndex].UserData is MultiPlayerCampaign) continue;
                    break;
                }

                GameMain.NetLobbyScreen.SelectMode(otherModeIndex);
                return true;
            };

            return background;
        }

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

        //static because we may need to instantiate the campaign if it hasn't been done yet
        public static void ClientRead(NetBuffer msg)
        {
            byte campaignID = msg.ReadByte();
            UInt16 updateID = msg.ReadUInt16();
            UInt16 saveID = msg.ReadUInt16();
            string mapSeed = msg.ReadString();
            UInt16 currentLocIndex = msg.ReadUInt16();
            UInt16 selectedLocIndex = msg.ReadUInt16();

            int money = msg.ReadInt32();

            UInt16 purchasedItemCount = msg.ReadUInt16();
            List<ItemPrefab> purchasedItems = new List<ItemPrefab>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                UInt16 itemPrefabIndex = msg.ReadUInt16();
                purchasedItems.Add(MapEntityPrefab.List[itemPrefabIndex] as ItemPrefab);
            }

            MultiPlayerCampaign campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (campaign == null || campaignID != campaign.CampaignID)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);

                GameMain.GameSession = new GameSession(null, savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));

                campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);
                campaign.CampaignID = campaignID;
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
    }


}
