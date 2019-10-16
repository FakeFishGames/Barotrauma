using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
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
                            checker.Optional("causeofdeath");
                            checker.Optional("affliction");
                            checker.Optional("roomname");
                            List<Traitor.TraitorMission.CharacterFilter> killFilters = new List<Traitor.TraitorMission.CharacterFilter>();
                            foreach (var attribute in Config.Attributes())
                            {
                                if (targetFilters.TryGetValue(attribute.Name.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture), out var filter))
                                {
                                    killFilters.Add((character) => filter(attribute.Value, character));
                                }
                            }
                            goal = new Traitor.GoalKillTarget((character) => killFilters.All(f => f(character)), 
                                (CauseOfDeathType)Enum.Parse(typeof(CauseOfDeathType), Config.GetAttributeString("causeofdeath", "Unknown"), true),
                                Config.GetAttributeString("affliction", null), Config.GetAttributeString("roomname", null));
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

                        case "injectpoison":
                            checker.Required("poison");
                            checker.Required("affliction");
                            checker.Optional(targetFilters.Keys.ToArray());
                            List<Traitor.TraitorMission.CharacterFilter> poisonFilters = new List<Traitor.TraitorMission.CharacterFilter>();
                            foreach (var attribute in Config.Attributes())
                            {
                                if (targetFilters.TryGetValue(attribute.Name.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture), out var filter))
                                {
                                    poisonFilters.Add((character) => filter(attribute.Value, character));
                                }
                            }
                            goal = new Traitor.GoalInjectTarget((character) => poisonFilters.All(f => f(character)), Config.GetAttributeString("poison", null), Config.GetAttributeString("affliction", null));
                            break;

                        case "unwire":
                            checker.Required("tag");
                            checker.Optional("connectionname");
                            checker.Optional("connectiondisplayname");
                            goal = new Traitor.GoalUnwiring(Config.GetAttributeString("tag", null), Config.GetAttributeString("connectionname", null), Config.GetAttributeString("connectiondisplayname)", null));
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

            public abstract void InstantiateGoals();
            public abstract Traitor.Objective Instantiate(IEnumerable<string> roles);
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

            private List<Traitor.Goal> goalInstances = null;

            public override void InstantiateGoals()
            {
                goalInstances = Goals.ConvertAll(goal =>
                {
                    var instance = goal.Instantiate();
                    if (instance == null)
                    {
                        GameServer.Log($"Failed to instantiate goal \"{goal.Type}\".", ServerLog.MessageType.Error);
                    }
                    return instance;
                }).FindAll(goal => goal != null);
            }

            public override Traitor.Objective Instantiate(IEnumerable<string> roles)
            {
                var result = new Traitor.Objective(InfoText, ShuffleGoalsCount, roles.ToArray(), goalInstances);
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
            private Traitor.GoalWaitForTraitors sharedGoal;

            public override void InstantiateGoals()
            {
                sharedGoal = new Traitor.GoalWaitForTraitors(Roles.Count);
            }

            public override Traitor.Objective Instantiate(IEnumerable<string> roles)
            {
                return new Traitor.Objective("TraitorObjectiveInfoTextWaitForOtherTraitors", -1, roles.ToArray(), new[] { sharedGoal });
            }

            public WaitObjective(ICollection<string> roles)
            {
                Roles.UnionWith(roles);
            }
        }

        public class Role
        {
            public readonly Traitor.TraitorMission.RoleFilter Filter;

            public Role(IEnumerable<Traitor.TraitorMission.RoleFilter> filters)
            {
                Filter = character => filters.All(filter => filter(character));
            }

            public Role()
            {
                Filter = character => true;
            }
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
                var pendingCount = 1;
                objectivesWithSync.Add(Objectives[0]);
                pendingRoles.UnionWith(Objectives[0].Roles);
                for (var i = 1; i < objectivesCount; ++i)
                {
                    var objective = Objectives[i];
                    if (pendingRoles.IsSupersetOf(objective.Roles))
                    {
                        if (pendingCount > 1)
                        {
                            objectivesWithSync.Add(new WaitObjective(objective.Roles));
                        }
                        pendingRoles.Clear();
                        pendingCount = 0;
                    }
                    objectivesWithSync.Add(objective);
                    pendingRoles.UnionWith(objective.Roles);
                    ++pendingCount;
                }
                if (pendingCount > 1 && pendingRoles.IsSubsetOf(Roles.Keys))
                {
                    // TODO: If last objective includes only one traitor, other traitors will get the wrong end message.
                    objectivesWithSync.Add(new WaitObjective(Roles.Keys));
                }
            }

            return new Traitor.TraitorMission(
                Identifier,
                StartText ?? "TraitorMissionStartMessage",
                EndMessageSuccessText ?? "TraitorObjectiveEndMessageSuccess",
                EndMessageSuccessDeadText ?? "TraitorObjectiveEndMessageSuccessDead",
                EndMessageSuccessDetainedText ?? "TraitorObjectiveEndMessageSuccessDetained",
                EndMessageFailureText ?? "TraitorObjectiveEndMessageFailure",
                EndMessageFailureDeadText ?? "TraitorObjectiveEndMessageFailureDead",
                EndMessageFailureDetainedText ?? "TraitorObjectiveEndMessageFailureDetained",
                Roles.ToDictionary(kv => kv.Key, kv => kv.Value.Filter),
                objectivesWithSync.SelectMany(objective =>
                {
                    objective.InstantiateGoals();
                    return objective.Roles.Select(role => objective.Instantiate(new[] { role }));
                }).ToArray());
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

        protected Role LoadRole(XElement roleRoot)
        {
            var filters = new List<Traitor.TraitorMission.RoleFilter>();
            var jobs = roleRoot.GetAttributeStringArray("jobs", null);
            if (jobs != null)
            {
                var jobsSet = new HashSet<string>(jobs.Select(job => job.ToLower(CultureInfo.InvariantCulture)));
                filters.Add(character => character.Info?.Job != null && jobsSet.Contains(character.Info.Job.Name.ToLower(CultureInfo.InvariantCulture)));
            }
            return new Role(filters);
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
                            checker.Optional("jobs");
                            Roles.Add(element.GetAttributeString("id", null), LoadRole(element));
                            break;
                    }
                }
            }
            if (!Roles.Any())
            {
                Roles.Add("traitor", new Role());
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
