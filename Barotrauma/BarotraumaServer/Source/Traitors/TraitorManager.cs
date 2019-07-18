// #define DISABLE_MISSIONS
#define SERVER_IS_TRAITOR
#define ALLOW_SOLO_TRAITOR
using Barotrauma.Networking;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public readonly Character Character;

        public class Goal
        {
            public Traitor Traitor { get; private set; }
            public TraitorMission Mission { get; internal set; }

            public virtual string StatusTextId { get; set; } = "TraitorGoalStatusTextFormat";
            public virtual string InfoTextId { get; set; } = null;
            public virtual string CompletedTextId { get; set; } = null;

            public virtual IEnumerable<string> StatusTextKeys => new string[] { "[infotext]", "[status]" };
            public virtual IEnumerable<string> StatusTextValues => new string[] {
                InfoText,
                TextManager.Get(IsCompleted ? "done" : "pending")
            };

            public virtual IEnumerable<string> InfoTextKeys => new string[] { };
            public virtual IEnumerable<string> InfoTextValues => new string[] { };

            public virtual IEnumerable<string> CompletedTextKeys => new string[] { };
            public virtual IEnumerable<string> CompletedTextValues => new string[] { };

            public virtual string StatusText => TextManager.GetWithVariables(StatusTextId, StatusTextKeys.ToArray(), StatusTextValues.ToArray());
            public virtual string InfoText => TextManager.GetWithVariables(InfoTextId, InfoTextKeys.ToArray(), InfoTextValues.ToArray());
            public virtual string CompletedText => CompletedTextId != null ? TextManager.GetWithVariables(CompletedTextId, CompletedTextKeys.ToArray(), CompletedTextValues.ToArray()) : StatusText;

            public virtual bool IsCompleted => false;

            public virtual bool Start(GameServer server, Traitor traitor)
            {
                Traitor = traitor;
                return true;
            }

            public virtual void Update(float deltaTime)
            {
            }

            protected Goal()
            {
            }
        }

        public class Objective
        {
            public Traitor Traitor { get; private set; }

            private readonly List<Goal> allGoals = new List<Goal>();
            private readonly List<Goal> pendingGoals = new List<Goal>();
            private readonly List<Goal> completedGoals = new List<Goal>();

            public bool IsCompleted => pendingGoals.Count <= 0;
            public bool IsPartiallyCompleted => completedGoals.Count > 0;
            public bool IsStarted { get; private set; } = false;

            public string InfoText { get; private set; } // TODO

            public virtual string GoalInfoFormatId { get; set; } = "TraitorObjectiveGoalInfoFormat";
            public string GoalInfos => string.Join("", allGoals.ConvertAll(goal => TextManager.GetWithVariables(GoalInfoFormatId, new string[] {
                "[statustext]"
            }, new string[] {
                goal.StatusText
            })));

            public virtual string StartMessageTextId { get; set; } = "TraitorObjectiveStartMessage";
            public virtual IEnumerable<string> StartMessageKeys => new string[] { "[traitorgoalinfos]" };
            public virtual IEnumerable<string> StartMessageValues => new string[] { GoalInfos };

            public virtual string StartMessageText => TextManager.GetWithVariables(StartMessageTextId, StartMessageKeys.ToArray(), StartMessageValues.ToArray());

            public virtual string StartMessageServerTextId { get; set; } = "TraitorObjectiveStartMessageServer";
            public virtual IEnumerable<string> StartMessageServerKeys => StartMessageKeys.Concat(new string[] { "[traitorname]" });
            public virtual IEnumerable<string> StartMessageServerValues => StartMessageValues.Concat(new string[] { Traitor?.Character?.Name ?? "(unknown)" });

            public virtual string StartMessageServerText => TextManager.GetWithVariables(StartMessageServerTextId, StartMessageServerKeys.ToArray(), StartMessageServerValues.ToArray());

            public virtual string EndMessageSuccessTextId { get; set; } = "TraitorObjectiveSuccess";
            public virtual string EndMessageSuccessDeadTextId { get; set; } = "TraitorObjectiveSuccessDead";
            public virtual string EndMessageSuccessDetainedTextId { get; set; } = "TraitorObjectiveSuccessDetained";
            public virtual string EndMessageFailureTextId { get; set; } = "TraitorObjectiveFailure";
            public virtual string EndMessageFailureDeadTextId { get; set; } = "TraitorObjectiveFailureDead";
            public virtual string EndMessageFailureDetainedTextId { get; set; } = "TraitorObjectiveFailureDetained";

            public virtual IEnumerable<string> EndMessageKeys => new string[] { "[traitorname]", "[traitorgoalinfos]" };
            public virtual IEnumerable<string> EndMessageValues => new string[] { Traitor?.Character?.Name ?? "(unknown)", GoalInfos };
            public virtual string EndMessageText
            {
                get
                {
                    var traitorIsDead = Traitor.Character.IsDead;
                    var traitorIsDetained = Traitor.Character.LockHands;
                    var messageId = IsCompleted
                        ? (traitorIsDead ? EndMessageSuccessDeadTextId : traitorIsDetained ? EndMessageSuccessDetainedTextId : EndMessageSuccessTextId)
                        : (traitorIsDead ? EndMessageFailureDeadTextId : traitorIsDetained ? EndMessageFailureDetainedTextId : EndMessageFailureTextId);
                    return TextManager.ReplaceGenderPronouns(TextManager.GetWithVariables(messageId, EndMessageKeys.ToArray(), EndMessageValues.ToArray()), Traitor.Character.Info.Gender);
                }
            }

            public bool Start(GameServer server, Traitor traitor)
            {
                Traitor = traitor;
                for (var i = 0; i < pendingGoals.Count; ++i)
                {
                    var goal = pendingGoals[i];
                    if (goal.Start(server, traitor))
                    {
                        ++i;
                    }
                    else
                    {
                        completedGoals.Add(goal);
                        pendingGoals.RemoveAt(i);
                    }
                }
                if (pendingGoals.Count <= 0)
                {
                    return false;
                }
                IsStarted = true;
                Client traitorClient = server.ConnectedClients.Find(c => c.Character == traitor.Character);
                GameMain.Server.SendDirectChatMessage(StartMessageText, traitorClient);
                GameMain.Server.SendDirectChatMessage(ChatMessage.Create(null, StartMessageText, ChatMessageType.MessageBox, null), traitorClient);
                return true;
            }

            public void End(GameServer server)
            {
                Client traitorClient = server.ConnectedClients.Find(c => c.Character == Traitor.Character);
                GameMain.Server.SendDirectChatMessage(EndMessageText, traitorClient);
                GameMain.Server.SendDirectChatMessage(ChatMessage.Create(null, EndMessageText, ChatMessageType.MessageBox, null), traitorClient);
            }

            public void Update(float deltaTime)
            {
                for (int i = 0; i < pendingGoals.Count;) {
                    var goal = pendingGoals[i];
                    goal.Update(deltaTime);
                    if (!goal.IsCompleted) {
                        ++i;
                    } else {
                        completedGoals.Add(goal);
                        pendingGoals.RemoveAt(i);
                        Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == goal.Traitor.Character);
                        GameMain.Server.SendDirectChatMessage(goal.CompletedText, traitorClient);
                    }
                }
            }

            public Objective(string infoText, params Goal[] goals)
            {
                InfoText = infoText;
                allGoals.AddRange(goals);
                pendingGoals.AddRange(goals);
            }
        }

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
                while (pendingObjectives.Count > 0) {
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

            public TraitorMission(string startText, params Objective[] objectives) {
                StartText = startText;
                allObjectives.AddRange(objectives);
                pendingObjectives.AddRange(objectives);
            }
        }

        public class GoalKillTarget : Goal
        {
            public TraitorMission.CharacterFilter Filter { get; private set; }
            public Character Target { get; private set; }

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { Target?.Name ?? "(unknown)" });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = Target?.IsDead ?? false;
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                Target = traitor.Mission.FindKillTarget(server, traitor.Character, Filter);
                return Target != null && !Target.IsDead;
            }

            public GoalKillTarget(TraitorMission.CharacterFilter filter) : base()
            {
                InfoTextId = "TraitorGoalKillTargetInfo";
                Filter = filter;
            }
        }

        public class GoalItemConditionLessThan : Goal
        {
            private readonly List<Item> targets = new List<Item>();
            private readonly float condition;

            public override bool IsCompleted => targets.TrueForAll(target => target.Condition <= condition);

            public GoalItemConditionLessThan(float condition, params Item[] targets): base()
            {
                this.targets.AddRange(targets);
                this.condition = condition;
            }
        }

        public class GoalWithDuration : Goal
        {
            private readonly Goal goal;
            private readonly float requiredDuration;
            private readonly bool countTotalDuration;

            private bool isCompleted = false;
            private float remainingDuration = float.NaN;

            public override IEnumerable<string> StatusTextKeys => goal.StatusTextKeys;
            public override IEnumerable<string> StatusTextValues => goal.StatusTextValues;

            public override IEnumerable<string> InfoTextKeys => goal.InfoTextKeys.Concat(new string[] { "[duration]" });
            public override IEnumerable<string> InfoTextValues => goal.InfoTextValues.Concat(new string[] { string.Format("{0:f}", requiredDuration) });

            public override IEnumerable<string> CompletedTextKeys => goal.CompletedTextKeys;
            public override IEnumerable<string> CompletedTextValues => goal.CompletedTextValues;

            public override string StatusText => goal.StatusText;
            public override string InfoText => goal.InfoText;
            public override string CompletedText => goal.CompletedText;

            public override bool IsCompleted => isCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (goal.IsCompleted)
                {
                    if (!float.IsNaN(remainingDuration))
                    {
                        remainingDuration -= deltaTime;
                    }
                    else
                    {
                        remainingDuration = requiredDuration;
                    }
                    isCompleted |= remainingDuration <= 0.0f;
                }
                else if (!countTotalDuration)
                {
                    remainingDuration = float.NaN;
                }
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                return goal.Start(server, traitor);
            }

            public GoalWithDuration(Goal goal, float requiredDuration, bool countTotalDuration) : base()
            {
                this.goal = goal;
                this.requiredDuration = requiredDuration;
                this.countTotalDuration = countTotalDuration;
            }
        }

        public class GoalCauseReactorMeltdown : GoalItemConditionLessThan
        {
            private static Item FindReactor()
            {
                return GameMain.GameSession.Submarine.GetItems(false).Find(item => item.GetComponent<Items.Components.Reactor>() != null);
            }

            public GoalCauseReactorMeltdown() : base(0.0f, FindReactor())
            {
                InfoTextId = "TraitorGoalCauseReactorMeltDown";
            }
        }

        public class GoalSabotageItem : Goal
        {
            private readonly string tag;
            private readonly float conditionThreshold;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[tag]", "[conditionthreshold]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { tag, string.Format("{0:f}", conditionThreshold * 100.0f });

            private bool isCompleted = false;
            public override bool IsCompleted => IsCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
            }

            public GoalSabotageItem(string tag, float conditionThreshold) : base() {
                this.tag = tag;
                this.conditionThreshold = conditionThreshold;
                foreach (var item in Item.ItemList)
                {
                    // if (item.GetComponent
                }

            }
        }

        public class GoalDestroyItemsWithTag : Goal
        {
            private readonly string tag;
            private readonly bool matchIdentifier;
            private readonly bool matchTag;
            private readonly bool matchInventory;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[percentage]", "[tag]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:f}", DestroyPercent * 100.0f), tag });

            private readonly float destroyPercent;
            protected float DestroyPercent => destroyPercent;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private int totalCount = 0;
            private int targetCount = 0;

            protected int CountMatchingItems(bool includeDestroyed)
            {
                int result = 0;
                foreach (var item in Item.ItemList)
                {
                    if (item == null || item.Prefab == null)
                    {
                        continue;
                    }
                    if (item.Submarine == null || item.Submarine.TeamID != Traitor.Character.TeamID)
                    {
                        continue;
                    }
                    if (!matchInventory && item.ParentInventory?.Owner is Character)
                    {
                        continue;
                    }
                    if (!includeDestroyed && (item.Condition <= 0.0f || /* item.CurrentHull == null || */!Traitor.Character.Submarine.IsEntityFoundOnThisSub(item, true)))
                    {
                        continue;
                    }
                    if ((matchIdentifier && item.prefab.Identifier == tag) || (matchTag && item.HasTag(tag))) {
                        ++result;
                    }
                }
                return result;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = CountMatchingItems(false) <= targetCount;
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                totalCount = CountMatchingItems(true);
                if (totalCount <= 0)
                {
                    return false;
                }
                targetCount = (int)(destroyPercent * totalCount);
                return true;
            }

            public GoalDestroyItemsWithTag(string tag, float destroyPercent, bool matchTag, bool matchIdentifier, bool matchInventory) : base() {
                InfoTextId = "TraitorGoalDestroyItems";
                this.tag = tag;
                this.destroyPercent = destroyPercent;
                this.matchTag = matchTag;
                this.matchIdentifier = matchIdentifier;
                this.matchInventory = matchInventory;
            }
        }

        public class GoalFloodPercentOfSub : Goal
        {
            private readonly float minimumFloodingAmount;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[percentage]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:f}", minimumFloodingAmount * 100.0f) });

            public override bool IsCompleted => GameMain.GameSession.EventManager.CurrentFloodingAmount >= minimumFloodingAmount;

            public GoalFloodPercentOfSub(float minimumFloodingAmount) : base()
            {
                InfoTextId = "TraitorGoalFloodPercentOfSub";
                this.minimumFloodingAmount = minimumFloodingAmount;
            }
        }

        public class GoalDetonateLocations : Goal
        {
            public class Location { } // TODO(xxx): Best way to identify target locations?
            public readonly List<Location> Locations = new List<Location>();
        }

        public string Role { get; private set; }
        public TraitorMission Mission { get; private set; }
        public Objective CurrentObjective => Mission.GetCurrentObjective(this);

        public Traitor(TraitorMission mission, string role, Character character)
        {
            Mission = mission;
            Role = role;
            Character = character;
        }

        public void Greet(GameServer server, string codeWords, string codeResponse)
        {
            string greetingMessage = TextManager.GetWithVariables(Mission.StartText, new string[] {
                "[codewords]", "[coderesponse]"
            }, new string[] {
                codeWords, codeResponse
            });
            var greetingChatMsg = ChatMessage.Create(null, greetingMessage, ChatMessageType.Server, null);
            var greetingMsgBox = ChatMessage.Create(null, greetingMessage, ChatMessageType.MessageBox, null);

            Client traitorClient = server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendDirectChatMessage(greetingChatMsg, traitorClient);
            GameMain.Server.SendDirectChatMessage(greetingMsgBox, traitorClient);

            Client ownerClient = server.ConnectedClients.Find(c => c.Connection == server.OwnerConnection);
            if (traitorClient != ownerClient && ownerClient != null && ownerClient.Character == null)
            {
                var ownerMsg = ChatMessage.Create(
                    null,//TextManager.Get("NewTraitor"),
                    CurrentObjective.StartMessageServerText,
                    ChatMessageType.MessageBox,
                    null
                );
                GameMain.Server.SendDirectChatMessage(ownerMsg, ownerClient);
            }
        }
    }

    partial class TraitorManager
    {
        public Traitor.TraitorMission Mission { get; private set; }
        public string CodeWords => Mission?.CodeWords;
        public string CodeResponse => Mission?.CodeResponse;

        public Dictionary<string, Traitor>.ValueCollection Traitors => Mission?.Traitors?.Values;

        public TraitorManager(GameServer server, int traitorCount)
        {
            if (traitorCount < 1) //what why how
            {
                traitorCount = 1;
                DebugConsole.ThrowError("Traitor Manager: TraitorCount somehow ended up less than 1, setting it to 1.");
            }
            Start(server, traitorCount);
        }

        private void Start(GameServer server, int traitorCount)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (server == null) return;
            Mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
            if (Mission != null)
            {
                Mission.Start(server, "traitor");
            }
        }

        public void Update(float deltaTime)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (Mission != null)
            {
                Mission.Update(deltaTime);
            }
        }

        public void CargoDestroyed()
        {
        }

        Dictionary<System.Type, System.Action<Barotrauma.Items.Components.ItemComponent>> sabotageItemHandlers = new Dictionary<System.Type, System.Action<Barotrauma.Items.Components.ItemComponent>> {
            {
                typeof(Barotrauma.Items.Components.Sonar), (sonar) =>
                {
                    System.Diagnostics.Debug.WriteLine("Sabotage sonar");
                }
            },
            {
                typeof(Barotrauma.Items.Components.Pump), (pump) =>
                {
                    System.Diagnostics.Debug.WriteLine("Sabotage pump");
                }
            },
            {
                typeof(Barotrauma.Items.Components.Reactor), (reactor) =>
                {
                    System.Diagnostics.Debug.WriteLine("Sabotage reactor");
                }
            }/*,
            {
                typeof(Barotrauma.Items.Components.Mask//
            }*/

        };

        public void SabotageItem(Barotrauma.Item item)
        {
            // TODO: Best way of recognizing items to sabotage? We also need to maintain an item count for each type we're interested in.
            if (item.Tags.Contains("oxygensource"))
            {
            }

            foreach (var component in item.Components) {
                if (sabotageItemHandlers.TryGetValue(component.GetType(), out var handler))
                {
                    handler(component);
                }
            }
        }
    
        public string GetEndMessage()
        {
#if DISABLE_MISSIONS
            return "";
#endif
            if (GameMain.Server == null || Mission == null) return "";

            string endMessage = "";

            foreach (var traitor in Mission.Traitors)
            {
                endMessage += traitor.Value.CurrentObjective?.EndMessageText ?? "";
            }

            return endMessage;
        }
    }
}
