#nullable enable
using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

internal static partial class OutpostBuilder
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, AddOutpostModuleData>(Plugin.NetworkHeaders.AddOutpostModule, ServerReadAddOutpostModule);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, RemoveOutpostModuleData>(Plugin.NetworkHeaders.RemoveOutpostModule, ServerReadRemoveOutpostModule);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, MoveOutpostModuleData>(Plugin.NetworkHeaders.MoveOutpostModule, ServerReadMoveOutpostModule);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, MovePendingOutpostModuleData>(Plugin.NetworkHeaders.MovePendingModule, ServerReadMovePendingOutpostModule);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, RemoveOutpostModuleData>(Plugin.NetworkHeaders.CancelOutpostModuleRemoval, ServerReadCancelOutpostModuleRemoval);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, CancelOutpostModuleAdditionData>(Plugin.NetworkHeaders.CancelOutpostModuleAddition, ServerReadCancelOutpostModuleAddition);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, RenameOutpostData>(Plugin.NetworkHeaders.RenameOutpost, ServerReadRenameOutpost);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, RequestCatalog>(Plugin.NetworkHeaders.RequestCatalog, ServerReadRequestCatalog);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, SaveAndReloadCurrentLocation>(Plugin.NetworkHeaders.SaveAndReloadLevel, ServerReadSaveAndReloadLevel);
    }

    internal static bool AllowedToManageOutpost(Client client)
    {
        return CampaignMode.AllowedToManageCampaign(client, ClientPermissions.ManageCampaign);
    }

    [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.SwapOutpostItem)]
    public static void SwapOutpostItem(SwapOutpostItemData data, Client client)
    {
        if (!AllowedToManageOutpost(client))
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to upgrade an outpost item, but doesn't have the appropriate permissions.");
            return;
        }

        if (GameMain.GameSession?.Campaign is not MultiPlayerCampaign mpCampaign)
        {
            DebugConsole.ThrowError("Failed to upgrade outpost item: Campaign not active.");
            return;
        }

        if (!ItemSwapStorage.TryGetItemSwapStorageForCampaign(mpCampaign, out ItemSwapStorage? storage))
        {
            DebugConsole.ThrowError("Failed to upgrade outpost item: Campaign storage not found.");
            return;
        }

        if (GameMain.GameSession.StartLocation is not { } location)
        {
            DebugConsole.ThrowError("Failed to upgrade outpost item: No start location or outpost found.");
            return;
        }

        PersistentOutpostData outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (!outpostData.IsPlayerOwned)
        {
            DebugConsole.ThrowError("Failed to upgrade outpost item: The outpost is not player-owned.");
            return;
        }

        Entity? entity = Entity.FindEntityByID(data.ItemId);
        if (entity is not Item item)
        {
            DebugConsole.ThrowError($"Failed to upgrade outpost item with ID {data.ItemId} to prefab {data.NewPrefab}: Item not found.");
            return;
        }

        ItemPrefab? newPrefab = ItemPrefab.Find(null, data.NewPrefab);
        if (newPrefab is null)
        {
            DebugConsole.ThrowError($"Failed to upgrade outpost item with ID {data.ItemId} to prefab {data.NewPrefab}. Prefab not found.");
            return;
        }

        if (!IsItemSwapAvailable(newPrefab, out var _))
        {
            DebugConsole.ThrowError($"Failed to upgrade outpost item with ID {data.ItemId} to prefab {data.NewPrefab}. Missing technology.");
            return;
        }

        if (!mpCampaign.TryPurchase(client, GetPriceForItemSwap(newPrefab)))
        {
            DebugConsole.ThrowError($"Failed to upgrade outpost item with ID {data.ItemId} to prefab {data.NewPrefab}. Not enough funds.");
            return;
        }

        storage.TryDeductItem(newPrefab);
        storage.StoreItem(item.Prefab);

        item.ReplaceWithLinkedItems(newPrefab);
    }

    private static bool IsClientAllowedToModifyOutpost(Client? client, PersistentOutpostData outpostData, bool throwErrorMessage)
    {
        if (client == null)
        {
            DebugConsole.ThrowError($"Failed to check for permissions to modify the outpost. Client was null.\n" + Environment.StackTrace);
            return false;
        }

        if (!outpostData.IsPlayerOwned)
        {
            if (throwErrorMessage)
            {
                DebugConsole.ThrowError($"Client {client.Name} attempted to modify the outpost. Not allowed, because the outpost is not player-owned.");
            }
            return false;
        }

        if (!AllowedToManageOutpost(client))
        {
            if (throwErrorMessage)
            {
                DebugConsole.ThrowError($"Client {client.Name} attempted to add a new outpost module, but doesn't have the appropriate permissions.");
            }
            return false;
        }
        return true;
    }

    private static void ServerReadRenameOutpost(RenameOutpostData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        string prevName = location.DisplayName.Value;

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true) || 
            !location.TryForceRawName(data.Name))
        {
            //send to client to correct the value at their end
            outpostData.ServerWriteState(client);
            return;
        }

        //send new name to everyone
        GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.outpostrenamedbyclient", 
            ("[oldname]", prevName), 
            ("[newname]", location.DisplayName), 
            ("[client]", NetworkMember.ClientLogName(client)))
            .Value);
        //send new name to everyone
        outpostData.ServerWriteState();
    }

    /// <summary>
    /// Is the client currently allowed to save and reload the level (adding any ready-to-build modules and removing modules marked for removal)?
    /// </summary>
    public static bool AllowSaveAndReloadSameLocation(Client client)
    {
        return AllowedToManageOutpost(client) && HasOutpostModulesReadyToBuildOrRemove();
    }

    private static void ServerReadSaveAndReloadLevel(SaveAndReloadCurrentLocation _, Client sender)
    {
        if (GameMain.GameSession?.GameMode is not MultiPlayerCampaign campaign) { return; }
        if (AllowSaveAndReloadSameLocation(sender))
        {
            campaign.SaveAndReloadLevel();
        }
    }

    private static void ServerReadRequestCatalog(RequestCatalog data, Client client)
    {
        if (client.Character == null) { return; }
        if (Level.Loaded.StartOutpost is not { } outpost) { return; }

        if (!OutpostCatalogPrefab.Prefabs.TryGet(data.CatalogIdentifier, out OutpostCatalogPrefab? catalogPrefab))
        {
            return;
        }

        if (CanSpawnOrEquipCatalogForCharacter(outpost, client.Character, catalogPrefab) != CantSpawnCatalogReason.None) { return; }

        SpawnOrEquipCatalogForCharacter(client.Character, catalogPrefab);
    }

    private static void ServerReadAddOutpostModule(AddOutpostModuleData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true))
        {
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        if (!PurchasableOutpostModulePrefab.Prefabs.TryGet(data.OutpostModuleIdentifier, out var module))
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to add a new outpost module, but could not find a module with the identifier {data.OutpostModuleIdentifier}.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (Entity.FindEntityByID(data.targetGapId) is not Gap targetGap)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to add a new outpost module, but could not find a gap with the id {data.targetGapId}.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (data.thisGapIndex < 0 || data.thisGapIndex >= module.Gaps.Count || module.Gaps[data.thisGapIndex] is not { } thisGap)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to add a new outpost module, but could not find a gap with the index {data.thisGapIndex} in the module {module}.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        if (TryPlaceModule(outpostData, module, thisGap, targetGap, data.hallwayLength, createNetworkEvent: true, client))
        {
            GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.addmodule",
                ("[modulename]", module.OutpostInfo.DisplayName),
                ("[client]", NetworkMember.ClientLogName(client))));
        }
        else
        {
            //sync anyway to correct the data client-side
            ServerWriteOutpostModuleChanges();
        }
    }

    private static void ServerReadRemoveOutpostModule(RemoveOutpostModuleData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true))
        {
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (outpost.OutpostModules.FirstOrDefault(m => m.Index == data.ModuleIndex) is not PlacedModule placedModule)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to remove an outpost module. Could not find a module with the index {data.ModuleIndex}.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        RemoveModule(outpostData, placedModule, createNetworkEvent: true);
        GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.removemodule",
            ("[modulename]", placedModule.Info.DisplayName),
            ("[client]", NetworkMember.ClientLogName(client))));
    }

    private static void ServerReadMoveOutpostModule(MoveOutpostModuleData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true))
        {
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (outpost.OutpostModules.FirstOrDefault(m => m.Index == data.ModuleIndex) is not PlacedModule movingModule)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move an outpost module. Could not find a module with the index {data.ModuleIndex}.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (!MathUtils.IsValid(data.newOffset))
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move an outpost module. Offset {data.newOffset} is invalid.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        if (movingModule.PreviousGap != null && movingModule.PreviousGap.ConnectedDoor == null)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move the module {movingModule.Name}, but there is no door linked to the gap, which most likely means the module is connected via a staircase and should not be movable.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        List<PlacedModule> modulesToMove = [];
        List<PendingOutpostModule> pendingModulesToMove = [];
        GetModulesToMove(outpost, outpostData, movingModule, ref modulesToMove, ref pendingModulesToMove);

        Vector2 moveAmount = data.newOffset - movingModule.TemporaryMoveOffset;
        if (CanMoveModulesWithoutOverlaps(outpost, modulesToMove, pendingModulesToMove, moveAmount))
        {
            foreach (var moduleToMove in modulesToMove)
            {
                moduleToMove.TemporaryMoveOffset += moveAmount;
            }
        }
        ServerWriteOutpostModuleChanges();

        DebugConsole.NewMessage($"Client {client.Name} moved the outpost module {movingModule.Info.DisplayName}.");
    }

    private static void ServerReadMovePendingOutpostModule(MovePendingOutpostModuleData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true))
        {
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (outpostData.GetPendingModule(data.ModuleIndex) is not { } pendingModule)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move a pending outpost module, but could not find a pending module with the index {data.ModuleIndex} (pending module count: {outpostData.PendingOutpostModules.Count()})");
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (!MathUtils.IsValid(data.hallwayLength))
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move a pending outpost module. Offset {data.hallwayLength} is invalid.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        pendingModule.HallwayLength = data.hallwayLength;
        ServerWriteOutpostModuleChanges(client);
        DebugConsole.NewMessage($"Client {client.Name} moved the outpost module {pendingModule.ModulePrefab?.OutpostInfo.DisplayName ?? "null"}.");
    }

    private static void ServerReadCancelOutpostModuleRemoval(RemoveOutpostModuleData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true))
        {
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (outpost.OutpostModules.FirstOrDefault(m => m.Index == data.ModuleIndex) is not PlacedModule placedModule)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to cancel outpost module removal. Could not find a module with the index {data.ModuleIndex}.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (placedModule.PreviousGap != null && placedModule.PreviousGap.ConnectedDoor == null)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to remove the module {placedModule.Name}, but there is no door linked to the gap, which most likely means the module is connected via a staircase and should not be removable.");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        CancelModuleRemoval(outpostData, placedModule, createNetworkEvent: true);
        DebugConsole.NewMessage($"Client {client.Name} cancelled the removal of the outpost module {placedModule.Info.DisplayName}.");
        GameMain.Server?.LogAndSendToChat($"{NetworkMember.ClientLogName(client)} cancelled the deconstruction of the module \"{placedModule.Info.DisplayName}\".");
    }

    private static void ServerReadCancelOutpostModuleAddition(CancelOutpostModuleAdditionData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        if (!IsClientAllowedToModifyOutpost(client, outpostData, throwErrorMessage: true))
        {
            ServerWriteOutpostModuleChanges(client);
            return;
        }
        if (outpostData.GetPendingModule(data.ModuleIndex) is not { } pendingModule)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to cancel adding an outpost module, but could not find a pending module with the index {data.ModuleIndex} (pending module count: {outpostData.PendingOutpostModules.Count()})");
            ServerWriteOutpostModuleChanges(client);
            return;
        }

        if (TryRemovePendingModule(outpost, outpostData, pendingModule, client, createNetworkEvent: true))
        {
            LocalizedString moduleName = pendingModule.ModulePrefab?.OutpostInfo.DisplayName ?? "null";
            GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.cancelmodule",
                ("[modulename]", moduleName),
                ("[client]", NetworkMember.ClientLogName(client))));
        }
        else
        {
            //sync anyway to correct the data client-side
            ServerWriteOutpostModuleChanges();
        }        
    }

    /// <summary>
    /// Sends the current state of the changes to outpost modules (pending additions and removals) to clients.
    /// </summary>
    private static void ServerWriteOutpostModuleChanges()
    {
        ServerWriteOutpostModuleChanges(GameMain.Server.ConnectedClients);
    }

    private static void ServerWriteOutpostModuleChanges(Client client)
    {
        ServerWriteOutpostModuleChanges(client.ToEnumerable());
    }

    private static void ServerWriteOutpostModuleChanges(IEnumerable<Client> targetClients)
    {
        if (GameMain.GameSession?.StartLocation is not { } location ||
            Level.Loaded?.StartOutpost is not { } outpost) { return; }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        var changesData =
            new OutpostModuleChangesData(
                outpostData.PendingOutpostModules.Select(module => module.ModulePrefab!.Identifier).ToArray(),
                outpostData.PendingOutpostModules.Select(module => module.ThisGapIndex).ToArray(),
                outpostData.PendingOutpostModules.Select(module => module.GapId).ToArray(),
                outpostData.PendingOutpostModules.Select(module => module.HallwayLength).ToArray(),
                outpostData.PendingOutpostModules.Select(module => module.TimeStepsRemainingToBuild).ToArray(),
                outpost.OutpostModules.Select(module => module.TemporaryMoveOffset).ToArray(),
                outpostData.PendingModuleRemovals.ToArray());

        foreach (var client in targetClients)
        {
            GameNetwork.SendToClient(client, Plugin.NetworkHeaders.OutpostModuleChanges, changesData);
        }
    }

    public static void ServerWriteOutpostOwnershipChange(Location location)
    {
        if (GameMain.GameSession?.Campaign?.Map is not { } map) { return; }

        int locationIndex = map.Locations.IndexOf(location);
        if (locationIndex == -1)
        {
            DebugConsole.ThrowError($"Failed to write outpost ownership change: location \"{location.DisplayName}\" not found on the map.");
        }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        var ownershipData = new SetOutpostOwnershipData(locationIndex, outpostData.IsPlayerOwned);
        foreach (var client in GameMain.Server.ConnectedClients)
        {
            GameNetwork.SendToClient(client, Plugin.NetworkHeaders.SetOutpostOwnership, ownershipData);
        }
    }

    public static void ClientJoined(Client client)
    {
        if (Level.Loaded?.LevelData is { Type: LevelData.LevelType.Outpost } &&
            GameMain.GameSession?.Campaign != null)
        {
            ServerWriteOutpostModuleChanges(client.ToEnumerable());
            
            // Send complete outpost state to the newly joined client
            if (GameMain.GameSession?.StartLocation is { } location)
            {
                var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
                outpostData.ServerWriteState(client);
                SendOutpostDwellerData(client, location, outpostData);                
            }
        }
    }
    
    private static void SendOutpostDwellerData(Client client, Location location, PersistentOutpostData outpostData)
    {
        foreach (var dwellerInfo in outpostData.DwellerManager.DwellerInfos)
        {
            if (outpostData.DwellerManager.SpawnedCharacters
                    .FirstOrDefault(c => c.Info == dwellerInfo.CharacterInfo)
                is Character { Removed: false, IsDead: false } character)
            {
                // Send dweller data
                GameNetwork.SendToClient(client,
                    Plugin.NetworkHeaders.AddDweller, 
                    new OutpostDwellerManager.OutpostDwellerData(character));
                
                // Sent for every dweller, not just the unavailable ones, so unavailability having ended gets synced too and before any assignments
                GameNetwork.SendToClient(client,
                    Plugin.NetworkHeaders.SetDwellerWorkAvailability,
                    new OutpostDwellerManager.WorkAvailabilityData(character, dwellerInfo.IsUnavailableForWork, dwellerInfo.UnavailabilityReason));

                // Send workstation assignment, if any
                if (dwellerInfo.AssignedWorkstationId != 0)
                {
                    GameNetwork.SendToClient(client,
                        Plugin.NetworkHeaders.AssignDwellerToWorkstation,
                        new OutpostDwellerManager.WorkstationAssignmentData(character, dwellerInfo.AssignedWorkstationId));
                }
            }
        }
    }    
}

