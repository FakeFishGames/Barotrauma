//#define SERVER_IS_TRAITOR
//#define ALLOW_SOLO_TRAITOR

using System;
using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class Traitor
    {
        public class TraitorMission
        {
            private static System.Random random = null;

            public static void InitializeRandom() => random = new System.Random((int)DateTime.UtcNow.Ticks);

            // All traitor related functionality should use the following interface for generating random values
            public static int Random(int n) => random.Next(n);

            // All traitor related functionality should use the following interface for generating random values
            public static double RandomDouble() => random.NextDouble();

            private static string wordsTxt = Path.Combine("Content", "CodeWords.txt");

            private readonly List<Objective> allObjectives = new List<Objective>();
            private readonly List<Objective> pendingObjectives = new List<Objective>();
            private readonly List<Objective> completedObjectives = new List<Objective>();

            public virtual bool IsCompleted => pendingObjectives.Count <= 0;

            public readonly Dictionary<string, Traitor> Traitors = new Dictionary<string, Traitor>();

            public readonly List<string> Roles = new List<string>();

            public string StartText { get; private set; }
            public string CodeWords { get; private set; }
            public string CodeResponse { get; private set; }

            public string GlobalEndMessageSuccessTextId { get; private set; }
            public string GlobalEndMessageSuccessDeadTextId { get; private set; }
            public string GlobalEndMessageSuccessDetainedTextId { get; private set; }
            public string GlobalEndMessageFailureTextId { get; private set; }
            public string GlobalEndMessageFailureDeadTextId { get; private set; }
            public string GlobalEndMessageFailureDetainedTextId { get; private set; }

            public virtual IEnumerable<string> GlobalEndMessageKeys => new string[] { "[traitorname]", "[traitorgoalinfos]" };
            public virtual IEnumerable<string> GlobalEndMessageValues {
                get {
                    var isSuccess = completedObjectives.Count >= allObjectives.Count;
                    return new string[] {
                        string.Join(", ", Traitors.Values.Select(traitor => traitor.Character?.Name ?? "(unknown)")),
                        (isSuccess ? completedObjectives.LastOrDefault() : pendingObjectives.FirstOrDefault())?.GoalInfos ?? ""
                    };
                }
            }

            public string GlobalEndMessage
            {
                get
                {
                    if (Traitors.Any() && allObjectives.Count > 0)
                    {

                        TextManager.JoinServerMessages("\n",
                            Traitors.Values.Select(traitor =>
                            {
                                var isSuccess = completedObjectives.Count >= allObjectives.Count;
                                var traitorIsDead = traitor.Character.IsDead;
                                var traitorIsDetained = traitor.Character.LockHands;
                                var messageId = isSuccess
                                    ? (traitorIsDead ? GlobalEndMessageSuccessDeadTextId : traitorIsDetained ? GlobalEndMessageSuccessDetainedTextId : GlobalEndMessageSuccessTextId)
                                    : (traitorIsDead ? GlobalEndMessageFailureDeadTextId : traitorIsDetained ? GlobalEndMessageFailureDetainedTextId : GlobalEndMessageFailureTextId);
                                return TextManager.FormatServerMessageWithGenderPronouns(traitor.Character?.Info?.Gender ?? Gender.None, messageId, GlobalEndMessageKeys.ToArray(), GlobalEndMessageValues.ToArray());
                            }).ToArray());
                    }
                    return "";
                }
            }

            public Objective GetCurrentObjective(Traitor traitor)
            {
                if (!Traitors.ContainsValue(traitor) || pendingObjectives.Count <= 0)
                {
                    return null;
                }
                return pendingObjectives.Find(objective => objective.Roles.Contains(traitor.Role));
            }

            protected List<Tuple<Client, Character>> FindTraitorCandidates(GameServer server, Character.TeamType team, ICollection<string> traitorRoles)
            {
                // TODO(xxx): Traitor role specific conditions should be taken into account here.
                var traitorCandidates = new List<Tuple<Client, Character>>();
#if SERVER_IS_TRAITOR
                if (server.Character != null)
                {
                    traitorCandidates.Add(server.Character);
                }
                else
#endif
                {
                    traitorCandidates.AddRange(server.ConnectedClients.FindAll(c => c.Character != null && !c.Character.IsDead && (team == Character.TeamType.None || c.Character.TeamID == team)).ConvertAll(client => Tuple.Create(client, client.Character)));
                }
                return traitorCandidates;
            }

            protected List<Character> FindCharacters()
            {
                List<Character> characters = new List<Character>();
                foreach (var character in Character.CharacterList)
                {
                    characters.Add(character);
                }
                return characters;
            }

            public virtual bool CanBeStarted(GameServer server, TraitorManager traitorManager, Character.TeamType team)
            {
                var traitorCandidates = FindTraitorCandidates(server, team, Roles);
                if (traitorCandidates.Count < Roles.Count)
                {
                    return false;
                }
                var characters = FindCharacters();
#if !ALLOW_SOLO_TRAITOR
                if (characters.Count < 2)
                {
                    return false;
                }
#endif
                return true;
            }

            public virtual bool Start(GameServer server, TraitorManager traitorManager, Character.TeamType team)
            {
                List<Character> characters = FindCharacters();
#if !ALLOW_SOLO_TRAITOR
                if (characters.Count < 2)
                {
                    return false;
                }
#endif
                var roleCandidates = new Dictionary<string, HashSet<Tuple<Client, Character>>>();
                foreach (var role in Roles)
                {
                    roleCandidates.Add(role, new HashSet<Tuple<Client, Character>>(FindTraitorCandidates(server, team, new[] { role })));
                    if (roleCandidates[role].Count <= 0)
                    {
                        return false;
                    }
                }
                var candidateRoleCounts = new Dictionary<Tuple<Client, Character>, int>();
                foreach (var candidateEntry in roleCandidates)
                {
                    foreach (var candidate in candidateEntry.Value)
                    {
                        candidateRoleCounts[candidate] = candidateRoleCounts.TryGetValue(candidate, out var count) ? count + 1 : 1;
                    }
                }
                var unassignedRoles = new List<string>(roleCandidates.Keys);
                unassignedRoles.Sort((a, b) => roleCandidates[a].Count - roleCandidates[b].Count);
                var assignedCandidates = new List<Tuple<string, Tuple<Client, Character>>>();
                while (unassignedRoles.Count > 0)
                {
                    var currentRole = unassignedRoles[0];
                    var availableCandidates = roleCandidates[currentRole].ToList();
                    if (availableCandidates.Count <= 0)
                    {
                        break;
                    }
                    unassignedRoles.RemoveAt(0);
                    availableCandidates.Sort((a, b) => candidateRoleCounts[b] - candidateRoleCounts[a]);
                    unassignedRoles.Sort((a, b) => roleCandidates[a].Count - roleCandidates[b].Count);

                    int numCandidates = 1;
                    for (int i = 1; i < availableCandidates.Count && candidateRoleCounts[availableCandidates[i]] == candidateRoleCounts[availableCandidates[0]]; ++i)
                    {
                        ++numCandidates;
                    }
                    var selected = TraitorManager.WeightedRandom(availableCandidates, 0, numCandidates, Random, t =>
                    {
                        var previousClient = server.FindPreviousClientData(t.Item1);
                        return Math.Max(
                            previousClient != null ? traitorManager.GetTraitorCount(previousClient) : 0,
                            traitorManager.GetTraitorCount(Tuple.Create(t.Item1.SteamID, t.Item1.Connection?.EndPointString ?? "")));
                    }, (t, c) => { traitorManager.SetTraitorCount(Tuple.Create(t.Item1.SteamID, t.Item1.Connection?.EndPointString ?? ""), c); }, 2, 3);

                    assignedCandidates.Add(Tuple.Create(currentRole, selected));
                    foreach (var candidate in roleCandidates.Values)
                    {
                        candidate.Remove(selected);
                    }
                }
                if (unassignedRoles.Count > 0)
                {
                    return false;
                }
                CodeWords = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
                CodeResponse = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
                Traitors.Clear();
                foreach (var candidate in assignedCandidates)
                {
                    var traitor = new Traitor(this, candidate.Item1, candidate.Item2.Item1.Character);
                    Traitors.Add(candidate.Item1, traitor);
                }

                var messages = new Dictionary<Traitor, List<string>>();
                foreach (var traitor in Traitors.Values)
                {
                    messages.Add(traitor, new List<string>());
                }
                foreach (var traitor in Traitors.Values)
                {
                    traitor.Greet(server, CodeWords, CodeResponse, message => messages[traitor].Add(message));
                }

                messages.ForEach(traitor => traitor.Value.ForEach(message => traitor.Key.SendChatMessage(message)));
                Update(0.0f, GameMain.Server.EndGame);
                messages.ForEach(traitor => traitor.Value.ForEach(message => traitor.Key.SendChatMessageBox(message)));
#if SERVER
                foreach (var traitor in Traitors.Values)
                {
                    GameServer.Log($"{traitor.Character.Name} is a traitor and the current goals are:\n{(traitor.CurrentObjective?.GoalInfos != null ? TextManager.GetServerMessage(traitor.CurrentObjective?.GoalInfos) : "(empty)")}", ServerLog.MessageType.ServerMessage);
                }
#endif
                return true;
            }

            public delegate void TraitorWinHandler();

            public virtual void Update(float deltaTime, TraitorWinHandler winHandler)
            {
                if (pendingObjectives.Count <= 0 || Traitors.Count <= 0)
                {
                    return;
                }
                if (Traitors.Values.Any(traitor => traitor.Character?.IsDead ?? true))
                {
                    Traitors.Values.ForEach(traitor => traitor.UpdateCurrentObjective(""));
                    return;
                }
                var startedObjectives = new List<Objective>();
                foreach (var traitor in Traitors.Values)
                {
                    var previousCompletedCount = completedObjectives.Count;
                    startedObjectives.Clear();
                    while (pendingObjectives.Count > 0)
                    {
                        var objective = GetCurrentObjective(traitor);
                        if (objective == null)
                        {
                            // No more objectives left for traitor or waiting for another traitor's objective.
                            break;
                        }
                        if (!objective.IsStarted)
                        {
                            if (!objective.Start(traitor))
                            {
                                pendingObjectives.Remove(objective);
                                completedObjectives.Add(objective);
                                if (pendingObjectives.Count > 0)
                                {
                                    objective.EndMessage();
                                }
                                continue;
                            }
                            startedObjectives.Add(objective);
                        }
                        objective.Update(deltaTime);
                        if (objective.IsCompleted)
                        {
                            pendingObjectives.Remove(objective);
                            completedObjectives.Add(objective);
                            if (pendingObjectives.Count > 0)
                            {
                                objective.EndMessage();
                            }
                            continue;
                        }
                        if (!objective.CanBeCompleted)
                        {
                            objective.EndMessage();
                            objective.End(true);
                            pendingObjectives.Clear();
                        }
                        break;
                    }
                    var completedMax = completedObjectives.Count - 1;
                    for (var i = previousCompletedCount; i <= completedMax; ++i)
                    {
                        var objective = completedObjectives[i];
                        objective.End(i < completedMax || pendingObjectives.Count > 0);
                    }
                    if (pendingObjectives.Count > 0)
                    {
                        startedObjectives.ForEach(objective => objective.StartMessage());
                    }
                }
                if (completedObjectives.Count >= allObjectives.Count)
                {
                    foreach (var traitor in Traitors)
                    {
                        SteamAchievementManager.OnTraitorWin(traitor.Value.Character);
                    }
                    winHandler();
                }
            }

            public delegate bool CharacterFilter(Character character);
            public Character FindKillTarget(Character traitor, CharacterFilter filter)
            {
                if (traitor == null) { return null; }

                List<Character> validCharacters = Character.CharacterList.FindAll(c =>
                    c.TeamID == traitor.TeamID &&
                    c != traitor &&
                    !c.IsDead &&
                    (filter == null || filter(c)));

                if (validCharacters.Count > 0)
                {
                    return validCharacters[Random(validCharacters.Count)];
                }

#if ALLOW_SOLO_TRAITOR
                return traitor;
#else
                return null;
#endif
            }

            public TraitorMission(string startText, string globalEndMessageSuccessTextId, string globalEndMessageSuccessDeadTextId, string globalEndMessageSuccessDetainedTextId, string globalEndMessageFailureTextId, string globalEndMessageFailureDeadTextId, string globalEndMessageFailureDetainedTextId, ICollection<string> roles, ICollection<Objective> objectives)
            {
                StartText = startText;
                GlobalEndMessageSuccessTextId = globalEndMessageSuccessTextId;
                GlobalEndMessageSuccessDeadTextId = globalEndMessageSuccessDeadTextId;
                GlobalEndMessageSuccessDetainedTextId = globalEndMessageSuccessDetainedTextId;
                GlobalEndMessageFailureTextId = globalEndMessageFailureTextId;
                GlobalEndMessageFailureDeadTextId = globalEndMessageFailureDeadTextId;
                GlobalEndMessageFailureDetainedTextId = globalEndMessageFailureDetainedTextId;
                Roles.AddRange(roles);
                allObjectives.AddRange(objectives);
                pendingObjectives.AddRange(objectives);
            }
        }
    }
}
