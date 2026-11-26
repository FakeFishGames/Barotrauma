using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal partial class OutpostDwellerManager : ISerializableEntity
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, WorkstationAssignmentData>(Plugin.NetworkHeaders.AssignDwellerToWorkstation, ServerReadAssignDwellerToWorkstation);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, MoveDwellerToCrewData>(Plugin.NetworkHeaders.MoveDwellerToCrew, ServerReadMoveDwellerToCrew);
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, MoveCrewToDwellerData>(Plugin.NetworkHeaders.MoveCrewToDweller, ServerReadMoveCrewToDweller);
    }

    private static bool IsClientAllowedToManageDwellers(Client? client, PersistentOutpostData outpostData, bool throwErrorMessage)
    {
        if (client == null)
        {
            if (throwErrorMessage)
            {
                DebugConsole.ThrowError($"Failed to check for permissions to manage dwellers. Client was null.\n" + Environment.StackTrace);
            }
            return false;
        }

        if (!outpostData.IsPlayerOwned)
        {
            if (throwErrorMessage)
            {
                DebugConsole.ThrowError($"Client {client.Name} attempted to manage dwellers. Not allowed, because the outpost is not player-owned.");
            }
            return false;
        }

        if (!OutpostBuilder.AllowedToManageOutpost(client))
        {
            if (throwErrorMessage)
            {
                DebugConsole.ThrowError($"Client {client.Name} attempted to manage dwellers, but doesn't have the appropriate permissions.");
            }
            return false;
        }
        return true;
    }

    private static void ServerReadAssignDwellerToWorkstation(WorkstationAssignmentData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location)
        {
            DebugConsole.ThrowError($"Failed to assign dweller to workstation: start location is null.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (!IsClientAllowedToManageDwellers(client, outpostData, throwErrorMessage: true))
        {
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to assign a dweller to workstation, but could not find a character with the ID {data.CharacterID}.");
            return;
        }

        var dwellerInfo = outpostData.DwellerManager.FindDwellerInfo(character);
        if (dwellerInfo == null)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to assign a character to workstation, but the character {character.DisplayName} is not a dweller.");
            return;
        }

        // Actually perform the assignment (this will broadcast to all clients)
        outpostData.DwellerManager.AssignCharacterToWorkstation(character, data.WorkstationId, client: client);
    }

    private static void ServerReadMoveDwellerToCrew(MoveDwellerToCrewData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location)
        {
            DebugConsole.ThrowError($"Failed to move dweller to crew: start location is null.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (!IsClientAllowedToManageDwellers(client, outpostData, throwErrorMessage: true))
        {
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move a dweller to crew, but could not find a character with the ID {data.CharacterID}.");
            return;
        }

        var dwellerInfo = outpostData.DwellerManager.FindDwellerInfo(character);
        if (dwellerInfo == null)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move a character to crew, but the character {character.DisplayName} is not a dweller.");
            return;
        }

        // Actually perform the move (this will broadcast to all clients via RemoveDweller)
        outpostData.DwellerManager.MoveDwellerToCrewMember(character);
        GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.clientmoveddwellertocrew",
            ("[dwellername]", character.DisplayName),
            ("[client]", NetworkMember.ClientLogName(client))));
    }

    private static void ServerReadMoveCrewToDweller(MoveCrewToDwellerData data, Client client)
    {
        if (GameMain.GameSession?.StartLocation is not { } location)
        {
            DebugConsole.ThrowError($"Failed to move crew to dweller: start location is null.");
            return;
        }

        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        if (!IsClientAllowedToManageDwellers(client, outpostData, throwErrorMessage: true))
        {
            return;
        }

        if (Entity.FindEntityByID(data.CharacterID) is not Character character)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move crew to dweller, but could not find a character with the ID {data.CharacterID}.");
            return;
        }

        if (character.IsPlayer)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move {character.DisplayName} from the crew to a dweller. Cannot move player characters to dwellers.");
            return;
        }

        var crewCharacters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
        if (crewCharacters.Count == 1)
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move {character.DisplayName} from the crew to a dweller. Cannot move the last character from the crew.");
            return;
        }

        // Check if character is actually a crew member
        if (!crewCharacters.Contains(character))
        {
            DebugConsole.ThrowError($"Client {client.Name} attempted to move a character to dweller, but the character {character.DisplayName} is not a crew member.");
            return;
        }

        // Actually perform the move (this will broadcast to all clients)
        outpostData.DwellerManager.MoveCrewMemberToDweller(character); 
        GameMain.Server?.LogAndSendToChat(TextManager.GetWithVariables("outpostbuilderui.clientmovedcrewtodweller",
            ("[dwellername]", character.DisplayName),
            ("[client]", NetworkMember.ClientLogName(client))));
    }
}