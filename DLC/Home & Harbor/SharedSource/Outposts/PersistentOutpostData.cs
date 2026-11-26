using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Plugins;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace HomeAndHarbor;

[RunConstructorOnInit]
internal partial class PersistentOutpostData : ISerializableEntity
{
    #region Networking

    /// <summary>
    /// A property that's used to sync <see cref="IsPlayerOwned"/> over the network" (can't use it directly since setting the state should be handled using <see cref="SetPlayerOwned(bool)"/>
    /// </summary>
    [NetworkSerialize]
    public bool IsPlayerOwnedNetworked 
    {
        get => IsPlayerOwned;
        set
        {
            SetPlayerOwned(value);
        }
    }

    [NetworkSerialize]
    public readonly struct PersistentOutpostNetworkData(PersistentOutpostData outpostData) : INetSerializableStruct
    {
        public readonly string Name = outpostData.Location?.DisplayName?.Value ?? string.Empty;
        public readonly int LocationIndex = GameMain.GameSession?.Map?.Locations.IndexOf(outpostData.Location) ?? -1;
        public readonly bool IsPlayerOwned = outpostData.IsPlayerOwned;
        public readonly bool HasShortcut = outpostData.HasShortcut;
        public readonly int OutpostWalletBalance = outpostData.OutpostWalletBalance;

        public void ApplyToPersistentOutpostData(PersistentOutpostData outpostData)
        {
            if (!Name.IsNullOrEmpty())
            {
                outpostData.Location.TryForceRawName(Name);
            }
            outpostData.SetPlayerOwned(IsPlayerOwned, createMessageBox: false);
            outpostData.HasShortcut = HasShortcut;
            outpostData.OutpostWalletBalance = OutpostWalletBalance;
        }
    }

    [NetworkSerialize]
    public readonly struct ResourcesNetworkData(PersistentOutpostData outpostData, ResourceManager resourceManager, bool createUINotification) : INetSerializableStruct
    {
        public readonly int LocationIndex = GameMain.GameSession?.Map?.Locations.IndexOf(outpostData.Location) ?? -1;
        public readonly ImmutableArray<Identifier> ResourceIdentifiers = GetSyncableResource(resourceManager).Select(r => r.Identifier).ToImmutableArray();
        public readonly ImmutableArray<float> ResourceValues = GetSyncableResource(resourceManager).Select(r => r.Amount).ToImmutableArray();
        public readonly bool CreateUINotification = createUINotification;

        private static IEnumerable<Resource> GetSyncableResource(ResourceManager resourceManager)
        {
            return resourceManager.ResourcesByIdentifier
                //do not try to sync readonly or combined resources (the latter get their values from the child resources)
                .Where(kvp => !kvp.Value.IsReadonly && kvp.Value.IsSyncable)
                .Select(kvp => kvp.Value);
        }

        public void ApplyToPersistentOutpostData(PersistentOutpostData outpostData)
        {
            if (ResourceIdentifiers.Length != ResourceValues.Length)
            {
                throw new InvalidOperationException("Failed to apply resources to outpost data: number of resource identifiers doesn't match the number of resource values.");
            }
            for (int i = 0; i < ResourceIdentifiers.Length; i++)
            {
                Identifier resourceId = ResourceIdentifiers[i];
                float resourceAmount = ResourceValues[i];
                if (outpostData.ResourceManager == null) { continue; }
#if CLIENT
                Resource? resource = outpostData.ResourceManager.GetResource(resourceId);
                float? prevAmount = resource?.Amount;
#endif
                outpostData.ResourceManager.SetResource(resourceId, resourceAmount, createUINotification: CreateUINotification);
            }
        }
    }
#endregion

    public readonly Location Location;

    public readonly OutpostDwellerManager DwellerManager;

    /// <summary>
    /// Null in non-player outposts
    /// </summary>
    public OutpostCreatureManager? CreatureManager { get; private set; }

    public readonly List<ResourceGenerator> ResourceGenerators = [];

    /// <summary>
    /// Base element containing all the data
    /// </summary>
    private readonly XElement? xmlElement;

    /// <summary>
    /// Element containing the saved outpost (technically a submarine)
    /// </summary>
    public XElement? OutpostEntitiesElement;

    private const string OutpostEntitiesElementName = "Outpost",
                         EventLogElementName = "Logs";

    public bool AllowChangingOwnership => 
        !Location.IsGateBetweenBiomes &&
        !Location.LocationTypeChangesBlocked;

    /// <summary>
    /// Can the players claim the outpost when its population drops below the insufficient population threshold
    /// (i.e. should the "outpostclaimable" and "outpostconquered" events be triggered)?
    /// Configured with the "AllowClaimingWhenPopulationLow" attribute in the location type, defaults to true.
    /// Does not prevent changing the ownership by other means (e.g. events that set the outpost player-owned directly).
    /// </summary>
    public bool AllowClaimingWhenPopulationLow =>
        AllowChangingOwnership &&
        LocationTypeAllowsClaimingWhenPopulationLow(Location.Type);

    public static bool LocationTypeAllowsClaimingWhenPopulationLow(LocationType locationType) =>
        locationType.ConfigElement.GetAttributeBool(nameof(AllowClaimingWhenPopulationLow), true);

    [Serialize(defaultValue: false, IsPropertySaveable.Yes)]
    public bool IsPlayerOwned { get; private set; }

    /// <summary>
    /// Whether this location has a shortcut node in its data.
    /// Use <see cref="LocationShortcutManager.IsShortcutAvailable"/> to check if the shortcut is actually usable.
    /// </summary>
    [Serialize(defaultValue: false, IsPropertySaveable.Yes), NetworkSerialize]
    public bool HasShortcut
    {
        get;
        private set;
    }

    [Serialize(defaultValue: "", IsPropertySaveable.Yes)]
    public Identifier OriginalLocationType { get; private set; }

    /// <summary>
    /// Wealth gained from player purchases during the current visit, accumulated until outpost exit (so it appears as a single log entry).
    /// </summary>
    [Serialize(defaultValue: 0f, IsPropertySaveable.Yes)]
    public float PendingWealthGainToLog { get; set; }

    /// <summary>
    /// Cash held by this outpost, in Mk. Currently only used for storing tax revenue.
    /// </summary>
    [Serialize(defaultValue: 0, IsPropertySaveable.Yes)]
    public int OutpostWalletBalance
    {
        get => outpostWalletBalance;
        set => outpostWalletBalance = Math.Clamp(value, 0, CampaignMode.MaxMoney);
    }
    private int outpostWalletBalance;

    /// <summary>
    /// -1 = unknown (e.g. in outposts which haven't been visited yet)
    /// </summary>
    public int CurrentPopulation
    {
        get
        {
            if (IsPlayerOwned)
            {
                return DwellerManager.CurrentPopulation;
            }
            else
            {
                return Location.Population;
            }
        }
    }

    public int BasePopulation
    {
        get
        {
            if (IsPlayerOwned)
            {
                return DwellerManager.CurrentPopulation;
            }
            else
            {
                return Location.BasePopulation;
            }
        }
        set
        {
            if (IsPlayerOwned)
            {
                DwellerManager.SetPopulation(value);
                //set the base population in player outposts too - even though we keep track of the dwellers manually for those,
                //the base game also needs to know the population for e.g. the map notifications about the population being low
                Location.BasePopulation = value;
            }
            else
            {
                Location.BasePopulation = value;
            }
        }
    }

    public int InitialPopulation
    {
        get => Location.InitialPopulation;
        set => Location.InitialPopulation = value;
    }

    [Serialize(defaultValue: 0, IsPropertySaveable.Yes)]
    public int WorldStepOnLastVisit
    {
        get;
        private set;
    }

    public int WorldStepsSinceLastVisit
    {
        get;
        private set;
    }

    private readonly List<OutpostBuilder.PendingOutpostModule> pendingOutpostModules = [];

    public IEnumerable<OutpostBuilder.PendingOutpostModule> PendingOutpostModules => pendingOutpostModules;

    public int GetPendingModuleIndex(OutpostBuilder.PendingOutpostModule pendingModule) => pendingOutpostModules.IndexOf(pendingModule);

    public Option<OutpostFacilityManager> OutpostFacilities = Option.None;

    /// <summary>
    /// Indices of outpost modules that are waiting to be removed
    /// </summary>
    private readonly List<int> pendingModuleRemovals = [];

    public IEnumerable<int> PendingModuleRemovals => pendingModuleRemovals;

    /// <summary>
    /// The pending module removals as they were in the latest save. These are the ones that get applied when the outpost is loaded:
    /// changes queued after that (e.g. mid-round) only get applied after the next save.
    /// Otherwise a client that reloads the level mid-round (e.g. by going to the lobby and back) could end up with different modules than the server,
    /// because the server has applied the changes that were pending at the start of the round, and the client would apply the ones that are pending now.
    /// </summary>
    private List<int> pendingModuleRemovalsInLatestSave = [];

    public IEnumerable<int> PendingModuleRemovalsInLatestSave => pendingModuleRemovalsInLatestSave;

    /// <summary>
    /// The pending modules as they were in the latest save. See <see cref="pendingModuleRemovalsInLatestSave"/>.
    /// </summary>
    private List<XElement> pendingModulesInLatestSave = [];

    /// <summary>
    /// Creates new instances of the pending modules as they were in the latest save (new ones every time, because loading the outpost modifies them, e.g. remaps the gap IDs).
    /// See <see cref="pendingModuleRemovalsInLatestSave"/>.
    /// </summary>
    public IEnumerable<OutpostBuilder.PendingOutpostModule> GetPendingModulesInLatestSave()
        => pendingModulesInLatestSave.Select(static element => new OutpostBuilder.PendingOutpostModule(element));

    private void SetPendingChangesInLatestSave(XElement outpostDataElement)
    {
        pendingModuleRemovalsInLatestSave = outpostDataElement.GetAttributeIntArray(nameof(pendingModuleRemovals), []).ToList();
        pendingModulesInLatestSave = outpostDataElement.Elements()
            .Where(static subElement => subElement.NameAsIdentifier() == nameof(OutpostBuilder.PendingOutpostModule))
            .Select(static subElement => new XElement(subElement))
            .ToList();
    }

    /// <summary>
    /// Null in locations without an outpost or a team.
    /// </summary>
    public ResourceManager? ResourceManager { get; private set; }

    public string Name => $"OutpostData ({Location?.DisplayName ?? "null"})";

    private class DescendingComparer : IComparer<int>
    {
        public static readonly DescendingComparer Instance = new();

        public int Compare(int x, int y)
        {
            if (x < y) { return 1; }
            if (x > y) { return -1; }

            return 0;
        }
    }

    public SortedDictionary<int, List<EconomyLogEntry>> Logs = new(DescendingComparer.Instance);

    public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

    static PersistentOutpostData()
    {
        HookService.RegisterHook<PluginOnProgressWorldDelegate>(OnProgressWorld);
        InitProjectSpecific();
    }

    private bool ShouldHaveResourceManager()
    {
        if (!Location.Type.HasOutpost || Location.Type.OutpostTeam == CharacterTeamType.None) { return false; }
        return Location.Type.ConfigElement.GetAttributeBool("hasresources", true);
    }

    private bool ShouldHaveCreatureManager()
    {
        if (!Location.Type.HasOutpost || Location.Type.OutpostTeam == CharacterTeamType.None) { return false; }
        return Location.Type.ConfigElement.GetAttributeBool("hasresources", true);
    }

    static partial void InitProjectSpecific();

    private PersistentOutpostData(Location location)
    {
        Location = location;
        if (ShouldHaveResourceManager())
        {
            ResourceManager = new ResourceManager(location, this);
        }
        if (ShouldHaveCreatureManager())
        {
            CreatureManager = new OutpostCreatureManager();
        }
        DwellerManager = new OutpostDwellerManager(location, this);

        location.LoadOutpost = (generationParams, locationType, location, onlyEntrance, allowInvalidOutpost) =>
        {
            int previousVisit = WorldStepOnLastVisit;
            WorldStepOnLastVisit = GameMain.GameSession?.Map?.WorldStepCount ?? 0;
            WorldStepsSinceLastVisit = WorldStepOnLastVisit - previousVisit;

            return OutpostBuilder.LoadOutpost(generationParams, this, onlyEntrance, allowInvalidOutpost);
        };
        SerializableProperties = SerializableProperty.GetProperties(this);
        LoadTrackedResources();
    }

    private PersistentOutpostData(XElement element, Location location) :
        this(location)
    {
        xmlElement = element;
        OutpostEntitiesElement = element.GetChildElement(OutpostEntitiesElementName);
        if (ShouldHaveResourceManager())
        {
            ResourceManager = new ResourceManager(location, this, element.GetChildElement(nameof(ResourceManager)));
        }
        SerializableProperty.DeserializeProperties(this, element);

        pendingModuleRemovals = element.GetAttributeIntArray(nameof(pendingModuleRemovals), []).ToList();
        SetPendingChangesInLatestSave(element);

        foreach (var subElement in element.Elements())
        {
            var elementName = subElement.NameAsIdentifier();
            if (elementName == nameof(OutpostBuilder.PendingOutpostModule))
            {
                pendingOutpostModules.Add(new OutpostBuilder.PendingOutpostModule(subElement));
            }
            else if (elementName == nameof(ResourceGenerator))
            {
                ResourceGenerators.Add(new ResourceGenerator(subElement, parentComponent: null, resourceManager: ResourceManager, parentDebugName: $"Location {location.DisplayName}"));
            }
            else if (elementName == nameof(OutpostDwellerManager))
            {
                DwellerManager.Load(subElement);
            }
            else if (elementName == nameof(OutpostCreatureManager))
            {
                if (ShouldHaveCreatureManager())
                {
                    CreatureManager ??= new OutpostCreatureManager();
                    CreatureManager.Load(subElement);
                }
            }
            else if (elementName == nameof(OutpostFacilityManager))
            {
                OutpostFacilities = Option.Some(OutpostFacilityManager.Load(subElement));
            }
            else if (elementName == EventLogElementName)
            {
                foreach (XElement logElement in subElement.Elements())
                {
                    int step = logElement.GetAttributeInt("step", 0);
                    List<EconomyLogEntry> logs = [];
                    foreach (XElement entryElement in logElement.Elements())
                    {
                        if (EconomyLogEntry.FromXML(entryElement) is { } entry)
                        {
                            logs.Add(entry);
                        }
                    }
                    Logs[step] = logs;
                }
            }
        }

        LoadTrackedResources();
    }

    /// <summary>
    /// List of tracked resources that are displayed to the player in UIs,
    /// TrackedResource is the source of the generation, while DisplayedResource
    /// should be the source of everything else like icon, value and texts that are
    /// shown in the interface.
    /// </summary>
    public ImmutableArray<(Resource TrackedResource, Resource DisplayedResource)> ResourcesToDisplayInUIs
        = ImmutableArray<(Resource, Resource)>.Empty;

    public void LoadTrackedResources()
    {
        if (ResourceManager is null) { return; }

        var builder = ImmutableArray.CreateBuilder<(Resource, Resource)>();

        foreach (Resource resource in ResourceManager.Resources.OrderBy(static r => r.Prefab.MenuOrder))
        {
            if (!resource.Prefab.ShowInResourcesUI) { continue; }

            Resource? displayedResource =
                resource.Prefab.ShowInResourcesUIAs.IsEmpty ?
                    resource :
                    ResourceManager.GetResource(resource.Prefab.ShowInResourcesUIAs);

            if (displayedResource is null)
            {
                DebugConsole.ThrowError($"Error in {nameof(LoadTrackedResources)}: Resource '{resource.Prefab.ShowInResourcesUIAs}' not found.");
                continue;
            }

            builder.Add((resource, displayedResource));
        }

        ResourcesToDisplayInUIs = builder.ToImmutable();
    }

    public void ResetResourceManager()
    {
        if (ShouldHaveResourceManager())
        {
            ResourceManager = new ResourceManager(Location, this);            
        }
        else
        {
            ResourceManager = null;
        }

        LoadTrackedResources();
    }

    /// <summary>
    /// Refreshes the <see cref="Resources.FacilityResource">facility resources</see> (static devices, bunks etc) present in the currently loaded outpost.
    /// Also refreshes the outpost UI client-side.
    /// </summary>
    public static void RefreshFacilityResources()
    {
        if (Level.Loaded is { Type: LevelData.LevelType.Outpost } &&
            Level.Loaded.StartLocation is { } location &&
            Level.Loaded.StartOutpost is { } outpost &&
            GetPersistentOutpostData(location) is { } outpostData)
        {
            outpostData.OutpostFacilities = Option.Some(OutpostFacilityManager.ConstructFromOutpost(outpost));
        }

#if CLIENT
        OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
#endif
    }

    public void ResetCreatureManager()
    {
        if (ShouldHaveCreatureManager())
        {
            CreatureManager = new OutpostCreatureManager();
        }
        else
        {
            CreatureManager = null;
        }
    }

    public void StoreResourceChangeEvent(EconomyLogEntry entry)
    {
#if !DEBUG
        if (entry.IsDebugOnly) { return; }
#endif
        if (entry.IsNoChange) { return; }
        if (GameMain.GameSession?.Campaign?.Map is not { } map) { return; }

        if (!Logs.TryGetValue(map.WorldStepCount, out List<EconomyLogEntry>? list))
        {
            list = [];
            Logs[map.WorldStepCount] = list;
        }
        MergeEntry(list, entry);

#if DEBUG
        const int maxLogAge = 16;
#else
        const int maxLogAge = 3;
#endif

        if (Logs.Count <= maxLogAge) { return; }
        Logs.Keys.ForEachMod(key =>
        {
            if (map.WorldStepCount - key < maxLogAge) { return; }
            Logs.Remove(key);
        });
    }

    private static void MergeEntry(List<EconomyLogEntry> originalEntries, EconomyLogEntry entryToAdd)
    {
        bool combined = false;
        for (int i = 0; i < originalEntries.Count; ++i)
        {
            if (!originalEntries[i].TryMerge(entryToAdd, out EconomyLogEntry? newEntry)) { continue; }
            if (newEntry.IsNoChange)
            {
                originalEntries.RemoveAt(i);
            }
            else
            {
                originalEntries[i] = newEntry;
            }
            combined = true;
            break;
        }
        if (!combined)
        {
            originalEntries.Add(entryToAdd);
        }
    }

    public void SetPlayerOwned(bool isPlayerOwned, bool force = false, bool createMessageBox = true, bool createNetworkEvent = true)
    {
        if (IsPlayerOwned == isPlayerOwned) { return; }

        if (!force && !AllowChangingOwnership)
        {
            DebugConsole.ThrowError($"Attempted to set isPlayerOwned on a location that doesn't allow location type changes ({Location.DisplayName}).");
            return; 
        }

        IsPlayerOwned = isPlayerOwned;

        DwellerManager.SetLocationPlayerOwned(isPlayerOwned);

        //only modify the loaded outpost if it's this location's (not e.g. the end level's outpost when the campaign ends)
        Submarine? outpost = Level.Loaded is { } level && level.StartLocation == Location ? level.StartOutpost : null;

        if (GameMain.GameSession?.Campaign is { } campaign)
        {
            if (isPlayerOwned)
            {
                campaign.SetExtraField(CampaignSaveLoadHooks.PlayerHasOwnedOutpost, true);
            }
            else
            {
                campaign.SetExtraField(
                    CampaignSaveLoadHooks.PlayerHasOwnedOutpost, 
                    campaign.Map.Locations.Any(l => GetPersistentOutpostData(l).IsPlayerOwned));
            }
        }

        if (IsPlayerOwned)
        {
            Faction? previousFaction = Location.Faction;
            //set to player-owned location type
            Location.Faction = null;
            Location.OverrideIconColor = Color.LightGreen;
            OriginalLocationType = Location.Type.Identifier;
            if (LocationType.Prefabs.TryGet("PlayerOutpost".ToIdentifier(), out LocationType? playerOwnedLocationType) && playerOwnedLocationType != null)
            {
                int prevInitialPopulation = Location.InitialPopulation;
                Location.ChangeType(GameMain.GameSession?.Campaign, playerOwnedLocationType, createNetworkEvent: createNetworkEvent);
                //normally location type changes set the initial population to "unknown" (as we can't tell how many NPCs spawn until the outpost is generated for the first time)
                //but in this case, when we keep the outpost and the NPCs as-is, we can keep the old value
                Location.InitialPopulation = prevInitialPopulation;
                if (outpost != null)
                {
                    Submarine.SetOutpostTeam(outpost, Location.Type.OutpostTeam);
                }
            }
            else
            {
                DebugConsole.ThrowError("Could not find the location type PlayerOutpost.");
            }
            Location.PreviousFaction = previousFaction;

            //if there's any path unlock events active, unlock the path and finish the event
            if (GameMain.GameSession?.EventManager is { } eventManager)
            {
                foreach (var activeEvent in eventManager.ActiveEvents.ToList())
                {
                    if (activeEvent is ScriptedEvent { Prefab.UnlockPathEvent: true } unlockPathEvent)
                    {
                        unlockPathEvent.GetAllActions().ForEach(actionWithIndentationLevel =>
                        {
                            if (actionWithIndentationLevel.action is UnlockPathAction unlockPathAction)
                            {
                                unlockPathAction.Update(deltaTime: 1.0f);
                            }
                            else if (actionWithIndentationLevel.action is ConversationAction conversationAction)
                            {
                                //reset conversation (remove conversation option from the NPC)
                                conversationAction.Reset();
                            }
                        });
                        activeEvent.Finish();
                    }
                }
            }
        }
        else
        {
            //set back to original location type
            Location.OverrideIconColor = null;
            if (!OriginalLocationType.IsEmpty)
            {
                if (LocationType.Prefabs.TryGet(OriginalLocationType, out LocationType? originalLocationType) && originalLocationType != null)
                {
                    Location.ChangeType(GameMain.GameSession?.Campaign, originalLocationType, createNetworkEvent: createNetworkEvent);
                }
                else
                {
                    DebugConsole.ThrowError($"Could not find the location type {OriginalLocationType}.");
                }
            }
        }

        if (outpost != null)
        {
            foreach (var placedModule in outpost.OutpostModules)
            {
                if (placedModule.Category.IsEmpty)
                {
                    placedModule.Category = 
                        OutpostBuilder.FindModulePrefab(placedModule)?.Category ?? 
                        Identifier.Empty;
                }
            }
            OutpostGenerator.SetWiresLocked(outpost, isLocked: !IsPlayerOwned);
            OutpostGenerator.SetDevicesDetachable(outpost, disableDetaching: !IsPlayerOwned);
            OutpostGenerator.SetAllowSellingOutpostItems(outpost, allowSelling: isPlayerOwned);
            if (isPlayerOwned)
            {
                OutpostGenerator.SetDevicesPlayerTeamInteractable(outpost);
                OutpostBuilder.ResetModuleSpecificReactorProperties(outpost);
            }
        }

        // Clear orders and recreate autonomous objectives for all crew members to reflect the new ownership status
        // This ensures objectives that should be ignored at outposts (like operating the reactor) are properly skipped/activated
        foreach (var character in GameSession.GetSessionCrewCharacters(CharacterType.Both))
        {
            if (character.AIController is HumanAIController humanAI)
            {
                // Clear any existing orders that might have been given before ownership changed
                humanAI.ObjectiveManager.ClearForcedOrder();
                humanAI.ObjectiveManager.CurrentOrders.Clear();

                // Recreate autonomous objectives with the new outpost ownership context
                humanAI.ObjectiveManager.CreateAutonomousObjectives();
            }
        }

        if (createMessageBox)
        {
#if CLIENT
            CreateOutpostOwnershipPopup();
#endif
        }
#if SERVER
        if (createNetworkEvent)
        {
            OutpostBuilder.ServerWriteOutpostOwnershipChange(Location);
            ServerWriteResources(createUINotification: false);
        }
#endif
    }

    /// <summary>
    /// Makes the location non-player-owned and discards its outpost data (modules, dwellers, resources, logs etc), keeping only the shortcut.
    /// Used when the end of the campaign is reached. Doesn't create network events: the clients do the same when the campaign ends.
    /// </summary>
    public static void ResetPlayerOwnedLocation(CampaignMode campaign, Location location)
    {
        if (location.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is not { IsPlayerOwned: true } outpostData) { return; }

        //Location.Reset reverts the location to Location.OriginalType,
        //but as a fallback, if the original type is PlayerOutpost, we set it to the default outpost type instead.
        //There used to be a bug that made OriginalType be set to the current type (see 816d33b), which has now been fixed,
        //but it may be still useful to have this fallback in case there's other ways in which the OriginalType can get reset
        if (location.OriginalType is { } originalType && originalType.Identifier == "PlayerOutpost" &&
            LocationType.Prefabs.TryGet("outpost".ToIdentifier(), out LocationType? outpostType) && outpostType != null)
        {
            DebugConsole.AddWarning($"Tried to reset the type of {location.DisplayName}, but the original type was PlayerOutpost. Resetting to a generic outpost.");
            location.OriginalType = outpostType;
        }
        //restore the original faction (otherwise a random one gets assigned when the type changes)
        location.Faction = location.OriginalFaction ?? location.PreviousFaction;
        //fallback: original/previous faction not set, reset to the most common faction
        if (location.Faction == null)
        {
            location.Faction = campaign.Factions.MaxBy(f => f.Prefab.ControlledOutpostPercentage);
        }
        outpostData.SetPlayerOwned(false, force: true, createMessageBox: false, createNetworkEvent: false);

        //the constructor also replaces the old data's Location.LoadOutpost callback
        var newOutpostData = new PersistentOutpostData(location)
        {
            HasShortcut = outpostData.HasShortcut
        };
        location.SetExtraField(nameof(PersistentOutpostData), newOutpostData);
    }

    public void SetHasShortCut(bool hasShortcut, bool createNetworkEvent)
    {
        if (hasShortcut == HasShortcut) { return; }
        HasShortcut = hasShortcut;
#if SERVER
        if (createNetworkEvent)
        {
            ServerWriteState();
        }
#endif
    }

#if CLIENT
    private void CreateOutpostOwnershipPopup()
    {
        new GUIMessageBox(
            TextManager.Get(IsPlayerOwned ? "outpostclaimedpopup.title" : "outpostlostpopup.title"),
            TextManager.GetWithVariable(IsPlayerOwned ? "outpostclaimedpopup.text" : "outpostlostpopup.text", "[outpostname]", Location.DisplayName));
    }
#endif

    #region Adding and removing modules

    /// <param name="module">The module we want to add.</param>
    /// <param name="thisGap">The gap in the module prefab which should attach to the previous module.</param>
    /// <param name="targetGap">The gap in the previous module that the new module should attach to.</param>
    public OutpostBuilder.PendingOutpostModule? AddPendingModule(PurchasableOutpostModulePrefab module, PurchasableOutpostModulePrefab.GapPositionInfo thisGap, Gap targetGap)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(targetGap);
        if (pendingOutpostModules.Any(p => p.Gap == targetGap))
        {
            throw new InvalidOperationException("Attempted to attach a module to a gap that already has a pending module.");
        }

        var newModule = new OutpostBuilder.PendingOutpostModule(module, thisGap, targetGap);
        pendingOutpostModules.Add(newModule);
        return newModule;
    }

    public OutpostBuilder.PendingOutpostModule? GetPendingModule(int index)
    {
        if (index < 0 || index >= pendingOutpostModules.Count)
        {
            return null;
        }
        return pendingOutpostModules[index];
    }

    public bool RemovePendingModule(OutpostBuilder.PendingOutpostModule pendingModule)
    {
        return pendingOutpostModules.Remove(pendingModule);
    }

    public void ClearPendingModules(bool onlyFinishedModules)
    {
        if (onlyFinishedModules)
        {
            pendingOutpostModules.RemoveAll(m => m.IsReadyToBuild);
        }
        else
        {
            pendingOutpostModules.Clear();
        }
    }

    public void QueueModuleRemoval(PlacedModule module)
    {
        if (!pendingModuleRemovals.Contains(module.Index))
        {
            pendingModuleRemovals.Add(module.Index);
        }
    }

    public bool CancelModuleRemoval(PlacedModule module)
    {
        return pendingModuleRemovals.Remove(module.Index);
    }

    public bool IsRemovalPending(PlacedModule module)
    {
        return pendingModuleRemovals.Contains(module.Index);
    }

    /// <summary>
    /// Clears all the currently pending removals (e.g. when a client is about to replace them with the ones received from the server).
    /// Does not clear <see cref="pendingModuleRemovalsInLatestSave"/>: those determine how the outpost is loaded until the next save,
    /// regardless of what's currently pending.
    /// </summary>
    public void ClearModuleRemovals()
    {
        pendingModuleRemovals.Clear();
    }

    /// <summary>
    /// Clears the removals that were pending in the latest save (= the ones that get applied when the outpost is loaded) from the list of pending removals.
    /// Should be done after these removals have been processed, see <see cref="OutpostBuilder.ApplyPendingModuleRemovals"/>
    /// </summary>
    public void ClearAppliedModuleRemovals()
    {
        pendingModuleRemovals.RemoveAll(pendingModuleRemovalsInLatestSave.Contains);
    }

    #endregion

    private static void OnProgressWorld(Map map, CampaignMode campaign)
    {
        if (map.Locations.Count == 0) { return; }

        foreach (Location location in map.Locations)
        {
            if (!location.HasOutpost() || !location.Discovered) { continue; }
            PersistentOutpostData data = GetPersistentOutpostData(location);
            if (data.IsPlayerOwned)
            {
                foreach (var pendingModule in data.PendingOutpostModules)
                {
                    pendingModule.TimeStepsRemainingToBuild--;
                }
                OutpostBuilder.UpdateSecondaryFactionFromPendingChanges(data);
            }
        }
    }

    #region Saving and loading

    public static void LoadLocation(Location location, CampaignMode mode, XElement locationElement)
    {
        if (locationElement.GetChildElement(nameof(PersistentOutpostData)) is { } outpostDataElement)
        {
            location.SetExtraField(nameof(PersistentOutpostData), 
                new PersistentOutpostData(outpostDataElement, location));
        }
    }

    public static PersistentOutpostData GetPersistentOutpostData(Location location)
    {
        if (location.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is { } data) { return data; }
        data = new PersistentOutpostData(location);
        location.SetExtraField(nameof(PersistentOutpostData), data);
        return data;
    }

    /// <summary>
    /// All locations the player currently owns an outpost in, sorted by name.
    /// </summary>
    public static IEnumerable<Location> GetOwnedLocations()
        => GameMain.GameSession?.Campaign?.Map?.Locations
                    .Where(static l => l.GetExtraField<PersistentOutpostData>(nameof(PersistentOutpostData)) is { IsPlayerOwned: true })
                    .OrderBy(static l => l.DisplayName.Value)
           ?? Enumerable.Empty<Location>();

    public static void SaveLocation(Location location, Map map, XElement locationElement)
    {
        var outpostData = GetPersistentOutpostData(location);
        var outpostDataElement = new XElement(nameof(PersistentOutpostData));
        outpostData.ResourceManager?.Save(outpostDataElement);

        SerializableProperty.SerializeProperties(outpostData, outpostDataElement);
        foreach (var pendingOutpostModule in outpostData.pendingOutpostModules)
        {
            pendingOutpostModule.Save(outpostDataElement);
        }
        outpostDataElement.Add(new XAttribute(nameof(pendingModuleRemovals), string.Join(',', outpostData.pendingModuleRemovals)));
        outpostData.SetPendingChangesInLatestSave(outpostDataElement);

        if (outpostData.IsPlayerOwned)
        {
            //save the creatures before saving the outpost (which also applies any pending module repositionings)
            //so we can save the creatures' position relative to the module they're currently in
            outpostData.CreatureManager?.Save(outpostDataElement);
            if (Level.Loaded is { Type: LevelData.LevelType.Outpost } &&
                Level.Loaded.StartLocation == location &&
                Level.Loaded.StartOutpost is { } outpost)
            {
                outpostData.SaveCurrentOutpost(outpost);
                outpostData.DwellerManager.SaveCurrentOutpostNPCs(outpost);
            }
            outpostDataElement.Add(outpostData.OutpostEntitiesElement);
            outpostData.DwellerManager.Save(outpostDataElement);
        }

        // Generators are saved after the outpost's items, since the item components may update their generators' state when saved
        foreach (var wrg in outpostData.ResourceGenerators)
        {
            wrg.Save(outpostDataElement);
        }

        if (outpostData.OutpostFacilities.TryUnwrap(out var facility))
        {
            facility.Save(outpostDataElement);
        }

        XElement logElement = new XElement(EventLogElementName);
        foreach (var (step, events) in outpostData.Logs)
        {
            XElement element = new XElement("Step", new XAttribute("step", step));
            foreach (EconomyLogEntry entry in events)
            {
                if (entry.IsDebugOnly) { continue; }
                element.Add(entry.ToXML());
            }
            logElement.Add(element);
        }
        outpostDataElement.Add(logElement);

        locationElement.Add(outpostDataElement);
    }

    private void SaveCurrentOutpost(Submarine outpost)
    {
        OutpostFacilities = Option.Some(OutpostFacilityManager.ConstructFromOutpost(outpost));

        DebugConsole.NewMessage($"Saving outpost {outpost.Info.DisplayName} to XML...");
        OutpostEntitiesElement = new XElement(OutpostEntitiesElementName);

        OutpostBuilder.CheckDuplicateEntitiesBetweenModules(outpost);
        OutpostBuilder.RefreshEntitiesInModules(outpost, outpost.OutpostModules);
        OutpostBuilder.RepositionWireNodes(outpost);

        foreach (var module in outpost.OutpostModules)
        {
            if (!module.TemporaryMoveOffset.NearlyEquals(Vector2.Zero))
            {
                module.PositionRelativeToSub += module.TemporaryMoveOffset;
                module.Offset += module.TemporaryMoveOffset;

                Submarine.RepositionEntities(module.TemporaryMoveOffset, module.Entities);
                // Moving the hulls makes the items re-check which hull they're in. Loose items that aren't inside a hull at that point
                // lose their submarine, and wouldn't get saved in the outpost. All the module's entities are in the outpost, so let's just restore it here
                module.Entities.ForEach(e => e.Submarine = outpost);

                module.TemporaryMoveOffset = Vector2.Zero;

                //remove previous hallway, will be recreated in OutpostBuilder when reloading the outpost
                module.HallwayEntities.ForEach(e => e.Remove());
                module.HallwayEntities.Clear();
                module.HallwayEntityIds.Clear();
            }
            module.Save(OutpostEntitiesElement);
        }
        OutpostBuilder.CheckDuplicateEntitiesBetweenModules(outpost);
        outpost.SaveToXElement(OutpostEntitiesElement);
    }

    #endregion
}