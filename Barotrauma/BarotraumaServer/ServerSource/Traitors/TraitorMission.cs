//#define ALLOW_SOLO_TRAITOR
//#define ALLOW_NONHUMANOID_TRAITOR

using System;
using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Security.Cryptography;
using Barotrauma.Extensions;

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

            /// <summary>
            /// Has the mission been completed (does not mean that the traitor necessarily won, the mission is considered completed if the traitor fails for whatever reason)
            /// </summary>
            public bool IsCompleted => pendingObjectives.Count <= 0;

            public readonly Dictionary<string, Traitor> Traitors = new Dictionary<string, Traitor>();
            public delegate bool RoleFilter(Character character);
            public readonly Dictionary<string, RoleFilter> Roles = new Dictionary<string, RoleFilter>();

            public string StartText { get; private set; }
            public string CodeWords { get; private set; }
            public string CodeResponse { get; private set; }

            public string GlobalEndMessageSuccessTextId { get; private set; }
            public string GlobalEndMessageSuccessDeadTextId { get; private set; }
            public string GlobalEndMessageSuccessDetainedTextId { get; private set; }
            public string GlobalEndMessageFailureTextId { get; private set; }
            public string GlobalEndMessageFailureDeadTextId { get; private set; }
            public string GlobalEndMessageFailureDetainedTextId { get; private set; }

            public readonly string Identifier;

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
                        return TextManager.JoinServerMessages("\n",
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

            protected List<Tuple<Client, Character>> FindTraitorCandidates(GameServer server, Character.TeamType team, RoleFilter traitorRoleFilter)
            {
                var traitorCandidates = new List<Tuple<Client, Character>>();
                foreach (Client c in server.ConnectedClients)
                {
                    if (c.Character == null || c.Character.IsDead || c.Character.Removed || !traitorRoleFilter(c.Character) ||
                        (team != Character.TeamType.None && c.Character.TeamID != team))
                    {
                        continue;
                    }
#if !ALLOW_NONHUMANOID_TRAITOR
                    if (!c.Character.IsHumanoid) { continue; }
#endif
                    traitorCandidates.Add(Tuple.Create(c, c.Character));
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

            protected List<Tuple<string, Tuple<Client, Character>>> AssignTraitors(GameServer server, TraitorManager traitorManager, Character.TeamType team)
            {
                List<Character> characters = FindCharacters();
#if !ALLOW_SOLO_TRAITOR
                if (characters.Count < 2)
                {
                    return null;
                }
#endif
                var roleCandidates = new Dictionary<string, HashSet<Tuple<Client, Character>>>();
                foreach (var role in Roles)
                {
                    roleCandidates.Add(role.Key, new HashSet<Tuple<Client, Character>>(FindTraitorCandidates(server, team, role.Value)));
                    if (roleCandidates[role.Key].Count <= 0)
                    {
                        return null;
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

                    var selected = ToolBox.SelectWeightedRandom(availableCandidates, availableCandidates.Select(c => Math.Max(c.Item1.RoundsSincePlayedAsTraitor, 0.1f)).ToList(), TraitorManager.Random);
                    DebugConsole.NewMessage("Setting User ID CARD");
                    var idCard = selected.Item2.Inventory.FindItemByIdentifier("idcard");
                    idCard.AddTag("traitor");
                    idCard.Description = "Test here are your tags " + idCard.Tags;
                    assignedCandidates.Add(Tuple.Create(currentRole, selected));
                    foreach (var candidate in roleCandidates.Values)
                    {
                        candidate.Remove(selected);
                    }
                }
                if (unassignedRoles.Count > 0)
                {
                    return null;
                }
                return assignedCandidates;
            }

            public bool CanBeStarted(GameServer server, TraitorManager traitorManager, Character.TeamType team)
            {
                foreach (var role in Roles)
                {
                    var candidates = FindTraitorCandidates(server, team, role.Value);
                    if (candidates.Count <= 0)
                    {
                        return false;
                    }
                }
                return AssignTraitors(server, traitorManager, team) != null;
            }

            public bool Start(GameServer server, TraitorManager traitorManager, Character.TeamType team)
            {
                var assignedCandidates = AssignTraitors(server, traitorManager, team);
                if (assignedCandidates == null)
                {
                    return false;
                }

                foreach (Client client in server.ConnectedClients)
                {
                    client.RoundsSincePlayedAsTraitor++;
                }

                Traitors.Clear();
                foreach (var candidate in assignedCandidates)
                {
                    var traitor = new Traitor(this, candidate.Item1, candidate.Item2.Item1.Character);
                    Traitors.Add(candidate.Item1, traitor);
                    candidate.Item2.Item1.RoundsSincePlayedAsTraitor = 0;
                }
                CodeWords = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
                CodeResponse = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
                
                /**if (pendingObjectives.Count <= 0 || !pendingObjectives[0].CanBeStarted(Traitors.Values))
                {
                    Traitors.Clear();
                    return false;
                }**/

                var pendingMessages = new Dictionary<Traitor, List<string>>();
                pendingMessages.Clear();
                foreach (var traitor in Traitors.Values)
                {
                    pendingMessages.Add(traitor, new List<string>());
                }
                foreach (var traitor in Traitors.Values)
                {
                    traitor.Greet(server, CodeWords, CodeResponse, message => pendingMessages[traitor].Add(message));
                }
                pendingMessages.ForEach(traitor => traitor.Value.ForEach(message => traitor.Key.SendChatMessage(message, Identifier)));
                pendingMessages.ForEach(traitor => traitor.Value.ForEach(message => traitor.Key.SendChatMessageBox(message, Identifier)));

                Update(0.0f, () => { GameMain.Server.TraitorManager.ShouldEndRound = true; });
#if SERVER
                foreach (var traitor in Traitors.Values)
                {
                    GameServer.Log($"{GameServer.CharacterLogName(traitor.Character)} is a traitor and the current goals are:\n{(traitor.CurrentObjective?.GoalInfos != null ? TextManager.GetServerMessage(traitor.CurrentObjective?.GoalInfos) : "(empty)")}, and codewords are:{CodeWords} and {CodeResponse}", ServerLog.MessageType.ServerMessage);
                }
#endif
                return true;
            }

            public delegate void TraitorWinHandler();

            public void Update(float deltaTime, TraitorWinHandler winHandler)
            {
                if (pendingObjectives.Count <= 0 || Traitors.Count <= 0)
                {
                    return;
                }
                /**if (Traitors.Values.Any(traitor => traitor.Character?.IsDead ?? true || traitor.Character.Removed))
                {
                    Traitors.Values.ForEach(traitor => traitor.UpdateCurrentObjective("", Identifier));
                    pendingObjectives.Clear();
                    Traitors.Clear();
                    return;
                }**/
                var startedObjectives = new List<Objective>();
                foreach (var traitor in Traitors.Values)
                {
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
                                //the mission fails if an objective cannot be started
                                if (completedObjectives.Count > 0)
                                {
                                    objective.EndMessage();
                                }
                                pendingObjectives.Clear();
                                break;
                            }
                            startedObjectives.Add(objective);
                        }
                        objective.Update(deltaTime);
                        if (objective.IsCompleted)
                        {
                            pendingObjectives.Remove(objective);
                            completedObjectives.Add(objective);
                            objective.EndMessage();
                            continue;
                        }
                        if (objective.IsStarted && !objective.CanBeCompleted)
                        {
                            objective.EndMessage();
                            pendingObjectives.Clear();
                        }
                        break;
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
                    //winHandler();
                }
            }

            public delegate bool CharacterFilter(Character character);
            public List<Character> FindKillTarget(Character traitor, CharacterFilter filter, int count = -1, float percentage = -1f)
            {
                if (traitor == null) { return null; }

                List<Character> validCharacters = Character.CharacterList.FindAll(c => c.TeamID == traitor.TeamID &&
                                                                                  c != traitor && !c.IsDead &&
                                                                                  (filter == null || filter(c)));

                int targetCount = 1;
                if (count > 0)
                {
                    targetCount = count;
                }
                else if (percentage > 0f)
                {
                    targetCount = (int)Math.Max(1, Math.Floor(validCharacters.Count * percentage));
                }

                List<Character> targetCharacters = new List<Character>();

                if (validCharacters.Count > 0)
                {
                    for (int i = 0; i < targetCount; i++)
                    {
                        if (validCharacters.Count == 0) break;
                        Character character = validCharacters[TraitorManager.RandomInt(validCharacters.Count)];
                        targetCharacters.Add(character);
                        validCharacters.Remove(character);
                    }
                    return targetCharacters;
                }

#if ALLOW_SOLO_TRAITOR
                targetCharacters.Add(traitor);
                return targetCharacters;
#else
                return null;
#endif
            }

            public string GetTargetNames(List<Character> targets)
            {
                string names = string.Empty;
                for (int i = 0; i < targets.Count; i++)
                {
                    names += targets[i].Name;

                    if (i < targets.Count - 1)
                    {
                        names += ", ";
                    }
                }

                if (names.Length > 0)
                {
                    return names;
                }
                else
                {
                    return TextManager.FormatServerMessage("unknown");
                }
            }

            public TraitorMission(string identifier, string startText, string globalEndMessageSuccessTextId, string globalEndMessageSuccessDeadTextId, string globalEndMessageSuccessDetainedTextId, string globalEndMessageFailureTextId, string globalEndMessageFailureDeadTextId, string globalEndMessageFailureDetainedTextId, IEnumerable<KeyValuePair<string, RoleFilter>> roles, ICollection<Objective> objectives)
            {
                Identifier = identifier;
                StartText = startText;
                GlobalEndMessageSuccessTextId = globalEndMessageSuccessTextId;
                GlobalEndMessageSuccessDeadTextId = globalEndMessageSuccessDeadTextId;
                GlobalEndMessageSuccessDetainedTextId = globalEndMessageSuccessDetainedTextId;
                GlobalEndMessageFailureTextId = globalEndMessageFailureTextId;
                GlobalEndMessageFailureDeadTextId = globalEndMessageFailureDeadTextId;
                GlobalEndMessageFailureDetainedTextId = globalEndMessageFailureDetainedTextId;
                foreach (var role in roles)
                {
                    Roles.Add(role.Key, role.Value);
                }
                allObjectives.AddRange(objectives);
                pendingObjectives.AddRange(objectives);
            }
        }
    }
}
