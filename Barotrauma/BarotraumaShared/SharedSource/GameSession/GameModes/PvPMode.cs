using Barotrauma.Extensions;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class PvPMode : MissionMode
    {
        public PvPMode(GameModePreset preset, IEnumerable<MissionPrefab> missionPrefabs) : base(preset, ValidateMissionPrefabs(missionPrefabs, MissionPrefab.PvPMissionClasses)) { }

        public PvPMode(GameModePreset preset, MissionType missionType, string seed) : base(preset, ValidateMissionType(missionType, MissionPrefab.PvPMissionClasses), seed) { }

        public void AssignTeamIDs(IEnumerable<Client> clients)
        {
            int team1Count = 0, team2Count = 0;
            //if a client has a preference, assign them to that team
            List<Client> unassignedClients = new List<Client>(clients);
            for (int i = 0; i < unassignedClients.Count; i++)
            {
                if (unassignedClients[i].PreferredTeam == CharacterTeamType.Team1 ||
                    unassignedClients[i].PreferredTeam == CharacterTeamType.Team2)
                {
                    assignTeam(unassignedClients[i], unassignedClients[i].PreferredTeam);
                    i--;
                }
            }
            
            //assign the rest of the clients to the team that has the least players
            while (unassignedClients.Any())
            {
                var randomClient = unassignedClients.GetRandom(Rand.RandSync.Unsynced);
                assignTeam(randomClient, team1Count < team2Count ? CharacterTeamType.Team1 : CharacterTeamType.Team2);
            }

            void assignTeam(Client client, CharacterTeamType team)
            {
                client.TeamID = team;
                unassignedClients.Remove(client);
                if (team == CharacterTeamType.Team1)
                {
                    team1Count++;
                }
                else
                {
                    team2Count++;
                }
            }
        }
    }
}
