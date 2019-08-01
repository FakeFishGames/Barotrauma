#define SERVER_IS_TRAITOR
#define ALLOW_SOLO_TRAITOR
using Barotrauma.Networking;
using Lidgren.Network;
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

                foreach (var character in Character.CharacterList)
                {
                    characters.Add(character);
                }
#if SERVER_IS_TRAITOR
                if (server.Character != null)
                {
                    traitorCandidates.Add(server.Character);
                }
                else
#endif
                {
                    traitorCandidates.AddRange(server.ConnectedClients.ConvertAll(client => client.Character));
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
                }
                Update(0.0f);
                foreach (var traitor in Traitors.Values)
                {
                    traitor.Greet(server, CodeWords, CodeResponse);
                }
#if SERVER
                // SendObjectiveMessage(server);
                foreach(var traitor in Traitors.Values)
                {
                    GameServer.Log(string.Format("{0} is the traitor and the current goals are:\n{1}", traitor.Character.Name, traitor.CurrentObjective?.GoalInfos != null ? TextManager.GetServerMessage(traitor.CurrentObjective?.GoalInfos) : "(empty)"), ServerLog.MessageType.ServerMessage);
                }
#endif
            }

            public virtual void Update(float deltaTime)
            {
                int previousCompletedCount = completedObjectives.Count;
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
                        continue;
                    }
                    break;
                }
                for (int i = previousCompletedCount; i < completedObjectives.Count; ++i)
                {
                    var objective = completedObjectives[i];
                    objective.End(GameMain.Server);
                }
            }

            public delegate bool CharacterFilter(Character character);
            public Character FindKillTarget(GameServer server, Character traitor, CharacterFilter filter)
            {
                int charactersCount = Character.CharacterList.Count;
                int targetIndex = Rand.Int(charactersCount);
                for (int i = 0; i < charactersCount; ++i)
                {
                    var character = Character.CharacterList[(targetIndex + i) % charactersCount];
                    if (character != null && character != traitor && (filter == null || filter(character)))
                    {
                        return character;
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
