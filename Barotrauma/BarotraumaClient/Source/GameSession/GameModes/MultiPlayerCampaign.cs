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
            GUIFrame background = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker");

            GUIFrame setupBox = new GUIFrame(new RectTransform(new Vector2(0.25f, 0.45f), background.RectTransform, Anchor.Center) { MinSize = new Point(500, 500) });
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), setupBox.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), paddedFrame.RectTransform,Anchor.TopCenter),
                TextManager.Get("CampaignSetup"), font: GUI.LargeFont);

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.07f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) }, isHorizontal: true)
            {
                RelativeSpacing = 0.02f
            };

            var campaignContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.8f), paddedFrame.RectTransform, Anchor.BottomLeft), style: null);
            
            var newCampaignContainer = new GUIFrame(new RectTransform(Vector2.One, campaignContainer.RectTransform, Anchor.BottomLeft), style: null);
            var loadCampaignContainer = new GUIFrame(new RectTransform(Vector2.One, campaignContainer.RectTransform, Anchor.BottomLeft), style: null);

            var campaignSetupUI = new CampaignSetupUI(true, newCampaignContainer, loadCampaignContainer);

            var newCampaignButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), buttonContainer.RectTransform),
                TextManager.Get("NewCampaign"))
            {
                OnClicked = (btn, obj) =>
                {
                    newCampaignContainer.Visible = true;
                    loadCampaignContainer.Visible = false;
                    return true;
                }
            };

            var loadCampaignButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.00f), buttonContainer.RectTransform),
                TextManager.Get("LoadCampaign"))
            {
                OnClicked = (btn, obj) =>
                {
                    newCampaignContainer.Visible = false;
                    loadCampaignContainer.Visible = true;
                    return true;
                }
            };

            loadCampaignContainer.Visible = false;

            campaignSetupUI.StartNewGame = (Submarine sub, string saveName, string mapSeed) =>
            {
                GameMain.GameSession = new GameSession(new Submarine(sub.FilePath, ""), saveName, 
                    GameModePreset.List.Find(g => g.Identifier == "multiplayercampaign"));
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
                if (!(GameMain.GameSession.GameMode is MultiPlayerCampaign))
                {
                    DebugConsole.ThrowError("Failed to load the campaign. The save file appears to be for a single player campaign.");
                    return;
                }

                var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);
                campaign.LastSaveID++;

                background.Visible = false;

                GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                campaign.Map.SelectRandomLocation(true);
            };

            var cancelButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.05f), paddedFrame.RectTransform, Anchor.BottomLeft), TextManager.Get("Cancel"))
            {
                OnClicked = (btn, obj) =>
                {
                    //find the first mode that's not multiplayer campaign and switch to that
                    background.Visible = false;
                    int otherModeIndex = 0;
                    for (otherModeIndex = 0; otherModeIndex < GameMain.NetLobbyScreen.ModeList.Content.CountChildren; otherModeIndex++)
                    {
                        if (GameMain.NetLobbyScreen.ModeList.Content.GetChild(otherModeIndex).UserData is MultiPlayerCampaign) continue;
                        break;
                    }

                    GameMain.NetLobbyScreen.SelectMode(otherModeIndex);
                    return true;
                }
            };

            return background;
        }

        public void ClientWrite(NetBuffer msg)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);
            msg.Write(map.SelectedMissionIndex == -1 ? byte.MaxValue : (byte)map.SelectedMissionIndex);

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (PurchasedItem pi in CargoManager.PurchasedItems)
            {
                msg.Write((UInt16)MapEntityPrefab.List.IndexOf(pi.ItemPrefab));
                msg.Write((UInt16)pi.Quantity);
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
            byte selectedMissionIndex = msg.ReadByte();

            int money = msg.ReadInt32();

            UInt16 purchasedItemCount = msg.ReadUInt16();
            List<PurchasedItem> purchasedItems = new List<PurchasedItem>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                UInt16 itemPrefabIndex = msg.ReadUInt16();
                UInt16 itemQuantity = msg.ReadUInt16();
                purchasedItems.Add(new PurchasedItem(MapEntityPrefab.List[itemPrefabIndex] as ItemPrefab, itemQuantity));
            }

            bool hasCharacterData = msg.ReadBoolean();
            CharacterInfo myCharacterInfo = null;
            if (hasCharacterData)
            {
                myCharacterInfo = CharacterInfo.ClientRead(Character.HumanConfigFile, msg);
            }
            
            MultiPlayerCampaign campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (campaign == null || campaignID != campaign.CampaignID)
            {
                string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer);

                GameMain.GameSession = new GameSession(null, savePath,
                    GameModePreset.List.Find(g => g.Identifier == "multiplayercampaign"));

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
                campaign.Map.SelectMission(selectedMissionIndex);

                campaign.Money = money;
                campaign.CargoManager.SetPurchasedItems(purchasedItems);

                if (myCharacterInfo != null)
                {
                    GameMain.NetworkMember.CharacterInfo = myCharacterInfo;
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(myCharacterInfo);
                }
                else
                {
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                }

                campaign.lastUpdateID = updateID;
            }
        }
    }


}
