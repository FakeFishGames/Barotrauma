using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;
using Lidgren.Network;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
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

        private static byte currentCampaignID;

        private List<CharacterCampaignData> characterData = new List<CharacterCampaignData>();

        public byte CampaignID
        {
            get; private set;
        }

        public MultiPlayerCampaign(GameModePreset preset, object param) : 
            base(preset, param)
        {
            currentCampaignID++;
            CampaignID = currentCampaignID;
        }
        
        private void SetDelegates()
        {
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
                Map.OnMissionSelected += (loc, mission) => { LastUpdateID++; };
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

        public CharacterCampaignData GetHostCharacterData()
        {
            return characterData.Find(cd => cd.IsHostCharacter);
        }

        public void AssignPlayerCharacterInfos(IEnumerable<Client> connectedClients, bool assignHost)
        {
            foreach (Client client in connectedClients)
            {
                if (client.SpectateOnly && GameMain.Server.AllowSpectating) continue;
                var matchingData = GetClientCharacterData(client);
                if (matchingData != null) client.CharacterInfo = matchingData.CharacterInfo;
            }

            if (assignHost)
            {
                var hostCharacterData = GetHostCharacterData();
                if (hostCharacterData?.CharacterInfo != null)
                {
                    GameMain.Server.CharacterInfo = hostCharacterData.CharacterInfo;
                }
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

        public override void Start()
        {
            base.Start();            
            lastUpdateID++;
        }


        protected override void WatchmanInteract(Character watchman, Character interactor)
        {
            if ((watchman.Submarine == Level.Loaded.StartOutpost && !Submarine.MainSub.AtStartPosition) ||
                (watchman.Submarine == Level.Loaded.EndOutpost && !Submarine.MainSub.AtEndPosition))
            {
                if (GameMain.Server != null)
                {
                    CreateDialog(new List<Character> { watchman }, "WatchmanInteractNoLeavingSub", 5.0f);
                }
                return;
            }

            bool hasPermissions = true;
            if (GameMain.Server != null)
            {
                var client = GameMain.Server.ConnectedClients.Find(c => c.Character == interactor);
                hasPermissions = client != null &&
                    (client.HasPermission(ClientPermissions.EndRound) || client.HasPermission(ClientPermissions.ManageCampaign));
                    CreateDialog(new List<Character> { watchman }, hasPermissions ? "WatchmanInteract" : "WatchmanInteractNotAllowed", 1.0f);
            }
#if CLIENT
            else if (GameMain.Client != null && interactor == Character.Controlled && hasPermissions)
            {
                var msgBox = new GUIMessageBox("", TextManager.Get("CampaignEnterOutpostPrompt")
                    .Replace("[locationname]", Submarine.MainSub.AtStartPosition ? Map.CurrentLocation.Name : Map.SelectedLocation.Name),
                    new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                msgBox.Buttons[0].OnClicked = (btn, userdata) =>
                {
                    GameMain.Client.RequestRoundEnd();
                    return true;
                };
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[1].OnClicked += msgBox.Close;
            
            }
#endif
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
            
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                if (c.HasSpawned)
                {
                    //client has spawned this round -> remove old data (and replace with new one if the client still has an alive character)
                    characterData.RemoveAll(cd => cd.MatchesClient(c));
                }
                
                if (c.Character?.Info != null && !c.Character.IsDead)
                {
                    characterData.Add(new CharacterCampaignData(c));
                }
            }

#if CLIENT
            GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
#endif

            if (GameMain.Server.Character != null)
            {
                characterData.RemoveAll(cd => cd.IsHostCharacter);
                if (!GameMain.Server.Character.IsDead)
                {
                    var hostCharacterData = new CharacterCampaignData(GameMain.Server);
                    characterData.Add(hostCharacterData);
#if CLIENT
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(hostCharacterData.CharacterInfo);
#endif
                }
            }

            //remove all items that are in someone's inventory
            foreach (Character c in Character.CharacterList)
            {
                c.Inventory?.DeleteAllItems();
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
                    map.MoveToNextLocation();

                    //select a random location to make sure we've got some destination
                    //to head towards even if the host/clients don't select anything
                    map.SelectRandomLocation(true);
                }
                map.ProgressWorld();

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
        }
        
        public static MultiPlayerCampaign LoadNew(XElement element)
        {
            MultiPlayerCampaign campaign = new MultiPlayerCampaign(GameModePreset.list.Find(gm => gm.Name == "Campaign"), null);
            campaign.Load(element);
            campaign.SetDelegates();
            
            return campaign;
        }

        public static string GetCharacterDataSavePath(string savePath)
        {
            return Path.Combine(SaveUtil.MultiplayerSaveFolder, Path.GetFileNameWithoutExtension(savePath) + "_CharacterData.xml");
        }

        public string GetCharacterDataSavePath()
        {
            return GetCharacterDataSavePath(GameMain.GameSession.SavePath);
        }

        public void Load(XElement element)
        {
            Money = element.GetAttributeInt("money", 0);
            CheatsEnabled = element.GetAttributeBool("cheatsenabled", false);
            if (CheatsEnabled)
            {
                DebugConsole.CheatsEnabled = true;
                if (GameMain.Config.UseSteam && !SteamAchievementManager.CheatsEnabled)
                {
                    SteamAchievementManager.CheatsEnabled = true;
#if CLIENT
                    new GUIMessageBox("Cheats enabled", "Cheat commands have been enabled on the server. You will not receive Steam Achievements until you restart the game.");       
#else
                    DebugConsole.NewMessage("Cheat commands have been enabled.", Color.Red);
#endif
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "map":
                        if (map == null)
                        {
                            //map not created yet, loading this campaign for the first time
                            map = Map.LoadNew(subElement);
                        }
                        else
                        {
                            //map already created, update it
                            //if we're not downloading the initial save file (LastSaveID > 0), 
                            //show notifications about location type changes
                            map.Load(subElement, LastSaveID > 0);
                        }
                        break;
                }
            }

            if (GameMain.Server != null)
            {
                characterData.Clear();
                string characterDataPath = GetCharacterDataSavePath();
                var characterDataDoc = XMLExtensions.TryLoadXml(characterDataPath);
                if (characterDataDoc?.Root == null) return;
                foreach (XElement subElement in characterDataDoc.Root.Elements())
                {
                    characterData.Add(new CharacterCampaignData(subElement));
                }
#if CLIENT
                var hostCharacterData = GetHostCharacterData();
                if (hostCharacterData?.CharacterInfo != null)
                {
                    GameMain.NetLobbyScreen.SetCampaignCharacterInfo(hostCharacterData.CharacterInfo);
                }
#endif
            }
        }

        public override void Save(XElement element)
        {
            XElement modeElement = new XElement("MultiPlayerCampaign",
                new XAttribute("money", Money),
                new XAttribute("cheatsenabled", CheatsEnabled));
            Map.Save(modeElement);
            element.Add(modeElement);

            //save character data to a separate file
            string characterDataPath = GetCharacterDataSavePath();
            XDocument characterDataDoc = new XDocument(new XElement("CharacterData"));
            foreach (CharacterCampaignData cd in characterData)
            {
                characterDataDoc.Root.Add(cd.Save());
            }
            try
            {
                characterDataDoc.Save(characterDataPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving multiplayer campaign characters to \"" + characterDataPath + "\" failed!", e);
            }

            lastSaveID++;
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
            msg.Write(map.SelectedMissionIndex == -1 ? byte.MaxValue : (byte)map.SelectedMissionIndex);

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
            byte selectedMissionIndex = msg.ReadByte();
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
            if (Map.SelectedConnection != null)
            {
                Map.SelectMission(selectedMissionIndex);
            }

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
