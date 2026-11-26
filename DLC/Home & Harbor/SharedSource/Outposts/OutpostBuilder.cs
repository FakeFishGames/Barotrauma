#nullable enable

using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Xml.Linq;

namespace HomeAndHarbor;

public enum CantSpawnCatalogReason
{
    None,
    NoCharacter,
    MissingOutpostManagement,
    MissingTechnology,
    HandsFull
}

[RunConstructorOnInit]
internal static partial class OutpostBuilder
{
    static OutpostBuilder()
    {
        HookService.RegisterHook<PluginCampaignOnEnterOutpostDelegate>(OnEnterOutpost);
        HookService.RegisterHook<PluginRoundStartedDelegate>(OnRoundStarted);
        HookService.RegisterHook<PluginRoundEndedDelegate>(OnRoundEnded);
        HookService.RegisterHook<PluginCampaignOnLeaveOutpostDelegate>(OnLeaveOutpost);
        HookService.RegisterHook<PluginSelectHighestSubmarineTierAvailable>(SelectHighestSubmarineTierAvailable);
    }

    public const int MaxModulesPerOutpost = 30;

    [NetworkSerialize]
    public readonly record struct AddOutpostModuleData(Identifier OutpostModuleIdentifier, int thisGapIndex, UInt16 targetGapId, float hallwayLength) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct RenameOutpostData(string Name) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct RemoveOutpostModuleData(int ModuleIndex) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct MoveOutpostModuleData(int ModuleIndex, Vector2 newOffset) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct MovePendingOutpostModuleData(int ModuleIndex, float hallwayLength) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct SaveAndReloadCurrentLocation() : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct RequestCatalog(Identifier CatalogIdentifier) : INetSerializableStruct;

    /// <summary>
    /// Data sent to the clients to inform them of changes to the outpost.
    /// </summary>
    /// <param name="PendingModuleIdentifiers">Identifiers of the <see cref="PendingOutpostModule">pending outpost module prefabs</see> placed this round.</param>
    /// <param name="PendingModuleThisGapIndices">Which gaps the pending outpost modules use to attach to previous modules? Indices in the <see cref="PendingOutpostModule.ModulePrefab.Gaps"/>module prefab's list of gaps.</param>
    /// <param name="PendingModuleTargetGapIds">IDs of the gaps the pending modules are attached to.</param>
    /// <param name="PendingModuleHallwayLengths">Lengths of the hallways between the pending modules and the modules they're attached to.</param>
    /// <param name="TimeStepsRemainingToBuild">Amount of world steps remaining for the building to be finished.</param>
    /// <param name="ModuleOffsets">How much existing modules have been moved?</param>
    /// <param name="PendingModuleRemovalIndices">Which modules are pending removal? See <see cref="PlacedModule.Index"/>.</param>
    [NetworkSerialize]
    public readonly record struct OutpostModuleChangesData(
        Identifier[] PendingModuleIdentifiers,
        int[] PendingModuleThisGapIndices,
        UInt16[] PendingModuleTargetGapIds,
        float[] PendingModuleHallwayLengths,
        int[] TimeStepsRemainingToBuild,
        Vector2[] ModuleOffsets,
        int[] PendingModuleRemovalIndices) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct CancelOutpostModuleAdditionData(int ModuleIndex) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct SetOutpostOwnershipData(int locationIndex, bool isPlayerOwned) : INetSerializableStruct;

    /// <summary>
    /// Used by the client to inform the server that we want to swap and item in the outpost.
    /// </summary>
    /// <param name="ItemId">Item we are swapping</param>
    /// <param name="NewPrefab">The new item prefab we want to swap to</param>
    [NetworkSerialize]
    public readonly record struct SwapOutpostItemData(ushort ItemId, Identifier NewPrefab) : INetSerializableStruct;

    public class PendingOutpostModule
    {
        public const float MaxHallwayLength = 2500.0f;

        /// <summary>
        /// Contains the items that were taken to start building this module, used to determine which items to refund if this pending module is cancelled.
        /// </summary>
        public Identifier[] ItemsTakenToBuild = [];

        /// <summary>
        /// Id of the gap to attach to.
        /// </summary>
        public ushort GapId;
        /// <summary>
        /// The gap this module is attached to.
        /// </summary>
        public Gap? Gap;
        public PurchasableOutpostModulePrefab? ModulePrefab;

        public OutpostModuleInfo.GapPosition SideToAttachTo =>
           ThisGap == null ? OutpostModuleInfo.GapPosition.None : OutpostGenerator.GetOpposingGapPosition(ThisGap.Position);

        /// <summary>
        /// Refers to the gap in this module that should attach to the previous module. Index in the <see cref="ModulePrefab.Gaps">module prefab's list of gaps</see>. 
        /// </summary>
        public readonly int ThisGapIndex;

        public PurchasableOutpostModulePrefab.GapPositionInfo? ThisGap =>
            ModulePrefab == null || ThisGapIndex < 0 || ThisGapIndex >= ModulePrefab.Gaps.Count ?
                null :
                ModulePrefab.Gaps[ThisGapIndex];

        private float hallwayLength;

        /// <summary>
        /// Length of the hallway that connects this module to the previous one. 
        /// Note that his can be negative: negative value means the hallway extends from the previous module to the left or down, depending on the direction of the module.
        /// </summary>
        public float HallwayLength
        {
            get => hallwayLength;
            set { hallwayLength = MathHelper.Clamp(value, -MaxHallwayLength, MaxHallwayLength); }
        }

        /// <summary>
        /// How many world steps must pass for the module to be built.
        /// </summary>
        public int TimeStepsRemainingToBuild;

        public bool IsReadyToBuild => TimeStepsRemainingToBuild <= 0;

        public PendingOutpostModule(PurchasableOutpostModulePrefab modulePrefab, PurchasableOutpostModulePrefab.GapPositionInfo thisGap, Gap targetGap)
        {
            ModulePrefab = modulePrefab;
            Gap = targetGap;
            GapId = targetGap.ID;
            ThisGapIndex = modulePrefab.Gaps.IndexOf(thisGap);
            TimeStepsRemainingToBuild = modulePrefab.TimeStepsToBuild;
            if (ThisGapIndex == -1)
            {
                throw new InvalidOperationException($"Could not find the specified gap ({thisGap}) in the module prefab {modulePrefab}");
            }
        }

        public PendingOutpostModule(XElement element)
        {
            Identifier moduleId = element.GetAttributeIdentifier(nameof(ModulePrefab), Identifier.Empty);
            if (!PurchasableOutpostModulePrefab.Prefabs.TryGet(moduleId, out var modulePrefab) || modulePrefab == null)
            {
                DebugConsole.ThrowError($"Failed to place a pending outpost module. Could not find a module with the identifier \"{moduleId}\".");
            }
            else
            {
                ModulePrefab = modulePrefab;
            }
            GapId = element.GetAttributeUInt16(nameof(GapId), Entity.NullEntityID);
            HallwayLength = element.GetAttributeFloat(nameof(HallwayLength), 0.0f);
            ThisGapIndex = element.GetAttributeInt(nameof(ThisGapIndex), -1);
            TimeStepsRemainingToBuild = element.GetAttributeInt(nameof(TimeStepsRemainingToBuild), 0);
            ItemsTakenToBuild = element.GetAttributeIdentifierArray(nameof(ItemsTakenToBuild), []);
        }

        public void Save(XElement parentElement)
        {
            parentElement.Add(
                new XElement(nameof(PendingOutpostModule),
                    new XAttribute(nameof(GapId), GapId),
                    new XAttribute(nameof(ModulePrefab), ModulePrefab?.Identifier ?? Identifier.Empty),
                    new XAttribute(nameof(ThisGapIndex), ThisGapIndex),
                    new XAttribute(nameof(HallwayLength), HallwayLength),
                    new XAttribute(nameof(TimeStepsRemainingToBuild), TimeStepsRemainingToBuild),
                    new XAttribute(nameof(ItemsTakenToBuild), string.Join(",", ItemsTakenToBuild))));
        }

        public override string ToString()
        {
            return $"{nameof(PendingOutpostModule)} ({ModulePrefab?.OutpostInfo.Name ?? "null"})";
        }
    }

    public enum OutpostCreationMode
    {
        Generate,
        Load
    }

    private static readonly Identifier ShipyardTechnologyIdentifier = "shipyard".ToIdentifier();
    private static readonly Identifier PlayerOutpostLocationIdentifier = "playeroutpost".ToIdentifier();

    public static OutpostCreationMode GetOutpostCreationMode(PersistentOutpostData outpostData) =>
        // Use Load mode only when there's actually saved data to load, otherwise fall back to Generate
        outpostData.IsPlayerOwned && outpostData.OutpostEntitiesElement != null
            ? OutpostCreationMode.Load
            : OutpostCreationMode.Generate;

    public static void Init()
    {
        InitProjectSpecific();
    }

    static partial void InitProjectSpecific();

    private static bool newHallwaysCreated;

    public static int GetPriceForItemSwap(ItemPrefab prefab)
    {
        if (ItemSwapStorage.TryGetItemSwapStorageForCurrentCampaign(out ItemSwapStorage? storage))
        {
            if (storage.GetItemCount(prefab) > 0)
            {
                return 0;
            }

            if (prefab.SwappableItem != null)
            {
                return prefab.SwappableItem.BasePrice / 10;
            }
            else
            {
                DebugConsole.ThrowError($"Item prefab '{prefab.Identifier}' does not have a swappable item defined.");
                return 0;
            }
        }

        DebugConsole.ThrowError($"'{nameof(ItemSwapStorage)}' is not available in the current campaign.");
        return 0;
    }

    /// <summary>
    /// Can the outpost item be swapped to the specified prefab? True if the item is already in the item swap storage (no research needed), or if the required technology has been researched.
    /// </summary>
    /// <param name="technologyPrefab">The technology required to unlock the swap, if any.</param>
    public static bool IsItemSwapAvailable(ItemPrefab prefab, out TechnologyPrefab? technologyPrefab)
    {
        technologyPrefab = null;

        if (ItemSwapStorage.TryGetItemSwapStorageForCurrentCampaign(out ItemSwapStorage? storage))
        {
            if (storage.GetItemCount(prefab) > 0)
            {
                return true;
            }
        }

        if (OutpostSwappableInfoPrefab.Prefabs.TryGet(prefab.Identifier, out OutpostSwappableInfoPrefab? swapInfo))
        {
            if (!TechnologyPrefab.Prefabs.TryGet(swapInfo.RequiredTechnology, out technologyPrefab))
            {
                DebugConsole.ThrowError($"Failed to find technology with identifier {swapInfo.RequiredTechnology}");
                return true;
            }

            if (ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager))
            {
                return researchManager.IsFinished(technologyPrefab);
            }
        }

        return true;
    }

    /// <summary>
    /// Hook for disallowing submarine item swaps that haven't been researched yet in player outposts.
    /// </summary>
    public static bool CanSwapSubmarineItem(Item item, ItemPrefab replacement, Location? location)
    {
        // Research is only required in the player's own outposts, other outposts use the faction requirements of the swappable item
        if (location == null || PersistentOutpostData.GetPersistentOutpostData(location) is not { IsPlayerOwned: true }) { return true; }
        if (!OutpostSwappableInfoPrefab.Prefabs.TryGet(replacement.Identifier, out OutpostSwappableInfoPrefab? swapInfo)) { return true; }
        if (!ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager)) { return true; }
        return researchManager.IsFinished(swapInfo.RequiredTechnology);
    }

    public static bool DoesHaveOutpostManagementItem(Submarine outpost) =>
        outpost.GetItems(alsoFromConnectedSubs: true)
        .Any(static item => item.GetComponent<OutpostManagementComponent>() != null);

    public static CantSpawnCatalogReason CanSpawnOrEquipCatalogForCharacter(Submarine outpost, Character? character, OutpostCatalogPrefab catalogPrefab)
    {
        bool hasOutpostManagementItem = DoesHaveOutpostManagementItem(outpost);

        if (!catalogPrefab.RequiredTechnologyIdentifier.IsEmpty &&
            ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager) &&
            !researchManager.IsFinished(catalogPrefab.RequiredTechnologyIdentifier))
        {
            return CantSpawnCatalogReason.MissingTechnology;
        }
        else if (!hasOutpostManagementItem && catalogPrefab.RequiresOutpostManagement)
        {
            return CantSpawnCatalogReason.MissingOutpostManagement;
        }
        else if (character == null || character.Removed || character.IsDead) 
        { 
            return CantSpawnCatalogReason.NoCharacter; 
        }
        else if (character.HeldItems.Any())
        {
            // Can't equip catalog if the item in the hands cant be unheld
            if (character.HeldItems.Any(static item => !item.CanBeUnheld))
            {
                return CantSpawnCatalogReason.HandsFull;
            }

            // Can't spawn catalog if there's no space in the inventory
            if (!character.Inventory.IsSlotEmpty(InvSlotType.Any) && 
                // Unless the character already has the catalog, in that case we can just equip it
                !character.Inventory.AllItems.Any(item => item.Prefab.Identifier == catalogPrefab.CatalogItemIdentifier))
            {
                return CantSpawnCatalogReason.HandsFull;
            }
        }

        return CantSpawnCatalogReason.None;
    }

    public static void SpawnOrEquipCatalogForCharacter(Character character, OutpostCatalogPrefab catalogPrefab)
    {
        Item? existingCatalog = character.Inventory.AllItems.FirstOrDefault(item => item.Prefab.Identifier == catalogPrefab.CatalogItemIdentifier);

        if (existingCatalog != null)
        {
            equipCatalog(existingCatalog);
        }
        else if (ItemPrefab.Prefabs.TryGet(catalogPrefab.CatalogItemIdentifier, out ItemPrefab? itemPrefab))
        {
            Entity.Spawner.AddItemToSpawnQueue(itemPrefab, character.Inventory, onSpawned: item =>
            {
                equipCatalog(item);
            });
        }

        void equipCatalog(Item catalogItem)
        {
            List<InvSlotType> allowedSlots = catalogItem.AllowedSlots.ToList();
            allowedSlots.Remove(InvSlotType.Any);
            character.Inventory.TryPutItem(catalogItem, character, allowedSlots);
        }
    }

    public static Submarine? LoadOutpost(OutpostGenerationParams generationParams, PersistentOutpostData outpostData, bool onlyEntrance = false, bool allowInvalidOutpost = false)
    {
        if (GetOutpostCreationMode(outpostData) != OutpostCreationMode.Load) { return null; }

        if (outpostData.OutpostEntitiesElement == null)
        {
            DebugConsole.ThrowError("Failed to load an outpost (outpost XML element not set, the outpost may not have been saved yet?).");
            return null;
        }

        DebugConsole.NewMessage($"Loading outpost {outpostData.Location.DisplayName}{(onlyEntrance ? " (only entrance)" : string.Empty)} from XML...");

        var entitiesToLoadElement = new XElement(outpostData.OutpostEntitiesElement);

        var outpostModules = new List<PlacedModule>();
        foreach (var subElement in outpostData.OutpostEntitiesElement.Elements(nameof(PlacedModule)))
        {
            try
            {
                outpostModules.Add(new PlacedModule(subElement));
            }
            catch (Exception e)
            {
                //Handle exception thrown if the specified module file can't be found. The module will not be loaded.
                DebugConsole.ThrowError("Error while loading an outpost.", e);
            }
        }

        //outposts don't necessarily have any modules, they may have been fully pre-built (e.g. tutorial outpost or some special hand-built outposts)
        if (outpostModules.Any())
        {
            var initialModule = outpostModules.First();
            if (onlyEntrance)
            {
                //remove entities that don't belong to the initial module
                foreach (var subElement in entitiesToLoadElement.Elements().ToList())
                {
                    ushort id = subElement.GetAttributeUInt16("id", Entity.NullEntityID);
                    if (id == Entity.NullEntityID) { continue; }
                    if (!initialModule.EntityIds.Contains(id))
                    {
                        subElement.Remove();
                    }
                }
            }
        }

        SubmarineInfo outpostInfo = new SubmarineInfo(filePath: string.Empty, element: entitiesToLoadElement)
        {
            DisplayName = outpostData.Location.DisplayName.Value,
            Type = SubmarineType.Outpost,
            //if we're loading the sub from XML, the initial supplies must have been spawned the first time it was generated
            InitialSuppliesSpawned = true
        };
        if (outpostData.IsPlayerOwned &&
            OutpostGenerationParams.OutpostParams.TryGet(PlayerOutpostLocationIdentifier, out OutpostGenerationParams? outpostGenerationParams))
        {
            outpostInfo.OutpostGenerationParams = outpostGenerationParams;
        }

        bool anyRemovals = !onlyEntrance && outpostData.PendingModuleRemovalsInLatestSave.Any();
        bool anyAdditions = !onlyEntrance && outpostData.GetPendingModulesInLatestSave().Any(p => p.IsReadyToBuild);
        newHallwaysCreated = false;

        Submarine outpost = new Submarine(outpostInfo, loadEntities: (Submarine sub) => 
            LoadOutpostEntities(sub, outpostData, outpostModules, onlyEntrance));
        if (onlyEntrance)
        {
            for (int i = outpost.OutpostModules.Count - 1; i > 0; i--)
            {
                outpost.OutpostModules.RemoveAt(i);
            }
        }

        OutpostGenerator.LinkOxygenGenerators(outpost.GetItems(alsoFromConnectedSubs: false), removeExistingLinks: true);        

        //must lock the doors that now lead to nowhere if we removed modules or only generated the initial module
        if (anyRemovals || anyAdditions || onlyEntrance || newHallwaysCreated) 
        { 
            OutpostGenerator.LockUnusedDoors(outpost.OutpostModules, removeUnusedGaps: true);
            OutpostGenerator.AlignLadders(outpost.OutpostModules);
        }

        //enable faction entities for "no faction" = disable coalition/separatist elements
        outpost.EnableFactionSpecificEntities(factionIdentifier: Identifier.Empty);

        if (outpostData.IsPlayerOwned)
        {
            Submarine.SetOutpostTeam(outpost, CharacterTeamType.Team1);
            if (!onlyEntrance)
            {
                outpostData.DwellerManager.SpawnNPCs(outpost);
            }
            OutpostGenerator.SetWiresLocked(outpost, isLocked: false);
            OutpostGenerator.SetDevicesDetachable(outpost, disableDetaching: false);
            OutpostGenerator.SetDevicesPlayerTeamInteractable(outpost);
            OutpostGenerator.SetAllowSellingOutpostItems(outpost, allowSelling: true);

            if (GameMain.GameSession?.Campaign is { } campaign)
            {
                float artificialLocationDifficulty = CalculateArtificialLevelDifficulty(campaign.Map, outpostData.Location);
                if (artificialLocationDifficulty > outpostData.Location.LevelData.Difficulty)
                {
                    DebugConsole.NewMessage($"Increased the difficulty of location {outpostData.Location.DisplayName} from {outpostData.Location.LevelData.Difficulty} to {artificialLocationDifficulty} based on the furthest visited level.", Color.Orange);
                    outpostData.Location.LevelData.Difficulty = artificialLocationDifficulty;
                }
                foreach (var connection in outpostData.Location.Connections)
                {
                    if (artificialLocationDifficulty > connection.Difficulty)
                    {
                        connection.Difficulty = artificialLocationDifficulty;
                    }
                }
            }
        }

        CheckLoadingErrors(outpost);

        return outpost;
    }

    private static bool HasOutpostModulesReadyToBuildOrRemove()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign) { return false; }
        if (!Level.IsLoadedOutpost ||
            Level.Loaded?.StartLocation is not { } startLocation || 
            PersistentOutpostData.GetPersistentOutpostData(startLocation) is not { IsPlayerOwned: true } outpostData) 
        { 
            return false; 
        }

        float elapsedWorldSteps = CampaignMode.GetElapsedWorldSteps(GameMain.GameSession?.RoundDuration ?? 0, CampaignMode.TransitionType.None);
        int elapsedWholeWorldSteps = (int)Math.Floor(elapsedWorldSteps);

        return outpostData.PendingOutpostModules.Any(p => p.TimeStepsRemainingToBuild <= elapsedWholeWorldSteps) || outpostData.PendingModuleRemovals.Any();
    }

    /// <summary>
    /// Resets reactor properties that NPC outpost modules may override back to the item prefab's values.
    /// E.g. reactors in abandoned outpost modules don't consume fuel, which shouldn't carry over to player-owned outposts.
    /// </summary>
    public static void ResetModuleSpecificReactorProperties(Submarine outpost)
    {
        foreach (Item item in outpost.GetItems(alsoFromConnectedSubs: true))
        {
            foreach (ReactorBase reactor in item.GetComponents<ReactorBase>())
            {
                SerializableProperty.DeserializeProperty(reactor, nameof(ReactorBase.FuelConsumptionRate), reactor.originalElement);
                if (reactor is Reactor nuclearReactor)
                {
                    SerializableProperty.DeserializeProperty(nuclearReactor, nameof(Reactor.MeltdownDelay), nuclearReactor.originalElement);
                    SerializableProperty.DeserializeProperty(nuclearReactor, nameof(Reactor.FireDelay), nuclearReactor.originalElement);
                }
            }
        }
    }

    private static float CalculateArtificialLevelDifficulty(Map map, Location location)
    {
        return Math.Max(location.LevelData.Difficulty, map.GetHighestVisitedLevelDifficulty());
    }

    private static void OnEnterOutpost(CampaignMode mode, Location location, Submarine outpost)
    {
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (outpostData == null) { return; }

        outpostData.CreatureManager?.OnEnterOutpost(mode, location, outpost);
        if (outpostData is { IsPlayerOwned: true })
        {
            DirtyHulls(outpost, outpostData.WorldStepsSinceLastVisit);
        }
    }

    private static void OnRoundStarted(GameSession gameSession)
    {
        if (gameSession?.StartLocation is not { } location || 
            Level.Loaded?.StartOutpost is not { } outpost ||
            !Level.IsLoadedOutpost)
        {
            return;
        }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (outpostData is { IsPlayerOwned: true })
        {
            //do this here rather than OnEnterOutpost,
            //to ensure players/bots have spawned so we can avoid spawning pests in the same rooms
            InfestVents(outpost, outpostData.WorldStepsSinceLastVisit);
        }
    }

    private static void OnRoundEnded(GameSession gameSession)
    {
        if (gameSession?.Campaign == null ||
            !Level.IsLoadedOutpost ||
            Level.Loaded?.StartLocation is not { } location)
        {
            return;
        }
        if (PersistentOutpostData.GetPersistentOutpostData(location) is { IsPlayerOwned: true } outpostData)
        {
            //world steps (in which modules may become ready to build) don't necessarily pass when the round ends (e.g. when reloading the outpost to finish module construction),
            //but modules marked for removal are removed anyway, so the faction needs to be updated here too
            UpdateSecondaryFactionFromPendingChanges(outpostData);
        }
    }

    private static void OnLeaveOutpost(CampaignMode campaignMode)
    {
        if (GameMain.GameSession?.StartLocation == null) { return; }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(GameMain.GameSession.StartLocation);
        if (outpostData == null) { return; }
        outpostData.CreatureManager?.OnLeaveOutpost(campaignMode);
    }

    private static int SelectHighestSubmarineTierAvailable(SubmarineClass subClass, Identifier locationType)
    {
        if (locationType != PlayerOutpostLocationIdentifier) { return -1; }

        if (!ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager))
        {
            return -1;
        }

        if (researchManager.IsFinished(ShipyardTechnologyIdentifier))
        {
            return SubmarineInfo.HighestTier;
        }

        return SubmarineInfo.HighestTier - 1;
    }

    private static void DirtyHulls(Submarine outpost, int worldStepsSinceLastVisit)
    {
        if (GameMain.NetworkMember is { IsClient: true }) { return; }
        if (worldStepsSinceLastVisit <= 0) { return; }

        foreach (var item in outpost.GetItems(alsoFromConnectedSubs: true))
        {
            if (item.CurrentHull == null) { continue; }
            //rough check for machines that could be dirtying up things
            if (item.Prefab.Category != MapEntityCategory.Machine ||
                item.GetComponent<Powered>() == null)
            {
                continue;
            }
            //only mechanically repaired machines get oily
            var repairable = item.GetComponent<Repairable>();
            if (repairable == null || !repairable.RequiredSkills.Any(s => s != null && s.Identifier == "mechanical")) { continue; }

            //5% chance of a machine getting dirty per world step
            float dirtProbability = worldStepsSinceLastVisit / 20.0f;
            if (Rand.Range(0.0f, 1.0f) > dirtProbability) { continue; }
            item.CurrentHull.AddDecal("oilyspatter".ToIdentifier(),
                item.WorldPosition + Rand.Vector(Rand.Range(0.0f, Math.Min(item.RectHeight, item.RectWidth) * 0.25f)),
                scale: 1.0f,
                isNetworkEvent: false);            
        }

        foreach (var hull in outpost.GetHulls(alsoFromConnectedSubs: true))
        {
            //calculate "how much we want to dirty up the hull" as a value between 0 and 100
            float dirtyAmount = 0.0f;
            float maxDirtinessPerWorldStep = 10.0f;

            //extra dirtyness for each pet in the hull
            foreach (var character in Character.CharacterList)
            {
                if (character.IsPet && character.CurrentHull == hull)
                {
                    dirtyAmount += 3.0f;
                }
            }

            //15 extra dirtyness for a fully flooded hull
            dirtyAmount += hull.WaterPercentage * 0.15f;

            dirtyAmount = Math.Min(dirtyAmount, maxDirtinessPerWorldStep);
            if (dirtyAmount <= 0.0f) { continue; }

            hull.DirtyAllSections(dirtyAmount / 100.0f * worldStepsSinceLastVisit, gradientToBottom: true, requiresUpdate: true);
        }
    }

    private static void InfestVents(Submarine outpost, int worldStepsSinceLastVisit)
    {
        if (GameMain.NetworkMember is { IsClient: true }) { return; }
        if (worldStepsSinceLastVisit <= 0) { return; }

        //empty hulls (no workstation or furniture) are easily infested,
        //occupied ones need to be dirtier and are less likely to get infested
        const float minDirtinessEmptyHull = 0.2f;
        const float minDirtinessOccupiedHull = 0.5f;
        const float occupiedHullChanceMultiplier = 0.5f;

        Identifier furnitureTag = "furniture".ToIdentifier();
        var occupiedHulls = new HashSet<Hull>();
        foreach (var item in outpost.GetItems(alsoFromConnectedSubs: true))
        {
            if (item.CurrentHull == null) { continue; }
            if (item.GetComponent<WorkstationComponent>() != null || item.HasTag(furnitureTag))
            {
                occupiedHulls.Add(item.CurrentHull);
            }
        }

        var infestableVents = outpost.GetItems(alsoFromConnectedSubs: true)
            .Where(item => item.GetComponent<InfestableVentComponent>() != null && item.CurrentHull != null)
            .Select(item => item.GetComponent<InfestableVentComponent>())
            .Where(vent => vent.CanBeInfested);

        int maxVentsToInfest = outpost.OutpostModules.Count / 2;
        int infestedVents = infestableVents.Count(vent => vent.IsInfested);

        foreach (var vent in infestableVents)
        {
            if (infestedVents >= maxVentsToInfest)
            {
                break;
            }

            Hull hull = vent.Item.CurrentHull;

            //decals can increase the dirtiness by 0.5
            float hullDirtiness = hull.AverageDirtiness + (float)hull.DecalCount / (Hull.MaxDecalsPerHull * 2);

            bool occupied = occupiedHulls.Contains(hull);
            float minDirtiness = occupied ? minDirtinessOccupiedHull : minDirtinessEmptyHull;
            if (hullDirtiness < minDirtiness) { continue; }

            // In an empty hull, having a hull dirtness of 0.5 and having 5 world steps pass would mean a 50% chance of infestation
            float ventInfestChance = (worldStepsSinceLastVisit / 5f) * hullDirtiness;
            if (occupied) { ventInfestChance *= occupiedHullChanceMultiplier; }

            if (Rand.Range(0f, 1f) <= ventInfestChance)
            {
                vent.Infest();
                infestedVents++;
            }         
        }
    }

    /// <summary>
    /// Add pending outpost modules to the outpost.
    /// </summary>
    /// <returns>List of new modules that were added to the outpost.</returns>
    private static List<PlacedModule> ProcessAdditions(Submarine outpost, PersistentOutpostData outpostData, IdRemap idRemap, List<MapEntity> entityList)
    {
        List<PlacedModule> newModules = [];
        //find the gaps the pending modules are attached to (including the ones that can't be built yet)
        foreach (var pendingModule in outpostData.PendingOutpostModules)
        {
            UpdateGap(pendingModule);
        }

        //build the modules that were pending in the latest save, not ones that may have been added after that (see PersistentOutpostData.pendingModuleRemovalsInLatestSave)
        foreach (var pendingModule in outpostData.GetPendingModulesInLatestSave())
        {
            ushort remappedGapId = UpdateGap(pendingModule);

            if (pendingModule.ModulePrefab == null) { continue; }
            if (!pendingModule.IsReadyToBuild) { continue; }

            PlacedModule? previousModule = null;
            PlacedModule.GapInfo? previousGapInfo = null;
            foreach (var module in outpost.OutpostModules)
            {
                foreach (var gapInfo in module.AllGaps)
                {
                    if (gapInfo.GapId == remappedGapId)
                    {
                        previousModule = module;
                        previousGapInfo = gapInfo;
                        break;
                    }
                    if (previousModule != null) { break; }
                }
            }

            if (previousModule == null || previousGapInfo == null)
            {
                DebugConsole.ThrowError($"Failed to place the pending module \"{pendingModule.ModulePrefab.OutpostInfo.DisplayName}\". Could not find find the previous module for a gap with the ID {pendingModule.GapId}.");
                continue;
            }

            if (previousGapInfo.Gap is null)
            {
                DebugConsole.ThrowError($"Failed to place the pending module \"{pendingModule.ModulePrefab.OutpostInfo.DisplayName}\". Could not find a gap in the previous module {previousModule.Info.DisplayName} (gap id: {pendingModule.GapId})");
                continue;
            }

            if (pendingModule.ThisGapIndex < 0 || pendingModule.ThisGapIndex >= pendingModule.ModulePrefab.Gaps.Count)
            {
                DebugConsole.ThrowError($"Failed to place the pending module \"{pendingModule.ModulePrefab.OutpostInfo.DisplayName}\". Gap index out of bounds ({pendingModule.ThisGapIndex}, module prefab {pendingModule.ModulePrefab} has {pendingModule.ModulePrefab.Gaps.Count} gaps.");
                continue;
            }

            var thisGapPosition = OutpostGenerator.GetOpposingGapPosition(pendingModule.SideToAttachTo);
            //can't use the module count as the index: if modules have been removed, the count may match the index of an existing module
            int newModuleIndex = outpost.OutpostModules.Any() ? outpost.OutpostModules.Max(m => m.Index) + 1 : 0;
            var placedModule =
                new PlacedModule(
                    newModuleIndex,
                    pendingModule.ModulePrefab.OutpostInfo,
                    previousModule,
                    thisGapPosition)
            {
                Category = pendingModule.ModulePrefab.Category
            };

            int idOffset = IdRemap.DetermineNewOffset();
            ushort remappedPreviousGapId = idRemap.GetOffsetId(pendingModule.GapId);
            Gap? previousGap = MapEntity.MapEntityList.FirstOrDefault(entity => entity is Gap gap && gap.ID == remappedPreviousGapId) as Gap;

            Rectangle? forceThisGap = pendingModule.ThisGapIndex > -1 ? pendingModule.ModulePrefab.Gaps[pendingModule.ThisGapIndex]?.Rect : null;

            if (previousGap != null)
            {
                //if the orientation of the gaps doesn't match, or if one has a door while the other doesn't, we must've chosen an incorrect gap (may happen when modules have been modified and the IDs no longer match)
                OutpostModuleInfo.GapPosition desiredPreviousGapPosition = pendingModule.SideToAttachTo;
                OutpostModuleInfo.GapPosition actualPreviousGapPosition = OutpostGenerator.GetGapPosition(previousGap);
                if (desiredPreviousGapPosition != actualPreviousGapPosition)
                {
                    DebugConsole.AddWarning($"The position of the gap the module {placedModule.Name} is trying to attach to is invalid (the module should attach to {desiredPreviousGapPosition} but the gap is at {actualPreviousGapPosition})." +
                        $"This may happen if the module files have been modified after the save was made. Trying to fix...");
                    if (OutpostGenerator.GetGap(placedModule.PreviousModule.Entities, desiredPreviousGapPosition) is Gap newGap)
                    {
                        previousGap = newGap;
                        DebugConsole.NewMessage("Found a new gap to attach to at the correct side of the module.");
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Failed to find a new gap to attach to at the correct side of the module. Skipping loading the module  {placedModule.Name}.");
                        continue;
                    }
                }
            }

            OutpostGenerator.LoadModule(outpost, placedModule, moduleIndex: newModuleIndex,outpost.Info.OutpostGenerationParams, idOffset: ref idOffset, 
                useGapWithRect: forceThisGap,
                forcePreviousGap: previousGap);

            if (placedModule.PreviousGap != null)
            {
                placedModule.PreviousGap?.SetDisabled(false);
                Gap.UpdateHulls();
            }

            foreach (var entity in placedModule.Entities)
            {
                if (entity is Gap gap)
                {
                    placedModule.AllGaps.Add(new PlacedModule.GapInfo(gap));
                }
            }
            Vector2 moveAmount = placedModule.Offset - previousModule.Offset +
                (placedModule.ThisGap is { IsHorizontal: true } ? Vector2.UnitX : Vector2.UnitY) * pendingModule.HallwayLength;
            placedModule.TemporaryMoveOffset = moveAmount;

            const float maxMoveAmount = 2000.0f;
            Vector2 moveDir = OutpostGenerator.GetGapDirection(thisGapPosition);
            Vector2 moveStep = moveDir * 50.0f;
            Vector2 currentMove = Vector2.Zero;
            while (outpost.OutpostModules.Any(otherModule => ModulesOverlap(placedModule, otherModule)))
            {
                placedModule.TemporaryMoveOffset += moveStep;
                currentMove += moveStep;
                if (OutpostGenerator.AnyModuleOverlapsWithModuleConnections(moduleWithHallway: placedModule, outpost.OutpostModules, modulesAlreadyInPlace: true) ||
                    OutpostGenerator.ModuleBelowInitialModule(placedModule, outpost.OutpostModules.First(), modulesAlreadyInPlace: true) ||
                    ModuleAboveRestrictedModule(placedModule, outpost.OutpostModules))
                {
                    placedModule.TemporaryMoveOffset -= currentMove;
                    DebugConsole.AddWarning($"Could not resolve {placedModule.Name} overlapping with other modules.");
                    break;
                }
                if (currentMove.Length() > maxMoveAmount) { break; }
            }

            placedModule.PositionRelativeToSub += placedModule.TemporaryMoveOffset;
            Submarine.RepositionEntities(placedModule.TemporaryMoveOffset, placedModule.Entities);
            placedModule.TemporaryMoveOffset = Vector2.Zero;

            outpost.OutpostModules.Add(placedModule);

            Gap.UpdateHulls();
            entityList.AddRange(OutpostGenerator.GenerateHallway(outpost, outpostData.Location.Type, placedModule, outpost.Info.OutpostGenerationParams));
            entityList.AddRange(placedModule.Entities);

            newModules.Add(placedModule);
        }
        outpostData.ClearPendingModules(onlyFinishedModules: true);
        return newModules;

        //makes sure the pending module's gap points to the correct gap after remapping IDs
        ushort UpdateGap(PendingOutpostModule pendingModule)
        {
            ushort remappedGapId = idRemap.GetOffsetId(pendingModule.GapId);
            pendingModule.Gap = Entity.FindEntityByID(remappedGapId) as Gap;
            if (pendingModule.Gap != null)
            {
                pendingModule.GapId = pendingModule.Gap.ID;
            }
            return remappedGapId;
        }
    }


    /// <summary>
    /// How much modules attached directly to each other are allowed to overlap with each other (more specifically, 
    /// how much the hulls of the first module can overlap with the bounds of the 2nd module or vice versa).
    /// Some amount of overlap is necessary, because aligning the modules' doors/hatches will always make them overlap somewhat.
    /// </summary>
    private const float OverlapToleranceBetweenAdjacentModules = 16.0f;

    /// <summary>
    /// How much the hulls of outpost modules are allowed to overlap. Must be larger than 0 due to have the modules 
    /// have been made: the hulls often extend inside floors/walls/ceilings, and placing the modules right against each other will cause some overlap between the hulls.
    /// </summary>
    private const float HullOverlapTolerance = 16.0f;

    private static bool ModulesOverlap(PlacedModule newModule, PlacedModule alreadyPlacedModule)
    {
        Rectangle bounds1 = newModule.Bounds;
        bounds1.Location += newModule.TemporaryMoveOffset.ToPoint();
        Rectangle bounds2 = alreadyPlacedModule.Bounds;
        bounds2.Location += alreadyPlacedModule.PositionRelativeToSub.ToPoint();

        //more tolerance on adjacent modules to prevent generating an unnecessary, small hallway between them
        if (newModule.PreviousModule == alreadyPlacedModule || alreadyPlacedModule.PreviousModule == newModule)
        {
            bounds1.Inflate(-OverlapToleranceBetweenAdjacentModules, -OverlapToleranceBetweenAdjacentModules);
            bounds2.Inflate(-OverlapToleranceBetweenAdjacentModules, -OverlapToleranceBetweenAdjacentModules);
        }

        Rectangle hullBounds1 = newModule.HullBounds;
        hullBounds1.Location += newModule.TemporaryMoveOffset.ToPoint();
        Rectangle hullBounds2 = alreadyPlacedModule.HullBounds;
        hullBounds2.Location += alreadyPlacedModule.PositionRelativeToSub.ToPoint();

        hullBounds1.Inflate(-HullOverlapTolerance, -HullOverlapTolerance);
        hullBounds2.Inflate(-HullOverlapTolerance, -HullOverlapTolerance);

        return hullBounds1.Intersects(hullBounds2) || hullBounds1.Intersects(bounds2) || hullBounds2.Intersects(bounds1);
    }

    /// <summary>
    /// Is the module above a module that doesn't allow modules above it (<see cref="PurchasableOutpostModulePrefab.DisallowModulesAbove"/>)?
    /// Or if the module itself is such a module, are there other modules above it?
    /// </summary>
    private static bool ModuleAboveRestrictedModule(PlacedModule module, IEnumerable<PlacedModule> otherModules)
    {
        bool disallowModulesAboveThis = FindModulePrefab(module) is { DisallowModulesAbove: true };
        foreach (PlacedModule otherModule in otherModules)
        {
            if (FindModulePrefab(otherModule) is { DisallowModulesAbove: true } &&
                IsAbove(module, otherModule))
            {
                return true;
            }
            if (disallowModulesAboveThis && IsAbove(otherModule, module))
            {
                return true;
            }
        }
        return false;

        static bool IsAbove(PlacedModule module, PlacedModule restrictedModule)
        {
            Rectangle bounds = GetHullBounds(module);
            Rectangle restrictedBounds = GetHullBounds(restrictedModule);
            //same tolerance as with overlaps, so modules attached right next to the restricted module don't count as being above it
            restrictedBounds.Inflate(-OverlapToleranceBetweenAdjacentModules, 0.0f);
            //y increases upwards in world space, so the top edge of the rectangles is at Y + Height
            return
                bounds.Y + bounds.Height > restrictedBounds.Y + restrictedBounds.Height &&
                bounds.Right > restrictedBounds.Left && bounds.Left < restrictedBounds.Right;
        }

        static Rectangle GetHullBounds(PlacedModule module)
        {
            Rectangle hullBounds = module.HullBounds;
            hullBounds.Location += (module.PositionRelativeToSub + module.TemporaryMoveOffset).ToPoint();
            return hullBounds;
        }
    }

    /// <summary>
    /// Remove the outpost modules that have been marked for removal.
    /// </summary>
    /// <returns>Were any modules removed?</returns>
    private static bool ApplyPendingModuleRemovals(Submarine outpost, PersistentOutpostData outpostData)
    {
        //remove the modules that were pending removal in the latest save,
        //not ones that may have been queued after that (see PersistentOutpostData.pendingModuleRemovalsInLatestSave)
        bool anyRemovals = outpostData.PendingModuleRemovalsInLatestSave.Any();
        foreach (int removal in outpostData.PendingModuleRemovalsInLatestSave)
        {
            PlacedModule? moduleToRemove = outpost.OutpostModules.FirstOrDefault(m => m.Index == removal);
            if (moduleToRemove == null)
            {
                DebugConsole.ThrowError($"Failed to remove a module marked for removal. Could not find a module with the index {removal}.");
                continue;
            }

            //the links of hallway entities aren't initialized until MapLoaded (unlike the module entities, see PlacedModule.InitializeEntities),
            //initialize them before removal so removing them also removes them from the linked entities' lists (e.g. the waypoints in the previous module's gap)
            MapEntity.InitializeLoadedLinks(moduleToRemove.HallwayEntities);

            //remove wires connected to the devices first
            //(if we remove any devices first, we can no longer find the wires that were connected to them)
            foreach (var entity in moduleToRemove.Entities)
            {
                if (entity is Item item)
                {
                    //remove all wires connected to the item too
                    foreach (var connectionPanel in item.GetComponents<ConnectionPanel>())
                    {
                        connectionPanel.GetAllWires().ToList().ForEach(wire => wire.Remove());                        
                    }
                }
            }

            foreach (var mapEntity in MapEntity.MapEntityList.ToList())
            {
                if (mapEntity is LinkedSubmarine linkedSub)
                {
                    if (moduleToRemove.EntityIds.Contains(linkedSub.OriginalLinkedToID))
                    {
                        linkedSub.Remove();
                    }
                }
            }

            foreach (var entity in moduleToRemove.Entities)
            {
                if (!entity.Removed) { entity.Remove(); }
            }

            foreach (var hallwayEntity in moduleToRemove.HallwayEntities.ToList())
            {
                if (!hallwayEntity.Removed)
                {
                    hallwayEntity.Remove();
                }
                moduleToRemove.HallwayEntityIds.Remove(hallwayEntity.ID);
                moduleToRemove.HallwayEntities.Remove(hallwayEntity);
            }
            outpost.OutpostModules.Remove(moduleToRemove);

            //the previous module's ladder can only have been extended into this module if this module was attached on top of it (see OutpostGenerator.AlignLadders)
            if (moduleToRemove.PreviousModule != null && moduleToRemove.ThisGapPosition == OutpostModuleInfo.GapPosition.Bottom)
            {
                foreach (var entity in moduleToRemove.PreviousModule.Entities)
                {
                    if (entity is not Item item || item.GetComponent<Ladder>() is not Ladder ladder) { continue; }
                    //if the ladder is at the position of this module's hatch, and extending up from the previous module,
                    //resize it back inside the previous module
                    if (ladder.Item.WorldPositionX > moduleToRemove.ThisGap.WorldRect.X && ladder.Item.WorldPositionX < moduleToRemove.ThisGap.WorldRect.Right &&
                        ladder.Item.WorldRect.Y > moduleToRemove.PreviousGap.WorldPosition.Y + 100)
                    {
                        ladder.Item.Rect = new Rectangle(ladder.Item.Rect.X, moduleToRemove.PreviousGap.Rect.Y, ladder.Item.Rect.Width, moduleToRemove.PreviousGap.Rect.Y - (ladder.Item.Rect.Y - ladder.Item.Rect.Height));
                    }
                }
            }

            //disable the gap the removed module was attached to
            moduleToRemove.PreviousGap?.SetDisabled(true);

            System.Diagnostics.Debug.Assert(moduleToRemove.Entities.All(static e => e.Removed));
        }
        outpostData.ClearAppliedModuleRemovals();

        return anyRemovals;
    }

    public static PurchasableOutpostModulePrefab? FindModulePrefab(PlacedModule placedModule)
    {
        return PurchasableOutpostModulePrefab.Prefabs.FirstOrDefault(p => p.OutpostInfo.Name == placedModule.Info.Name);
    }

    /// <summary>
    /// Updates the location's secondary faction based on the modules that will be added or removed the next time the outpost is loaded.
    /// Important to do at that point, because this may cause stores and hires to be regenerated, and we sync those to clients via the save file.
    /// </summary>
    public static void UpdateSecondaryFactionFromPendingChanges(PersistentOutpostData outpostData)
    {
        //clients receive the faction, stores and hires from the server
        if (GameMain.NetworkMember is { IsClient: true }) { return; }
        if (!outpostData.IsPlayerOwned) { return; }

        Identifier? factionToSet = null;
        foreach (var pendingModule in outpostData.PendingOutpostModules)
        {
            if (pendingModule.ModulePrefab == null || !pendingModule.IsReadyToBuild) { continue; }
            if (!pendingModule.ModulePrefab.OutpostSecondaryFaction.IsEmpty)
            {
                factionToSet = pendingModule.ModulePrefab.OutpostSecondaryFaction;
            }
        }

        //removals can only be queued while in the outpost, so the outpost should be loaded if there's removals pending
        bool removingSecondaryFactionModule = false;
        if (outpostData.PendingModuleRemovals.Any() &&
            Level.IsLoadedOutpost &&
            Level.Loaded is { StartOutpost: { } outpost } level && level.StartLocation == outpostData.Location)
        {
            foreach (var module in outpost.OutpostModules)
            {
                if (FindModulePrefab(module) is not { } prefab || prefab.OutpostSecondaryFaction.IsEmpty) { continue; }
                if (outpostData.IsRemovalPending(module))
                {
                    removingSecondaryFactionModule = true;
                }
                else
                {
                    factionToSet ??= prefab.OutpostSecondaryFaction;
                }
            }
        }

        if (factionToSet != null)
        {
            SetSecondaryFaction(outpostData.Location, factionToSet, parentDebugName: nameof(UpdateSecondaryFactionFromPendingChanges));
        }
        else if (removingSecondaryFactionModule)
        {
            SetSecondaryFaction(outpostData.Location, null, parentDebugName: nameof(UpdateSecondaryFactionFromPendingChanges));
        }
    }

    /// <summary>
    /// Changes the location's secondary faction, and refreshes the stores and hires accordingly.
    /// </summary>
    private static void SetSecondaryFaction(Location location, Identifier? factionIdentifier, string parentDebugName)
    {
        Faction? faction = null;
        if (factionIdentifier != null && GameMain.GameSession?.Campaign is { } campaign)
        {
            if (campaign.GetFaction(factionIdentifier.Value) is { } matchingFaction)
            {
                faction = matchingFaction;
            }
            else
            {
                DebugConsole.ThrowError($"Error in {parentDebugName}: could not find a faction with the identifier \"{factionIdentifier}\".");
                return;
            }
        }

        if (location.SecondaryFaction == faction) { return; }
        location.SecondaryFaction = faction;

        //clients receive the stores and hires from the server
        if (GameMain.NetworkMember is { IsClient: true }) { return; }
        //update rather than recreate the stores: adds the items the new faction allows selling and removes the ones it no longer allows,
        //without removing stores not defined in the location type (e.g. ones created by workstations)
        location.UpdateStores(createStoresIfNotCreated: false);
        location.ResetHireableCharacters();
    }

    private static void CheckLoadingErrors(Submarine sub)
    {
        foreach (MapEntity entity in MapEntity.MapEntityList)
        {
            if (entity.IdFreed || entity.OriginalModuleIndex == -1) { continue; }
            if (entity.Submarine == sub && !sub.OutpostModules.SelectMany(m => m.Entities).Contains(entity))
            {
                //items in cabinets don't need to be accounted for here
                if (entity is Item item && item.Container != null) { continue; }

                DebugConsole.ThrowError($"Loaded outpost module entity {entity.Name} was not marked as being part of any module.");
#if DEBUG
                if (entity is Structure structure)
                {
                    structure.SpriteColor = Color.Magenta;
                }
#endif
            }
        }
        CheckDuplicateEntitiesBetweenModules(sub);
    }

    /// <summary>
    /// Checks whether there's any entities that are considered to belong to more than one module or hallway, which can cause various issues with the outpost generation and building. If any are found, an error is thrown in the debug console.
    /// </summary>
    public static void CheckDuplicateEntitiesBetweenModules(Submarine sub)
    {
        foreach (var module in sub.OutpostModules)
        {
            foreach (var entity in module.Entities)
            {
                var otherModule = sub.OutpostModules.FirstOrDefault(m => m != module && m.Entities.Contains(entity));
                if (otherModule != null)
                {
                    DebugConsole.ThrowError($"Loaded outpost module entity {entity.Name} was marked as being part of two different modules: {module.Info.DisplayName} and {otherModule.Info.DisplayName}.");
                }
                var otherModuleHallway = sub.OutpostModules.FirstOrDefault(m => m.HallwayEntities.Contains(entity));
                if (otherModuleHallway != null)
                {
                    DebugConsole.ThrowError($"Loaded outpost module entity {entity.Name} was marked as being part of the module {module.Info.DisplayName} and the hallway of {otherModuleHallway.Info.DisplayName}.");
                }
            }
        }
        foreach (var module in sub.OutpostModules)
        {
            foreach (var entity in module.HallwayEntities)
            {
                var otherModuleHallway = sub.OutpostModules.FirstOrDefault(m => m != module && m.HallwayEntities.Contains(entity));
                if (otherModuleHallway != null)
                {
                    DebugConsole.ThrowError($"Loaded outpost module entity {entity.Name} was marked as being part of the hallways of two different modules: {module.Info.DisplayName} and {otherModuleHallway.Info.DisplayName}.");
                }
            }
        }

    }

    private static List<MapEntity> LoadOutpostEntities(Submarine outpost, PersistentOutpostData outpostData, List<PlacedModule> outpostModules, bool onlyEntrance = false)
    {
        outpost.OutpostModules.AddRange(outpostModules);
        List<MapEntity> newEntities = MapEntity.LoadAll(outpost, outpost.Info.SubmarineElement, outpost.Info.FilePath, outpost.IdOffset);

        var idRemap = new IdRemap(outpostData.OutpostEntitiesElement, outpost.IdOffset);

        PlacedModule.InitializeLoadedModules(
            onlyEntrance ? outpost.OutpostModules.Take(1) : outpost.OutpostModules,
            MapEntity.MapEntityList.Where(me => me.Submarine == outpost),
            idRemap);

        foreach (var module in outpost.OutpostModules)
        {
            if (module.HallwayEntities.None() && module.PreviousModuleIndex > -1)
            {
                //un-disable gaps between the modules
                //(one of them may have been disabled if the modules were placed directly against each other without a hallway in between)
                module.ThisGap?.SetDisabled(false);
                module.ThisGap?.ConnectedDoor?.SetDisabled(false);
                module.ThisGap?.ConnectedDoor?.SetDisabled(false);
                module.PreviousGap?.SetDisabled(false);

                foreach (var wayPoint in WayPoint.WayPointList)
                {
                    if ((module.ThisGap?.ConnectedDoor != null && wayPoint.ConnectedDoor == module.ThisGap.ConnectedDoor) ||
                        (module.PreviousGap?.ConnectedDoor != null && wayPoint.ConnectedDoor == module.PreviousGap.ConnectedDoor))
                    {
                        wayPoint.SetDisabled(false);
                    }
                }

                Gap.UpdateHulls();
                foreach (var newHallwayEntity in OutpostGenerator.GenerateHallway(outpost, outpostData.Location.Type, module, outpost.Info.OutpostGenerationParams))
                {
                    if (!module.HallwayEntities.Contains(newHallwayEntity)) { module.HallwayEntities.Add(newHallwayEntity); }
                }
                System.Diagnostics.Debug.Assert(module.HallwayEntities.Distinct().Count() == module.HallwayEntities.Count, "Duplicate entities present in the list of hallway entities.");
                module.HallwayEntityIds = module.HallwayEntities.Select(e => e.ID).ToImmutableArray();
                if (module.HallwayEntities.Count > 0)
                {
                    newEntities.AddRange(module.HallwayEntities);
                    newHallwaysCreated = true;
                }
            }
        }

        if (!onlyEntrance)
        {
            ProcessAdditions(outpost, outpostData, idRemap, newEntities);
            ApplyPendingModuleRemovals(outpost, outpostData);
        }

        return newEntities;
    }

    #region Adding and removing individual modules

    /// <summary>
    /// Can the player afford purchasing the specific module? Note that this can be an expensive method to call, because it may require checking the submarine for materials for building the module.
    /// </summary>
    public static bool CanAfford(PurchasableOutpostModulePrefab module, bool checkRequiredResources = false, Client? client = null)
    {
        if (GameMain.GameSession?.Campaign is not { } campaign) { return false; }
        if (checkRequiredResources && !HasRequiredResources(module, client)) { return false; }
        if (!module.Sold) { return false; }
        return campaign.CanAfford(module.Price, client);
    }

    /// <summary>
    /// Does the player have all the required resources to purchase the module? 
    /// Note that this is an expensive method to call, because it requires checking the submarine for materials for building the module.
    /// </summary>
    public static bool HasRequiredResources(PurchasableOutpostModulePrefab module, Client? client = null)
    {
        if (GameMain.GameSession?.Campaign == null) { return false; }
        Character? character = client == null ? Character.Controlled : client.Character;
        return UpgradeResourceCost.HasResources(module.ResourceCosts, character);
    }

    public static bool HasRequiredResearch(PurchasableOutpostModulePrefab module)
    {
        if (module.RequiredResearch.IsEmpty) { return true; }

        if (ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
        {
            return researchManager.IsFinished(module.RequiredResearch);
        }

        return true;
    }

    /// <summary>
    /// Does the player have enough money, resources and the appropriate permissions to purchase the module?
    /// </summary>
    public static bool CanPurchase(PurchasableOutpostModulePrefab module, PersistentOutpostData outpostData, Client? client = null)
    {
        if (!CanAfford(module, checkRequiredResources: true, client)) { return false; }
        if (MaximumNumberOfModulesInCategoryReached(module, outpostData)) { return false; }
        if (!module.Sold) { return false; }
#if CLIENT
        if (!AllowedToManageOutpost()) { return false; }
#elif SERVER
        if (client == null || !AllowedToManageOutpost(client)) { return false; }
#endif
        if (!HasRequiredResearch(module)) { return false; }
        return true;
    }

    public static bool MaximumNumberOfModulesReached(PersistentOutpostData outpostData)
    {
        return Level.Loaded?.StartOutpost is Submarine outpost && CountModules(outpost, outpostData) >= MaxModulesPerOutpost;
    }
    
    public static bool MaximumNumberOfModulesInCategoryReached(PurchasableOutpostModulePrefab module, PersistentOutpostData outpostData)
    {
        return Level.Loaded?.StartOutpost is Submarine outpost && CountModules(module.Category, outpost, outpostData) >= module.MaxCountPerCategory;
    }
    
    public static int CountModules(Submarine outpost, PersistentOutpostData outpostData) => outpost.OutpostModules.Count + outpostData.PendingOutpostModules.Count();
    
    public static int CountModules(Identifier category, Submarine outpost, PersistentOutpostData outpostData) => 
        outpost.OutpostModules.Count(m => m.Category == category) + outpostData.PendingOutpostModules.Count(m => m.ModulePrefab is PurchasableOutpostModulePrefab prefab && prefab.Category == category);

    public static bool TryTakeResources(PurchasableOutpostModulePrefab module, out IReadOnlyList<ItemPrefab> takenResources, Client? client = null)
    {
        if (!module.ResourceCosts.Any())
        {
            takenResources = new List<ItemPrefab>();
            return true;
        }
        Character? character = client == null ? Character.Controlled : client.Character;
        return UpgradeResourceCost.TryTakeResources(module.ResourceCosts, character, out takenResources);
    }

    /// <summary>
    /// Is the specified gap valid for placing modules on it?
    /// Does not check whether there's something currently attached to the gap, just whether it's in principle valid (e.g. not linked to a door that explicitly disallows use between outpost modules).
    /// </summary>
    public static bool IsValidGapForModulePlacement(Gap targetGap)
    {
        if (targetGap == null || targetGap.Removed) { return false; }
        if (targetGap.linkedTo.Any(lt => lt is Item item && item.GetComponent<Door>() is { UseBetweenOutpostModules: false }))
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Try to place the module (requires sufficient funds and permissions), and deduct the price from the wallet and take the required resources if successful.
    /// </summary>
    public static bool TryPlaceModule(PersistentOutpostData outpostData, PurchasableOutpostModulePrefab module, PurchasableOutpostModulePrefab.GapPositionInfo thisGap, Gap targetGap, float hallwayLength, bool createNetworkEvent, Client? client = null)
    {
        if (!IsValidGapForModulePlacement(targetGap))
        {
            DebugConsole.ThrowError($"Failed to place the outpost module \"{module.OutpostInfo?.DisplayName ?? "unknown"}\". The selected gap is not valid for module placement.");
            return false;
        }
        if (!CanPurchase(module, outpostData, client)) { return false; }
        if (GameMain.GameSession?.Campaign is { } campaign)
        {
            if (module.OutpostInfo?.OutpostModuleInfo is not OutpostModuleInfo moduleInfo ||
                !moduleInfo.GapPositions.HasFlag(OutpostGenerator.GetOpposingGapPosition(OutpostGenerator.GetGapPosition(targetGap))))
            {
                DebugConsole.ThrowError($"Failed to place the outpost module \"{module.OutpostInfo?.DisplayName ?? "unknown"}\". No suitable gap available to attach to the target gap.");
                return false;
            }

            if (MaximumNumberOfModulesReached(outpostData))
            {
                DebugConsole.ThrowError($"Cannot add more modules. The maximum number of modules ({MaxModulesPerOutpost}) has been reached.");
                return false;
            }

            if (campaign.TryPurchase(client, module.Price) &&
                TryTakeResources(module, out var takenResources, client))
            {
                var newModule = outpostData.AddPendingModule(module, thisGap, targetGap);
                if (newModule != null)
                {
                    newModule.ItemsTakenToBuild = takenResources.Select(static prefab => prefab.Identifier).ToArray();
                    newModule.HallwayLength = hallwayLength;
#if CLIENT
                    if (createNetworkEvent && GameMain.IsMultiplayer)
                    {
                        ClientWriteAddOutpostModule(module, thisGap, targetGap, hallwayLength);
                    }
#elif SERVER
                    ServerWriteOutpostModuleChanges();
#endif
                    return true;
                }

            }
        }
        return false;
    }

    public static void RemoveModule(PersistentOutpostData outpostData, PlacedModule module, bool createNetworkEvent)
    {
        outpostData.QueueModuleRemoval(module);
#if CLIENT
        if (createNetworkEvent && GameMain.IsMultiplayer)
        {
            ClientWriteRemoveOutpostModule(module);
        }
#elif SERVER
        ServerWriteOutpostModuleChanges();
#endif
    }

    public static void CancelModuleRemoval(PersistentOutpostData outpostData, PlacedModule module, bool createNetworkEvent)
    {
        outpostData.CancelModuleRemoval(module);
#if CLIENT
        if (createNetworkEvent && GameMain.IsMultiplayer)
        {
            ClientWriteCancelOutpostModuleRemoval(module);
        }
#elif SERVER
        ServerWriteOutpostModuleChanges();
#endif
    }

    /// <summary>
    /// Removes a pending module from the outpost (i.e. cancels the purchase of a module) and refunds the price to the wallet.
    /// </summary>
    public static bool TryRemovePendingModule(Submarine outpost, PersistentOutpostData outpostData, PendingOutpostModule pendingModule, Client? client, bool createNetworkEvent)
    {
#if CLIENT
        if (createNetworkEvent && GameMain.IsMultiplayer)
        {
            ClientWriteCancelOutpostModuleAddition(outpostData, pendingModule);
            //don't let the client cancel or modify money themselves, just tell the server we want to cancel
            return false;
        }
#endif
        if (!outpostData.RemovePendingModule(pendingModule)) { return false; }

        if (pendingModule.ModulePrefab != null)
        {
            if (GameMain.GameSession?.Campaign?.Bank is { } bank)
            {
                bank.Give(pendingModule.ModulePrefab.Price);
            }

            foreach (var itemIdentifier in pendingModule.ItemsTakenToBuild)
            {
                if (!ItemPrefab.Prefabs.TryGet(itemIdentifier, out ItemPrefab? itemPrefab))
                {
                    DebugConsole.ThrowError($"Failed to find item prefab with identifier {itemIdentifier} when giving pending module refund.");
                    continue;
                }

                Character? characterToGive = client?.Character;
                if (GameMain.NetworkMember is null)
                {
                    characterToGive = Character.Controlled;
                }

                Entity.Spawner.AddItemToSpawnQueue(itemPrefab, characterToGive?.WorldPosition ?? outpost.WorldPosition, onSpawned: (Item item) =>
                {
                    if (characterToGive != null)
                    {
                        if (characterToGive.Inventory.TryPutItem(item, user: null, item.AllowedSlots))
                        {
                            return;
                        }
                    }

                    // Put the item in a free storage item if the character inventory is full or there's no character to give the items to
                    foreach (Item storageItem in outpost.GetItems(alsoFromConnectedSubs: true).Where(static i => i.OwnInventory != null && !i.HiddenInGame))
                    {
                        if (storageItem.OwnInventory.TryPutItem(item, user: null, item.AllowedSlots))
                        {
                            break;
                        }
                    }
                });
            }
        }
#if SERVER
        ServerWriteOutpostModuleChanges();
#endif
        return true;
    }

    public static void GetModulesToMove(
        Submarine outpost,
        PersistentOutpostData outpostData,
        PlacedModule moduleToMove,  
        ref List<PlacedModule> modulesToMove, 
        ref List<PendingOutpostModule> pendingOutpostModulesToMove)
    {
        modulesToMove.Clear();
        pendingOutpostModulesToMove.Clear();
        OutpostGenerator.GetSubsequentModules(moduleToMove, allModules: outpost.OutpostModules, ref modulesToMove);

        //add pending modules that are attached to any of the modules we're moving
        foreach (var module in modulesToMove)
        {
            foreach (var pendingModule in outpostData.PendingOutpostModules)
            {
                if (module.IsGapInThisModule(pendingModule.Gap))
                {
                    pendingOutpostModulesToMove.Add(pendingModule);
                }
            }
        }
    }

    public static bool CanMoveModulesWithoutOverlaps(Submarine outpost, IEnumerable<PlacedModule> modulesToMove, IEnumerable<PendingOutpostModule> pendingModulesToMove, Vector2 moveAmountWorld)
    {
        bool overlapFound = false;
        foreach (var module in modulesToMove)
        {
            module.TemporaryMoveOffset += moveAmountWorld;
        }
        var stationaryModules = outpost.OutpostModules.Except(modulesToMove).ToList();
        if (OutpostGenerator.FindOverlap(modulesToMove, stationaryModules, out PlacedModule? overlap1, out PlacedModule? overlap2, checkOverlapWithAttachedModule: false, modulesAlreadyInPlace: true))
        {
            overlapFound = true;
        }
        //check if the hallways of the moved modules (including the one leading to the module they're attached to) overlap with the modules that stay in place,
        //or with the hallways between them
        else if (stationaryModules.Any(m => OutpostGenerator.ModuleOverlapsWithModuleConnections(m, modulesToMove, modulesAlreadyInPlace: true)))
        {
            overlapFound = true;
        }
        //check if the moved modules overlap with the hallways between the modules that stay in place
        else if (modulesToMove.Any(m => OutpostGenerator.ModuleOverlapsWithModuleConnections(m, stationaryModules, modulesAlreadyInPlace: true)))
        {
            overlapFound = true;
        }
        foreach (var module in modulesToMove)
        {
            module.TemporaryMoveOffset -= moveAmountWorld;
        }
        return !overlapFound;
    }

    /// <summary>
    /// Repositions wire nodes in the outpost according to the amounts the modules have been moved (<see cref="PlacedModule.TemporaryMoveOffset"/>)
    /// </summary>
    public static void RepositionWireNodes(Submarine outpost)
    {
        HashSet<Wire> processedWires = new HashSet<Wire>();

        foreach (var module in outpost.OutpostModules)
        {
            if (module.TemporaryMoveOffset.NearlyEquals(Vector2.Zero)) { continue; }

            foreach (var entity in module.Entities)
            {
                if (entity is not Item item) { continue; }

                //if the item has a connection panel, we need to move the wires too
                foreach (var connectionPanel in item.GetComponents<ConnectionPanel>())
                {
                    foreach (var wire in connectionPanel.GetAllWires())
                    {
                        //the wire was already processed by some other module
                        if (processedWires.Contains(wire)) { continue; }
                        processedWires.Add(wire);

                        List<Vector2> wireNodes = wire.GetNodes();
                        if (wireNodes.None()) { continue; }
                        //if the other side of the wire is not connected to some another module, no need to move it
                        if (wire.Connections.None(c => c != null && !module.Entities.Contains(c.Item))) { continue; }

                        PlacedModule? moduleWireIsIn = outpost.OutpostModules.FirstOrDefault(m => m.Entities.Contains(wire.Item));

                        Vector2 wirePositionOffset = outpost.Position + outpost.HiddenSubPosition;

                        PlacedModule? prevModule = null;
                        List<Vector2> newWireNodes = [];

                        for (int i = 0; i < wireNodes.Count; i++)
                        {
                            Hull? hull = Hull.FindHull(wireNodes[i] + wirePositionOffset, guess: item.CurrentHull, useWorldCoordinates: true);

                            //remove nodes from hallways (it'd be really tricky to reposition them considering there might not even be a hallway after we're done moving the modules)
                            if (outpost.OutpostModules.Any(m => m.HallwayEntities.Contains(hull)))
                            {
                                continue;
                            }

                            //which module currently contains this node?
                            PlacedModule? nodeModule =
                                outpost.OutpostModules.FirstOrDefault(m => m.Entities.Contains(hull) || m.HallwayEntities.Contains(hull)) ??
                                //use the previous module as a fallback -> if the wire goes outside hulls, it's probably still attached to somewhere near the previous module (e.g. some outside structure)
                                prevModule;

                            //how much should this node move?
                            Vector2 desiredMovement =
                                nodeModule?.TemporaryMoveOffset ?? Vector2.Zero;

                            //how much movement already happened automatically because the wire entity itself belongs to this module?
                            Vector2 alreadyAppliedMovement = moduleWireIsIn?.TemporaryMoveOffset ?? Vector2.Zero;

                            newWireNodes.Add(wireNodes[i] + desiredMovement - alreadyAppliedMovement);

                            prevModule = nodeModule;
                        }

                        wire.SetNodes(newWireNodes);
                    }
                }
            }
        }
    }


    public static void RefreshEntitiesInModules(Submarine outpost, IEnumerable<PlacedModule> modules)
    {
        foreach (var module in modules)
        {
            module.Entities.RemoveAll(e => e.Removed);
        }
        foreach (var module in modules)
        {
            var hulls = module.Entities.Where(e => e is Hull).Cast<Hull>();
            foreach (var item in Item.ItemList)
            {
                if (module.Entities.Contains(item)) { continue; }
                if (module.HallwayEntities.Contains(item)) { continue; }
                if (item.FindParentInventory(inv => inv is CharacterInventory) != null) { continue; }
                if (item.IsDisabled) { continue; }

                bool isLooseItemInHull =
                    item.CurrentHull != null && hulls.Contains(item.CurrentHull) &&
                    item.body is { Enabled: true, BodyType: FarseerPhysics.BodyType.Dynamic };
                bool isContainedInHull = item.RootContainer?.CurrentHull != null && hulls.Contains(item.RootContainer.CurrentHull);
                bool hasBeenAttachedDuringRound = 
                    item.GetComponent<Holdable>() is { Attachable: true, Attached: true } holdable && 
                    //the item must have been deattached at some point - if it hasn't been,
                    //it must still be attached to the module where it was initially attached,
                    //and we don't need to move it to another module
                    holdable.DeattachedDuringRound &&
                    holdable.AttachTargetStructure != null &&
                    module.Entities.Contains(holdable.AttachTargetStructure);
                bool isWireConnectedToThisModule =
                    item.GetComponent<Wire>() is Wire wire && 
                    //let the 1st non-null connection decide which module to save the wire in
                    wire.Connections.FirstOrDefault(c => c != null) is Connection firstConnection && module.Entities.Contains(firstConnection.Item);

                if (isLooseItemInHull || isContainedInHull || hasBeenAttachedDuringRound || isWireConnectedToThisModule)
                {
                    module.Entities.Add(item);

                    foreach (var otherModule in modules)
                    {
                        if (otherModule == module) { continue; }
                        if (otherModule.Entities.Contains(item))
                        {
                            otherModule.Entities.Remove(item);
                        }
                    }
                }
            }
        }

        foreach (var module in modules)
        {
            // Remove entities that won't be saved in the outpost (e.g. items taken to the player sub),
            // otherwise the module would refer to IDs that don't exist when the outpost is loaded
            module.Entities.RemoveAll(e => !outpost.IsEntitySavedWithSubmarine(e));
            module.EntityIds = module.Entities.Select(e => e.ID).ToImmutableArray();
            module.HallwayEntities.RemoveAll(e => !outpost.IsEntitySavedWithSubmarine(e));
            module.HallwayEntityIds = module.HallwayEntities.Select(e => e.ID).ToImmutableArray();
        }
        foreach (var module in modules)
        {
            foreach (var otherModule in modules)
            {
                if (module == otherModule) { continue; }
                foreach (ushort id in module.EntityIds)
                {
                    if (otherModule.EntityIds.Contains(id))
                    {
                        DebugConsole.ThrowError(
                            $"Something went wrong in {nameof(RefreshEntitiesInModules)}."+
                            $" Duplicate entity ID {id} between modules: {module.Entities.FirstOrDefault(e => e.ID == id)?.Name ?? "null"} - {otherModule.Entities.FirstOrDefault(e => e.ID == id)?.Name ?? "null"}, in modules {module.Name} and {otherModule.Name}.");
                    }
                }
            }
        }
    }
#endregion
}

