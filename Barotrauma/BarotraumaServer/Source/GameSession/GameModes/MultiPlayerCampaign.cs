using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System;
using System.Linq;
using System.Xml.Linq;
using Lidgren.Network;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        public static void StartNewCampaign(string savePath, string subName, string seed)
        {
            if (string.IsNullOrWhiteSpace(savePath)) return;

            GameMain.GameSession = new GameSession(new Submarine(subName, ""), savePath, GameModePreset.list.Find(g => g.Name == "Campaign"));
            var campaign = ((MultiPlayerCampaign)GameMain.GameSession.GameMode);
            campaign.GenerateMap(seed);
            campaign.SetDelegates();

            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            GameMain.GameSession.Map.SelectRandomLocation(true);
            SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            campaign.LastSaveID++;

            DebugConsole.NewMessage("Campaign started!", Color.Cyan);
            DebugConsole.NewMessage(GameMain.GameSession.Map.CurrentLocation.Name + " -> " + GameMain.GameSession.Map.SelectedLocation.Name, Color.Cyan);
        }

        public static void LoadCampaign(string selectedSave)
        {
            SaveUtil.LoadGame(selectedSave);
            ((MultiPlayerCampaign)GameMain.GameSession.GameMode).LastSaveID++;
            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            GameMain.GameSession.Map.SelectRandomLocation(true);

            DebugConsole.NewMessage("Campaign loaded!", Color.Cyan);
            DebugConsole.NewMessage(GameMain.GameSession.Map.CurrentLocation.Name + " -> " + GameMain.GameSession.Map.SelectedLocation.Name, Color.Cyan);
        }

        public static void StartCampaignSetup()
        {
            DebugConsole.NewMessage("********* CAMPAIGN SETUP *********", Color.White);
            DebugConsole.ShowQuestionPrompt("Do you want to start a new campaign? Y/N", (string arg) =>
            {
                if (arg.ToLowerInvariant() == "y" || arg.ToLowerInvariant() == "yes")
                {
                    DebugConsole.ShowQuestionPrompt("Enter a save name for the campaign:", (string saveName) =>
                    {
                        StartNewCampaign(saveName, GameMain.NetLobbyScreen.SelectedSub.FilePath, GameMain.NetLobbyScreen.LevelSeed);
                    });
                }
                else
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

                        LoadCampaign(saveFiles[saveIndex]);
                    });
                }
            });
        }


        partial void SetDelegates()
        {
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
            }
        }

        public void DiscardClientCharacterData(Client client)
        {
            characterData.RemoveAll(cd => cd.MatchesClient(client));
        }

        public CharacterCampaignData GetClientCharacterData(Client client)
        {
            return characterData.Find(cd => cd.MatchesClient(client));
        }

        public void AssignClientCharacterInfos(IEnumerable<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                var matchingData = GetClientCharacterData(client);
                if (matchingData != null) client.CharacterInfo = matchingData.CharacterInfo;
            }
        }

        public Dictionary<Client, Job> GetAssignedJobs(IEnumerable<Client> connectedClients)
        {
            var assignedJobs = new Dictionary<Client, Job>();
            foreach (Client client in connectedClients)
            {
                var matchingData = GetClientCharacterData(client);
                if (matchingData != null) assignedJobs.Add(client, matchingData.CharacterInfo.Job);
            }
            return assignedJobs;
        }


        public void ServerWrite(NetBuffer msg, Client c)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            msg.Write(CampaignID);
            msg.Write(lastUpdateID);
            msg.Write(lastSaveID);
            msg.Write(map.Seed);
            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex);

            msg.Write(Money);

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (PurchasedItem pi in CargoManager.PurchasedItems)
            {
                msg.Write((UInt16)MapEntityPrefab.List.IndexOf(pi.ItemPrefab));
                msg.Write((UInt16)pi.Quantity);
            }

            var characterData = GetClientCharacterData(c);
            if (characterData?.CharacterInfo == null)
            {
                msg.Write(false);
            }
            else
            {
                msg.Write(true);
                characterData.CharacterInfo.ServerWrite(msg);
            }
        }

        public void ServerRead(NetBuffer msg, Client sender)
        {
            UInt16 selectedLocIndex = msg.ReadUInt16();
            UInt16 purchasedItemCount = msg.ReadUInt16();

            List<PurchasedItem> purchasedItems = new List<PurchasedItem>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                UInt16 itemPrefabIndex = msg.ReadUInt16();
                UInt16 itemQuantity = msg.ReadUInt16();
                purchasedItems.Add(new PurchasedItem(MapEntityPrefab.List[itemPrefabIndex] as ItemPrefab, itemQuantity));
            }

            if (!sender.HasPermission(ClientPermissions.ManageCampaign))
            {
                DebugConsole.ThrowError("Client \"" + sender.Name + "\" does not have a permission to manage the campaign");
                return;
            }

            Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);

            List<PurchasedItem> currentItems = new List<PurchasedItem>(CargoManager.PurchasedItems);
            foreach (PurchasedItem pi in currentItems)
            {
                CargoManager.SellItem(pi.ItemPrefab, pi.Quantity);
            }

            foreach (PurchasedItem pi in purchasedItems)
            {
                CargoManager.PurchaseItem(pi.ItemPrefab, pi.Quantity);
            }
        }
    }
}
