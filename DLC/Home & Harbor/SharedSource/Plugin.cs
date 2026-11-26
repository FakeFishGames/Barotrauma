using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace HomeAndHarbor;

public partial class Plugin : IBarotraumaPlugin
{
    public static readonly Assembly DlcAssembly = typeof(Plugin).Assembly;

    public enum NetworkHeaders
    {
        UpdatePersistentOutpostData,
        // Outpost Building
        RenameOutpost,
        AddOutpostModule,
        RemoveOutpostModule,
        MoveOutpostModule,
        MovePendingModule,
        CancelOutpostModuleRemoval,
        CancelOutpostModuleAddition,
        OutpostModuleChanges,
        SetOutpostOwnership,
        SwapOutpostItem,
        UpdateItemSwapStorage,
        SyncItemSwapStorage,
        SaveAndReloadLevel,
        // Dwellers
        AddDweller,
        RemoveDweller,
        AssignDwellerToWorkstation,
        SetDwellerWorkAvailability,
        MoveDwellerToCrew,
        MoveCrewToDweller,
        //resources
        UpdateResources,
        //outpost management
        OpenOutpostManagementUI,
        OpenCampaignInteraction,
        ShowOutpostMessage,
        //research
        UpdateResearch,
        SyncResearch,
        ClientSetResearch,
        UpdateShortcutsUnlocked,
        RequestCatalog,
        TriggerEnding
    }

    public void Init()
    {
        DebugConsole.NewMessage("DLC loaded", Color.Lime);

#if DEBUG
        DebugConsole.RegisterCommand(
            command: "testunloading",
            helpMessage: "Adds a property to the current campaign which would prevent unloading if there is a bug.",
            flags: CommandFlags.DoNotRelayToServer,
            onCommandExecuted: OnTestUnloadingCommandExecuted);

        DebugConsole.RegisterCommand(
            command: "modcrashtest",
            helpMessage: "Crash the game.",
            flags: CommandFlags.DoNotRelayToServer,
            onCommandExecuted: static args => throw new NullReferenceException());
#endif

        DebugConsole.RegisterCommand(
            command: "setresource",
            helpMessage: "setresource [resource] [amount] [location name]: Set the amount of an outpost resource to a specific value. If the location name is omitted, the resources of the current outpost are modified.",
            flags: CommandFlags.IsCheat,
            onCommandExecuted: SetResourceExecuted,
            getValidArgs: () =>
            {
                if (GameMain.GameSession?.Campaign is not { } campaign) { return [[]]; }
                var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
                if (outpostData?.DwellerManager == null) { return [[]]; }
                return
                [
                    ResourcePrefab.Prefabs.Select(p => p.Identifier.Value).ToArray(),
                    ["0"],
                    campaign.Map.Locations.Where(l => l.HasOutpost()).Select(l => l.DisplayName.Value).ToArray()
                ];
            });

        DebugConsole.RegisterCommand(
            command: "outpostwallet",
            helpMessage: "outpostwallet: Show the current outpost's wallet balance.",
            flags: CommandFlags.IsCheat,
            onCommandExecuted: OutpostWalletExecuted);

        DebugConsole.RegisterCommand(
            command: "outpostdwellertocrew",
            helpMessage: "outpostdwellertocrew [character name]: Make one of the outpost dwellers of the current outpost a member of the crew.",
            flags: CommandFlags.IsCheat,
            onCommandExecuted: OutpostDwellerToCrewExecuted,
            getValidArgs: () =>
            {
                if (GameMain.GameSession?.Campaign is not { } campaign) { return [[]]; }
                var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
                if (outpostData?.DwellerManager == null) { return [[]]; }
                return
                [
                    outpostData.DwellerManager.SpawnedCharacters.Select(c => c.Name).ToArray(),
                ];
            });

        DebugConsole.RegisterCommand(
            command: "crewtooutpostdweller",
            helpMessage: "crewtooutpostdweller [character name]: Make one of the crew members an outpost dweller in the current outpost.",
            flags: CommandFlags.IsCheat,
            onCommandExecuted: CrewToOutpostDwellerExecuted,
            getValidArgs: () =>
            {
                return
                [
                    GameSession.GetSessionCrewCharacters(CharacterType.Both).Select(c => c.Name).ToArray()
                ];
            });

        DebugConsole.RegisterCommand(
            command: "assigndwellertoworkstation",
            helpMessage: "assigndwellertoworkstation [NPC name] [workstation name] [workstation index]: assigns an outpost dweller to a work station.",
            flags: CommandFlags.DoNotRelayToServer,
            onCommandExecuted: OnAssignDwellerToWorkstationCommandExecuted,
            getValidArgs: () =>
            {
                List<EventPrefab> eventPrefabs = EventSet.GetAllEventPrefabs().Where(prefab => prefab.Identifier != Identifier.Empty).ToList();
                return new[]
                {
                    Character.CharacterList.Where(c => c.IsHuman && c.TeamID == CharacterTeamType.FriendlyNPC).Select(c => c.Name).ToArray(),
                    Item.ItemList.Where(i => i.GetComponent<WorkstationComponent>() != null).Distinct().Select(i => i.Name).ToArray(),
                    ["0"]
                };
            });


        DebugConsole.RegisterCommand("printsavedoutpostcreatures", "printsavedoutpostcreatures: Prints information about saved outpost creatures.",
            flags: CommandFlags.IsCheat,
            getValidArgs: null,
            onCommandExecuted: static args =>
            {
                if (GameMain.GameSession.Campaign is not { } campaign)
                {
                    DebugConsole.ThrowError("No campaign running.");
                    return;
                }

                var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
                if (outpostData.CreatureManager == null)
                {
                    DebugConsole.ThrowError($"Location {campaign.CurrentLocation.DisplayName} has no creature manager.");
                    return;
                }
                
                foreach (OutpostCreatureInfo info in outpostData.CreatureManager.CreatureInfos)
                {
                    DebugConsole.NewMessage($"{info.Character?.Name ?? "NULL"} : {nameof(OutpostCreatureInfo.LastReproductionWorldStep)} = {info.LastReproductionWorldStep}, {nameof(OutpostCreatureInfo.WorldStepsInOutpost)} = {info.WorldStepsInOutpost}");
                }

            });

        DebugConsole.RegisterCommand("infestvent", "infestvent: Randomly infests a vent on the main submarine or player outpost.",
            flags: CommandFlags.IsCheat,
            getValidArgs: null,
            onCommandExecuted: static args =>
            {
                var vents =
                    Submarine.Loaded
                        .Where(sub => sub.TeamID == CharacterTeamType.Team1)
                        .SelectMany(sub => sub.GetItems(alsoFromConnectedSubs: true))
                        .Where(item => item.GetComponent<InfestableVentComponent>() != null)
                        .Select(item => item.GetComponent<InfestableVentComponent>())
                        .Where(vent => !vent.IsInfested && vent.CanBeInfested);

                var vent = vents.GetRandomUnsynced();
                if (vent != null)
                {
                    vent.Infest();
                    DebugConsole.NewMessage($"Vent in hull {vent.Item.CurrentHull} infested.");
                }
            });

        #region Technology Tree

        DebugConsole.RegisterCommand("addresearchpoints", "addresearchpoints [amount]: Adds the specified amount of research points.",
            flags: CommandFlags.IsCheat,
            getValidArgs: null,
            onCommandExecuted: static args =>
            {
                if (args.Length == 0 || !Int32.TryParse(args[0], out int amount))
                {
                    DebugConsole.NewMessage("No valid amount provided");
                    return;
                }

                if (!ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
                {
                    DebugConsole.NewMessage($"Not in a campaign.");
                    return;
                }

                researchManager.AddResearchPoints(amount);

                DebugConsole.NewMessage($"Added {amount} ResearchPoints");
            });

        DebugConsole.RegisterCommand("selectresearch", "selectresearch [technology name/id]: Starts researching the specified technology.",
            flags: CommandFlags.IsCheat,
            getValidArgs: static () => [[.. TechnologyPrefab.Prefabs.Select(prefab => prefab.Name.Value), .. TechnologyPrefab.Prefabs.Keys.Select(id => id.Value)]],
            onCommandExecuted: static args =>
            {
                if (args.Length == 0)
                {
                    DebugConsole.NewMessage("No technology name provided.");
                    return;
                }

                if (!ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
                {
                    DebugConsole.NewMessage($"Not in a campaign.");
                    return;
                }

                TechnologyPrefab? technology = TechnologyPrefab.Prefabs.Find(prefab => prefab.Name == args[0]);
                if (technology == null && !TechnologyPrefab.Prefabs.TryGet(args[0], out technology))
                {
                    DebugConsole.NewMessage($"Technology with name or identifier \"{args[0]}\" not found.");
                    return;
                }

                researchManager.SelectActiveResearch(technology!);

                DebugConsole.NewMessage($"Selected research {technology!.Identifier} with progress {researchManager.ActiveResearch!.Progress}");
            });

        DebugConsole.RegisterCommand("unlocktechnology|unlockresearch|research", "unlocktechnology [technology all/name/id]: Unlocks all technologies/the technology with the given name or identifier.",
            flags: CommandFlags.IsCheat,
            getValidArgs: static () => [[
                    .. TechnologyPrefab.Prefabs.Select(prefab => prefab.Name.Value), 
                    .. TechnologyPrefab.Prefabs.Keys.Select(id => id.Value), 
                    .. new string[] { "All" }]],
            onCommandExecuted: static args =>
            {
                if (args.Length == 0)
                {
                    DebugConsole.NewMessage("No technology name provided.");
                    return;
                }

                if (!ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out ResearchManager? researchManager))
                {
                    DebugConsole.NewMessage($"Not in a campaign.");
                    return;
                }

                if (args[0].ToLowerInvariant() == "all")
                {
                    TechnologyPrefab.Prefabs.ForEach(prefab => researchManager.Unlock(prefab));
                    return;
                }

                TechnologyPrefab? technology = TechnologyPrefab.Prefabs.Find(prefab => prefab.Name == args[0]);
                if (technology == null && !TechnologyPrefab.Prefabs.TryGet(args[0], out technology))
                {
                    DebugConsole.NewMessage($"Technology with name or identifier \"{args[0]}\" not found.");
                    return;
                }

                researchManager.Unlock(technology!);

                DebugConsole.NewMessage($"Unlocked {technology!.Identifier}");
            }
        );

        #endregion

        HookService.RegisterHook<PluginLocationSaveDelegate>(PersistentOutpostData.SaveLocation);
        HookService.RegisterHook<PluginLocationLoadDelegate>(PersistentOutpostData.LoadLocation);

        HookService.RegisterHook<PluginRoundStartedDelegate>(OutpostDwellerManager.RoundStarted);

        HookService.RegisterHook<PluginCanSwapItemDelegate>(OutpostBuilder.CanSwapSubmarineItem);

        HookService.RegisterHook<PluginCampaignMapGenerationFinishedDelegate>(LocationShortcutManager.GenerateShortcuts);
        HookService.RegisterHook<PluginOnProgressWorldDelegate>(EnableDescendantsSpreadingAction.SpreadDescendants);
        EnableDescendantsSpreadingAction.RegisterDebugCommands();

        HookService.RegisterHook<PluginModifyRequiredNPCConversationFlags>(NPCConversationsCustom.AddRequiredConversationFlags);
        HookService.RegisterHook<PluginModifyCharacterSpecificNPCConversationFlags>(NPCConversationsCustom.AddCharacterSpecificConversationFlags);

        ContentFileRegistrar.RegisterContentFile<ExampleContentFile>();
        ContentFileRegistrar.RegisterContentFile<EconomySettingsFile>();
        ContentFileRegistrar.RegisterContentFile<OutpostResourcesFile>();
        ContentFileRegistrar.RegisterContentFile<PurchasableOutpostModulesFile>();
        ContentFileRegistrar.RegisterContentFile<TechnologyFile>();
        ContentFileRegistrar.RegisterContentFile<TechnologyTreeFile>();
        ContentFileRegistrar.RegisterContentFile<TechnologyRecipeUnlockFile>();
        ContentFileRegistrar.RegisterContentFile<OutpostSwappableInfoFile>();
        ContentFileRegistrar.RegisterContentFile<OutpostCatalogFile>();

        EventActionRegistrar.RegisterEventAction<SetOutpostOwnershipAction>();
        EventActionRegistrar.RegisterEventAction<OutpostCeasefireAction>();
        EventActionRegistrar.RegisterEventAction<CreateShortcutAction>();
        EventActionRegistrar.RegisterEventAction<EnableShortcutNetworkAction>();
        EventActionRegistrar.RegisterEventAction<EnableDescendantsSpreadingAction>();
        EventActionRegistrar.RegisterEventAction<MissionActionExtended>();
        EventActionRegistrar.RegisterEventAction<ModifyResourceAction>();
        EventActionRegistrar.RegisterEventAction<CheckResourceAction>();
        EventActionRegistrar.RegisterEventAction<ConvertNPCToDwellerAction>();
        EventActionRegistrar.RegisterEventAction<RemoveNPCDwellerAction>();
        EventActionRegistrar.RegisterEventAction<DwellerTagAction>();
        EventActionRegistrar.RegisterEventAction<SetDwellerUnavailableAction>();
        EventActionRegistrar.RegisterEventAction<CheckWorkstationAction>();
        EventActionRegistrar.RegisterEventAction<WaitForPlayerOwnedOutpostAction>();
        EventActionRegistrar.RegisterEventAction<OpenOutpostManagementUIAction>();
        EventActionRegistrar.RegisterEventAction<OpenCampaignInteractionAction>();
        EventActionRegistrar.RegisterEventAction<OutpostStatusConversationAction>();
        EventActionRegistrar.RegisterEventAction<CollectOutpostMoneyAction>();
        EventActionRegistrar.RegisterEventAction<CleanupSubmarineItemsAction>();
        EventActionRegistrar.RegisterEventAction<CheckModuleConstructionAction>();
        EventActionRegistrar.RegisterEventAction<UnlockTechnologyAction>();
        EventActionRegistrar.RegisterEventAction<CheckTechnologyAction>();
        EventActionRegistrar.RegisterEventAction<DlcEndingAction>();
        EventActionRegistrar.RegisterEventAction<TryTriggerResourceMissionAction>();
        EventActionRegistrar.RegisterEventAction<DiscoverLocationAction>();
        EventActionRegistrar.RegisterEventAction<UnlockMissionToUnvisitedLocationAction>();

        AIObjectiveRegistrar.RegisterObjectiveFactory(new AIObjectiveRanchingSpecificPenFactory());

        GameNetwork.RegisterNetworkHeaders<NetworkHeaders>();
        GameNetwork.RegisterAllStaticHandlers();



        OutpostBuilder.Init();
        OpenOutpostManagementUIAction.Init();
        OpenCampaignInteractionAction.Init();
        OutpostConversationActionBase.Init();

        ItemComponentRegistrar.RegisterAllItemComponents();

        InitProjectSpecific();
        ConstructorHelper.RunConstructors();

        GCHelper.RegisterForWatch(this, "Home & Harbor plugin");
    }

    public void OnContentLoaded()
    {
        foreach (var purchasableModule in PurchasableOutpostModulePrefab.Prefabs)
        {
            purchasableModule.InitializeFacilities();
        }

        //rough check for vanilla modules the player can see on the outpost management UI after taking over an outpost
        // - lets make sure those have a PurchaseableModule definition to get them to show correctly
#if DEBUG
        foreach (var sub in SubmarineInfo.SavedSubmarines)
        {
            if (sub.Type != SubmarineType.OutpostModule) { continue; }
            if (sub.OutpostModuleInfo.ModuleFlags.Any(f => f.Contains("Abandoned")) ||
                sub.Name.Contains("Abandoned") ||
                sub.Name.Contains("EndOutpost") ||
                sub.Name.Contains("Hallway") ||
                sub.Name.Contains("Hall4way") ||
                sub.Name.Contains("Alien") ||
                sub.Name.Contains("OutpostCombat") ||
                sub.Name.Contains("Docking_BuddingOutpost") ||
                sub.Name is "Outpost" or "Outpost1" or "Outpost2" or "Outpost3")
            {
                continue;
            }
            if (sub.OutpostModuleInfo.Commonness == 0) { continue; }

            var matchingPurchasableModule = PurchasableOutpostModulePrefab.Prefabs.Find(m => m.OutpostInfo.Name == sub.Name);
            if (matchingPurchasableModule == null)
            {
                DebugConsole.AddWarning(
                    $"There is no PurchasableOutpostModulePrefab for module file {sub.FilePath}." +
                    " This means it will not show correctly in the outpost management UI if an NPC outpost with this module is taken over.");
            }
        }
#endif
    }

    public partial void InitProjectSpecific();

    /// <summary>
    /// A class that prevents the assembly from being unloaded
    /// if it is loaded into the game and the appropriate object
    /// is not cleaned up.
    /// </summary>
    private sealed class UnloadPreventer
    {
        public UnloadPreventer()
            => GCHelper.RegisterForWatch(this);
    }

    private void OnTestUnloadingCommandExecuted(string[] args)
    {
        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError("No campaign running.");
            return;
        }

        campaign.UnloadChecker = new UnloadPreventer();
        DebugConsole.NewMessage("OK", Color.Green);
    }

    private static void OutpostDwellerToCrewExecuted(string[] args)
    {
        if (GameMain.GameSession.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError("No campaign running.");
            return;
        }
        if (args.Length == 0)
        {
            DebugConsole.ThrowError("No outpost dweller specified.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
        var character = outpostData.DwellerManager.SpawnedCharacters.FirstOrDefault(c => c.Name == args[0]);
        if (character == null)
        {
            DebugConsole.ThrowError($"Could not find an outpost dweller with the name {args[0]}.");
            return;
        }
        
        outpostData.DwellerManager.MoveDwellerToCrewMember(character);
    }
    
    private static void CrewToOutpostDwellerExecuted(string[] args)
    {
        if (GameMain.GameSession.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError("No campaign running.");
            return;
        }
        if (args.Length == 0)
        {
            DebugConsole.ThrowError("No crew member specified.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation);
        var character = GameSession.GetSessionCrewCharacters(CharacterType.Both).FirstOrDefault(c => c.Name == args[0]);
        if (character == null)
        {
            DebugConsole.ThrowError($"Could not find a crew member with the name {args[0]}.");
            return;
        }
        
        outpostData.DwellerManager.MoveCrewMemberToDweller(character);
    }

    private static void SetResourceExecuted(string[] args)
    {
        if (GameMain.GameSession.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError("No campaign running.");
            return;
        }
        if (args.Length == 0)
        {
            DebugConsole.ThrowError("No resource specified.");
            return;
        }
        if (args.Length < 2)
        {
            DebugConsole.ThrowError("No amount specified.");
            return;
        }

        Location? location = campaign.CurrentLocation;
        if (args.Length == 3)
        {
            location = campaign.Map.Locations.FirstOrDefault(l => l.DisplayName.Value.Equals(args[2], StringComparison.OrdinalIgnoreCase));
            if (location == null)
            {
                DebugConsole.ThrowError($"Could not find a location with the name {args[2]}.");
                return;
            }
        }

        if (!float.TryParse(args[1], out float amount))
        {
            DebugConsole.ThrowError($"\"{args[1]}\" is not a valid resource amount.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (outpostData.ResourceManager == null)
        {
            DebugConsole.ThrowError($"Location {location.DisplayName} has no resource manager.");
            return;
        }
        outpostData.ResourceManager.SetResource(args[0].ToIdentifier(), amount, createNetworkEvent: true);
    }

    private static void OutpostWalletExecuted(string[] args)
    {
        if (GameMain.GameSession.Campaign is CampaignMode campaign &&
            campaign.CurrentLocation is Location location &&
            PersistentOutpostData.GetPersistentOutpostData(campaign.CurrentLocation) is PersistentOutpostData outpostData)
        {
            DebugConsole.NewMessage($"Outpost wallet balance at {location.DisplayName}: {outpostData.OutpostWalletBalance} mk", Color.LightGreen);
        }
    }

    private void OnAssignDwellerToWorkstationCommandExecuted(string[] args)
    {
        if (args.Length == 0)
        {
            DebugConsole.ThrowError("No character argument given.");
            return;
        }
        var character = Character.CharacterList.FirstOrDefault(c => c.Name == args[0]);
        if (character == null)
        {
            DebugConsole.ThrowError($"Character {args[0]} not found.");
            return;
        }
        if (args.Length == 1)
        {
            DebugConsole.ThrowError("Work station name not given.");
            return;
        }
        var matchingWorkstations =
            Item.ItemList
                .Where(i => i.GetComponent<WorkstationComponent>() != null)
                .Where(it => it.Prefab.Identifier == args[1] || it.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase));
        if (matchingWorkstations.None())
        {
            DebugConsole.ThrowError("No matching work stations found.");
            return;
        }

        int workstationIndex = 0;
        if (args.Length == 3)
        {
            if (!int.TryParse(args[2], out workstationIndex))
            {
                DebugConsole.ThrowError($"Failed to parse the argument {args[2]} as an index.");
            }
        }
        var targetWorkstation = matchingWorkstations.ElementAtOrDefault(workstationIndex);
        if (targetWorkstation == null)
        {
            DebugConsole.ThrowError($"Could not find a work station with the name {args[1]} and index {args[2]}");
            return;
        }

        targetWorkstation.GetComponent<WorkstationComponent>().AssignCharacter(character);
    }

    public void Dispose()
    {
#if CLIENT
        OutpostManagementUI.Instance = null;
#endif
        DebugConsole.NewMessage("DLC unloaded", Color.Red);
    }
}
