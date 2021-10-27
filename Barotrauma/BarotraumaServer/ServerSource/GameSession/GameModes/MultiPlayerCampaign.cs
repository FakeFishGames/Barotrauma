using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MultiPlayerCampaign : CampaignMode
    {
        private readonly List<CharacterCampaignData> characterData = new List<CharacterCampaignData>();

        private bool forceMapUI;
        public bool ForceMapUI
        {
            get { return forceMapUI; }
            set
            {
                if (forceMapUI == value) { return; }
                forceMapUI = value;
                LastUpdateID++;
            }
        }

        public bool GameOver { get; private set; }

        class SavedExperiencePoints
        {
            public readonly ulong SteamID;
            public readonly string EndPoint;
            public readonly int ExperiencePoints;

            public SavedExperiencePoints(Client client)
            {
                SteamID = client.SteamID;
                EndPoint = client.Connection.EndPointString;
                ExperiencePoints = client.Character?.Info?.ExperiencePoints ?? 0;
            }

            public SavedExperiencePoints(XElement element)
            {
                SteamID = element.GetAttributeUInt64("steamid", 0);
                EndPoint = element.GetAttributeString("endpoint", string.Empty);
                ExperiencePoints = element.GetAttributeInt("points", 0);
            }
        }

        private readonly List<SavedExperiencePoints> savedExperiencePoints = new List<SavedExperiencePoints>();

        public override bool Paused
        {
            get { return ForceMapUI || CoroutineManager.IsCoroutineRunning("LevelTransition"); }
        }

        public static void StartNewCampaign(string savePath, string subPath, string seed, CampaignSettings settings)
        {
            if (string.IsNullOrWhiteSpace(savePath)) { return; }

            GameMain.GameSession = new GameSession(new SubmarineInfo(subPath), savePath, GameModePreset.MultiPlayerCampaign, settings, seed);
            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            SaveUtil.SaveGame(GameMain.GameSession.SavePath);

            DebugConsole.NewMessage("Campaign started!", Color.Cyan);
            DebugConsole.NewMessage("Current location: " + GameMain.GameSession.Map.CurrentLocation.Name, Color.Cyan);
            ((MultiPlayerCampaign)GameMain.GameSession.GameMode).LoadInitialLevel();
        }

        public static void LoadCampaign(string selectedSave)
        {
            GameMain.NetLobbyScreen.ToggleCampaignMode(true);
            SaveUtil.LoadGame(selectedSave);
            if (GameMain.GameSession.GameMode is MultiPlayerCampaign mpCampaign)
            {
                mpCampaign.LastSaveID++;
            }
            else
            {
                DebugConsole.ThrowError("Unexpected game mode: " + GameMain.GameSession.GameMode);
                return;
            }
            DebugConsole.NewMessage("Campaign loaded!", Color.Cyan);
            DebugConsole.NewMessage(
                GameMain.GameSession.Map.SelectedLocation == null ?
                GameMain.GameSession.Map.CurrentLocation.Name :
                GameMain.GameSession.Map.CurrentLocation.Name + " -> " + GameMain.GameSession.Map.SelectedLocation.Name, Color.Cyan);
        }

        protected override void LoadInitialLevel()
        {
            NextLevel = map.SelectedConnection?.LevelData ?? map.CurrentLocation.LevelData;
            MirrorLevel = false;
            GameMain.Server.StartGame();
        }

        public static void StartCampaignSetup()
        {
            DebugConsole.NewMessage("********* CAMPAIGN SETUP *********", Color.White);
            DebugConsole.ShowQuestionPrompt("Do you want to start a new campaign? Y/N", (string arg) =>
            {
                if (arg.Equals("y", StringComparison.OrdinalIgnoreCase) || arg.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    DebugConsole.ShowQuestionPrompt("Enter a save name for the campaign:", (string saveName) =>
                    {
                        string savePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveName);
                        StartNewCampaign(savePath, GameMain.NetLobbyScreen.SelectedSub.FilePath, GameMain.NetLobbyScreen.LevelSeed, CampaignSettings.Empty);
                    });
                }
                else
                {
                    var saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer, includeInCompatible: false).ToArray();
                    if (saveFiles.Length == 0)
                    {
                        DebugConsole.ThrowError("No save files found.");
                        return;
                    }
                    DebugConsole.NewMessage("Saved campaigns:", Color.White);
                    for (int i = 0; i < saveFiles.Length; i++)
                    {
                        DebugConsole.NewMessage("   " + i + ". " + saveFiles[i], Color.White);
                    }
                    DebugConsole.ShowQuestionPrompt("Select a save file to load (0 - " + (saveFiles.Length - 1) + "):", (string selectedSave) =>
                    {
                        int saveIndex = -1;
                        if (!int.TryParse(selectedSave, out saveIndex)) { return; }

                        if (saveIndex < 0 || saveIndex >= saveFiles.Length)
                        {
                            DebugConsole.ThrowError("Invalid save file index.");
                        }
                        else
                        {
                            LoadCampaign(saveFiles[saveIndex]);
                        }
                    });
                }
            });
        }

        public override void Start()
        {
            base.Start();
            lastUpdateID++;
        }

        private static bool IsOwner(Client client) => client != null && client.Connection == GameMain.Server.OwnerConnection;

        /// <summary>
        /// There is a client-side implementation of the method in <see cref="CampaignMode"/>
        /// </summary>
        public bool AllowedToEndRound(Client client)
        {
            //allow ending the round if the client has permissions, is the owner, the only client in the server,
            //or if no-one has permissions
            return
                client.HasPermission(ClientPermissions.ManageRound) ||
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Server.ConnectedClients.Count == 1 ||
                IsOwner(client) ||
                GameMain.Server.ConnectedClients.None(c =>
                    c.InGame && (IsOwner(c) || c.HasPermission(ClientPermissions.ManageRound) || c.HasPermission(ClientPermissions.ManageCampaign)));
        }

        /// <summary>
        /// There is a client-side implementation of the method in <see cref="CampaignMode"/>
        /// </summary>
        public bool AllowedToManageCampaign(Client client)
        {
            //allow ending the round if the client has permissions, is the owner, or the only client in the server,
            //or if no-one has management permissions
            return
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Server.ConnectedClients.Count == 1 ||
                IsOwner(client) ||
                GameMain.Server.ConnectedClients.None(c =>
                    c.InGame && (IsOwner(c) || c.HasPermission(ClientPermissions.ManageCampaign)));
        }

        public void SaveExperiencePoints(Client client)
        {
            ClearSavedExperiencePoints(client);
            savedExperiencePoints.Add(new SavedExperiencePoints(client));
        }
        public int GetSavedExperiencePoints(Client client)
        {
            return savedExperiencePoints.Find(s => s.SteamID != 0 && client.SteamID == s.SteamID || client.EndpointMatches(s.EndPoint))?.ExperiencePoints ?? 0;
        }
        public void ClearSavedExperiencePoints(Client client)
        {
            savedExperiencePoints.RemoveAll(s => s.SteamID != 0 && client.SteamID == s.SteamID || client.EndpointMatches(s.EndPoint));
        }

        public void LoadPets()
        {
            if (petsElement != null)
            {
                PetBehavior.LoadPets(petsElement);
            }
        }

        public void SavePlayers()
        {
            List<CharacterCampaignData> prevCharacterData = new List<CharacterCampaignData>(characterData);
            //client character has spawned this round -> remove old data (and replace with an up-to-date one if the client still has a character)
            characterData.RemoveAll(cd => cd.HasSpawned);

            //refresh the character data of clients who are still in the server
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                if (c.Character != null && c.Character.Info == null)
                {
                    c.Character = null;
                }

                if (c.HasSpawned && c.CharacterInfo != null && c.CharacterInfo.CauseOfDeath != null && c.CharacterInfo.CauseOfDeath.Type != CauseOfDeathType.Disconnected)
                {
                    //the client has opted to spawn this round with Reaper's Tax
                    if (c.WaitForNextRoundRespawn.HasValue && !c.WaitForNextRoundRespawn.Value)
                    {
                        c.CharacterInfo.StartItemsGiven = false;
                        characterData.RemoveAll(cd => cd.MatchesClient(c));
                        characterData.Add(new CharacterCampaignData(c, giveRespawnPenaltyAffliction: true));
                        continue;
                    }
                }
                var characterInfo = c.Character?.Info ?? c.CharacterInfo;
                if (characterInfo == null) { continue; }
                if (c.CharacterInfo.CauseOfDeath != null && characterInfo.CauseOfDeath.Type != CauseOfDeathType.Disconnected)
                {
                    RespawnManager.ReduceCharacterSkills(characterInfo);
                }
                c.CharacterInfo = characterInfo;
                characterData.RemoveAll(cd => cd.MatchesClient(c));
                characterData.Add(new CharacterCampaignData(c));
            }

            //refresh the character data of clients who aren't in the server anymore
            foreach (CharacterCampaignData data in prevCharacterData)
            {
                if (data.HasSpawned && !characterData.Any(cd => cd.IsDuplicate(data)))
                {
                    var character = Character.CharacterList.Find(c => c.Info == data.CharacterInfo && !c.IsHusk);
                    if (character != null && (!character.IsDead || character.CauseOfDeath?.Type == CauseOfDeathType.Disconnected))
                    {
                        data.Refresh(character);
                        characterData.Add(data);
                    }
                }
            }

            characterData.ForEach(cd => cd.HasSpawned = false);

            petsElement = new XElement("pets");
            PetBehavior.SavePets(petsElement);

            //remove all items that are in someone's inventory
            foreach (Character c in Character.CharacterList)
            {
                if (c.Inventory == null) { continue; }
                if (Level.Loaded.Type == LevelData.LevelType.Outpost && c.Submarine != Level.Loaded.StartOutpost)
                {
                    Map.CurrentLocation.RegisterTakenItems(c.Inventory.AllItems.Where(it => it.SpawnedInOutpost && it.OriginalModuleIndex > 0));
                }

                if (c.Info != null && c.IsBot)
                {
                    if (c.IsDead && c.CauseOfDeath?.Type != CauseOfDeathType.Disconnected) { CrewManager.RemoveCharacterInfo(c.Info); }
                    c.Info.HealthData = new XElement("health");
                    c.CharacterHealth.Save(c.Info.HealthData);
                    c.Info.InventoryData = new XElement("inventory");
                    c.SaveInventory();
                    c.Info.SaveOrderData();
                }

                c.Inventory.DeleteAllItems();
            }
        }

        protected override IEnumerable<object> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults)
        {
            lastUpdateID++;

            switch (transitionType)
            {
                case TransitionType.None:
                    throw new InvalidOperationException("Level transition failed (no transitions available).");
                case TransitionType.ReturnToPreviousLocation:
                    //deselect destination on map
                    map.SelectLocation(-1);
                    break;
                case TransitionType.ProgressToNextLocation:
                    Map.MoveToNextLocation();
                    break;
                case TransitionType.End:
                    EndCampaign();
                    IsFirstRound = true;
                    break;
            }

            Map.ProgressWorld(transitionType, (float)(Timing.TotalTime - GameMain.GameSession.RoundStartTime));

            bool success = GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);
            if (success)
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    if (c.Character?.HasAbilityFlag(AbilityFlags.RetainExperienceForNewCharacter) ?? false)
                    {
                        (GameMain.GameSession?.GameMode as MultiPlayerCampaign)?.SaveExperiencePoints(c);
                    }
                }
            }

            GameMain.GameSession.EndRound("", traitorResults, transitionType);
            
            //--------------------------------------

            if (success)
            {
                SavePlayers();

                yield return CoroutineStatus.Running;

                if (leavingSub != Submarine.MainSub && !leavingSub.DockedTo.Contains(Submarine.MainSub))
                {
                    Submarine.MainSub = leavingSub;
                    GameMain.GameSession.Submarine = leavingSub;
                    GameMain.GameSession.SubmarineInfo = leavingSub.Info;
                    leavingSub.Info.FilePath = System.IO.Path.Combine(SaveUtil.TempPath, leavingSub.Info.Name + ".sub");
                    var subsToLeaveBehind = GetSubsToLeaveBehind(leavingSub);
                    GameMain.GameSession.OwnedSubmarines.Add(leavingSub.Info);
                    foreach (Submarine sub in subsToLeaveBehind)
                    {
                        GameMain.GameSession.OwnedSubmarines.RemoveAll(s => s != leavingSub.Info && s.Name == sub.Info.Name);
                        MapEntity.mapEntityList.RemoveAll(e => e.Submarine == sub && e is LinkedSubmarine);
                        LinkedSubmarine.CreateDummy(leavingSub, sub);
                    }
                }
                NextLevel = newLevel;
                GameMain.GameSession.SubmarineInfo = new SubmarineInfo(GameMain.GameSession.Submarine);

                if (PendingSubmarineSwitch != null)
                {
                    SubmarineInfo previousSub = GameMain.GameSession.SubmarineInfo;
                    GameMain.GameSession.SubmarineInfo = PendingSubmarineSwitch;
                    PendingSubmarineSwitch = null;

                    for (int i = 0; i < GameMain.GameSession.OwnedSubmarines.Count; i++)
                    {
                        if (GameMain.GameSession.OwnedSubmarines[i].Name == previousSub.Name)
                        {
                            GameMain.GameSession.OwnedSubmarines[i] = previousSub;
                            break;
                        }
                    }
                }

                SaveUtil.SaveGame(GameMain.GameSession.SavePath);
            }
            else
            {
                PendingSubmarineSwitch = null;
                GameMain.Server.EndGame(TransitionType.None);
                LoadCampaign(GameMain.GameSession.SavePath);
                LastSaveID++;
                LastUpdateID++;
                yield return CoroutineStatus.Success;
            }

            CrewManager?.ClearCurrentOrders();

            //--------------------------------------

            GameMain.Server.EndGame(transitionType);

            ForceMapUI = false;

            NextLevel = newLevel;
            MirrorLevel = mirror;

            //give clients time to play the end cinematic before starting the next round
            if (transitionType == TransitionType.End)
            {
                yield return new WaitForSeconds(EndCinematicDuration);
            }
            else
            {
                yield return new WaitForSeconds(EndTransitionDuration * 0.5f);
            }

            GameMain.Server.StartGame();

            yield return CoroutineStatus.Success;
        }

        partial void InitProjSpecific()
        {
            if (GameMain.Server != null)
            {
                CargoManager.OnItemsInBuyCrateChanged += () => { LastUpdateID++; };
                CargoManager.OnPurchasedItemsChanged += () => { LastUpdateID++; };
                CargoManager.OnSoldItemsChanged += () => { LastUpdateID++; };
                UpgradeManager.OnUpgradesChanged += () => { LastUpdateID++; };
                Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
                Map.OnMissionsSelected += (loc, mission) => { LastUpdateID++; };
                Reputation.OnAnyReputationValueChanged += () => { LastUpdateID++; };
            }
            //increment save ID so clients know they're lacking the most up-to-date save file
            LastSaveID++;
        }

        public void DiscardClientCharacterData(Client client)
        {
            characterData.RemoveAll(cd => cd.MatchesClient(client));
        }

        public CharacterCampaignData GetClientCharacterData(Client client)
        {
            return characterData.Find(cd => cd.MatchesClient(client));
        }

        public CharacterCampaignData SetClientCharacterData(Client client)
        {
            characterData.RemoveAll(cd => cd.MatchesClient(client));
            var data = new CharacterCampaignData(client);
            characterData.Add(data);
            return data;
        }

        public void AssignClientCharacterInfos(IEnumerable<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                if (client.SpectateOnly && GameMain.Server.ServerSettings.AllowSpectating) { continue; }
                var matchingData = GetClientCharacterData(client);
                if (matchingData != null) { client.CharacterInfo = matchingData.CharacterInfo; }
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

        public override void Update(float deltaTime)
        {
            if (CoroutineManager.IsCoroutineRunning("LevelTransition")) { return; }

            Map?.Radiation?.UpdateRadiation(deltaTime);

            base.Update(deltaTime);
            if (Level.Loaded != null)
            {
                if (Level.Loaded.Type == LevelData.LevelType.LocationConnection)
                {
                    var transitionType = GetAvailableTransition(out _, out Submarine leavingSub); 
                    if (transitionType == TransitionType.End)
                    {
                        LoadNewLevel();
                    }
                    else if (GameMain.Server.ConnectedClients.Count == 0 || GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead))
                    {
                        if (transitionType == TransitionType.ProgressToNextLocation && Level.Loaded.EndOutpost != null && Level.Loaded.EndOutpost.DockedTo.Contains(leavingSub))
                        {
                            LoadNewLevel();
                        }
                        else if (transitionType == TransitionType.ReturnToPreviousLocation && Level.Loaded.StartOutpost != null && Level.Loaded.StartOutpost.DockedTo.Contains(leavingSub))
                        {
                            LoadNewLevel();
                        }
                    }
                }
                else if (Level.Loaded.Type == LevelData.LevelType.Outpost)
                {
                    KeepCharactersCloseToOutpost(deltaTime);
                }
            }
        }

        public override void End(TransitionType transitionType = TransitionType.None)
        {
            GameOver = !GameMain.Server.ConnectedClients.Any(c => c.InGame && c.Character != null && !c.Character.IsDead);
            base.End(transitionType);
        }

        public void ServerWrite(IWriteMessage msg, Client c)
        {
            System.Diagnostics.Debug.Assert(map.Locations.Count < UInt16.MaxValue);

            Reputation reputation = Map?.CurrentLocation?.Reputation;

            msg.Write(IsFirstRound);
            msg.Write(CampaignID);
            msg.Write(lastUpdateID);
            msg.Write(lastSaveID);
            msg.Write(map.Seed);
            msg.Write(map.CurrentLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.CurrentLocationIndex);
            msg.Write(map.SelectedLocationIndex == -1 ? UInt16.MaxValue : (UInt16)map.SelectedLocationIndex); 
            
            var selectedMissionIndices = map.GetSelectedMissionIndices();
            msg.Write((byte)selectedMissionIndices.Count());
            foreach (int selectedMissionIndex in selectedMissionIndices)
            {
                msg.Write((byte)selectedMissionIndex);
            }

            msg.Write(map.AllowDebugTeleport);
            msg.Write(reputation != null);
            if (reputation != null) { msg.Write(reputation.Value); }

            // hopefully we'll never have more than 128 factions
            msg.Write((byte)Factions.Count);
            foreach (Faction faction in Factions)
            {
                msg.Write(faction.Prefab.Identifier);
                msg.Write(faction.Reputation.Value);
            }

            msg.Write(ForceMapUI);

            msg.Write(Money);
            msg.Write(PurchasedHullRepairs);
            msg.Write(PurchasedItemRepairs);
            msg.Write(PurchasedLostShuttles);

            if (map.CurrentLocation != null)
            {
                msg.Write((byte)map.CurrentLocation?.AvailableMissions.Count());
                foreach (Mission mission in map.CurrentLocation.AvailableMissions)
                {
                    msg.Write(mission.Prefab.Identifier);
                    if (mission.Locations[0] == mission.Locations[1])
                    {
                        msg.Write((byte)255);
                    }
                    else
                    {
                        Location missionDestination = mission.Locations[0] == map.CurrentLocation ? mission.Locations[1] : mission.Locations[0];
                        LocationConnection connection = map.CurrentLocation.Connections.Find(c => c.OtherLocation(map.CurrentLocation) == missionDestination);
                        msg.Write((byte)map.CurrentLocation.Connections.IndexOf(connection));
                    }
                }

                // Store balance
                msg.Write(true);
                msg.Write((UInt16)map.CurrentLocation.StoreCurrentBalance);
            }
            else
            {
                msg.Write((byte)0);
                // Store balance
                msg.Write(false);
            }

            msg.Write((UInt16)CargoManager.ItemsInBuyCrate.Count);
            foreach (PurchasedItem pi in CargoManager.ItemsInBuyCrate)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, 100);
            }

            msg.Write((UInt16)CargoManager.PurchasedItems.Count);
            foreach (PurchasedItem pi in CargoManager.PurchasedItems)
            {
                msg.Write(pi.ItemPrefab.Identifier);
                msg.WriteRangedInteger(pi.Quantity, 0, 100);
            }

            msg.Write((UInt16)CargoManager.SoldItems.Count);
            foreach (SoldItem si in CargoManager.SoldItems)
            {
                msg.Write(si.ItemPrefab.Identifier);
                msg.Write((UInt16)si.ID);
                msg.Write(si.Removed);
                msg.Write(si.SellerID);
            }

            msg.Write((ushort)UpgradeManager.PendingUpgrades.Count);
            foreach (var (prefab, category, level) in UpgradeManager.PendingUpgrades)
            {
                msg.Write(prefab.Identifier);
                msg.Write(category.Identifier);
                msg.Write((byte)level);
            }

            msg.Write((ushort)UpgradeManager.PurchasedItemSwaps.Count);
            foreach (var itemSwap in UpgradeManager.PurchasedItemSwaps)
            {
                msg.Write(itemSwap.ItemToRemove.ID);
                msg.Write(itemSwap.ItemToInstall?.Identifier ?? string.Empty);
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

        public void ServerRead(IReadMessage msg, Client sender)
        {
            UInt16 currentLocIndex  = msg.ReadUInt16();
            UInt16 selectedLocIndex = msg.ReadUInt16();

            byte selectedMissionCount = msg.ReadByte();
            List<int> selectedMissionIndices = new List<int>();
            for (int i = 0; i < selectedMissionCount; i++)
            {
                selectedMissionIndices.Add(msg.ReadByte());
            }

            bool purchasedHullRepairs = msg.ReadBoolean();
            bool purchasedItemRepairs = msg.ReadBoolean();
            bool purchasedLostShuttles = msg.ReadBoolean();

            UInt16 buyCrateItemCount = msg.ReadUInt16();
            List<PurchasedItem> buyCrateItems = new List<PurchasedItem>();
            for (int i = 0; i < buyCrateItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                buyCrateItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 purchasedItemCount = msg.ReadUInt16();
            List<PurchasedItem> purchasedItems = new List<PurchasedItem>();
            for (int i = 0; i < purchasedItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                int itemQuantity = msg.ReadRangedInteger(0, CargoManager.MaxQuantity);
                purchasedItems.Add(new PurchasedItem(ItemPrefab.Prefabs[itemPrefabIdentifier], itemQuantity));
            }

            UInt16 soldItemCount = msg.ReadUInt16();
            List<SoldItem> soldItems = new List<SoldItem>();
            for (int i = 0; i < soldItemCount; i++)
            {
                string itemPrefabIdentifier = msg.ReadString();
                UInt16 id = msg.ReadUInt16();
                bool removed = msg.ReadBoolean();
                byte sellerId = msg.ReadByte();
                soldItems.Add(new SoldItem(ItemPrefab.Prefabs[itemPrefabIdentifier], id, removed, sellerId));
            }

            ushort purchasedUpgradeCount = msg.ReadUInt16();
            List<PurchasedUpgrade> purchasedUpgrades = new List<PurchasedUpgrade>();
            for (int i = 0; i < purchasedUpgradeCount; i++)
            {
                string upgradeIdentifier = msg.ReadString();
                UpgradePrefab prefab = UpgradePrefab.Find(upgradeIdentifier);

                string categoryIdentifier = msg.ReadString();
                UpgradeCategory category = UpgradeCategory.Find(categoryIdentifier);

                int upgradeLevel = msg.ReadByte();

                if (category == null || prefab == null) { continue; }
                purchasedUpgrades.Add(new PurchasedUpgrade(prefab, category, upgradeLevel));
            }

            ushort purchasedItemSwapCount = msg.ReadUInt16();
            List<PurchasedItemSwap> purchasedItemSwaps = new List<PurchasedItemSwap>();
            for (int i = 0; i < purchasedItemSwapCount; i++)
            {
                UInt16 itemToRemoveID = msg.ReadUInt16();
                Item itemToRemove = Entity.FindEntityByID(itemToRemoveID) as Item;

                string itemToInstallIdentifier = msg.ReadString();
                ItemPrefab itemToInstall = string.IsNullOrEmpty(itemToInstallIdentifier) ? null : ItemPrefab.Find(string.Empty, itemToInstallIdentifier);

                if (itemToRemove == null) { continue; }

                purchasedItemSwaps.Add(new PurchasedItemSwap(itemToRemove, itemToInstall));
            }

            if (!AllowedToManageCampaign(sender))
            {
                DebugConsole.ThrowError("Client \"" + sender.Name + "\" does not have a permission to manage the campaign");
                return;
            }
            
            Location location = Map.CurrentLocation;
            int hullRepairCost      = location?.GetAdjustedMechanicalCost(HullRepairCost)     ?? HullRepairCost;
            int itemRepairCost      = location?.GetAdjustedMechanicalCost(ItemRepairCost)     ?? ItemRepairCost;
            int shuttleRetrieveCost = location?.GetAdjustedMechanicalCost(ShuttleReplaceCost) ?? ShuttleReplaceCost;

            if (purchasedHullRepairs != this.PurchasedHullRepairs)
            {
                if (purchasedHullRepairs && Money >= hullRepairCost)
                {
                    this.PurchasedHullRepairs = true;
                    Money -= hullRepairCost;
                }
                else if (!purchasedHullRepairs)
                {
                    this.PurchasedHullRepairs = false;
                    Money += hullRepairCost;
                }
            }
            if (purchasedItemRepairs != this.PurchasedItemRepairs)
            {
                if (purchasedItemRepairs && Money >= itemRepairCost)
                {
                    this.PurchasedItemRepairs = true;
                    Money -= itemRepairCost;
                }
                else if (!purchasedItemRepairs)
                {
                    this.PurchasedItemRepairs = false;
                    Money += itemRepairCost;
                }
            }
            if (purchasedLostShuttles != this.PurchasedLostShuttles)
            {
                if (GameMain.GameSession?.SubmarineInfo != null &&
                    GameMain.GameSession.SubmarineInfo.LeftBehindSubDockingPortOccupied)
                {
                    GameMain.Server.SendDirectChatMessage(TextManager.FormatServerMessage("ReplaceShuttleDockingPortOccupied"), sender, ChatMessageType.MessageBox);
                }
                else if (purchasedLostShuttles && Money >= shuttleRetrieveCost)
                {
                    this.PurchasedLostShuttles = true;
                    Money -= shuttleRetrieveCost;
                }
                else if (!purchasedItemRepairs)
                {
                    this.PurchasedLostShuttles = false;
                    Money += shuttleRetrieveCost;
                }
            }

            if (currentLocIndex < Map.Locations.Count && Map.AllowDebugTeleport)
            {
                Map.SetLocation(currentLocIndex);
            }

            Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
            if (Map.SelectedLocation == null) { Map.SelectRandomLocation(preferUndiscovered: true); }
            if (Map.SelectedConnection != null) { Map.SelectMission(selectedMissionIndices); }

            CheckTooManyMissions(Map.CurrentLocation, sender);

            List<PurchasedItem> currentBuyCrateItems = new List<PurchasedItem>(CargoManager.ItemsInBuyCrate);
            currentBuyCrateItems.ForEach(i => CargoManager.ModifyItemQuantityInBuyCrate(i.ItemPrefab, -i.Quantity));
            buyCrateItems.ForEach(i => CargoManager.ModifyItemQuantityInBuyCrate(i.ItemPrefab, i.Quantity));

            CargoManager.SellBackPurchasedItems(new List<PurchasedItem>(CargoManager.PurchasedItems));
            CargoManager.PurchaseItems(purchasedItems, false);

            // for some reason CargoManager.SoldItem is never cleared by the server, I've added a check to SellItems that ignores all
            // sold items that are removed so they should be discarded on the next message
            CargoManager.BuyBackSoldItems(new List<SoldItem>(CargoManager.SoldItems));
            CargoManager.SellItems(soldItems);

            foreach (var (prefab, category, _) in purchasedUpgrades)
            {
                UpgradeManager.PurchaseUpgrade(prefab, category);

                // unstable logging
                int price = prefab.Price.GetBuyprice(UpgradeManager.GetUpgradeLevel(prefab, category), Map?.CurrentLocation);
                int level = UpgradeManager.GetUpgradeLevel(prefab, category);
                GameServer.Log($"SERVER: Purchased level {level} {category.Identifier}.{prefab.Identifier} for {price}", ServerLog.MessageType.ServerMessage);
            }

            foreach (var purchasedItemSwap in purchasedItemSwaps)
            {
                if (purchasedItemSwap.ItemToInstall == null)
                {
                    UpgradeManager.CancelItemSwap(purchasedItemSwap.ItemToRemove);
                }
                else
                {
                    UpgradeManager.PurchaseItemSwap(purchasedItemSwap.ItemToRemove, purchasedItemSwap.ItemToInstall);
                }
            }
            foreach (Item item in Item.ItemList)
            {
                if (item.PendingItemSwap != null && !purchasedItemSwaps.Any(it => it.ItemToRemove == item))
                {
                    UpgradeManager.CancelItemSwap(item);
                    item.PendingItemSwap = null;
                }
            }
        }

        public void ServerReadCrew(IReadMessage msg, Client sender)
        {
            int[] pendingHires = null;

            bool updatePending = msg.ReadBoolean();
            if (updatePending)
            {
                ushort pendingHireLength = msg.ReadUInt16();
                pendingHires = new int[pendingHireLength];
                for (int i = 0; i < pendingHireLength; i++)
                {
                    pendingHires[i] = msg.ReadInt32();
                }
            }

            bool validateHires = msg.ReadBoolean();

            bool renameCharacter = msg.ReadBoolean();
            int renamedIdentifier = -1;
            string newName = null;
            bool existingCrewMember = false;
            if (renameCharacter)
            {
                renamedIdentifier = msg.ReadInt32();
                newName = msg.ReadString();
                existingCrewMember = msg.ReadBoolean();
            }

            bool fireCharacter = msg.ReadBoolean();
            int firedIdentifier = -1;
            if (fireCharacter) { firedIdentifier = msg.ReadInt32(); }

            Location location = map?.CurrentLocation;
            List<CharacterInfo> hiredCharacters = new List<CharacterInfo>();
            CharacterInfo firedCharacter = null;

            if (location != null && AllowedToManageCampaign(sender))
            {
                if (fireCharacter)
                {
                    firedCharacter = CrewManager.CharacterInfos.FirstOrDefault(info => info.GetIdentifier() == firedIdentifier);
                    if (firedCharacter != null && (firedCharacter.Character?.IsBot ?? true))
                    {
                        CrewManager.FireCharacter(firedCharacter);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Tried to fire an invalid character ({firedIdentifier})");
                    }
                }

                if (renameCharacter)
                {
                    CharacterInfo characterInfo = null;
                    if (existingCrewMember && CrewManager != null)
                    {
                        characterInfo = CrewManager.CharacterInfos.FirstOrDefault(info => info.GetIdentifierUsingOriginalName() == renamedIdentifier);
                    }
                    else if(!existingCrewMember && location.HireManager != null)
                    {
                        characterInfo = location.HireManager.AvailableCharacters.FirstOrDefault(info => info.GetIdentifierUsingOriginalName() == renamedIdentifier);
                    }
                    
                    if (characterInfo != null && (characterInfo.Character?.IsBot ?? true))
                    {
                        if (existingCrewMember)
                        {
                            CrewManager.RenameCharacter(characterInfo, newName);
                        }
                        else
                        {
                            location.HireManager.RenameCharacter(characterInfo, newName);
                        }
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Tried to rename an invalid character ({renamedIdentifier})");
                    }
                }

                if (location.HireManager != null)
                {
                    if (validateHires)
                    {
                        foreach (CharacterInfo hireInfo in location.HireManager.PendingHires)
                        {
                            if (TryHireCharacter(location, hireInfo))
                            {
                                hiredCharacters.Add(hireInfo);
                            };
                        }
                    }
                    
                    if (updatePending)
                    {
                        List<CharacterInfo> pendingHireInfos = new List<CharacterInfo>();
                        foreach (int identifier in pendingHires)
                        {
                            CharacterInfo match = location.GetHireableCharacters().FirstOrDefault(info => info.GetIdentifierUsingOriginalName() == identifier);
                            if (match == null)
                            {
                                DebugConsole.ThrowError($"Tried to add a character that doesn't exist ({identifier}) to pending hires");
                                continue;
                            }

                            pendingHireInfos.Add(match);
                            if (pendingHireInfos.Count + CrewManager.CharacterInfos.Count() >= CrewManager.MaxCrewSize)
                            {
                                break;
                            }
                        }
                        location.HireManager.PendingHires = pendingHireInfos;
                    }

                    location.HireManager.AvailableCharacters.ForEachMod(info =>
                    {
                        if(!location.HireManager.PendingHires.Contains(info))
                        {
                            location.HireManager.RenameCharacter(info, info.OriginalName);
                        }
                    });
                }
            }

            // bounce back
            if (renameCharacter && existingCrewMember)
            {
                SendCrewState(hiredCharacters, (renamedIdentifier, newName), firedCharacter);
            }
            else
            {
                SendCrewState(hiredCharacters, default, firedCharacter);
            }
        }

        /// <summary>
        /// Notifies the clients of the current bot situation like syncing pending and available hires
        /// </summary>
        /// <param name="hiredCharacters">Inform the clients that these characters have been hired.</param>
        /// <param name="firedCharacter">Inform the clients that this character has been fired.</param>
        /// <remarks>
        /// It might be obsolete to sync available hires. I found that the available hires are always the same between
        /// the client and the server when there's only one person on the server but when a second person joins both of
        /// their available hires are different from the server.
        /// </remarks>
        public void SendCrewState(List<CharacterInfo> hiredCharacters, (int id, string newName) renamedCrewMember, CharacterInfo firedCharacter)
        {
            List<CharacterInfo> availableHires = new List<CharacterInfo>();
            List<CharacterInfo> pendingHires = new List<CharacterInfo>();

            if (map.CurrentLocation != null && map.CurrentLocation.Type.HasHireableCharacters)
            {
                availableHires = map.CurrentLocation.GetHireableCharacters().ToList();
                pendingHires = map.CurrentLocation?.HireManager.PendingHires;
            }

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.CREW);

                msg.Write((ushort)availableHires.Count);
                foreach (CharacterInfo hire in availableHires)
                {
                    hire.ServerWrite(msg);
                    msg.Write(hire.Salary);
                }
            
                msg.Write((ushort)pendingHires.Count);
                foreach (CharacterInfo pendingHire in pendingHires)
                {
                    msg.Write(pendingHire.GetIdentifierUsingOriginalName());
                }

                msg.Write((ushort)(hiredCharacters?.Count ?? 0));
                if(hiredCharacters != null)
                {
                    foreach (CharacterInfo info in hiredCharacters)
                    {
                        info.ServerWrite(msg);
                        msg.Write(info.Salary);
                    }
                }

                bool validRenaming = renamedCrewMember.id > -1 && !string.IsNullOrEmpty(renamedCrewMember.newName);
                msg.Write(validRenaming);
                if (validRenaming)
                {
                    msg.Write(renamedCrewMember.id);
                    msg.Write(renamedCrewMember.newName);
                }

                msg.Write(firedCharacter != null);
                if (firedCharacter != null) { msg.Write(firedCharacter.GetIdentifier()); }

                GameMain.Server.ServerPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        public override void Save(XElement element)
        {
            element.Add(new XAttribute("campaignid", CampaignID));
            XElement modeElement = new XElement("MultiPlayerCampaign",
                new XAttribute("money", Money),
                new XAttribute("purchasedlostshuttles", PurchasedLostShuttles),
                new XAttribute("purchasedhullrepairs", PurchasedHullRepairs),
                new XAttribute("purchaseditemrepairs", PurchasedItemRepairs),
                new XAttribute("cheatsenabled", CheatsEnabled));
            modeElement.Add(Settings.Save());
            CampaignMetadata?.Save(modeElement);
            Map.Save(modeElement);
            CargoManager?.SavePurchasedItems(modeElement);
            UpgradeManager?.Save(modeElement);

            if (petsElement != null)
            {
                modeElement.Add(petsElement);
            }

            // save bots
            CrewManager.SaveMultiplayer(modeElement);

            XElement savedExperiencePointsElement = new XElement("SavedExperiencePoints");
            foreach (var savedExperiencePoint in savedExperiencePoints)
            {
                savedExperiencePointsElement.Add(new XElement("Point",
                    new XAttribute("steamid", savedExperiencePoint.SteamID),
                    new XAttribute("endpoint", savedExperiencePoint?.EndPoint ?? string.Empty),
                    new XAttribute("points", savedExperiencePoint.ExperiencePoints)));
            }

            // save available submarines
            XElement availableSubsElement = new XElement("AvailableSubs");
            for (int i = 0; i < GameMain.NetLobbyScreen.CampaignSubmarines.Count; i++)
            {
                availableSubsElement.Add(new XElement("Sub", new XAttribute("name", GameMain.NetLobbyScreen.CampaignSubmarines[i].Name)));
            }
            modeElement.Add(availableSubsElement);

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
                characterDataDoc.SaveSafe(characterDataPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving multiplayer campaign characters to \"" + characterDataPath + "\" failed!", e);
            }

            lastSaveID++;
            DebugConsole.Log("Campaign saved, save ID " + lastSaveID);
        }
    }
}
