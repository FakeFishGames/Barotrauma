#define SERVER_IS_TRAITOR
#define ALLOW_SOLO_TRAITOR
using Barotrauma.Networking;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    partial class Traitor
    {
        public class TraitorMission
        {
            private static string wordsTxt = Path.Combine("Content", "CodeWords.txt");

            private readonly List<Objective> allObjectives = new List<Objective>();
            private readonly List<Objective> pendingObjectives = new List<Objective>();
            private readonly List<Objective> completedObjectives = new List<Objective>();

            public virtual bool IsCompleted => pendingObjectives.Count <= 0;

            public readonly Dictionary<string, Traitor> Traitors = new Dictionary<string, Traitor>();

            public string StartText { get; private set; }
            public string CodeWords { get; private set; }
            public string CodeResponse { get; private set; }

            // TODO(xxx): Mission start, end messages

            public Objective GetCurrentObjective(Traitor traitor)
            {
                return pendingObjectives.Count > 0 ? pendingObjectives[0] : null;
            }

            public virtual void Start(GameServer server, params string[] traitorRoles)
            {
                List<Character> characters = new List<Character>(); //ANYONE can be a target.
                List<Character> traitorCandidates = new List<Character>(); //Keep this to not re-pick traitors twice

                foreach (Client client in server.ConnectedClients)
                {
                    if (client.Character != null)
                    {
                        characters.Add(client.Character);
#if !SERVER_IS_TRAITOR
                        if (server.Character == null)
#endif
                        {
                            traitorCandidates.Add(client.Character);
                        }
                    }
                }

                if (server.Character != null)
                {
                    characters.Add(server.Character); //Add host character
                    traitorCandidates.Add(server.Character);
                }
#if !ALLOW_SOLO_TRAITOR
                if (characters.Count < 2)
                {
                    return;
                }
#endif
                CodeWords = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
                CodeResponse = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);

                foreach (var role in traitorRoles)
                {

                    int traitorIndex = Rand.Int(traitorCandidates.Count);
                    Character traitorCharacter = traitorCandidates[traitorIndex];
                    traitorCandidates.Remove(traitorCharacter);

                    var traitor = new Traitor(this, role, traitorCharacter);
                    Traitors.Add(role, traitor);
                    traitor.Greet(server, CodeWords, CodeResponse);
                }
            }

            public virtual void Update(float deltaTime)
            {
                while (pendingObjectives.Count > 0)
                {
                    var objective = pendingObjectives[0];
                    if (!objective.IsStarted)
                    {
                        if (!objective.Start(GameMain.Server, Traitors["traitor"]))
                        {
                            pendingObjectives.RemoveAt(0);
                            completedObjectives.Add(objective);
                            continue;
                        }
                    }
                    objective.Update(deltaTime);
                    if (objective.IsCompleted)
                    {
                        pendingObjectives.RemoveAt(0);
                        completedObjectives.Add(objective);
                        objective.End(GameMain.Server);
                        continue;
                    }
                    break;
                }
            }

            public delegate bool CharacterFilter(Character character);
            public Character FindKillTarget(GameServer server, Character traitor, CharacterFilter filter)
            {
                int connectedClientsCount = server.ConnectedClients.Count;
                int targetIndex = Rand.Int(connectedClientsCount);
                for (int i = 0; i < connectedClientsCount; ++i)
                {
                    var client = server.ConnectedClients[(targetIndex + i) % connectedClientsCount];
                    if (client.Character != null && client.Character != traitor && (filter == null || filter(client.Character)))
                    {
                        return client.Character;
                    }
                }
#if ALLOW_SOLO_TRAITOR
                return traitor;
#else
                return null;
#endif
            }

            public TraitorMission(string startText, params Objective[] objectives)
            {
                StartText = startText;
                allObjectives.AddRange(objectives);
                pendingObjectives.AddRange(objectives);
            }
        }
    }
}
