using Barotrauma;

namespace HomeAndHarbor;

internal partial class OutpostDwellerManager : ISerializableEntity
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, OutpostDwellerData>(Plugin.NetworkHeaders.AddDweller, ClientAddDweller);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, OutpostDwellerData>(Plugin.NetworkHeaders.RemoveDweller, ClientRemoveDweller);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, WorkstationAssignmentData>(Plugin.NetworkHeaders.AssignDwellerToWorkstation, ClientAssignDwellerToWorkstation);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, WorkAvailabilityData>(Plugin.NetworkHeaders.SetDwellerWorkAvailability, ClientSetDwellerWorkAvailability);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, MoveDwellerToCrewData>(Plugin.NetworkHeaders.MoveDwellerToCrew, ClientMoveDwellerToCrew);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, MoveCrewToDwellerData>(Plugin.NetworkHeaders.MoveCrewToDweller, ClientMoveCrewToDweller);

        HookService.RegisterHook<PluginMidRoundSyncReceiveStartedDelegate>(OnMidRoundSyncStarted);
    }

    private static void ClientAddDweller(OutpostDwellerData data)
    {
        ClientModifyDweller(data, remove: false);
    }

    private static void ClientRemoveDweller(OutpostDwellerData data)
    {
        ClientModifyDweller(data, remove: true);
    }

    private static void ClientModifyDweller(OutpostDwellerData data, bool remove)
    {
        if (GameMain.GameSession is not { IsRunning: true })
        {
            //don't try to read if a round is not currently running (e.g. if the client is still in the server lobby)
            return;
        }
        if (GameMain.GameSession?.Map is not { } map)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(PersistentOutpostData)}, map is null.");
            return;
        }
        if (GameMain.GameSession?.StartLocation is not { } location || Level.Loaded.StartOutpost is not { } outpost)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(OutpostDwellerData)}, start outpost is null.");
            return;
        }
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(OutpostDwellerData)}, could not find a character with the ID {data.CharacterID}.");
            return;
        }
        if (remove)
        {
            outpostData.DwellerManager.RemoveDweller(character, createNetworkEvent: false, fireUIEvents: true);
        }
        else
        {
            // Make sure character isn't already a dweller to avoid duplicates
            if (outpostData.DwellerManager.FindDwellerInfo(character) == null)
            {
                // Remove from crew first to avoid dual membership during sync
                GameMain.GameSession?.CrewManager?.RemoveCharacter(character, removeInfo: true, changeItemTeams: false, createNetworkEvent: false);

                // Apply the conversion logic client-side to match server state
                ConvertToDwellerGeneric(character, outpostData.DwellerManager, createNetworkEvent: false, fireUIEvents: true);
            }
        }
    }

    private static void ClientAssignDwellerToWorkstation(WorkstationAssignmentData data)
    {
        if (GameMain.GameSession is not { IsRunning: true })
        {
            //don't try to read if a round is not currently running (e.g. if the client is still in the server lobby)
            return;
        }
        if (GameMain.GameSession?.StartLocation is not { } location)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(WorkstationAssignmentData)}, start location is null.");
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(WorkstationAssignmentData)}, could not find a character with the ID {data.CharacterID}.");
            return;
        }
        
        PersistentOutpostData.GetPersistentOutpostData(location).DwellerManager
            .AssignCharacterToWorkstation(character, data.WorkstationId, createNetworkEvent: false);
    }

    private static void ClientSetDwellerWorkAvailability(WorkAvailabilityData data)
    {
        // Don't try to read if a round is not currently running (e.g. if the client is still in the server lobby)
        if (GameMain.GameSession is not { IsRunning: true }) { return; }

        if (GameMain.GameSession.StartLocation is not { } location ||
            PersistentOutpostData.GetPersistentOutpostData(location) is not PersistentOutpostData outpostData)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(WorkAvailabilityData)}, StartLocation is null or has no PersistentOutpostData");
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character ||
            outpostData.DwellerManager.FindDwellerInfo(character) is not { } dwellerInfo)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(WorkAvailabilityData)}, character ID {data.CharacterID} not found or not a dweller of this outpost.");
            return;
        }

        // The server counts the work shifts, the clients only need to know whether the dweller is currently unavailable for work and why
        dwellerInfo.WorkShiftsUnavailable = data.Unavailable ? -1 : 0;
        dwellerInfo.UnavailabilityReason = data.Reason;

        // Refresh the dweller UI
        outpostData.DwellerManager.OnDwellerWorkstationChanged.Invoke(new DwellerChangedEvent(character, null, null));
    }

    private static void ClientWriteAssignDwellerToWorkstation(Character character, int workstationId)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.AssignDwellerToWorkstation, new WorkstationAssignmentData(character, workstationId));
    }

    private static void ClientWriteMoveDwellerToCrew(Character character)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.MoveDwellerToCrew, new MoveDwellerToCrewData(character));
    }

    private static void ClientWriteMoveCrewToDweller(Character character)
    {
        GameNetwork.Send(Plugin.NetworkHeaders.MoveCrewToDweller, new MoveCrewToDwellerData(character));
    }

    private static void ClientMoveDwellerToCrew(MoveDwellerToCrewData data)
    {
        if (GameMain.GameSession is not { IsRunning: true })
        {
            //don't try to read if a round is not currently running (e.g. if the client is still in the server lobby)
            return;
        }

        if (GameMain.GameSession?.StartLocation is not { } location)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(MoveDwellerToCrewData)}, start location is null.");
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(MoveDwellerToCrewData)}, could not find a character with the ID {data.CharacterID}.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        // Remove from dweller list (the crew manager handles adding to crew via its own network message)
        outpostData.DwellerManager.RemoveDweller(character, createNetworkEvent: false, fireUIEvents: false);

        // Apply the crew job the server picked
        if (character.Info != null &&
            JobPrefab.Prefabs.TryGet(data.JobIdentifier, out JobPrefab? jobPrefab) &&
            character.Info.Job?.Prefab != jobPrefab)
        {
            AssignCrewJob(character, jobPrefab);
            if (GameMain.GameSession?.CrewManager is { } crewManager && crewManager.GetCharacterInfos().Contains(character.Info))
            {
                // The crew list entry was already created (with the old job icon) when the crew manager's crew add event arrived, so recreate it correctly now
                crewManager.RemoveCharacterFromCrewList(character);
                crewManager.AddCharacterToCrewList(character);
            }
        }

        outpostData.DwellerManager.OnDwellerRemoved?.Invoke(character);
        outpostData.DwellerManager.OnCharacterAddedToCrew?.Invoke(character);
    }

    private static void ClientMoveCrewToDweller(MoveCrewToDwellerData data)
    {
        if (GameMain.GameSession is not { IsRunning: true })
        {
            //don't try to read if a round is not currently running (e.g. if the client is still in the server lobby)
            return;
        }

        if (GameMain.GameSession?.StartLocation is not { } location)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(MoveCrewToDwellerData)}, start location is null.");
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Failed to read {nameof(MoveCrewToDwellerData)}, could not find a character with the ID {data.CharacterID}.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);

        // Add to dweller list (the crew manager handles removing from crew via its own network message)
        if (outpostData.DwellerManager.FindDwellerInfo(character) == null)
        {
            // Apply the conversion logic client-side to match server state
            ConvertToDwellerGeneric(character, outpostData.DwellerManager, createNetworkEvent: false, fireUIEvents: false);
        }

        outpostData.DwellerManager.OnCharacterRemovedFromCrew.Invoke(character);
        outpostData.DwellerManager.OnDwellerAdded.Invoke(character);
    }

    private static void OnMidRoundSyncStarted()
    {
        // Clear all workstation assignments when midround sync starts, the server will send fresh data

        foreach (var workstation in WorkstationComponent.WorkstationComponents.ToList())
        {
            if (workstation.AssignedNPCCount > 0)
            {
                workstation.ClearAllAssignments();
            }
        }
        
        if (GameMain.GameSession?.StartLocation is { } location)
        {
            var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
            foreach (var dwellerInfo in outpostData.DwellerManager.DwellerInfos.ToList())
            {
                if (dwellerInfo.AssignedWorkstationId != 0)
                {
                    dwellerInfo.AssignedWorkstationId = 0;
                }
                dwellerInfo.ClearWorkUnavailability();
            }
        }
    }
}
