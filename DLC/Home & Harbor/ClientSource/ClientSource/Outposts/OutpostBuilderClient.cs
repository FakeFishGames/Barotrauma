#nullable enable
using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal static partial class OutpostBuilder
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, OutpostModuleChangesData>(Plugin.NetworkHeaders.OutpostModuleChanges, ClientReadOutpostModuleChanges);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, SetOutpostOwnershipData>(Plugin.NetworkHeaders.SetOutpostOwnership, ClientReadOutpostOwnership);

        HookService.RegisterHook<PluginOnItemReplacedDelegate>(OnSwapItem);
    }

    /// <summary>
    /// Does the player have permissions to manage the outpost (build modules and devices, change the name, etc)
    /// </summary>
    public static bool AllowedToManageOutpost()
    {
        return CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageCampaign);
    }

    /// <summary>
    /// Is the player currently allowed to save and reload the level (adding any ready-to-build modules and removing modules marked for removal)?
    /// </summary>
    public static bool AllowSaveAndReloadLevel()
    {
        return AllowedToManageOutpost() && HasOutpostModulesReadyToBuildOrRemove();
    }

    public static void ClientWriteRenameOutpost(string name)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.RenameOutpost, new RenameOutpostData(name));
    }

    public static void ClientRequestCatalog(OutpostCatalogPrefab catalogType)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.RequestCatalog, new RequestCatalog(catalogType.Identifier));
    }
    
    public static void ClientWriteAddOutpostModule(PurchasableOutpostModulePrefab modulePrefab, PurchasableOutpostModulePrefab.GapPositionInfo thisGap, Gap targetGap, float hallwayLength)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.AddOutpostModule, new AddOutpostModuleData(
            modulePrefab.Identifier,
            modulePrefab.Gaps.IndexOf(thisGap), 
            targetGap.ID, 
            hallwayLength));
    }

    public static void ClientWriteRemoveOutpostModule(PlacedModule module)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.RemoveOutpostModule, new RemoveOutpostModuleData(module.Index));
    }

    public static void ClientWriteMoveOutpostModule(PlacedModule module)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.MoveOutpostModule, new MoveOutpostModuleData(module.Index, module.TemporaryMoveOffset));
    }

    public static void ClientWriteMovePendingOutpostModule(PersistentOutpostData outpostData, PendingOutpostModule module)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.MovePendingModule, new MovePendingOutpostModuleData(outpostData.GetPendingModuleIndex(module), module.HallwayLength));
    }
    public static void ClientWriteCancelOutpostModuleRemoval(PlacedModule module)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.CancelOutpostModuleRemoval, new RemoveOutpostModuleData(module.Index));
    }

    public static void ClientWriteCancelOutpostModuleAddition(PersistentOutpostData outpostData, PendingOutpostModule module)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.CancelOutpostModuleAddition, new CancelOutpostModuleAdditionData(outpostData.GetPendingModuleIndex(module)));
    }

    private static void OnSwapItem(Item oldItem, Item newItem)
    {
        if (oldItem.Submarine is not { Info.IsOutpost: true }) { return; }
        OutpostManagementUI.Instance?.OutpostBuilderUI?.ItemSwapUI.OnItemReplaced(oldItem, newItem);
    }

    private static void ClientReadOutpostModuleChanges(OutpostModuleChangesData data)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded?.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        outpostData.ClearPendingModules(onlyFinishedModules: false);
        outpostData.ClearModuleRemovals();

        for (int i = 0; i < data.PendingModuleIdentifiers.Length; i++)
        {
            Identifier moduleIdentifier = data.PendingModuleIdentifiers[i];
            int thisGapIndex = data.PendingModuleThisGapIndices[i];
            ushort targetGapId = data.PendingModuleTargetGapIds[i];
            float hallwayLength = data.PendingModuleHallwayLengths[i];
            int timeStepsRemainingToBuild = data.TimeStepsRemainingToBuild[i];

            if (!PurchasableOutpostModulePrefab.Prefabs.TryGet(moduleIdentifier, out var module))
            {
                DebugConsole.ThrowError($"Failed to add an outpost module. Could not find a module with the identifier {moduleIdentifier}.");
                continue;
            }
            if (thisGapIndex < 0 || thisGapIndex >= module.Gaps.Count || module.Gaps[thisGapIndex] is not { } thisGap)
            {
                DebugConsole.ThrowError($"Failed to add an outpost module. Could not find a gap with the id {thisGapIndex}.");
                return;
            }
            if (Entity.FindEntityByID(targetGapId) is not Gap targetGap)
            {
                DebugConsole.ThrowError($"Failed to add an outpost module. Could not find a target gap with the id {targetGapId}.");
                return;
            }
            PendingOutpostModule? newModule = outpostData.AddPendingModule(module, thisGap, targetGap);
            if (newModule != null)
            {
                newModule.TimeStepsRemainingToBuild = timeStepsRemainingToBuild;
                newModule.HallwayLength = hallwayLength;
            }
            DebugConsole.NewMessage($"Added the module {module.OutpostInfo.DisplayName}.");
        }

        for (int i = 0; i < data.ModuleOffsets.Length; i++)
        {
            outpost.OutpostModules[i].TemporaryMoveOffset = data.ModuleOffsets[i];
        }

        foreach (int moduleRemovalIndex in data.PendingModuleRemovalIndices)
        {
            if (outpost.OutpostModules.FirstOrDefault(m => m.Index == moduleRemovalIndex) is not PlacedModule placedModule)
            {
                DebugConsole.ThrowError($"Failed to remove an outpost module. Could not find a module with the index {moduleRemovalIndex}.");
                continue;
            }
            outpostData.QueueModuleRemoval(placedModule);
        }

        OutpostManagementUI.RecreateMiniMap();
    }

    private static void ClientReadOutpostOwnership(SetOutpostOwnershipData data)
    {
        if (GameMain.GameSession?.Campaign?.Map is not { } map) { return; }

        if (data.locationIndex < 0 || data.locationIndex >= map.Locations.Count)
        {
            DebugConsole.ThrowError($"Failed to read outpost ownership data: location index {data.locationIndex} out of bounds.");
            return;
        }

        Location location = map.Locations[data.locationIndex];
        //reset previous faction so the faction doesn't try to reclaim the outpost
        location.PreviousFaction = null;
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        outpostData.SetPlayerOwned(data.isPlayerOwned, force: true);
    }
}

