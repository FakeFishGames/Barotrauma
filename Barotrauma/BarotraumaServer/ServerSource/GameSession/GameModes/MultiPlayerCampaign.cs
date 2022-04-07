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
        private readonly Dictionary<ushort, Wallet> walletsToCheck = new Dictionary<ushort, Wallet>();
        private readonly HashSet<NetWalletTransaction> transactions = new HashSet<NetWalletTransaction>();
        private const float clientCheckInterval = 10;
        private float clientCheckTimer = clientCheckInterval;

        public override Wallet GetWallet(Client client = null)
        {
            if (client is null) { throw new ArgumentNullException(nameof(client), "Client should not be null in multiplayer"); }

            if (client.Character is { } character)
            {
                return character.Wallet;
            }

            return Wallet.Invalid;
        }

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
                        DebugConsole.NewMessage("   " + i + ". " + saveFiles[i].FilePath, Color.White);
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
                            LoadCampaign(saveFiles[saveIndex].FilePath);
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
        public bool AllowedToManageCampaign(Client client, ClientPermissions permissions)
        {
            //allow managing the campaign if the client has permissions, is the owner, or the only client in the server,
            //or if no-one has management permissions
            return
                client.HasPermission(permissions) ||
                client.HasPermission(ClientPermissions.ManageCampaign) ||
                GameMain.Server.ConnectedClients.Count == 1 ||
                IsOwner(client) ||
                GameMain.Server.ConnectedClients.None(c => c.InGame && (IsOwner(c) || c.HasPermission(permissions)));
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
            //refresh the character data of clients who are still in the server
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                //ignore if the character is controlling a monster
                //(we'll just use the previously saved campaign data if there's any)
                if (c.Character != null && c.Character.Info == null)
                {
                    c.Character = null;
                }
                //use the info of the character the client is currently controlling
                // or the previously saved info if not (e.g. if the client has been spectating or died)
                var characterInfo = c.Character?.Info;
                var matchingCharacterData = characterData.Find(d => d.MatchesClient(c));
                if (matchingCharacterData != null)
                {
                    //hasn't spawned this round -> don't touch the data
                    if (!matchingCharacterData.HasSpawned) { continue; }
                    characterInfo ??= matchingCharacterData.CharacterInfo;
                }
                if (characterInfo == null) { continue; }
                //reduce skills if the character has died
                if (characterInfo.CauseOfDeath != null && characterInfo.CauseOfDeath.Type != CauseOfDeathType.Disconnected)
                {
                    RespawnManager.ReduceCharacterSkills(characterInfo);
                    characterInfo.RemoveSavedStatValuesOnDeath();
                    characterInfo.CauseOfDeath = null;
                }
                c.CharacterInfo = characterInfo;
                SetClientCharacterData(c);
            }

            //refresh the character data of clients who aren't in the server anymore
            List<CharacterCampaignData> prevCharacterData = new List<CharacterCampaignData>(characterData);
            foreach (CharacterCampaignData data in prevCharacterData)
            {
                if (data.HasSpawned && !GameMain.Server.ConnectedClients.Any(c => data.MatchesClient(c)))
                {
                    var character = Character.CharacterList.Find(c => c.Info == data.CharacterInfo && !c.IsHusk);
                    if (character != null && (!character.IsDead || character.CauseOfDeath?.Type == CauseOfDeathType.Disconnected))
                    {
                        characterData.RemoveAll(cd => cd.IsDuplicate(data));
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
                    Map.CurrentLocation.RegisterTakenItems(c.Inventory.AllItems.Where(it => it.SpawnedInCurrentOutpost && it.OriginalModuleIndex > 0));
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

        protected override IEnumerable<CoroutineStatus> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults)
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
                    TotalPassedLevels++;
                    break;
                case TransitionType.End:
                    EndCampaign();
                    IsFirstRound = true;
                    break;
                case TransitionType.ProgressToNextEmptyLocation:
                    TotalPassedLevels++;
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
                PendingSubmarineSwitch = null;
            }
            else
            {
                PendingSubmarineSwitch = null;
                GameMain.Server.EndGame(TransitionType.None, wasSaved: false);
                LoadCampaign(GameMain.GameSession.SavePath);
                LastSaveID++;
                LastUpdateID++;
                yield return CoroutineStatus.Success;
            }

            CrewManager?.ClearCurrentOrders();

            //--------------------------------------

            GameMain.Server.EndGame(transitionType, wasSaved: true);

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
            CargoManager.OnItemsInBuyCrateChanged += () => { LastUpdateID++; };
            CargoManager.OnPurchasedItemsChanged += () => { LastUpdateID++; };
            CargoManager.OnSoldItemsChanged += () => { LastUpdateID++; };
            UpgradeManager.OnUpgradesChanged += () => { LastUpdateID++; };
            Map.OnLocationSelected += (loc, connection) => { LastUpdateID++; };
            Map.OnMissionsSelected += (loc, mission) => { LastUpdateID++; };
            Reputation.OnAnyReputationValueChanged += () => { LastUpdateID++; };

            //increment save ID so clients know they're lacking the most up-to-date save file
            LastSaveID++;
        }

        public bool CanPurchaseSub(SubmarineInfo info, Client client)
            => GetWallet(client).CanAfford(info.Price) && GetCampaignSubs().Contains(info);

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

            UpdateClientsToCheck(deltaTime);
            UpdateWallets();
        }

        private void UpdateClientsToCheck(float deltaTime)
        {
            if (clientCheckTimer < clientCheckInterval)
            {
                clientCheckTimer += deltaTime;
                return;
            }

            clientCheckTimer = 0;
            walletsToCheck.Clear();
            walletsToCheck.Add(0, Bank);

            foreach (Character character in GameSession.GetSessionCrewCharacters(CharacterType.Player))
            {
                walletsToCheck.Add(character.ID, character.Wallet);
            }
        }

        private void UpdateWallets()
        {
            foreach (var (id, wallet) in walletsToCheck)
            {
                if (wallet.HasTransactions())
                {
                    transactions.Add(wallet.DequeueAndMergeTransactions(id));
                }
            }

            if (transactions.Count == 0) { return; }

            NetWalletUpdate walletUpdate = new NetWalletUpdate
            {
                Transactions = transactions.ToArray()
            };

            transactions.Clear();

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage().WithHeader(ServerPacketHeader.MONEY);
                ((INetSerializableStruct)walletUpdate).Write(msg);
                GameMain.Server?.ServerPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
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

            var subList = GameMain.NetLobbyScreen.GetSubList();
            List<int> ownedSubmarineIndices = new List<int>();
            for (int i = 0; i < subList.Count; i++)
            {
                if (GameMain.GameSession.OwnedSubmarines.Any(s => s.Name == subList[i].Name))
                {
                    ownedSubmarineIndices.Add(i);
                }
            }
            msg.Write((ushort)ownedSubmarineIndices.Count);
            foreach (int index in ownedSubmarineIndices)
            {
                msg.Write((ushort)index);
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
                bool hasStores = map.CurrentLocation.Stores != null && map.CurrentLocation.Stores.Any();
                msg.Write(hasStores);
                if (hasStores)
                {
                    msg.Write((byte)map.CurrentLocation.Stores.Count);
                    foreach (var store in map.CurrentLocation.Stores.Values)
                    {
                        msg.Write(store.Identifier);
                        msg.Write((UInt16)store.Balance);
                    }
                }
            }
            else
            {
                msg.Write((byte)0);
                // Store balance
                msg.Write(false);
            }

            WriteItems(msg, CargoManager.ItemsInBuyCrate);
            WriteItems(msg, CargoManager.ItemsInSellFromSubCrate);
            WriteItems(msg, CargoManager.PurchasedItems);
            WriteItems(msg, CargoManager.SoldItems);

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
                msg.Write(itemSwap.ItemToInstall?.Identifier ?? Identifier.Empty);
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

            var buyCrateItems = ReadPurchasedItems(msg, sender);
            var subSellCrateItems = ReadPurchasedItems(msg, sender);
            var purchasedItems = ReadPurchasedItems(msg, sender);
            var soldItems = ReadSoldItems(msg);

            ushort purchasedUpgradeCount = msg.ReadUInt16();
            List<PurchasedUpgrade> purchasedUpgrades = new List<PurchasedUpgrade>();
            for (int i = 0; i < purchasedUpgradeCount; i++)
            {
                Identifier upgradeIdentifier = msg.ReadIdentifier();
                UpgradePrefab prefab = UpgradePrefab.Find(upgradeIdentifier);

                Identifier categoryIdentifier = msg.ReadIdentifier();
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
                Identifier itemToInstallIdentifier = msg.ReadIdentifier();
                ItemPrefab itemToInstall = itemToInstallIdentifier.IsEmpty ? null : ItemPrefab.Find(string.Empty, itemToInstallIdentifier);
                if (!(Entity.FindEntityByID(itemToRemoveID) is Item itemToRemove)) { continue; }
                purchasedItemSwaps.Add(new PurchasedItemSwap(itemToRemove, itemToInstall));
            }

            Location location = Map.CurrentLocation;
            int hullRepairCost = location?.GetAdjustedMechanicalCost(HullRepairCost) ?? HullRepairCost;
            int itemRepairCost = location?.GetAdjustedMechanicalCost(ItemRepairCost) ?? ItemRepairCost;
            int shuttleRetrieveCost = location?.GetAdjustedMechanicalCost(ShuttleReplaceCost) ?? ShuttleReplaceCost;
            Wallet personalWallet = GetWallet(sender);

            if (purchasedHullRepairs != PurchasedHullRepairs)
            {
                switch (purchasedHullRepairs)
                {
                    case true when personalWallet.CanAfford(hullRepairCost):
                        personalWallet.Deduct(hullRepairCost);
                        PurchasedHullRepairs = true;
                        GameAnalyticsManager.AddMoneySpentEvent(hullRepairCost, GameAnalyticsManager.MoneySink.Service, "hullrepairs");
                        break;
                    case false:
                        PurchasedHullRepairs = false;
                        personalWallet.Refund(hullRepairCost);
                        break;
                }
            }

            if (purchasedItemRepairs != PurchasedItemRepairs)
            {
                switch (purchasedItemRepairs)
                {
                    case true when personalWallet.CanAfford(itemRepairCost):
                        personalWallet.Deduct(itemRepairCost);
                        PurchasedItemRepairs = true;
                        GameAnalyticsManager.AddMoneySpentEvent(itemRepairCost, GameAnalyticsManager.MoneySink.Service, "devicerepairs");
                        break;
                    case false:
                        PurchasedItemRepairs = false;
                        personalWallet.Refund(itemRepairCost);
                        break;
                }
            }

            if (purchasedLostShuttles != PurchasedLostShuttles)
            {
                if (GameMain.GameSession?.SubmarineInfo != null && GameMain.GameSession.SubmarineInfo.LeftBehindSubDockingPortOccupied)
                {
                    GameMain.Server.SendDirectChatMessage(TextManager.FormatServerMessage("ReplaceShuttleDockingPortOccupied"), sender, ChatMessageType.MessageBox);
                }
                else if (purchasedLostShuttles && personalWallet.TryDeduct(shuttleRetrieveCost))
                {
                    PurchasedLostShuttles = true;
                    GameAnalyticsManager.AddMoneySpentEvent(shuttleRetrieveCost, GameAnalyticsManager.MoneySink.Service, "retrieveshuttle");
                }
                else if (!purchasedItemRepairs)
                {
                    PurchasedLostShuttles = false;
                    personalWallet.Refund(shuttleRetrieveCost);
                }
            }

            if (currentLocIndex < Map.Locations.Count && Map.AllowDebugTeleport)
            {
                Map.SetLocation(currentLocIndex);
            }

            if (AllowedToManageCampaign(sender, ClientPermissions.ManageMap))
            {
                Map.SelectLocation(selectedLocIndex == UInt16.MaxValue ? -1 : selectedLocIndex);
                if (Map.SelectedLocation == null) { Map.SelectRandomLocation(preferUndiscovered: true); }
                if (Map.SelectedConnection != null) { Map.SelectMission(selectedMissionIndices); }
                CheckTooManyMissions(Map.CurrentLocation, sender);
            }

            var prevBuyCrateItems = new Dictionary<Identifier, List<PurchasedItem>>();
            foreach (var kvp in CargoManager.ItemsInBuyCrate)
            {
                prevBuyCrateItems.Add(kvp.Key, new List<PurchasedItem>(kvp.Value));
            }
            foreach (var store in prevBuyCrateItems)
            {
                foreach (var item in store.Value)
                {
                    CargoManager.ModifyItemQuantityInBuyCrate(store.Key, item.ItemPrefab, -item.Quantity, sender);
                }
            }
            foreach (var store in buyCrateItems)
            {
                foreach (var item in store.Value)
                {
                    CargoManager.ModifyItemQuantityInBuyCrate(store.Key, item.ItemPrefab, item.Quantity, sender);
                }
            }

            var prevPurchasedItems = new Dictionary<Identifier, List<PurchasedItem>>();
            foreach (var kvp in CargoManager.PurchasedItems)
            {
                prevPurchasedItems.Add(kvp.Key, new List<PurchasedItem>(kvp.Value));
            }
            foreach (var store in prevPurchasedItems)
            {
                CargoManager.SellBackPurchasedItems(store.Key, store.Value, sender);
            }
            foreach (var store in purchasedItems)
            {
                CargoManager.PurchaseItems(store.Key, store.Value, false, sender);
            }            

            bool allowedToSellSubItems = AllowedToManageCampaign(sender, ClientPermissions.SellSubItems);
            if (allowedToSellSubItems)
            {
                var prevSubSellCrateItems = new Dictionary<Identifier, List<PurchasedItem>>(CargoManager.ItemsInSellFromSubCrate);
                foreach (var store in prevSubSellCrateItems)
                {
                    foreach (var item in store.Value)
                    {
                        CargoManager.ModifyItemQuantityInSubSellCrate(store.Key, item.ItemPrefab, -item.Quantity, sender);
                    }
                }
                foreach (var store in subSellCrateItems)
                {
                    foreach (var item in store.Value)
                    {
                        CargoManager.ModifyItemQuantityInSubSellCrate(store.Key, item.ItemPrefab, item.Quantity, sender);
                    }
                }
            }

            bool allowedToSellInventoryItems = AllowedToManageCampaign(sender, ClientPermissions.SellInventoryItems);
            if (allowedToSellInventoryItems && allowedToSellSubItems)
            {
                // for some reason CargoManager.SoldItem is never cleared by the server, I've added a check to SellItems that ignores all
                // sold items that are removed so they should be discarded on the next message
                var prevSoldItems = new Dictionary<Identifier, List<SoldItem>>(CargoManager.SoldItems);
                foreach (var store in prevSoldItems)
                {
                    CargoManager.BuyBackSoldItems(store.Key, store.Value);
                }
                foreach (var store in soldItems)
                {
                    CargoManager.SellItems(store.Key, store.Value);
                }
            }
            else if (allowedToSellInventoryItems || allowedToSellSubItems)
            {
                var prevSoldItems = new Dictionary<Identifier, List<SoldItem>>(CargoManager.SoldItems);
                foreach (var store in prevSoldItems)
                {
                    store.Value.RemoveAll(predicate);
                    CargoManager.BuyBackSoldItems(store.Key, store.Value);
                }
                foreach (var store in soldItems)
                {
                    store.Value.RemoveAll(predicate);
                }
                foreach (var store in soldItems)
                {
                    CargoManager.SellItems(store.Key, store.Value);
                }
                bool predicate(SoldItem i) => allowedToSellInventoryItems != (i.Origin == SoldItem.SellOrigin.Character);
            }

            foreach (var (prefab, category, _) in purchasedUpgrades)
            {
                UpgradeManager.PurchaseUpgrade(prefab, category, client: sender);

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
                    UpgradeManager.PurchaseItemSwap(purchasedItemSwap.ItemToRemove, purchasedItemSwap.ItemToInstall, client: sender);
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

        public void ServerReadMoney(IReadMessage msg, Client sender)
        {
            NetWalletTransfer transfer = INetSerializableStruct.Read<NetWalletTransfer>(msg);

            switch (transfer.Sender)
            {
                case Some<ushort> { Value: var id }:
                    if (id != sender.CharacterID && !AllowedToManageCampaign(sender, ClientPermissions.ManageMoney)) { return; }

                    Wallet wallet = GetWalletByID(id);
                    if (wallet is InvalidWallet) { return; }

                    TransferMoney(wallet);
                    break;
                case None<ushort> _:
                    if (!AllowedToManageCampaign(sender, ClientPermissions.ManageMoney))
                    {
                        if (transfer.Receiver is Some<ushort> { Value: var receiverId } && receiverId == sender.CharacterID)
                        {
                            GameMain.Server?.Voting.StartTransferVote(sender, null, transfer.Amount, sender);
                            GameServer.Log($"{sender.Name} started a vote to transfer {transfer.Amount} mk from the bank.", ServerLog.MessageType.Money);
                        }
                        return;
                    }

                    TransferMoney(Bank);
                    break;
            }

            void TransferMoney(Wallet from)
            {
                if (!from.TryDeduct(transfer.Amount)) { return; }

                switch (transfer.Receiver)
                {
                    case Some<ushort> { Value: var id }:
                        Wallet wallet = GetWalletByID(id);
                        if (wallet is InvalidWallet) { return; }

                        wallet.Give(transfer.Amount);
                        GameServer.Log($"{sender.Name} transferred {transfer.Amount} mk to {wallet.GetOwnerLogName()} from {from.GetOwnerLogName()}.", ServerLog.MessageType.Money);
                        break;
                    case None<ushort> _:
                        Bank.Give(transfer.Amount);
                        GameServer.Log($"{sender.Name} transferred {transfer.Amount} mk to {Bank.GetOwnerLogName()} from {from.GetOwnerLogName()}.", ServerLog.MessageType.Money);
                        break;
                }
            }

            Wallet GetWalletByID(ushort id)
            {
                Character targetCharacter = Character.CharacterList.FirstOrDefault(c => c.ID == id);
                return targetCharacter is null ? Wallet.Invalid : targetCharacter.Wallet;
            }
        }

        public void ServerReadRewardDistribution(IReadMessage msg, Client sender)
        {
            NetWalletSetSalaryUpdate update = INetSerializableStruct.Read<NetWalletSetSalaryUpdate>(msg);

            if (!AllowedToManageCampaign(sender, ClientPermissions.ManageMoney)) { return; }

            Character targetCharacter = Character.CharacterList.FirstOrDefault(c => c.ID == update.Target);
            targetCharacter?.Wallet.SetRewardDistribution(update.NewRewardDistribution);
            GameServer.Log($"{sender.Name} changed the salary of {targetCharacter?.Name ?? "the bank"} to {update.NewRewardDistribution}%.", ServerLog.MessageType.Money);
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

            if (location != null && AllowedToManageCampaign(sender, ClientPermissions.ManageHires))
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
                            if (TryHireCharacter(location, hireInfo, sender))
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
                new XAttribute("purchasedlostshuttles", PurchasedLostShuttles),
                new XAttribute("purchasedhullrepairs", PurchasedHullRepairs),
                new XAttribute("purchaseditemrepairs", PurchasedItemRepairs),
                new XAttribute("cheatsenabled", CheatsEnabled));

            modeElement.Add(Settings.Save());
            modeElement.Add(SaveStats());
            modeElement.Add(Bank.Save());
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
