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

        public class Context
        {
            public List<Character> Characters;
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
            };

            public Traitor.Goal Instantiate()
            {
                Traitor.Goal goal = null;
                switch (Config.GetAttributeString("type", "").ToLowerInvariant())
                {
                    case "killtarget":
                        {
                            List<Traitor.TraitorMission.CharacterFilter> filters = new List<Traitor.TraitorMission.CharacterFilter>();
                            foreach (var attribute in Config.Attributes())
                            {
                                if (targetFilters.TryGetValue(attribute.Name.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture), out var filter))
                                {
                                    filters.Add((character) => filter(attribute.Value, character));
                                }
                            }
                            goal = new Traitor.GoalKillTarget((character) => filters.All(f => f(character)));
                        }
                        break;
                    case "destroyitems":
                        {
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
                            else
                            {
                                GameServer.Log(string.Format("No tag attribute specified for \"destroyitems\" goal."), ServerLog.MessageType.Error);
                            }
                        }
                        break;
                    case "sabotage":
                        {
                            var tag = Config.GetAttributeString("tag", null);
                            if (tag != null)
                            {
                                goal = new Traitor.GoalSabotageItems(tag, Config.GetAttributeFloat("threshold", 20.0f));

                            }
                            else
                            {
                                GameServer.Log(string.Format("No tag attribute specified for \"sabotage\" goal."), ServerLog.MessageType.Error);
                            }
                        }
                        break;
                    case "floodsub":
                        goal = new Traitor.GoalFloodPercentOfSub(Config.GetAttributeFloat("percentage", 100.0f) / 100.0f);
                        break;
                    case "finditem":
                        goal = new Traitor.GoalFindItem(Config.GetAttributeString("identifier", null), Config.GetAttributeBool("preferNew", true), Config.GetAttributeBool("allowNew", true), Config.GetAttributeBool("allowExisting", true), Config.GetAttributeStringArray("allowedContainers", new string[] { "steelcabinet", "mediumsteelcabinet", "suppliescabinet" }));
                        break;
                    case "replaceinventory":
                        goal = new Traitor.GoalReplaceInventory(Config.GetAttributeStringArray("containers", new string[] { }), Config.GetAttributeStringArray("replacements", new string[] { }), Config.GetAttributeFloat("percentage", 100.0f) / 100.0f);
                        break;
                    case "reachdistancefromsub":
                        goal = new Traitor.GoalReachDistanceFromSub(Config.GetAttributeFloat("distance", 100.0f));
                        break;
                }
                if (goal == null)
                {
                    return goal;
                }
                foreach (var element in Config.Elements())
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "modifier":
                            {
                                var modifierType = element.GetAttributeString("type", "");
                                switch (modifierType)
                                {
                                    case "duration":
                                        goal = new Traitor.GoalWithDuration(goal, element.GetAttributeFloat("duration", 5.0f), element.GetAttributeBool("cumulative", false));
                                        break;
                                    case "timelimit":
                                        goal = new Traitor.GoalWithTimeLimit(goal, element.GetAttributeFloat("timelimit", 180.0f));
                                        break;
                                }
                            }
                            break;
                    }
                }
                foreach (var element in Config.Elements())
                {
                    switch (element.Name.ToString().ToLowerInvariant())
                    {
                        case "infotext":
                            {
                                var id = element.GetAttributeString("id", null);
                                if (id != null)
                                {
                                    goal.InfoTextId = id;
                                }
                            }
                            break;
                        case "completedtext":
                            {
                                var id = element.GetAttributeString("id", null);
                                if (id != null)
                                {
                                    goal.CompletedTextId = id;
                                }
                            }
                            break;
                    }
                }
                return goal;
            }
        }

        public class Objective
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

            public Traitor.Objective Instantiate()
            {
                var result = new Traitor.Objective(InfoText, ShuffleGoalsCount, Goals.ConvertAll(goal => {
                    var instance = goal.Instantiate();
                    if (instance == null)
                    {
                        GameServer.Log(string.Format("Failed to instantiate goal \"{0}\".", goal.Type), ServerLog.MessageType.Error);
                    }
                    return instance;
                }).FindAll(goal => goal != null).ToArray());
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
        /*
        public class Role
        {
            public string Job;
        }

        public readonly Dictionary<string, Role> Roles = new Dictionary<string, Role>();
        */
        public readonly string Identifier;
        public readonly string StartText;
        public readonly string EndMessageSuccessText;
        public readonly string EndMessageSuccessDeadText;
        public readonly string EndMessageSuccessDetainedText;
        public readonly string EndMessageFailureText;
        public readonly string EndMessageFailureDeadText;
        public readonly string EndMessageFailureDetainedText;
        
        public readonly List<Objective> Objectives = new List<Objective>();

        public Traitor.TraitorMission Instantiate()
        {
            return new Traitor.TraitorMission(
                StartText ?? "TraitorMissionStartMessage", 
                EndMessageSuccessText ?? "TraitorObjectiveEndMessageSuccess",
                EndMessageSuccessDeadText ?? "TraitorObjectiveEndMessageSuccessDead",
                EndMessageSuccessDetainedText ?? "TraitorObjectiveEndMessageSuccessDetained",
                EndMessageFailureText ?? "TraitorObjectiveEndMessageFailure",
                EndMessageFailureDeadText ?? "TraitorObjectiveEndMessageFailureDead",
                EndMessageFailureDetainedText ?? "TraitorObjectiveEndMessageFailureDetained",
                Objectives.ConvertAll(objective => objective.Instantiate()).ToArray());
        }

        protected Goal LoadGoal(XElement goalRoot)
        {
            var goalType = goalRoot.GetAttributeString("type", "");
            return new Goal(goalType, goalRoot);
        }

        protected Objective LoadObjective(XElement objectiveRoot)
        {
            var result = new Objective();
            result.ShuffleGoalsCount = objectiveRoot.GetAttributeInt("shuffleGoalsCount", -1);
            foreach (var element in objectiveRoot.Elements())
            {
                switch(element.Name.ToString().ToLowerInvariant())
                {
                    case "infotext":
                        result.InfoText = element.GetAttributeString("id", null);
                        break;
                    case "startmessage":
                        result.StartMessageTextId = element.GetAttributeString("id", null);
                        break;
                    case "startmessageserver":
                        result.StartMessageServerTextId = element.GetAttributeString("id", null);
                        break;
                    case "endmessagesuccess":
                        result.EndMessageSuccessTextId = element.GetAttributeString("id", null);
                        break;
                    case "endmessagesuccessdead":
                        result.EndMessageSuccessDeadTextId = element.GetAttributeString("id", null);
                        break;
                    case "endmessagesuccessdetained":
                        result.EndMessageSuccessDetainedTextId = element.GetAttributeString("id", null);
                        break;
                    case "endmessagefailure":
                        result.EndMessageFailureTextId = element.GetAttributeString("id", null);
                        break;
                    case "endmessagefailuredead":
                        result.EndMessageFailureDeadTextId = element.GetAttributeString("id", null);
                        break;
                    case "endmessagefailuredetained":
                        result.EndMessageFailureDetainedTextId = element.GetAttributeString("id", null);
                        break;
                    case "goal":
                        {
                            var goal = LoadGoal(element);
                            if (goal != null)
                            {
                                result.Goals.Add(goal);
                            }
                        }
                        break;
                }
            }
            return result;
        }

        public TraitorMissionPrefab(XElement missionRoot)
        {
            Identifier = missionRoot.GetAttributeString("identifier", null);
            foreach (var element in missionRoot.Elements())
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "startinfotext":
                        StartText = element.GetAttributeString("id", null);
                        break;
                    case "endmessagesuccess":
                        EndMessageSuccessText = element.GetAttributeString("id", null);
                        break;
                    case "endmessagesuccessdead":
                        EndMessageSuccessDeadText = element.GetAttributeString("id", null);
                        break;
                    case "endmessagesuccessdetained":
                        EndMessageSuccessDetainedText = element.GetAttributeString("id", null);
                        break;
                    case "endmessagefailure":
                        EndMessageFailureText = element.GetAttributeString("id", null);
                        break;
                    case "endmessagefailuredead":
                        EndMessageFailureDeadText = element.GetAttributeString("id", null);
                        break;
                    case "endmessagefailuredetained":
                        EndMessageFailureDetainedText = element.GetAttributeString("id", null);
                        break;
                    case "objective":
                        {
                            var objective = LoadObjective(element);
                            if (objective != null)
                            {
                                Objectives.Add(objective);
                            }
                        }
                        break;
                }
            }
        }
    }
}
