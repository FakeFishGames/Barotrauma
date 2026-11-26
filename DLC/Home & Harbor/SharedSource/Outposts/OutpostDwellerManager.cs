using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using HomeAndHarbor.Resources;
using System.Xml.Linq;

namespace HomeAndHarbor;

internal readonly record struct DwellerChangedEvent(Character Character, WorkstationComponent? UnassignedWorkstation, WorkstationComponent? AssignedWorkstation);

/// <summary>
/// Handles storing and managing persistent outpost NPCs in a player outpost.
/// </summary>
internal partial class OutpostDwellerManager : ISerializableEntity
{
    #region Networking

    [NetworkSerialize]
    public readonly struct OutpostDwellerData(Character character) : INetSerializableStruct
    {
        public readonly ushort CharacterID = character.ID;
    }

    [NetworkSerialize]
    public readonly struct WorkstationAssignmentData(Character character, int workstationId) : INetSerializableStruct
    {
        public readonly ushort CharacterID = character.ID;
        public readonly int WorkstationId = workstationId;
    }

    /// <summary>
    /// Tells the clients whether a dweller is unavailable for work, and why.
    /// </summary>
    [NetworkSerialize]
    public readonly struct WorkAvailabilityData(Character character, bool unavailable, Identifier reason) : INetSerializableStruct
    {
        public readonly ushort CharacterID = character.ID;
        public readonly bool Unavailable = unavailable;
        public readonly Identifier Reason = reason;
    }

    [NetworkSerialize]
    public readonly struct MoveDwellerToCrewData(Character character) : INetSerializableStruct
    {
        public readonly ushort CharacterID = character.ID;
        public readonly Identifier JobIdentifier = character.Info?.Job?.Prefab.Identifier ?? Identifier.Empty;
    }

    [NetworkSerialize]
    public readonly struct MoveCrewToDwellerData(Character character) : INetSerializableStruct
    {
        public readonly ushort CharacterID = character.ID;
    }
    #endregion

    #region UI Events

    /// <summary>
    /// Fired when a character's workstation assignment changes (including unassignment and displacement)
    /// </summary>
    public NamedEvent<DwellerChangedEvent> OnDwellerWorkstationChanged = new();

    /// <summary>
    /// Fired when a character gains dweller status
    /// </summary>
    public NamedEvent<Character> OnDwellerAdded = new();

    /// <summary>
    /// Fired when a character loses dweller status
    /// </summary>
    public NamedEvent<Character> OnDwellerRemoved = new();

    /// <summary>
    /// Fired when a character is added to the crew
    /// </summary>
    public NamedEvent<Character> OnCharacterAddedToCrew = new();

    /// <summary>
    /// Fired when a character is removed from the crew
    /// </summary>
    public NamedEvent<Character> OnCharacterRemovedFromCrew = new();
    #endregion

    public enum NPCSpawnStatusType
    {
        /// <summary>
        /// The NPCs that were generated for the current outpost become the persistent outpost NPCs.
        /// Used when a player takes control of the current procedurally generated outpost.
        /// </summary>
        UseCurrentOutpostNPCs,
        /// <summary>
        /// NPCs should be generated and turned into persistent outpost NPCs when the player enters the outpost.
        /// Used when a player takes control of a remote outpost.
        /// </summary>
        GenerateNPCs,
        /// <summary>
        /// NPCs have been saved into XML and should be used as persistent outpost NPCs.
        /// </summary>
        UseSavedNPCs
    }

    private readonly List<OutpostDwellerInfo> dwellerInfos = [];

    private readonly List<Character> spawnedCharacters = [];
    private readonly HashSet<Character> charactersSubscribedToOnKilled = [];

    /// <summary>
    /// Cached reference to the outpost dweller job prefab to avoid repeated lookups
    /// </summary>
    private static JobPrefab? cachedOutpostDwellerJobPrefab;

    private readonly Location location;
    private readonly PersistentOutpostData outpostData;

    private XElement? xmlElement;

    [Serialize(defaultValue: NPCSpawnStatusType.GenerateNPCs, isSaveable: IsPropertySaveable.Yes)]
    public NPCSpawnStatusType NPCSpawnStatus
    {
        get;
        private set;
    }

    public string Name => nameof(OutpostDwellerManager);

    public int CurrentPopulation =>
        IsCurrentLevel && !convertedThisRound ?
            spawnedCharacters.Count(c => !c.Removed && !c.IsDead) :
            dwellerInfos.Count;

    public bool IsCurrentLevel => 
        Level.Loaded is { Type: LevelData.LevelType.Outpost } &&
        Level.Loaded.StartLocation == location &&
        Level.Loaded.StartOutpost != null;

    private bool convertedThisRound;

    public IEnumerable<Character> SpawnedCharacters => spawnedCharacters;

    public IEnumerable<OutpostDwellerInfo> DwellerInfos => dwellerInfos;

    public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

    static OutpostDwellerManager()
    {
        InitProjectSpecific();
    }

    static partial void InitProjectSpecific();

    public OutpostDwellerManager(Location location, PersistentOutpostData outpostData)
    {
        this.location = location;
        this.outpostData = outpostData;
        SerializableProperties = SerializableProperty.GetProperties(this);
    }

    public void Load(XElement element)
    {
        SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

        xmlElement = element;
        dwellerInfos.Clear();
        foreach (XElement subElement in element.Elements())
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "outpostdwellerinfo":
                    dwellerInfos.Add(new OutpostDwellerInfo(new ContentXElement(contentPackage: null, subElement)));
                    break;
            }
        }
    }

    public OutpostDwellerInfo? FindDwellerInfo(Character? character)
    {
        return dwellerInfos.FirstOrDefault(dwellerInfo => dwellerInfo.CharacterInfo == character?.Info);
    }

    /// <summary>
    /// Returns the workstation the given dweller is currently assigned to, or null if the character isn't a dweller of this outpost or isn't assigned to any workstation here.
    /// </summary>
    public WorkstationComponent? GetAssignedWorkstation(Character? character)
    {
        if (FindDwellerInfo(character) is OutpostDwellerInfo dwellerInfo &&
            dwellerInfo.AssignedWorkstationId != 0) // 0 == unassigned
        {
            return WorkstationComponent.FindWorkstationWithID(dwellerInfo.AssignedWorkstationId);
        }

        return null;
    }

    public void SetLocationPlayerOwned(bool isPlayerOwned)
    {
        DebugConsole.NewMessage($"OutpostDwellerManager.SetLocationPlayerOwned({isPlayerOwned}) called for location {location.DisplayName}");
        
        if (!isPlayerOwned)
        {
            DebugConsole.NewMessage($"Location {location.DisplayName} is no longer player-owned, clearing dweller data");
            dwellerInfos.Clear();
            NPCSpawnStatus = NPCSpawnStatusType.GenerateNPCs;
            return;
        }

        //current location became player-owned -> store the information about the current outpost NPCs
        if (Level.Loaded is { Type: LevelData.LevelType.Outpost } &&
            Level.Loaded.StartLocation == location &&
            Level.Loaded.StartOutpost is { } outpost)
        {
            if (GameMain.NetworkMember is not { IsClient: true }) // In MP, server handles this
            {
                ConvertCurrentOutpostNPCsToPersistent(outpost);
            }
        }
        else
        {
            //remote location became player-owned
            //the NPCs in the remote location aren't stored in any way (just generated from a seed),
            //mark that we need to generate some NPCs when the player enters
            NPCSpawnStatus = NPCSpawnStatusType.GenerateNPCs;
        }
    }

    public static void RoundStarted(GameSession gameSession)
    {
        if (gameSession?.EventManager is not { } eventManager) { return; }
        if (Level.Loaded?.StartLocation is { } location &&
            Level.Loaded.Type == LevelData.LevelType.Outpost &&
            PersistentOutpostData.GetPersistentOutpostData(location) is { } outpostData)
        {
            if (!outpostData.IsPlayerOwned)
            {
                //no characters were spawned by dweller manager (= NPCs were generated normally in an NPC-owned outpost)
                //we need to find and initialize them at this point
                if (outpostData.DwellerManager.spawnedCharacters.None())
                {
                    if (Level.Loaded.StartOutpost?.Info is { }  outpostInfo)
                    {
                        outpostData.DwellerManager.spawnedCharacters.AddRange(outpostInfo.OutpostNPCs.SelectMany(kvp => kvp.Value).Distinct());
                        outpostData.DwellerManager.InitSpawnedNPCs();
                    }
                }

                //abandoned outpost missions: offer to claim the outpost as soon as the objectives are completed,
                //instead of requiring the players to leave and come back
                bool hasAbandonedOutpostMission = false;
                foreach (Mission mission in gameSession.Missions)
                {
                    if (mission is not AbandonedOutpostMission || !mission.Locations.Contains(location)) { continue; }
                    hasAbandonedOutpostMission = true;
                    mission.OnMissionStateChanged -= OnAbandonedOutpostMissionStateChanged;
                    mission.OnMissionStateChanged += OnAbandonedOutpostMissionStateChanged;
                }

                //don't offer to claim the outpost at the start of the round if there's a mission to clear it first
                if (!hasAbandonedOutpostMission &&
                    location.IsBelowInsufficientPopulationThreshold &&
                    outpostData.AllowClaimingWhenPopulationLow &&
                    outpostData.AllowChangingOwnership &&
                    GameMain.GameSession?.Campaign != null)
                {
                    TryTriggerOutpostClaimableEvent(eventManager);
                }
            }
            else
            {
                //player-owned outposts don't have any well-defined initial population
                location.InitialPopulation = -1;
                // Re-initialize interactions for all assigned workstations in player-owned outposts, necessary because
                // interactions may fail to initialize during loading when GameSession isn't ready yet
                outpostData.DwellerManager.InitializeWorkstationInteractions();
            }
        }
    }

    private static readonly Identifier OutpostClaimableEventTag = "outpostclaimable".ToIdentifier();

    private static void TryTriggerOutpostClaimableEvent(EventManager eventManager)
    {
        //already triggered this round
        if (eventManager.FindTriggeredEvent(ev => ev.Prefab.Tags.Contains(OutpostClaimableEventTag)) != null) { return; }
        eventManager.TryTriggerEvent(eventIdentifier: Identifier.Empty, eventTag: OutpostClaimableEventTag);
    }

    private static void OnAbandonedOutpostMissionStateChanged(Mission mission)
    {
        //clients should not do any of this by themselves, the server triggers the event
        if (GameMain.NetworkMember is { IsClient: true }) { return; }
        if (mission is not AbandonedOutpostMission { ObjectivesCompleted: true }) { return; }
        if (GameMain.GameSession is not { IsRunning: true, Campaign: not null, EventManager: { } eventManager }) { return; }
        if (Level.Loaded is not { Type: LevelData.LevelType.Outpost, StartLocation: { } location } ||
            !mission.Locations.Contains(location))
        {
            return;
        }
        if (PersistentOutpostData.GetPersistentOutpostData(location) is not { IsPlayerOwned: false, AllowChangingOwnership: true }) { return; }

        TryTriggerOutpostClaimableEvent(eventManager);
    }

    private void InitSpawnedNPCs()
    {
        foreach (var spawnedNpc in spawnedCharacters)
        {
            if (!charactersSubscribedToOnKilled.Contains(spawnedNpc))
            {
                spawnedNpc.OnDeath += OnNPCKilled;
                charactersSubscribedToOnKilled.Add(spawnedNpc);
            }
#if DEBUG

            Identifier headSetId = "headset".ToIdentifier();
            Item? headset = spawnedNpc.Inventory.FindItemByIdentifier(headSetId, recursive: true);
            if (headset == null)
            {
                DebugConsole.ThrowError($"Debug-only error: dweller {spawnedNpc.DisplayName} (job: {spawnedNpc.JobIdentifier}) does not have a headset. " +
                    "This means they can't be given orders from outside speaking range if moved to the crew. Unless the headset was removed manually, this is probably unintentional, and a headset should be added to the character's job items.");
            }

#endif
        }
        OutpostGenerator.AssignCommander(spawnedCharacters);
    }

    /// <summary>
    /// Re-initializes interactions for all characters assigned to workstations.
    /// Called during RoundStarted to ensure interactions are properly set up again after loading from save.
    /// </summary>
    private void InitializeWorkstationInteractions()
    {
        foreach (var workstation in WorkstationComponent.WorkstationComponents)
        {
            if (workstation.UniqueId == 0) { continue; }
            foreach (var dwellerInfo in dwellerInfos)
            {
                if (dwellerInfo.AssignedWorkstationId == workstation.UniqueId)
                {
                    // Find the spawned character matching this dweller info
                    var character = spawnedCharacters.FirstOrDefault(c => c.Info == dwellerInfo.CharacterInfo);
                    if (character != null)
                    {
                        workstation.InitializeInteractions(character);
                    }
                }
            }
        }
    }

    private void OnNPCKilled(Character character, CauseOfDeath causeOfDeath)
    {
        if (GameMain.GameSession?.EventManager is not { } eventManager || GameMain.GameSession.Campaign == null) { return; }
        //clients should not do any of this by themselves, but rather rely on server updates
        if (GameMain.NetworkMember is { IsClient: true }) { return; }
        if (!GameMain.GameSession.IsRunning) { return; }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        //InitialPopulation is -1 (unknown) e.g. in outposts that were player-owned at the start of the round
        if (location.BasePopulation > -1 && location.InitialPopulation > 0)
        {
            bool allowClaiming = outpostData == null ?
                PersistentOutpostData.LocationTypeAllowsClaimingWhenPopulationLow(location.Type) :
                !outpostData.IsPlayerOwned && outpostData.AllowClaimingWhenPopulationLow;
            if (allowClaiming)
            {
                if (spawnedCharacters.Count(c => !c.IsDead && !c.Removed) / (float)location.InitialPopulation < location.Type.InsufficientPopulationThreshold)
                {
                    Identifier eventTag = "outpostconquered".ToIdentifier();
                    if (eventManager.FindTriggeredEvent(ev => ev.Prefab.Tags.Contains(eventTag)) != null)
                    {
                        //already triggered
                        return;
                    }
                    eventManager.TryTriggerEvent(eventIdentifier: Identifier.Empty, eventTag: eventTag);
                }
            }
        }
        //non-player-owned outposts don't keep track of the dwellers using the dweller manager, nor do they use workstations,
        //so we don't need to unassign here (doing that would just cause a console error)
        if (outpostData is { IsPlayerOwned: true })
        {
            AssignCharacterToWorkstation(character, 0);
            RemoveDweller(character, createNetworkEvent: true, fireUIEvents: true);
        }
        if (outpostData?.ResourceManager is ResourceManager resourceManager)
        {
            foreach (var resource in resourceManager.Resources)
            {
                if (MathUtils.NearlyEqual(resource.Prefab.DecreaseOnNPCDeath, 0.0f)) { continue; }
                float oldAmount = resource.Amount;
                if (resource is VariableResource variableResource)
                {
                    variableResource.AddTemporaryEffect(resource.Prefab.DecreaseOnNPCDeath, ModifyResourceAction.OperationType.Subtract, resource.Prefab.DecreaseDurationOnNPCDeath);
                }
                else
                {
                    resource.Amount -= resource.Prefab.DecreaseOnNPCDeath;
                }
#if SERVER
                outpostData.ServerWriteResources(createUINotification: true);
#endif
                var logEntry = EconomyLogEntrySpecial.Create("economylog.resourcelostduetodeath".ToIdentifier(), resource, oldAmount, resource.Amount, relatedResource: null, relatedLocation: null);
                outpostData.StoreResourceChangeEvent(logEntry);
            }
        }
    }

    /// <summary>
    /// Stores the current procedurally generated outpost NPCs as persistent outpost NPCs.
    /// </summary>
    private void ConvertCurrentOutpostNPCsToPersistent(Submarine outpost)
    {
        DebugConsole.NewMessage($"ConvertCurrentOutpostNPCsToPersistent called for outpost {outpost.Info.Name}");
        
        if (convertedThisRound)
        {
            DebugConsole.ThrowError("ConvertCurrentOutpostNPCsToPersistent called multiple times in the same round! This could lead to duplicated NPCs.");
            return;
        }
        
        NPCSpawnStatus = NPCSpawnStatusType.UseCurrentOutpostNPCs;

        foreach (Character character in charactersSubscribedToOnKilled)
        {
            character.OnDeath -= OnNPCKilled;
        }
        charactersSubscribedToOnKilled.Clear();
        // Clear existing lists before converting, to avoid duplicates
        spawnedCharacters.Clear();
        dwellerInfos.Clear();
        
        int convertedCount = 0;
        foreach (Character character in outpost.Info.OutpostNPCs.SelectMany(kvp => kvp.Value).Distinct())
        {
            if (character.IsDead || character.Removed) { continue; }

            if (character.Info == null)
            {
                DebugConsole.AddWarning($"Could not store character {character.Name} in {nameof(OutpostDwellerManager)} (the character has no {nameof(CharacterInfo)}).");
                continue;
            }
            //only convert the NPCs created initially by the outpost generator, not e.g. "extra" outpost NPCs spawned by events or missions
            if (!character.SpawnedByOutpostGenerator) { continue; }

            ConvertNPCToDweller(character, partOfBulkConversion: true);

            convertedCount++;
        }

        DebugConsole.NewMessage($"Converted {convertedCount} NPCs to persistent dwellers");
        convertedThisRound = true;
        InitSpawnedNPCs();

        // Invalidate event cache once after all conversions to ensure events can find the new dwellers but with minimum overhead
        if (convertedCount > 0)
        {
            // Use any character to trigger setting the flag which will do a universal cache refresh
            GameMain.GameSession?.EventManager.EntitySpawned(spawnedCharacters.FirstOrDefault());
        }
    }

    /// <summary>
    /// Clears AI orders, objectives, and pathfinding state from a character.
    /// </summary>
    public static void ClearAIState(Character character)
    {
        if (character.AIController is not HumanAIController humanAI) { return; }

        //no need to do anything if the character is dead,
        //attempting to create new objectives for the dead character would just lead to unnecessary console errors
        if (character.IsDead || character.Removed) { return; }

        if (humanAI.SteeringManager is IndoorsSteeringManager indoorsSteeringManager)
        {
            indoorsSteeringManager.Reset();
        }

        // Clear the idle objective's target hull, so the character doesn't get stuck there
        if (humanAI.ObjectiveManager.GetObjective<AIObjectiveIdle>() is AIObjectiveIdle idleObjective)
        {
            idleObjective.TargetHull = null;
            idleObjective.Behavior = AIObjectiveIdle.BehaviorType.Active;
        }

        // Forced orders eg. from events or special tasks
        humanAI.ObjectiveManager.ClearForcedOrder();

        // Clear orders (eg. operate turrets, reactors etc.), first the persisted state from CharacterInfo and then runtime state from HumanAIController
        character.Info?.ClearCurrentOrders();
        humanAI.ObjectiveManager.CurrentOrders.Clear();

        // Wait timer in case they were told to wait somewhere
        humanAI.ObjectiveManager.WaitTimer = 0f;

        // Refresh autonomous objectives to reflect the new job's objectives (if any)
        humanAI.ObjectiveManager.CreateAutonomousObjectives();
    }

    /// <summary>
    /// Applies the runtime state (things stored on the Character rather than CharacterInfo, so isn't persisted) that
    /// every dweller character needs, whether the character is being converted or respawned.
    /// </summary>
    private static void InitializeDwellerCharacter(Character character)
    {
        character.TeamID = CharacterTeamType.FriendlyNPC;
        character.SetOriginalTeamAndChangeTeam(newTeam: CharacterTeamType.FriendlyNPC, processImmediately: true);

        // Let the player manage the dweller's gear and prosthetics and drag them around like a crew bot's, even though they stay on the FriendlyNPC team
        character.GrantsFriendlyAccess = true;

        if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
        {
            // Clear any special campaign interaction types
            campaignMode.AssignNPCMenuInteraction(character, CampaignMode.InteractionType.None);

            character.MerchantIdentifier = Identifier.Empty;
        }
    }

    /// <summary>
    /// Applies the generic part of the dweller conversion logic to a character. This includes team assignment, job
    /// conversion, cleanup of titles and title colors, AI orders and so on.
    /// This method should ALWAYS be called when converting any character (NPC or crew) to a dweller.
    /// </summary>
    private static void ConvertToDwellerGeneric(Character character, OutpostDwellerManager dwellerManager, bool createNetworkEvent = true, bool fireUIEvents = true)
    {
        InitializeDwellerCharacter(character);

        foreach (Item item in character.Inventory.AllItems)
        {
            //if the character is loaded from a human prefab with preconfigured items, its ID card gets assigned to the sub it spawns in
            //we don't want that in this case, the outpost dwellers' ID cards and headsets should function the same way as the player crew's
            //- and if we didn't do this, the dwellers' ID cards wouldn't even work in the outpost because the SubmarineSpecificID of the outpost changes when it gets converted
            CrewManager.ApplyTeamChangeToItem(item, character.TeamID);
            // Headsets use the player team, so the dwellers can communicate with the crew (assuming equipped headset)
            if (item.GetComponent<Barotrauma.Items.Components.WifiComponent>() is { } wifiComponent)
            {
                wifiComponent.TeamID = CharacterTeamType.Team1;
            }
        }

        if (character.Info == null)
        {
            DebugConsole.NewMessage($"Converted character {character.DisplayName} to dweller, but they seem to be lacking a CharacterInfo?");
            return;
        }
        
        Job originalJob = character.Info.Job;

        // Just to be sure, clear any existing job override first
        character.Info.JobOverride = null;

        // Clear any special title the NPC might have (like Outpost Manager etc)
        character.Info.Title = string.Empty;
        
        // Clear the HumanPrefab reference so converted dwellers don't retain their original NPC job definition
        character.Info.HumanPrefabIds = default;

        // If the character has a hidden NPC job (commoner, outpostmanager, etc), convert them to a generic outpostdweller job
        if (originalJob.Prefab.HiddenJob)
        {
            cachedOutpostDwellerJobPrefab ??= JobPrefab.Prefabs.TryGet("outpostdweller".ToIdentifier(), out JobPrefab? prefab) ? prefab : null;
            if (cachedOutpostDwellerJobPrefab != null)
            {
                character.Info.Job = new Job(cachedOutpostDwellerJobPrefab, isPvP: false);
                if (originalJob.GetSkills().Any())
                {
                    character.Info.Job.OverrideSkills(originalJob.GetSkills().ToDictionary(s => s.Identifier, s => s.Level));
                }
            }
            else
            {
                DebugConsole.ThrowError("Could not find the 'outpostdweller' job prefab for NPC conversion!");
            }
        }

        // Dwellers shouldn't carry over any hostility from before the conversion (e.g. if the outpost was taken over by force)
        character.Faction = Identifier.Empty;
        (character.AIController as HumanAIController)?.ClearHostileTeam(CharacterTeamType.Team1);

        // Clear all AI orders and tasks, then recreate autonomous objectives to reflect the new job
        ClearAIState(character);

        // Finally, add to the dweller manager
        dwellerManager.AddDweller(character, createNetworkEvent, fireUIEvents);
    }

    /// <summary>
    /// Converts a single NPC character (NPC or crew) into a permanent outpost dweller.
    /// </summary>
    /// <param name="character">The character to convert</param>
    /// <param name="partOfBulkConversion">If this call is a part of a looped bulk conversion, we can skip unnecessary checks for performance reasons and assume the calling code will take responsibility for them. Defaults to false.</param>
    /// <returns>True if the conversion was successful, otherwise false</returns>
    public bool ConvertNPCToDweller(Character? character, bool partOfBulkConversion = false)
    {
        if (character?.Info == null)
        {
            DebugConsole.AddWarning($"Could not convert character to dweller: character or character info is null.");
            return false;
        }

        // Check if character is already a dweller (never the case with bulk conversions)
        if (!partOfBulkConversion && FindDwellerInfo(character) != null)
        {
            DebugConsole.AddWarning($"Character {character.DisplayName} is already a dweller, skipping conversion.");
            return false;
        }

        // If the character is a crew member, move them from crew to dweller (never the case with bulk conversions)
        if (!partOfBulkConversion && GameSession.GetSessionCrewCharacters(CharacterType.Both).Contains(character))
        {
            MoveCrewMemberToDweller(character);
            DebugConsole.NewMessage($"Successfully converted {character.DisplayName} from crewmember to outpost dweller.");
            return true;
        }

        // For other NPCs, do just the standard conversion
        ConvertToDwellerGeneric(character, this, createNetworkEvent: GameMain.NetworkMember is { IsServer: true });

        if (!partOfBulkConversion)
        {
            // Notify event system that characters have changed significantly enough for cache invalidation
            GameMain.GameSession?.EventManager.EntitySpawned(character);

            DebugConsole.NewMessage($"Successfully converted {character.DisplayName} to outpost dweller.");
        }

        return true;
    }

    public bool RemoveDwellerStatus(Character? character, bool invalidateEventCache = true)
    {
        if (character?.Info == null)
        {
            DebugConsole.AddWarning($"Could not convert character to dweller: character or character info is null.");
            return false;
        }

        var dwellerInfo = FindDwellerInfo(character);
        if (dwellerInfo is not OutpostDwellerInfo)
        {
            DebugConsole.AddWarning($"Character {character.DisplayName} is not a dweller, cannot remove dweller status.");
            return false;
        }

        RemoveDweller(character, createNetworkEvent: GameMain.NetworkMember is { IsServer: true });

        if (invalidateEventCache)
        {
            // Notify event system that characters have changed significantly enough for cache invalidation
            GameMain.GameSession?.EventManager.EntitySpawned(character);
        }

        return true;
    }

    private void AddDweller(Character character, bool createNetworkEvent = true, bool fireUIEvents = true)
    {
        // Check if character is already a dweller to avoid duplicates
        if (FindDwellerInfo(character) != null)
        {
            DebugConsole.AddWarning($"Character {character.DisplayName} is already a dweller, skipping AddDweller.");
            return;
        }

        if (!charactersSubscribedToOnKilled.Contains(character))
        {
            character.OnDeath += OnNPCKilled;
            charactersSubscribedToOnKilled.Add(character);
        }
        spawnedCharacters.Add(character);
        dwellerInfos.Add(new OutpostDwellerInfo(character));
        
        if (fireUIEvents)
        {
            OnDwellerAdded.Invoke(character);
        }
#if SERVER
        if (createNetworkEvent)
        {
            GameNetwork.Broadcast(Plugin.NetworkHeaders.AddDweller, new OutpostDwellerData(character));
        }
#endif
    }

    public void RemoveDweller(Character character, bool createNetworkEvent = true, bool fireUIEvents = true)
    {
        character.OnDeath -= OnNPCKilled;
        spawnedCharacters.Remove(character);
        dwellerInfos.RemoveAll(dwellerInfo => dwellerInfo.CharacterInfo == character.Info);

        character.GrantsFriendlyAccess = false;
        
        if (fireUIEvents)
        {
            OnDwellerRemoved.Invoke(character);
        }
#if SERVER
        if (createNetworkEvent)
        {
            GameNetwork.Broadcast(Plugin.NetworkHeaders.RemoveDweller, new OutpostDwellerData(character));
        }
#endif
    }

    public void SpawnNPCs(Submarine outpost)
    {
        if (NPCSpawnStatus == NPCSpawnStatusType.GenerateNPCs)
        {
            OutpostGenerator.SpawnNPCs(GameMain.GameSession.StartLocation, outpost,
                /*spawn the initial NPCs even if the playeroutpost type normally doesn't spawn any NPCs and uses the persistent NPCs instead - 
                 we don't have those persistent NPCs yet, we need to spawn something first*/                
                spawnNPCsEvenIfDisallowedByOutpostGenerationParameters: true);
            ConvertCurrentOutpostNPCsToPersistent(outpost);
        }
        else
        {
            spawnedCharacters.Clear();
            for (int i = 0; i < dwellerInfos.Count; i++)
            {
                CharacterInfo characterInfo = dwellerInfos[i].CharacterInfo;
                WayPoint? spawnPoint = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, 
                    SpawnType.Human,
                    characterInfo.HumanPrefab?.GetModuleFlags(),
                    characterInfo.HumanPrefab?.GetSpawnPointTags());
                ISpatialEntity? spawnPos = 
                    spawnPoint as ISpatialEntity ?? 
                    outpost.GetHulls(true).GetRandom(Rand.RandSync.Unsynced);
                if (spawnPos == null)
                {
                    DebugConsole.ThrowError("Failed to find any spawn positions for an outpost dweller (no spawnpoints or hulls in the outpost?)");
                    break;
                }
                var npc = Character.Create(characterInfo.SpeciesName, spawnPos.WorldPosition, seed: characterInfo.GetIdentifier().ToString(), characterInfo, hasAi: true, createNetworkEvent: false);
                CrewManager.InitializeCharacter(npc, mainSubWaypoint: spawnPoint, spawnWaypoint: spawnPoint, Rand.RandSync.ServerAndClient, createNetworkEvent: false);
                npc.AnimController.FindHull(spawnPos.WorldPosition, setSubmarine: true);
                InitializeDwellerCharacter(npc);
                outpost.Info.AddOutpostNPC(npc);
                if (GameMain.NetworkMember?.ServerSettings != null && !GameMain.NetworkMember.ServerSettings.KillableNPCs)
                {
                    npc.CharacterHealth.Unkillable = true;
                }
                if (characterInfo.HumanPrefab is { } humanPrefab)
                {
                    ISpatialEntity gotoTarget = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, SpawnType.Human, humanPrefab.GetModuleFlags(), humanPrefab.GetSpawnPointTags());
                    humanPrefab.InitializeCharacter(npc, gotoTarget);
                    npc.TeleportTo(gotoTarget.WorldPosition);
                }
                spawnedCharacters.Add(npc);
                dwellerInfos[i].OnCharacterSpawned(npc);
            }
            location.BasePopulation = spawnedCharacters.Count;
            //player-owned outposts don't have any well-defined initial population
            location.InitialPopulation = -1;
            InitSpawnedNPCs();
        }
    }

    public void SaveCurrentOutpostNPCs(Submarine outpost)
    {
        List<OutpostDwellerInfo> newDwellerInfos = new List<OutpostDwellerInfo>();
        xmlElement = new XElement(nameof(OutpostDwellerManager));
        foreach (Character character in spawnedCharacters)
        {
            if (character.Info == null)
            {
                DebugConsole.AddWarning($"A character with no info ({character.Name}) was present in the list of OutpostNPCs. Skipping saving...");
                continue;
            }
            if (character.IsDead || character.Removed) { continue; }
            character.Info.StartItemsGiven = true;
            var newDwellerInfo = FindDwellerInfo(character) ?? new OutpostDwellerInfo(character);
            newDwellerInfo.Save(xmlElement);
            newDwellerInfos.Add(newDwellerInfo);
        }
        dwellerInfos.Clear();
        dwellerInfos.AddRange(newDwellerInfos);
        location.BasePopulation = dwellerInfos.Count;
        //player-owned outposts don't have any well-defined initial population
        location.InitialPopulation = -1;
        NPCSpawnStatus = NPCSpawnStatusType.UseSavedNPCs;
    }

    public void Save(XElement parentElement)
    {
        convertedThisRound = false;
        xmlElement = new XElement(nameof(OutpostDwellerManager));
        foreach (var dwellerInfo in dwellerInfos)
        {
            dwellerInfo.Save(xmlElement);
        }
        SerializableProperty.SerializeProperties(this, xmlElement);
        parentElement.Add(xmlElement);
    }

    public void SetPopulation(int value)
    {
        if (value < 0)
        {
            DebugConsole.ThrowError($"Attempted to set the population of {location?.DisplayName ?? "unknown"} to a negative value!\n" + Environment.StackTrace);
            return;
        }
        int oldPopulation = CurrentPopulation;
        if (value < oldPopulation)
        {
            for (int i = 0; i < oldPopulation - value && dwellerInfos.Any(); i++)
            {
                dwellerInfos.RemoveAt(0);
            }
        }
        else if (value > oldPopulation)
        {
            for (int i = 0; i < value - oldPopulation; i++)
            {
                var character = CreateNewCharacter();
                dwellerInfos.Add(new OutpostDwellerInfo(character));
            }
        }
    }

    private static CharacterInfo CreateNewCharacter()
    {
        var humanPrefab =
            NPCSet.Get("playeroutpostnpcs".ToIdentifier(), Rand.RandSync.Unsynced) ??
            NPCSet.Get("outpostnpcs1".ToIdentifier(), "commoner".ToIdentifier());
        if (humanPrefab == null)
        {
            DebugConsole.ThrowError("Could not find player outpostnpcs or a human prefab \"commoner\" from \"outpostnpcs1\".");
            return new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: JobPrefab.Get("assistant".ToIdentifier()));
        }
        return humanPrefab.CreateCharacterInfo();
    }

    /// <summary>
    /// Moves a crew member to become an outpost dweller.
    /// </summary>
    public void MoveCrewMemberToDweller(Character character)
    {
#if CLIENT
        if (GameMain.IsMultiplayer)
        {
            ClientWriteMoveCrewToDweller(character);
            return;
        }
#endif
        Resource? populationResource = outpostData.ResourceManager?.GetResource(new Identifier("basepopulation"));
        Resource? resourceToLog = populationResource?.GetResourceToLogAs() ?? populationResource;
        float? oldResourceAmount = resourceToLog?.Amount;

        // Remove before converting, so the item teams get updated while the character is still on the player team (dwellers' headsets should stay on it)
        GameMain.GameSession?.CrewManager?.RemoveCharacter(character, removeInfo: true, changeItemTeams: true, createNetworkEvent: true);

        ConvertToDwellerGeneric(character, this, createNetworkEvent: false, fireUIEvents: false);

        // Notify event system that characters have changed significantly enough for cache invalidation
        GameMain.GameSession?.EventManager.EntitySpawned(character);

        SetHeadsetEquipped(character, equipped: false);

#if SERVER
        GameNetwork.Broadcast(Plugin.NetworkHeaders.MoveCrewToDweller, new MoveCrewToDwellerData(character));
#endif

        OnCharacterRemovedFromCrew.Invoke(character);
        OnDwellerAdded.Invoke(character);

        if (resourceToLog != null)
        {
            outpostData.StoreResourceChangeEvent(EconomyLogEntryGeneric.Create(LogType.PlayerResourceTransfer, resourceToLog, oldResourceAmount!.Value, resourceToLog.Amount, location));
        }
    }

    /// <summary>
    /// Moves an outpost dweller to become a crew member.
    /// </summary>
    public void MoveDwellerToCrewMember(Character character)
    {
#if CLIENT
        if (GameMain.IsMultiplayer)
        {
            ClientWriteMoveDwellerToCrew(character);
            return;
        }
#endif
        if (character.Info == null)
        {
            DebugConsole.ThrowError($"Dweller {character.DisplayName} doesn't seem to have a CharacterInfo?");
            return;
        }

        if (FindDwellerInfo(character) is { IsUnavailableForWork: true })
        {
            DebugConsole.NewMessage($"{character.DisplayName} is unavailable for work, cannot move them to the crew. How did this even happen?");
            return;
        }

        Resource? populationResource = outpostData.ResourceManager?.GetResource(new Identifier("basepopulation"));
        Resource? resourceToLog = populationResource?.GetResourceToLogAs() ?? populationResource;
        float? oldResourceAmount = resourceToLog?.Amount;

        // If necessary (based on the actual job, not override), assign appropriate crew job based on highest skill
        if (CharacterLackingCrewJob(character.Info.ActualJob.Prefab))
        {
            AssignCrewJobBasedOnSkills(character);
        }

        // Clear workstation assignment before moving to crew
        if (FindDwellerInfo(character) is OutpostDwellerInfo dwellerInfo && dwellerInfo.AssignedWorkstationId != 0)
        {
            AssignCharacterToWorkstation(character, 0);
        }

        SetHeadsetEquipped(character, equipped: true);

        RemoveDweller(character, createNetworkEvent: false, fireUIEvents: false);
        character.SetOriginalTeamAndChangeTeam(newTeam: CharacterTeamType.Team1, processImmediately: true);
        GameMain.GameSession?.CrewManager?.AddCharacter(character, changeItemTeams: true, createNetworkEvent: true);

        // Notify event system that characters have changed significantly enough for cache invalidation
        GameMain.GameSession?.EventManager.EntitySpawned(character);

#if SERVER
        GameNetwork.Broadcast(Plugin.NetworkHeaders.MoveDwellerToCrew, new MoveDwellerToCrewData(character));
#endif

        OnDwellerRemoved.Invoke(character);
        OnCharacterAddedToCrew.Invoke(character);

        if (resourceToLog != null)
        {
            outpostData.StoreResourceChangeEvent(EconomyLogEntryGeneric.Create(LogType.PlayerResourceTransfer, resourceToLog, oldResourceAmount!.Value, resourceToLog.Amount, location));
        }
    }

    private static void SetHeadsetEquipped(Character character, bool equipped)
    {
        Identifier headSetId = "headset".ToIdentifier();
        Item? headset = character.Inventory.FindItemByIdentifier(headSetId, recursive: true);
        if (headset != null)
        {
            if (equipped)
            {
                character.Inventory.TryPutItem(headset, character, allowedSlots: InvSlotType.Headset.ToEnumerable());
            }
            else
            {
                character.Inventory.TryPutItem(headset, character, allowedSlots: InvSlotType.Any.ToEnumerable());
            }
        }
    }
    
    public void AssignCharacterToWorkstation(Character character, int newWorkstationId, bool createNetworkEvent = true, Client? client = null)
    {
#if CLIENT
        if (GameMain.IsMultiplayer && createNetworkEvent)
        {
            ClientWriteAssignDwellerToWorkstation(character, newWorkstationId);
            return;
        }
#endif
        if (FindDwellerInfo(character) is not OutpostDwellerInfo dwellerInfo)
        {
            DebugConsole.ThrowError($"Could not find dweller info for character {character.DisplayName} (ID: {character.ID})");
            return;
        }

        bool isUnassignment = newWorkstationId == 0;

        if (!isUnassignment && dwellerInfo.IsUnavailableForWork && GameMain.NetworkMember is not { IsClient: true })
        {
            DebugConsole.NewMessage($"{character.DisplayName} is not available for work, cannot assign them to the workstation {newWorkstationId}.");
            return;
        }

        // If the (un)assignment is already fully in effect, no need to continue from here.
        if (dwellerInfo.AssignedWorkstationId == newWorkstationId &&
            (isUnassignment
                ? !WorkstationComponent.WorkstationComponents.Any(workstation => workstation.HasCharacterAssigned(character))
                : WorkstationComponent.FindWorkstationWithID(newWorkstationId) is WorkstationComponent alreadyAssignedWorkstation &&
                  alreadyAssignedWorkstation.HasCharacterAssigned(character)))
        {
#if SERVER
            if (createNetworkEvent)
            {
                GameNetwork.Broadcast(Plugin.NetworkHeaders.AssignDwellerToWorkstation, new WorkstationAssignmentData(character, newWorkstationId));
            }
#endif
            return;
        }

        WorkstationComponent? oldWorkstation = GetAssignedWorkstation(character),
                              newWorkstation = isUnassignment ? null : WorkstationComponent.FindWorkstationWithID(newWorkstationId);

        // Remove from old workstation if character was previously assigned
        if (oldWorkstation != null)
        {
            oldWorkstation.UnassignCharacter(character);
        }

        WorkstationComponent? newAssignedWorkstation = null;

        if (isUnassignment)
        {
            // Update dweller info for unassignment
            dwellerInfo.AssignedWorkstationId = 0;
            dwellerInfo.ClearWorkstationSkill();
        }
        else if (newWorkstation != null)
        {
            // Displace/replace/oust an existing worker if workstation capacity would be exceeded
            if (newWorkstation.AssignedNPCCount >= newWorkstation.MaxAssignedNPCs)
            {
                var dwellerToDisplace = dwellerInfos.FirstOrDefault(displaced => 
                    displaced.AssignedWorkstationId == newWorkstationId);

                if (dwellerToDisplace != null && 
                    spawnedCharacters.FirstOrDefault(c =>
                        c.Info == dwellerToDisplace.CharacterInfo) is Character displacedCharacter)
                {
                    DebugConsole.NewMessage($"Workstation {newWorkstationId} is full, displacing {displacedCharacter.DisplayName} before assigning {character.DisplayName}");
                    AssignCharacterToWorkstation(displacedCharacter, 0, createNetworkEvent, client);
                }
            }

            // Update dweller workstation assignment and related skill settings
            dwellerInfo.AssignedWorkstationId = newWorkstationId;
            dwellerInfo.SetWorkstationSkill(newWorkstation.SkillIdentifier, newWorkstation.SkillIncreasePerWorldStep);
            newWorkstation.AssignCharacter(character);
            newAssignedWorkstation = newWorkstation;
        }
        else
        {
            DebugConsole.ThrowError($"Could not find workstation with ID {newWorkstationId} when trying to assign {character.DisplayName}");
            return;
        }

        // Trigger UI event for workstation assignment changes (including unassignment and displacement)
        OnDwellerWorkstationChanged.Invoke(new DwellerChangedEvent(character, oldWorkstation, newWorkstation));
#if SERVER
        if (client != null && newAssignedWorkstation != oldWorkstation)
        {
            if (newAssignedWorkstation != null)
            {
                GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.clientassigneddweller",
                    ("[dwellername]", character.DisplayName),
                    ("[workstation]", newAssignedWorkstation.Item.Name),
                    ("[client]", NetworkMember.ClientLogName(client))));
            }
            else if (oldWorkstation != null)
            {
                GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.clientunassigneddweller",
                    ("[dwellername]", character.DisplayName),
                    ("[workstation]", oldWorkstation.Item.Name),
                    ("[client]", NetworkMember.ClientLogName(client))));
            }
        }

        if (createNetworkEvent)
        {
            GameNetwork.Broadcast(Plugin.NetworkHeaders.AssignDwellerToWorkstation, new WorkstationAssignmentData(character, newWorkstationId));
        }
#endif
    }

    /// <summary>
    /// Makes a dweller unavailable for work for a number of work shifts (world steps), or clears that if <paramref name="workShifts"/>
    /// is 0. They're unassigned from their workstation and can't be assigned to another one until the shifts have passed.
    /// </summary>
    /// <param name="workShifts">How many work shifts the dweller stays unavailable for work. 0 resets unavailability, -1 makes it indefinite until this is explicitly called again with 0 to clear it or >0 to make it finite again.</param>
    /// <param name="reason">Optional text tag shown in the dweller UI in place of the workstation name, so events can give flavor for the unavailability. Defaults to the generic 'dwellerui.unavailable'.</param>
    public bool SetWorkUnavailability(Character character, int workShifts, Identifier reason)
    {
        if (GameMain.NetworkMember is { IsClient: true }) { return false; }

        if (FindDwellerInfo(character) is not OutpostDwellerInfo dwellerInfo)
        {
            DebugConsole.ThrowError($"Could not find dweller info for character {character.DisplayName} (ID: {character.ID})");
            return false;
        }

        if (workShifts == 0)
        {
            dwellerInfo.ClearWorkUnavailability();
        }
        else
        {
            dwellerInfo.WorkShiftsUnavailable = workShifts;
            dwellerInfo.UnavailabilityReason = reason;
            // A dweller who has become unavailable for work abandons their work objective, clears the job override, and returns the workstation's items
            AssignCharacterToWorkstation(character, 0);
        }

        // Refresh the UI now, can't rely on the unassignment above having done it because it couldn't if there was no workstation to unassign from
        OnDwellerWorkstationChanged.Invoke(new DwellerChangedEvent(character, null, null));

#if SERVER
        GameNetwork.Broadcast(Plugin.NetworkHeaders.SetDwellerWorkAvailability,
            new WorkAvailabilityData(character, dwellerInfo.IsUnavailableForWork, dwellerInfo.UnavailabilityReason));
#endif
        return true;
    }

    /// <summary>
    /// Counts down the work shifts of every dweller who's currently unavailable for work.
    /// </summary>
    public void ProgressWorkUnavailability()
    {
        if (GameMain.NetworkMember is { IsClient: true }) { return; }

        foreach (OutpostDwellerInfo dwellerInfo in dwellerInfos)
        {
            // Negative values mean the dweller stays unavailable indefinitely, so only count down positive ones
            if (dwellerInfo.WorkShiftsUnavailable > 0)
            {
                dwellerInfo.WorkShiftsUnavailable--;
                if (dwellerInfo.WorkShiftsUnavailable == 0)
                {
                    dwellerInfo.ClearWorkUnavailability();

#if SERVER
                    // Broadcast the change for any dwellers in the currently loaded outpost
                    if (spawnedCharacters.FirstOrDefault(c => c.Info == dwellerInfo.CharacterInfo) is Character { Removed: false } character)
                    {
                        GameNetwork.Broadcast(Plugin.NetworkHeaders.SetDwellerWorkAvailability,
                            new WorkAvailabilityData(character, dwellerInfo.IsUnavailableForWork, dwellerInfo.UnavailabilityReason));
                    }
#endif
                }
            }
        }
    }

    /// <summary>
    /// Assigns an appropriate crew job to a character based on their highest skill.
    /// </summary>
    private static void AssignCrewJobBasedOnSkills(Character character)
    {
        if (character.Info?.Job == null) {
            DebugConsole.ThrowError($"Character {character.DisplayName} is missing a Job!");
            return;
        }

        Job oldJob = character.Info.Job;
        JobPrefab? matchingJobPrefab = null;

        var skills = oldJob.GetSkills().ToList();
        if (skills.Count == 0)
        {
            // The character has no skills, so pick a random Job with the same weighted distribution as used elsewhere in the game
            matchingJobPrefab = JobPrefab.Random(Rand.RandSync.Unsynced, predicate: job => job.Identifier != "outpostdweller");
        }
        else
        {
            // Find all skills tied for the highest level (round using truncation the same way as when displaying the skills, so it's clear for the player which one gets used) 
            int highestLevel = (int)skills.Max(s => s.Level);
            var tiedSkills = skills.Where(s => (int)s.Level == highestLevel);

            // Find all Jobs whose primary skill matches (any of) the highest skill(s)
            var matchingJobs = JobPrefab.Prefabs
                .Where(job => !job.HiddenJob &&
                              job.Identifier != "outpostdweller".ToIdentifier() &&
                              job.PrimarySkill != null &&
                              tiedSkills.Any(s => s.Identifier == job.PrimarySkill.Identifier));

            if (matchingJobs.Any())
            {
                // Randomize between matching Jobs if multiple skills are tied
                matchingJobPrefab = matchingJobs.GetRandomUnsynced();
            }
        }

        // Fall back to assistant if no matching Job found
        if (matchingJobPrefab == null)
        {
            if (JobPrefab.Prefabs.TryGet("assistant".ToIdentifier(), out JobPrefab? assistantPrefab))
            {
                matchingJobPrefab = assistantPrefab;
            }
            else
            {
                DebugConsole.ThrowError($"Could not find a suitable crew job for {character.DisplayName}, and the job 'assistant' does not exist as fallback!");
                return;
            }
        }

        AssignCrewJob(character, matchingJobPrefab);
    }

    /// <summary>
    /// Assigns the given crew job to the character, preserving all their existing skills.
    /// </summary>
    private static void AssignCrewJob(Character character, JobPrefab jobPrefab)
    {
        if (character.Info?.Job == null)
        {
            DebugConsole.ThrowError($"Character {character.DisplayName} is missing a Job!");
            return;
        }

        Job oldJob = character.Info.Job;

        // Clear any job override first
        character.Info.JobOverride = null;

        // Assign the new Job, and preserve all existing skills
        character.Info.Job = new Job(jobPrefab, isPvP: false);
        character.Info.Job.OverrideSkills(oldJob.GetSkills().ToDictionary(s => s.Identifier, s => s.Level));
    }

    private static bool CharacterLackingCrewJob(JobPrefab jobPrefab)
    {
        return jobPrefab.Identifier == "outpostdweller" || jobPrefab.HiddenJob;
    }
}
