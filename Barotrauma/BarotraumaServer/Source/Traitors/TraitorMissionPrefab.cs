using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma {

    class TraitorMissionPrefab
    {
        public class TraitorMissionEntry
        {
            public readonly TraitorMissionPrefab Prefab;
            public int SelectedWeight;

            public TraitorMissionEntry(XElement element)
            {
                Prefab = new TraitorMissionPrefab(element);
                SelectedWeight = 0;
            }
        }
        public static readonly List<TraitorMissionEntry> List = new List<TraitorMissionEntry>();

        public static void Init()
        {
            var files = GameMain.Instance.GetFilesOfType(ContentType.TraitorMissions);
            foreach (string file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc?.Root == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    List.Add(new TraitorMissionEntry(element));
                }
            }
        }

        public static TraitorMissionPrefab RandomPrefab()
        {
            return TraitorManager.WeightedRandom(List, Traitor.TraitorMission.Random, entry => entry.SelectedWeight, (entry, weight) => entry.SelectedWeight = weight, 2, 3)?.Prefab;
        }

        private class AttributeChecker : IDisposable
        {
            private readonly XElement element;
            private readonly HashSet<string> required = new HashSet<string>();
            private readonly HashSet<string> optional = new HashSet<string>();

            public void Optional(params string[] names)
            {
                optional.UnionWith(names);
            }

            public void Required(params string[] names)
            {
                required.UnionWith(names);
            }

            public void Dispose()
            {
                foreach (var requiredName in required)
                {
                    if (element.Attributes().All(attribute => attribute.Name != requiredName))
                    {
                        GameServer.Log($"Required attribute \"{requiredName}\" is missing in \"{element.Name}\"", ServerLog.MessageType.Error);
                    }
                }
                foreach (var attribute in element.Attributes())
                {
                    var attributeName = attribute.Name.ToString();
                    if (!required.Contains(attributeName) && !optional.Contains(attributeName))
                    {
                        GameServer.Log($"Unsupported attribute \"{attributeName}\" in \"{element.Name}\"", ServerLog.MessageType.Error);
                    }
                }
            }

            public AttributeChecker(XElement element)
            {
                this.element = element;
            }
        }

        public class Goal
        {
            public readonly string Type;
            public readonly XElement Config;

            public Goal(string type, XElement config)
            {
                Type = type;
                Config = config;
            }

            private delegate bool TargetFilter(string value, Character character);
            private static Dictionary<string, TargetFilter> targetFilters = new Dictionary<string, TargetFilter>()
            {
                { "job", (value, character) => value.Equals(character.Info.Job.Prefab.Identifier, StringComparison.OrdinalIgnoreCase) },
                { "role", (value, character) => value.Equals(GameMain.Server.TraitorManager.GetTraitorRole(character), StringComparison.OrdinalIgnoreCase) }
            };

            public Traitor.Goal Instantiate()
            {
                Traitor.Goal goal = null;
                using (var checker = new AttributeChecker(Config))
                {
                    checker.Required("type");
                    var goalType = Config.GetAttributeString("type", "");
                    switch (goalType.ToLowerInvariant())
                    {
                        case "killtarget":
                        {
                            checker.Optional(targetFilters.Keys.ToArray());
                            List<Traitor.TraitorMission.CharacterFilter> filters = new List<Traitor.TraitorMission.CharacterFilter>();
                            foreach (var attribute in Config.Attributes())
                            {
                                if (targetFilters.TryGetValue(attribute.Name.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture), out var filter))
                                {
                                    filters.Add((character) => filter(attribute.Value, character));
                                }
                            }
                            goal = new Traitor.GoalKillTarget((character) => filters.All(f => f(character)));
                            break;
                        }
                        case "destroyitems":
                        {
                            checker.Required("tag");
                            checker.Optional("percentage", "matchIdentifier", "matchTag", "matchInventory");
                            var tag = Config.GetAttributeString("tag", null);
                            if (tag != null)
                            {
                                goal = new Traitor.GoalDestroyItemsWithTag(
                                    tag,
                                    Config.GetAttributeFloat("percentage", 100.0f) / 100.0f,
                                    Config.GetAttributeBool("matchIdentifier", true),
                                    Config.GetAttributeBool("matchTag", true),
                                    Config.GetAttributeBool("matchInventory", false));
                            }
                            break;
                        }
                        case "sabotage":
                        {
                            checker.Required("tag");
                            checker.Optional("threshold");
                            var tag = Config.GetAttributeString("tag", null);
                            if (tag != null)
                            {
                                goal = new Traitor.GoalSabotageItems(tag, Config.GetAttributeFloat("threshold", 20.0f));
                            }
                            break;
                        }
                        case "floodsub":
                            checker.Optional("percentage");
                            goal = new Traitor.GoalFloodPercentOfSub(Config.GetAttributeFloat("percentage", 100.0f) / 100.0f);
                            break;
                        case "finditem":
                            checker.Required("identifier");
                            checker.Optional("preferNew", "allowNew", "allowExisting", "allowedContainers");
                            goal = new Traitor.GoalFindItem(Config.GetAttributeString("identifier", null), Config.GetAttributeBool("preferNew", true), Config.GetAttributeBool("allowNew", true), Config.GetAttributeBool("allowExisting", true), Config.GetAttributeStringArray("allowedContainers", new string[] {"steelcabinet", "mediumsteelcabinet", "suppliescabinet"}));
                            break;
                        case "replaceinventory":
                            checker.Required("containers", "replacements");
                            checker.Optional("percentage");
                            goal = new Traitor.GoalReplaceInventory(Config.GetAttributeStringArray("containers", new string[] { }), Config.GetAttributeStringArray("replacements", new string[] { }), Config.GetAttributeFloat("percentage", 100.0f) / 100.0f);
                            break;
                        case "reachdistancefromsub":
                            checker.Optional("distance");
                            goal = new Traitor.GoalReachDistanceFromSub(Config.GetAttributeFloat("distance", 10000.0f));
                            break;
                        default:
                            GameServer.Log($"Unrecognized goal type \"{goalType}\".", ServerLog.MessageType.Error);
                            break;
                    }
                }
                if (goal == null)
                {
                    return null;
                }
                foreach (var element in Config.Elements())
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "modifier":
                        {
                            using (var checker = new AttributeChecker(element))
                            {
                                checker.Required("type");
                                var modifierType = element.GetAttributeString("type", "");
                                switch (modifierType)
                                {
                                    case "duration":
                                    {
                                        checker.Optional("cumulative", "duration", "infotext");
                                        var isCumulative = element.GetAttributeBool("cumulative", false);
                                        goal = new Traitor.GoalHasDuration(goal, element.GetAttributeFloat("duration", 5.0f), isCumulative, element.GetAttributeString("infotext", isCumulative ? "TraitorGoalWithCumulativeDurationInfoText" : "TraitorGoalWithDurationInfoText"));
                                        break;
                                    }
                                    case "timelimit":
                                        checker.Optional("timelimit", "infotext");
                                        goal = new Traitor.GoalHasTimeLimit(goal, element.GetAttributeFloat("timelimit", 180.0f), element.GetAttributeString("infotext", "TraitorGoalWithTimeLimitInfoText"));
                                        break;
                                    case "optional":
                                        checker.Optional("infotext");
                                        goal = new Traitor.GoalIsOptional(goal, element.GetAttributeString("infotext", "TraitorGoalIsOptionalInfoText"));
                                        break;
                                    default:
                                        GameServer.Log($"Unrecognized modifier type \"{modifierType}\".", ServerLog.MessageType.Error);
                                        break;
                                }
                            }
                            break;
                        }
                    }
                }
                foreach (var element in Config.Elements())
                {
                    var elementName = element.Name.ToString().ToLowerInvariant();
                    switch (elementName)
                    {
                        case "modifier":
                            // loaded above
                            break;
                        case "infotext":
                        {
                            using (var checker = new AttributeChecker(element))
                            {
                                checker.Required("id");
                                var id = element.GetAttributeString("id", null);
                                if (id != null)
                                {
                                    goal.InfoTextId = id;
                                }
                            }
                            break;
                        }
                        case "completedtext":
                        {
                            using (var checker = new AttributeChecker(element))
                            {
                                checker.Required("id");
                                var id = element.GetAttributeString("id", null);
                                if (id != null)
                                {
                                    goal.CompletedTextId = id;
                                }
                            }
                            break;
                        }
                        default:
                            GameServer.Log($"Unrecognized element \"{element.Name}\" in goal.", ServerLog.MessageType.Error);
                            break;
                    }
                }
                return goal;
            }
        }


        public abstract class ObjectiveBase
        {
            public HashSet<string> Roles { get; } = new HashSet<string>();

            public abstract Traitor.Objective Instantiate(string role);
        }

        protected class Objective : ObjectiveBase
        {
            public string InfoText { get; internal set; }
            public string StartMessageTextId { get; internal set; }
            public string StartMessageServerTextId { get; internal set; }
            public string EndMessageSuccessTextId { get; internal set; }
            public string EndMessageSuccessDeadTextId { get; internal set; }
            public string EndMessageSuccessDetainedTextId { get; internal set; }
            public string EndMessageFailureTextId { get; internal set; }
            public string EndMessageFailureDeadTextId { get; internal set; }
            public string EndMessageFailureDetainedTextId { get; internal set; }
            public int ShuffleGoalsCount { get; internal set; }

            public readonly List<Goal> Goals = new List<Goal>();

            public override Traitor.Objective Instantiate(string role)
            {
                var result = new Traitor.Objective(InfoText, ShuffleGoalsCount, new [] { role }, Goals.ConvertAll(goal => {
                    var instance = goal.Instantiate();
                    if (instance == null)
                    {
                        GameServer.Log($"Failed to instantiate goal \"{goal.Type}\".", ServerLog.MessageType.Error);
                    }
                    return instance;
                }).FindAll(goal => goal != null));
                if (StartMessageTextId != null)
                {
                    result.StartMessageTextId = StartMessageTextId;
                }
                if (StartMessageServerTextId != null)
                {
                    result.StartMessageServerTextId = StartMessageServerTextId;
                }
                if (EndMessageSuccessTextId != null)
                {
                    result.EndMessageSuccessTextId = EndMessageSuccessTextId;
                }
                if (EndMessageSuccessDeadTextId != null)
                {
                    result.EndMessageSuccessDeadTextId = EndMessageSuccessDeadTextId;
                }
                if (EndMessageSuccessDetainedTextId != null)
                {
                    result.EndMessageSuccessDetainedTextId = EndMessageSuccessDetainedTextId;
                }
                if (EndMessageFailureTextId != null)
                {
                    result.EndMessageFailureTextId = EndMessageFailureTextId;
                }
                if (EndMessageFailureDeadTextId != null)
                {
                    result.EndMessageFailureDeadTextId = EndMessageFailureDeadTextId;
                }
                if (EndMessageFailureDetainedTextId != null)
                {
                    result.EndMessageFailureDetainedTextId = EndMessageFailureDetainedTextId;
                }
                return result;
            }
        }

        protected class WaitObjective : ObjectiveBase
        {
            private readonly Traitor.GoalWaitForTraitors sharedGoal;

            public override Traitor.Objective Instantiate(string role)
            {
                return new Traitor.Objective("TraitorObjectiveInfoTextWaitForOtherTraitors", -1, new [] { role }, new[] { sharedGoal });
            }

            public WaitObjective(ICollection<string> roles)
            {
                Roles.UnionWith(roles);
                sharedGoal = new Traitor.GoalWaitForTraitors(Roles.Count);
            }
        }

        public class Role
        {
            // public string Job;
        }
        public readonly Dictionary<string, Role> Roles = new Dictionary<string, Role>();

        public readonly string Identifier;
        public readonly string StartText;
        public readonly string EndMessageSuccessText;
        public readonly string EndMessageSuccessDeadText;
        public readonly string EndMessageSuccessDetainedText;
        public readonly string EndMessageFailureText;
        public readonly string EndMessageFailureDeadText;
        public readonly string EndMessageFailureDetainedText;

        public readonly List<ObjectiveBase> Objectives = new List<ObjectiveBase>();

        public Traitor.TraitorMission Instantiate()
        {
            var objectivesWithSync = new List<ObjectiveBase>();
            var objectivesCount = Objectives.Count;
            if (objectivesCount > 0)
            {
                var pendingRoles = new HashSet<string>();
                objectivesWithSync.Add(Objectives[0]);
                pendingRoles.UnionWith(Objectives[0].Roles);
                for (var i = 1; i < objectivesCount; ++i)
                {
                    var objective = Objectives[i];
                    if (pendingRoles.IsSupersetOf(objective.Roles))
                    {
                        objectivesWithSync.Add(new WaitObjective(objective.Roles));
                        pendingRoles.Clear();
                    }
                    objectivesWithSync.Add(objective);
                    pendingRoles.UnionWith(objective.Roles);
                }
                if (pendingRoles.IsSubsetOf(Roles.Keys))
                {
                    // TODO(xxx): Correct end message for WaitObjective
                    objectivesWithSync.Add(new WaitObjective(Roles.Keys));
                }
            }
            return new Traitor.TraitorMission(
                StartText ?? "TraitorMissionStartMessage",
                EndMessageSuccessText ?? "TraitorObjectiveEndMessageSuccess",
                EndMessageSuccessDeadText ?? "TraitorObjectiveEndMessageSuccessDead",
                EndMessageSuccessDetainedText ?? "TraitorObjectiveEndMessageSuccessDetained",
                EndMessageFailureText ?? "TraitorObjectiveEndMessageFailure",
                EndMessageFailureDeadText ?? "TraitorObjectiveEndMessageFailureDead",
                EndMessageFailureDetainedText ?? "TraitorObjectiveEndMessageFailureDetained",
                Roles.Keys, // TODO(xxx): Full role data to mission
                objectivesWithSync.SelectMany(objective => objective.Roles.Select(objective.Instantiate)).ToArray());
        }

        protected Goal LoadGoal(XElement goalRoot)
        {
            var goalType = goalRoot.GetAttributeString("type", "");
            return new Goal(goalType, goalRoot);
        }

         protected Objective LoadObjective(XElement objectiveRoot, string[] allRoles)
         {
            var allRolesSet = new HashSet<string>(allRoles);
            var result = new Objective
            {
                ShuffleGoalsCount = objectiveRoot.GetAttributeInt("shuffleGoalsCount", -1)
            };
            var objectiveRoles = objectiveRoot.GetAttributeStringArray("roles", allRoles);
            if (!allRolesSet.IsSupersetOf(objectiveRoles))
            {
                var unrecognized = new HashSet<string>(objectiveRoles);
                unrecognized.ExceptWith(allRoles);
                GameServer.Log($"Undefined role(s) \"{string.Join(", ", unrecognized)}\" set for Objective.", ServerLog.MessageType.Error);
            }
            result.Roles.UnionWith(allRolesSet.Intersect(objectiveRoles));

            foreach (var element in objectiveRoot.Elements())
            {
                using (var checker = new AttributeChecker(element))
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "infotext":
                            checker.Required("id");
                            result.InfoText = element.GetAttributeString("id", null);
                            break;
                        case "startmessage":
                            checker.Required("id");
                            result.StartMessageTextId = element.GetAttributeString("id", null);
                            break;
                        case "startmessageserver":
                            checker.Required("id");
                            result.StartMessageServerTextId = element.GetAttributeString("id", null);
                            break;
                        case "endmessagesuccess":
                            checker.Required("id");
                            result.EndMessageSuccessTextId = element.GetAttributeString("id", null);
                            break;
                        case "endmessagesuccessdead":
                            checker.Required("id");
                            result.EndMessageSuccessDeadTextId = element.GetAttributeString("id", null);
                            break;
                        case "endmessagesuccessdetained":
                            checker.Required("id");
                            result.EndMessageSuccessDetainedTextId = element.GetAttributeString("id", null);
                            break;
                        case "endmessagefailure":
                            checker.Required("id");
                            result.EndMessageFailureTextId = element.GetAttributeString("id", null);
                            break;
                        case "endmessagefailuredead":
                            checker.Required("id");
                            result.EndMessageFailureDeadTextId = element.GetAttributeString("id", null);
                            break;
                        case "endmessagefailuredetained":
                            checker.Required("id");
                            result.EndMessageFailureDetainedTextId = element.GetAttributeString("id", null);
                            break;
                        case "goal":
                        {
                            var goal = LoadGoal(element);
                            if (goal != null)
                            {
                                result.Goals.Add(goal);
                            }
                            break;
                        }
                        default:
                            GameServer.Log($"Unrecognized element \"{element.Name}\" under Objective.", ServerLog.MessageType.Error);
                            break;
                    }
                }
            }
            return result;
        }

        public TraitorMissionPrefab(XElement missionRoot)
        {
            Identifier = missionRoot.GetAttributeString("identifier", null);
            foreach (var element in missionRoot.Elements())
            {
                using (var checker = new AttributeChecker(element))
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "role":
                            checker.Required("id");
                            Roles.Add(element.GetAttributeString("id", null), new Role { });
                            break;
                    }
                }
            }
            if (!Roles.Any())
            {
                Roles.Add("traitor", new Role { });
            }
            foreach (var element in missionRoot.Elements())
            {
                using (var checker = new AttributeChecker(element))
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "role":
                            // handled above
                            break;
                        case "startinfotext":
                            checker.Required("id");
                            StartText = element.GetAttributeString("id", null);
                            break;
                        case "endmessagesuccess":
                            checker.Required("id");
                            EndMessageSuccessText = element.GetAttributeString("id", null);
                            break;
                        case "endmessagesuccessdead":
                            checker.Required("id");
                            EndMessageSuccessDeadText = element.GetAttributeString("id", null);
                            break;
                        case "endmessagesuccessdetained":
                            checker.Required("id");
                            EndMessageSuccessDetainedText = element.GetAttributeString("id", null);
                            break;
                        case "endmessagefailure":
                            checker.Required("id");
                            EndMessageFailureText = element.GetAttributeString("id", null);
                            break;
                        case "endmessagefailuredead":
                            checker.Required("id");
                            EndMessageFailureDeadText = element.GetAttributeString("id", null);
                            break;
                        case "endmessagefailuredetained":
                            checker.Required("id");
                            EndMessageFailureDetainedText = element.GetAttributeString("id", null);
                            break;
                        case "objective":
                        {
                            var objective = LoadObjective(element, Roles.Keys.ToArray());
                            if (objective != null)
                            {
                                Objectives.Add(objective);
                            }
                            break;
                        }
                        default:
                            GameServer.Log($"Unrecognized element \"{element.Name}\"under TraitorMission.", ServerLog.MessageType.Error);
                            break;
                    }
                }
            }
        }
    }
}
