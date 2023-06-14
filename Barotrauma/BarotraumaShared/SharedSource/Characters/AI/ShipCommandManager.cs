using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ShipCommandManager
    {
        public readonly Character character;
        public readonly HumanAIController humanAIController;

        private bool active;
        public bool Active
        {
            get { return active; }
            set
            {
                active = value ? TryInitializeShipCommandManager() : value;
            }
        }

        public Submarine EnemySubmarine
        {
            get;
            private set;
        }

        public Submarine CommandedSubmarine
        {
            get;
            private set;
        }

        private Steering steering;
        public readonly List<Vector2> patrolPositions = new List<Vector2>();
        public enum NavigationStates
        {
            Inactive,
            Patrol,
            Aggressive
        }

        public NavigationStates NavigationState { get; private set; } = NavigationStates.Inactive;

        float navigationTimer = 0f;
        private readonly float navigationInterval = 4f;

        float timeUntilRam;
        private const float RamTimerMax = 17.5f;

        public readonly List<ShipIssueWorker> ShipIssueWorkers = new List<ShipIssueWorker>();
        public const float MinimumIssueThreshold = 10f;
        private const float IssueDevotionBuffer = 5f;

        private float decisionTimer = 6f;
        private readonly float decisionInterval = 6f;

        private float timeSinceLastCommandDecision;
        private float timeSinceLastNavigation;

        public readonly List<Character> AlliedCharacters = new List<Character>();
        public readonly List<Character> EnemyCharacters = new List<Character>();

        private readonly List<ShipIssueWorker> attendedIssues = new List<ShipIssueWorker>();
        private readonly List<ShipIssueWorker> availableIssues = new List<ShipIssueWorker>();
        private readonly List<ShipGlobalIssue> shipGlobalIssues = new List<ShipGlobalIssue>();

        public ShipCommandManager(Character character)
        {
            this.character = character;
            humanAIController = character.AIController as HumanAIController;
        }

        public void Update(float deltaTime)
        {
            if (!Active || character.IsArrested) { return; }
            decisionTimer -= deltaTime;
            if (decisionTimer <= 0.0f)
            {
                UpdateCommandDecision(timeSinceLastCommandDecision);
                decisionTimer = decisionInterval * Rand.Range(0.8f, 1.2f);
                timeSinceLastCommandDecision = decisionTimer;
            }

            navigationTimer -= deltaTime;
            if (navigationTimer <= 0.0f)
            {
                UpdateNavigation(timeSinceLastNavigation);
                navigationTimer = navigationInterval * Rand.Range(0.8f, 1.2f);
                timeSinceLastNavigation = navigationTimer;
            }
        }

        public static void ShipCommandLog(string text)
        {
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                DebugConsole.NewMessage(text);
            }
        }

        static bool WithinRange(float range, float distanceSquared)
        {
            return range * range > distanceSquared;
        }

        void UpdateNavigation(float timeSinceLastUpdate)
        {
            if (steering == null || EnemySubmarine == null)
            {
                return;
            }

            float distanceSquaredEnemy = Vector2.DistanceSquared(CommandedSubmarine.WorldPosition, EnemySubmarine.WorldPosition);

            if (NavigationState != NavigationStates.Aggressive)
            {
                if (WithinRange(7000f, distanceSquaredEnemy))
                {
#if DEBUG
                    ShipCommandLog("Ship " + CommandedSubmarine + " was within the aggro range of " + EnemySubmarine);
#endif
                    NavigationState = NavigationStates.Aggressive;
                }
                else if (WithinRange(40000f, distanceSquaredEnemy))
                {
                    NavigationState = NavigationStates.Patrol;
                }
            }

            if (NavigationState == NavigationStates.Aggressive)
            {
                steering.AITacticalTarget = EnemySubmarine.WorldPosition;
                if (WithinRange(8500f, distanceSquaredEnemy) && !WithinRange(1500f, distanceSquaredEnemy)) // if we are within enemy ship's range for ramTimerMax, try to ram them instead (if we're not already very close)
                {
                    if (steering.AIRamTimer > 0f)
                    {
#if DEBUG
                        ShipCommandLog("Ship " + CommandedSubmarine + " was still ramming, " + steering.AIRamTimer + " left");
#endif
                    }
                    else
                    {
                        timeUntilRam -= timeSinceLastUpdate;
#if DEBUG
                        ShipCommandLog("Ship " + CommandedSubmarine + " was close enough to ram, " + timeUntilRam + " left until ramming");
#endif

                        if (timeUntilRam <= 0f)
                        {
#if DEBUG
                            ShipCommandLog("Ship " + CommandedSubmarine + " is attempting to ram!");
#endif
                            steering.AIRamTimer = 50f;
                            timeUntilRam = RamTimerMax * Rand.Range(0.9f, 1.1f);
                        }
                    }
                }
                else
                {
                    steering.AIRamTimer = 0f;
                    timeUntilRam = RamTimerMax * Rand.Range(0.9f, 1.1f);
                }
            }
            else if (patrolPositions.Any())
            {
                float distanceSquaredPatrol = Vector2.DistanceSquared(CommandedSubmarine.WorldPosition, patrolPositions.First());

                if (WithinRange(7000f, distanceSquaredPatrol))
                {
                    Vector2 lastPosition = patrolPositions.First();
                    patrolPositions.RemoveAt(0);
                    patrolPositions.Add(lastPosition);
                }
                steering.AITacticalTarget = patrolPositions.First();
            }
        }

        public bool AbleToTakeOrder(Character character)
        {
            return !character.IsIncapacitated && !character.LockHands && character.Submarine == CommandedSubmarine;
        }

        void UpdateCommandDecision(float timeSinceLastUpdate)
        {

#if DEBUG
            ShipCommandLog("Updating command for character " + character);
#endif

            shipGlobalIssues.ForEach(c => c.CalculateGlobalIssue());

            AlliedCharacters.Clear();
            EnemyCharacters.Clear();

            bool isEmergency = false;

            foreach (Character potentialCharacter in Character.CharacterList)
            {
                if (!HumanAIController.IsActive(potentialCharacter)) { continue; }

                if (HumanAIController.IsFriendly(character, potentialCharacter, true) && potentialCharacter.AIController is HumanAIController)
                {
                    if (AbleToTakeOrder(potentialCharacter))
                    {
                        AlliedCharacters.Add(potentialCharacter);
                    }
                }
                else
                {
                    EnemyCharacters.Add(potentialCharacter);
                    if (potentialCharacter.Submarine == CommandedSubmarine) // if enemies are on board, don't issue normal orders anymore
                    {
                        isEmergency = true;
                    }
                }
            }

            attendedIssues.Clear();
            availableIssues.Clear();

            foreach (ShipIssueWorker shipIssueWorker in ShipIssueWorkers)
            {
                float importance = shipIssueWorker.CalculateImportance(isEmergency);
                if (shipIssueWorker.OrderAttendedTo(timeSinceLastUpdate))
                {
#if DEBUG
                    ShipCommandLog("Current importance for " + shipIssueWorker + " was " + importance + " and it was already being attended by " + shipIssueWorker.OrderedCharacter);
#endif
                    InsertIssue(shipIssueWorker, attendedIssues);
                }
                else
                {
#if DEBUG
                    ShipCommandLog("Current importance for " + shipIssueWorker + " was " + importance + " and it is not attended to");
#endif
                    shipIssueWorker.RemoveOrder();
                    InsertIssue(shipIssueWorker, availableIssues);
                }
            }

            static void InsertIssue(ShipIssueWorker issue, List<ShipIssueWorker> list)
            {
                int index = 0;
                while (index < list.Count && list[index].Importance > issue.Importance)
                {
                    index++;
                }
                list.Insert(index, issue);
            }

            ShipIssueWorker mostImportantIssue = availableIssues.FirstOrDefault();

            float bestValue = 0f;
            Character bestCharacter = null;

            if (mostImportantIssue != null && mostImportantIssue.Importance >= MinimumIssueThreshold)
            {
                IEnumerable<Character> bestCharacters = CrewManager.GetCharactersSortedForOrder(mostImportantIssue.SuggestedOrder, AlliedCharacters, character, true);

                foreach (Character orderedCharacter in bestCharacters)
                {
                    float issueApplicability = mostImportantIssue.Importance;

                    // prefer not to switch if not qualified
                    issueApplicability *= mostImportantIssue.SuggestedOrder.AppropriateJobs.Contains(orderedCharacter.Info.Job.Prefab.Identifier) ? 1f : 0.75f;
                    
                    ShipIssueWorker occupiedIssue = attendedIssues.FirstOrDefault(i => i.OrderedCharacter == orderedCharacter);

                    if (occupiedIssue != null)
                    {
                        if (occupiedIssue.GetType() == mostImportantIssue.GetType() && mostImportantIssue is ShipIssueWorkerGlobal && occupiedIssue is ShipIssueWorkerGlobal)
                        {
                            continue;
                        }

                        // reverse redundancy to ensure certain issues can be switched over easily (operating weapons)
                        if (mostImportantIssue.AllowEasySwitching && occupiedIssue.AllowEasySwitching)
                        {
                            issueApplicability /= mostImportantIssue.CurrentRedundancy;
                        }

                        // give slight preference if not qualified for current job
                        issueApplicability += occupiedIssue.SuggestedOrder.AppropriateJobs.Contains(orderedCharacter.Info.Job.Prefab.Identifier) ? 0 : 7.5f;

                        // prefer not to switch orders unless considerably more important
                        issueApplicability -= IssueDevotionBuffer;

                        if (issueApplicability + IssueDevotionBuffer < occupiedIssue.Importance)
                        {
                            continue;
                        }
                    }

                    // prefer first one in bestCharacters in tiebreakers
                    if (issueApplicability > bestValue)
                    {
                        bestValue = issueApplicability;
                        bestCharacter = orderedCharacter;
                    }
                }
            }

            if (bestCharacter != null && mostImportantIssue != null)
            {
#if DEBUG
                ShipCommandLog("Setting " + mostImportantIssue + " for character " + bestCharacter);
#endif
                mostImportantIssue.SetOrder(bestCharacter);
            }
            else  // if we didn't give an order, let's try to dismiss someone instead
            {
                foreach (ShipIssueWorker shipIssueWorker in ShipIssueWorkers)
                {
                    if (shipIssueWorker.Importance <= 0f && shipIssueWorker.OrderAttendedTo())
                    {
#if DEBUG
                        ShipCommandLog("Dismissing " + shipIssueWorker + " for character " + shipIssueWorker.OrderedCharacter);
#endif
                        var order = new Order(OrderPrefab.Dismissal, null).WithManualPriority(3).WithOrderGiver(character);
                        shipIssueWorker.OrderedCharacter.SetOrder(order, isNewOrder: true);
                        shipIssueWorker.RemoveOrder();
                        break;
                    }
                }
            }
        }

        bool TryInitializeShipCommandManager()
        {
            CommandedSubmarine = character.Submarine;

            if (CommandedSubmarine == null)
            {
                DebugConsole.ThrowError("TryInitializeShipCommandManager failed: CommandedSubmarine was null for character " + character);
                return false;
            }

            EnemySubmarine = Submarine.MainSubs[0] == CommandedSubmarine ? Submarine.MainSubs[1] : Submarine.MainSubs[0];

            if (EnemySubmarine == null)
            {
                DebugConsole.ThrowError("TryInitializeShipCommandManager failed: EnemySubmarine was null for character " + character);
                return false;
            }

            timeUntilRam = RamTimerMax * Rand.Range(0.9f, 1.1f);

            ShipIssueWorkers.Clear();

            if (CommandedSubmarine.GetItems(false).Find(i => i.HasTag("reactor") && !i.NonInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                var order = new Order(OrderPrefab.Prefabs["operatereactor"], "powerup".ToIdentifier(), reactor.Item, reactor);
                ShipIssueWorkers.Add(new ShipIssueWorkerPowerUpReactor(this, order));
            }

            if (CommandedSubmarine.GetItems(false).Find(i => i.HasTag("navterminal") && !i.NonInteractable) is Item nav && nav.GetComponent<Steering>() is Steering steeringComponent)
            {
                steering = steeringComponent;
                var order = new Order(OrderPrefab.Prefabs["steer"], "navigatetactical".ToIdentifier(), nav, steeringComponent);
                ShipIssueWorkers.Add(new ShipIssueWorkerSteer(this, order));
            }

            foreach (Item item in CommandedSubmarine.GetItems(true).FindAll(i => i.HasTag("turret") && !i.HasTag("hardpoint")))
            {
                var order = new Order(OrderPrefab.Prefabs["operateweapons"], item, item.GetComponent<Turret>());
                ShipIssueWorkers.Add(new ShipIssueWorkerOperateWeapons(this, order));
            }

            int crewSizeModifier = 2;
            // these issueworkers revolve around a singular, shared issue, which is injected into them to prevent redundant calculations
            ShipGlobalIssueFixLeaks shipGlobalIssueFixLeaks = new ShipGlobalIssueFixLeaks(this);
            for (int i = 0; i < crewSizeModifier; i++)
            {
                var order = OrderPrefab.Prefabs["fixleaks"].CreateInstance(OrderPrefab.OrderTargetType.Entity);
                ShipIssueWorkers.Add(new ShipIssueWorkerFixLeaks(this, order, shipGlobalIssueFixLeaks));
            }
            shipGlobalIssues.Add(shipGlobalIssueFixLeaks);

            ShipGlobalIssueRepairSystems shipGlobalIssueRepairSystems = new ShipGlobalIssueRepairSystems(this);
            for (int i = 0; i < crewSizeModifier; i++)
            {
                var order = OrderPrefab.Prefabs["repairsystems"].CreateInstance(OrderPrefab.OrderTargetType.Entity);
                ShipIssueWorkers.Add(new ShipIssueWorkerRepairSystems(this, order, shipGlobalIssueRepairSystems));
            }
            shipGlobalIssues.Add(shipGlobalIssueRepairSystems);

            return true;
        }
    }
}
