#define SERVER_IS_TRAITOR
#define ALLOW_SOLO_TRAITOR
using Barotrauma.Networking;
using System.Collections.Generic;
using System.IO;

namespace Barotrauma
{
    partial class Traitor
    {
        public readonly Character Character;

        public class Goal
        {
            public readonly Traitor Traitor;

            public virtual string StatusText => TextManager.GetFormatted("TraitorGoalStatusTextFormat", false, InfoText, IsCompleted ? "done" : "pending");

            public virtual string InfoText => "(not implemented)";
            public virtual string CompletedText => StatusText;
            // public virtual string DescriptionText => "(not implemented)";

            public virtual bool IsCompleted => false;

            public virtual void Update(float deltaTime)
            {
            }

            protected Goal()
            {
                throw new System.Exception("Attempt to instantiate Task with no implementation.");
            }

            protected Goal(Traitor traitor)
            {
                Traitor = traitor;
            }
        }

        public class Objective
        {
            public readonly Traitor Traitor;

            private readonly List<Goal> allGoals = new List<Goal>();
            private readonly List<Goal> pendingGoals = new List<Goal>();
            private readonly List<Goal> completedGoals = new List<Goal>();

            public bool IsCompleted => pendingGoals.Count <= 0;
            public bool IsPartiallyCompleted => completedGoals.Count > 0; 

            public string GoalInfos => string.Join("\n", allGoals.ConvertAll(goal => string.Concat("- ", goal.StatusText)));

            public virtual string GetStartMessageText()
            {
                return TextManager.GetWithVariable("TraitorObjectiveStartMessage", "[traitorgoalinfos]", GoalInfos);
            }

            public virtual string GetStartMessageTextServer()
            {
                return TextManager.GetWithVariables("TraitorObjectiveStartMessageServer", new string[] { "[traitorname] [traitorgoalinfos]" }, new string[] { Traitor.Character.Name, GoalInfos });
            }

            public virtual string GetEndMessageText()
            {
                var traitorIsDead = Traitor.Character.IsDead;
                var traitorIsDetained = Traitor.Character.LockHands;
                var messageTag = string.Format("TraitorObjectiveEndMessage{0}{1}", IsCompleted ? "Success" : "Failure", traitorIsDead ? "Dead" : traitorIsDetained ? "Detained" : "");
                return TextManager.ReplaceGenderPronouns(
                    TextManager.GetWithVariables(messageTag,
                        new string[2] { "[traitorname]", "[traitorgoalinfos]" },
                        new string[2] { Traitor.Character.Name, GoalInfos }
                    ), Traitor.Character.Info.Gender) + "\n";
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

            public Objective(Traitor traitor, params Goal[] goals)
            {
                Traitor = traitor;
                allGoals.AddRange(goals);
                pendingGoals.AddRange(goals);
            }
        }

        public class TraitorMission
        {
            private readonly List<Objective> pendingObjectives = new List<Objective>();
            private readonly List<Objective> completedObjectives = new List<Objective>();

            public virtual bool IsCompleted => pendingObjectives.Count <= 0;

            // TODO(xxx): Mission start, end messages

            public virtual void Update()
            {
                if (pendingObjectives.Count > 0) {
                    var objective = pendingObjectives[0];
                    if (objective.IsCompleted)
                    {
                        pendingObjectives.RemoveAt(0);
                        completedObjectives.Add(objective);
                        Client traitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == objective.Traitor.Character);
                        GameMain.Server.SendDirectChatMessage(objective.GetEndMessageText(), traitorClient);
                        if (pendingObjectives.Count > 0)
                        {
                            Objective nextObjective = pendingObjectives[0];
                            Client nextTraitorClient = GameMain.Server.ConnectedClients.Find(c => c.Character == objective.Traitor.Character);
                            GameMain.Server.SendDirectChatMessage(nextObjective.GetStartMessageText(), nextTraitorClient);
                        }
                    }
                } 
            }

            public TraitorMission(params Objective[] objectives) {
                pendingObjectives.AddRange(objectives);
            }
        }

        public class GoalDestroyCargo : Goal
        {
            public float DistanceFromSub;
        }

        public class GoalJamSonar : Goal
        {
        }

        public class GoalSabotagePumps : Goal
        {
            public float Duration;
        }

        public class GoalSabotageOxygenGenerator : Goal
        {
            public float Duration;
        }

        public class GoalSabotageDivingGear : Goal
        {
        }

        public class GoalInfectWithHusk : Goal
        {
            public Character Target;
        }

        public class GoalSabotageAmmo : Goal
        {
        }

        public class GoalSabotageEngine : Goal
        {
            public float Duration;
        }

        public class GoalKillTarget : Goal
        {
            public readonly Character Target;

            public override string InfoText => TextManager.GetWithVariable("TraitorGoalKillTargetInfo", "[targetname]", Target.Name);

            public override bool IsCompleted => Target.IsDead == true;
            
            public GoalKillTarget(Traitor traitor, Character target): base(traitor)
            {
                Target = target;
            }
        }

        public class GoalKillAllCrew : Goal
        {
        }

        public class GoalItemConditionLessThan : Goal
        {
            private readonly List<Item> targets = new List<Item>();
            private readonly float condition;

            public override bool IsCompleted => targets.TrueForAll(target => target.Condition <= condition);

            public GoalItemConditionLessThan(Traitor traitor, float condition, params Item[] targets): base(traitor)
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

            public GoalWithDuration(Traitor traitor, Goal goal, float requiredDuration, bool countTotalDuration) : base(traitor)
            {
                this.goal = goal;
                this.requiredDuration = requiredDuration;
                this.countTotalDuration = countTotalDuration;
            }
        }

        public class GoalCauseReactorMeltdown : GoalItemConditionLessThan
        {
            public override string InfoText => TextManager.Get("TraitorGoalCauseReactorMeltDown");

            private static Item FindReactor()
            {
                return GameMain.GameSession.Submarine.GetItems(false).Find(item => item.GetComponent<Items.Components.Reactor>() != null);
            }

            public GoalCauseReactorMeltdown(Traitor traitor) : base(traitor, 0.0f, FindReactor())
            {
            }
        }

        public class GoalDestroyAllNonInventoryItemsWithTag : Goal
        {
            private readonly string tag;

            private readonly float destroyPercent;
            protected float DestroyPercent => destroyPercent;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (isCompleted)
                {
                    return;
                }
                int total = 0;
                int destroyed = 0;
                foreach (var item in Item.ItemList) {
                    // TODO(xxx): Verify the conditions checked for here..
                    if (item == null || item.Prefab == null)
                    {
                        continue;
                    }
                    if (item.Submarine == null || item.Submarine.TeamID != Traitor.Character.TeamID || item.ParentInventory?.Owner is Character)
                    {
                        continue;
                    }
                    if (item.Prefab.Identifier != tag && !item.HasTag(tag))
                    {
                        continue;
                    }
                    ++total;
                    if (item.CurrentHull == null || item.Condition <= 0.0f || (!(Traitor.Character.Submarine?.IsEntityFoundOnThisSub(item, true) ?? true))) {
                        ++destroyed;
                    }
                }
                isCompleted |= destroyed >= (int)(total * destroyPercent);
            }

            public GoalDestroyAllNonInventoryItemsWithTag(Traitor traitor, string tag, float destroyPercent) : base(traitor) {
                this.tag = tag;
                this.destroyPercent = destroyPercent;
            }
        }

        public class GoalDestroyOxygenTanks : GoalDestroyAllNonInventoryItemsWithTag
        {
            public override string InfoText => TextManager.GetFormatted("TraitorGoalDestroyAllOxygenTanks", false, 100.0f * DestroyPercent);

            public GoalDestroyOxygenTanks(Traitor traitor) : base(traitor, "oxygensource", 0.5f)
            {
            }
        }

        public class GoalFloodPercentOfSub : Goal
        {
            private readonly float minimumFloodingAmount;

            public override string InfoText => TextManager.GetFormatted("TraitorGoalFloodPercentOfSub", false, 100.0f * minimumFloodingAmount);

            public override bool IsCompleted => GameMain.GameSession.EventManager.CurrentFloodingAmount >= minimumFloodingAmount;

            public GoalFloodPercentOfSub(Traitor traitor, float minimumFloodingAmount) : base(traitor)
            {
                this.minimumFloodingAmount = minimumFloodingAmount;
            }
        }

        public class GoalDetonateLocations : Goal
        {
            public class Location { } // TODO(xxx): Best way to identify target locations?
            public readonly List<Location> Locations = new List<Location>();
        }

        public Objective CurrentObjective;

        public Traitor(Character character)
        {
            Character = character;
        }

        public void Greet(GameServer server, string codeWords, string codeResponse)
        {
            string greetingMessage = CurrentObjective.GetStartMessageText();
            string moreAgentsMessage = TextManager.GetWithVariables("TraitorMoreAgentsMessage",
                new string[2] { "[codewords]", "[coderesponse]" }, new string[2] { codeWords, codeResponse });
            
            var greetingChatMsg = ChatMessage.Create(null, greetingMessage, ChatMessageType.Server, null);
            var moreAgentsChatMsg = ChatMessage.Create(null, moreAgentsMessage, ChatMessageType.Server, null);

            var moreAgentsMsgBox = ChatMessage.Create(null, moreAgentsMessage, ChatMessageType.MessageBox, null);
            var greetingMsgBox = ChatMessage.Create(null, greetingMessage, ChatMessageType.MessageBox, null);

            Client traitorClient = server.ConnectedClients.Find(c => c.Character == Character);
            GameMain.Server.SendDirectChatMessage(greetingChatMsg, traitorClient);
            GameMain.Server.SendDirectChatMessage(moreAgentsChatMsg, traitorClient);
            GameMain.Server.SendDirectChatMessage(greetingMsgBox, traitorClient);
            GameMain.Server.SendDirectChatMessage(moreAgentsMsgBox, traitorClient);

            Client ownerClient = server.ConnectedClients.Find(c => c.Connection == server.OwnerConnection);
            if (traitorClient != ownerClient && ownerClient != null && ownerClient.Character == null)
            {
                var ownerMsg = ChatMessage.Create(
                    null,//TextManager.Get("NewTraitor"),
                    CurrentObjective.GetStartMessageTextServer(),
                    ChatMessageType.MessageBox,
                    null
                );
                GameMain.Server.SendDirectChatMessage(ownerMsg, ownerClient);
            }
        }
    }

    partial class TraitorManager
    {
        private static string wordsTxt = Path.Combine("Content", "CodeWords.txt");

        public List<Traitor> TraitorList
        {
            get { return traitorList; }
        }

        private List<Traitor> traitorList = new List<Traitor>();
        private Traitor.TraitorMission mission = new Traitor.TraitorMission();

        public string codeWords, codeResponse;

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
            if (server == null) return;

            List<Character> characters = new List<Character>(); //ANYONE can be a target.
            List<Character> traitorCandidates = new List<Character>(); //Keep this to not re-pick traitors twice
            List<Character> targetCandidates = new List<Character>(); // Target candidates in shuffled order

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
                    targetCandidates.Add(client.Character);
                }
            }

            if (server.Character != null)
            {
                characters.Add(server.Character); //Add host character
                traitorCandidates.Add(server.Character);
                targetCandidates.Add(server.Character);
            }
#if !ALLOW_SOLO_TRAITOR
            if (characters.Count < 2)
            {
                return;
            }
#endif

            codeWords = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);
            codeResponse = ToolBox.GetRandomLine(wordsTxt) + ", " + ToolBox.GetRandomLine(wordsTxt);

            while (traitorCount-- > 0 && traitorCandidates.Count > 0)
            {
                int traitorIndex = Rand.Int(traitorCandidates.Count);
                Character traitorCharacter = traitorCandidates[traitorIndex];
                traitorCandidates.Remove(traitorCharacter);

                //Add them to the list
                traitorList.Add(new Traitor(traitorCharacter));
            }
            var targetCandidatesCount = targetCandidates.Count;
            for (int i = 0; i < targetCandidatesCount; ++i)
            {
                int swapIndex = Rand.Int(targetCandidatesCount);
                if (i != swapIndex)
                {
                    var temp = targetCandidates[i];
                    traitorCandidates[i] = targetCandidates[swapIndex];
                    targetCandidates[swapIndex] = temp;
                }
            }
            //Now that traitors have been decided, let's do objectives in post for deciding things like Document Exchange.
            foreach (Traitor traitor in traitorList)
            {
                Character traitorCharacter = traitor.Character;
                int startPosition = Rand.Int(targetCandidatesCount);
                int targetsCount = 1 + Rand.Int(targetCandidatesCount - 1);
                List<Traitor.Goal> goals = new List<Traitor.Goal>(targetsCount);
                for (int i = 0; i < targetsCount;)
                {
                    var candidate = targetCandidates[(startPosition + i) % targetCandidatesCount];
#if !ALLOW_SOLO_TRAITOR
                    if (candidate != traitor.Character)
#endif
                    {
                        goals.Add(new Traitor.GoalKillTarget(traitor, candidate));
                        ++i;
                    }
                }
                goals.Add(new Traitor.GoalCauseReactorMeltdown(traitor));
                goals.Add(new Traitor.GoalDestroyOxygenTanks(traitor));
                goals.Add(new Traitor.GoalFloodPercentOfSub(traitor, 0.5f));
                traitor.CurrentObjective = new Traitor.Objective(traitor, goals.ToArray());
                traitor.Greet(server, codeWords, codeResponse);
            }
        }

        public void Update(float deltaTime)
        {
            traitorList.ForEach(traitor => traitor.CurrentObjective?.Update(deltaTime));
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
            if (GameMain.Server == null || traitorList.Count <= 0) return "";

            string endMessage = "";

            foreach (Traitor traitor in traitorList)
            {
                endMessage += traitor.CurrentObjective.GetEndMessageText();
            }

            return endMessage;
        }
    }
}
