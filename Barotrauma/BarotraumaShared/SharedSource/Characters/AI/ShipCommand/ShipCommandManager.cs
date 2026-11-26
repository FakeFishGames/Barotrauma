using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public partial class ShipCommandManager
    {
        public AICharacter Commander { get; private set; }
        public Submarine CommandedSubmarine { get; private set; }

        public bool Enabled { get; set; }

        private readonly List<Submarine> targetSubmarines = new List<Submarine>();

        /// <summary>
        /// Potential enemy submarine.
        /// </summary>
        public IEnumerable<Submarine> TargetSubmarines
        {
            get => targetSubmarines;
        }

        private Steering steering;

        /// <summary>
        /// Positions the ship will navigate through (in order).
        /// </summary>
        public readonly List<Vector2> PatrolPositions = [];

        /// <summary>
        /// How close to a patrol position must the sub be to consider the position reached and to start heading towards the next position.
        /// </summary>
        public float CloseEnoughToPatrolPosition = 7000.0f;

        public enum NavigationStates
        {
            Stationary,
            Patrol,
            Ram,
            NavigateToFiringPosition,
            NavigateToBoardingPodFiringPosition,
            ReadyToLaunchBoardingPod
        }

        private NavigationStates navigationState = NavigationStates.Stationary;
        public NavigationStates NavigationState 
        {
            get => navigationState;
            private set
            {
                navigationState = value;
                if (navigationState != NavigationStates.Ram)
                {
                    rammingTarget = null;
                }
            }
        }
        
        public bool AllowPatrolling = true;
        public bool AllowRamming = true;

        private float navigationTimer = 0f;
        private readonly float navigationInterval = 4f;

        private readonly Identifier BoardingPodLauncherTag = "boardinglauncher".ToIdentifier();
        private readonly Identifier BoardingPodTag = "boardingammo".ToIdentifier();

        /// <summary>
        /// Cooldown during which the crew won't attempt boarding.
        /// We start with the cooldown set so the sub doesn't start boarding immediately at the start of the round.
        /// </summary>
        private float boardingCooldownTimer = BoardingCooldown;
        private const float BoardingCooldown = 120.0f;

        /// <summary>
        /// Timer for launching the boarding pod once in position. If launching the pod takes too long for whatever reason (bot can't find a pod or load it into a loader?), 
        /// the boarding attempt is aborted.
        /// </summary>
        private float boardingPodLaunchTimer;
        private const float MaxBoardingPodLaunchWaitTime = 60.0f;

        private float timeUntilRam;
        private Submarine rammingTarget;
        private const float RamTimerMax = 17.5f;

        /// <summary>
        /// When above zero, the ship won't attempt to ram again (but it will still finish the ongoing ram if any).
        /// </summary>
        private float rammingCooldownTimer;

        private const float RammingCooldown = 120.0f;

        public readonly List<ShipIssueWorker> ShipIssueWorkers = [];
        public const float MinimumIssueThreshold = 10f;
        private const float IssueDevotionBuffer = 5f;

        private float decisionTimer = 6f;
        private readonly float decisionInterval = 6f;

        private const float CloseEnoughToFiringPosition = 500f;
        private const float RefreshFiringPositionsInterval = 60.0f;
        private const float ForceChangeFiringPositionInterval = 60.0f;

        private float timeSinceLastCommandDecision;
        private float timeSinceLastNavigation;

        private bool initialAmmoChecked;

        private readonly List<Character> activeCharacters = [];
        private readonly List<Character> alliedCharacters = [];
        public readonly List<Character> EnemyCharacters = [];

        private readonly List<ShipIssueWorker> attendedIssues = [];
        private readonly List<ShipIssueWorker> availableIssues = [];
        private readonly List<ShipGlobalIssue> shipGlobalIssues = [];

        private readonly List<Turret> turrets = [];
        private readonly List<Turret> boardingPodLaunchers = [];
        private readonly List<Character> currentPendingBoarders = [];

        /// <param name="Position">World position</param>
        /// <param name="MyTurretCoverage">How good turret coverage the sub has from this position. Does not have any well-define range, and should be treated like a weight for comparing different firing positions.</param>
        /// <param name="EnemyTurretDanger">How good turret coverage enemies have on this position. Does not have any well-define range, and should be treated like a weight for comparing different firing positions.</param>
        /// <param name="Identifier">Used in determining which positions we've already tried - intended to be the same at a specific angle from a specific target.</param>
        private record FiringPosition(Vector2 Position, float MyTurretCoverage, float EnemyTurretDanger, Identifier Identifier)
        {
            /// <summary>
            /// Does not have any well-define range, and should be treated like a weight for comparing different firing positions.
            /// </summary>
            public float Priority => MyTurretCoverage / EnemyTurretDanger;
        }

        private readonly List<FiringPosition> potentialFiringPositions = [];

        private readonly List<Character> crew = [];

        /// <summary>
        /// Is issuing orders only allowed to characters with an appropriate job (e.g. only engineers operate the reactor, security operates turrets)
        /// </summary>
        public bool RequireJobAppropriateOrders;

        public bool StopObjectivesIfHostilesOnBoard = true;

        public ShipCommandManager(AICharacter commander)
        {
            Commander = commander;
            CommandedSubmarine = commander.Submarine;
            if (CommandedSubmarine == null)
            {
                DebugConsole.ThrowError($"Error when initializing {nameof(ShipCommandManager)} failed: CommandedSubmarine was null for character {commander}.");
                Enabled = false;
            }
        }
        
        public void InitializeShipCommandManager(IEnumerable<Submarine> potentialEnemySubmarine, IEnumerable<Character> crew)
        {
            if (TargetSubmarines.Any())
            {
                DebugConsole.ThrowError("ShipCommandManager already initialized!");
                return;
            }

            if (CommandedSubmarine == null)
            {
                DebugConsole.ThrowError($"{nameof(InitializeShipCommandManager)} failed: CommandedSubmarine was null.");
                Enabled = false;
                return;
            }

            this.crew.Clear();
            this.crew.AddRange(crew);

            targetSubmarines.AddRange(potentialEnemySubmarine);
            timeUntilRam = RamTimerMax * Rand.Range(0.9f, 1.1f);
            ShipIssueWorkers.Clear();
            if (CommandedSubmarine.GetItems(false).Find(i => i.HasTag(Tags.Reactor) && !i.NonInteractable)?.GetComponent<ReactorBase>() is ReactorBase reactor)
            {
                var order = new Order(OrderPrefab.Prefabs["operatereactor"], "powerup".ToIdentifier(), reactor.Item, reactor);
                ShipIssueWorkers.Add(new ShipIssueWorkerPowerUpReactor(this, order));
            }
            if (CommandedSubmarine.GetItems(false).Find(i => i.HasTag(Tags.NavTerminal) && !i.NonInteractable) is Item nav && nav.GetComponent<Steering>() is Steering steeringComponent)
            {
                steering = steeringComponent;
                var order = new Order(OrderPrefab.Prefabs["steer"], "navigatetactical".ToIdentifier(), nav, steeringComponent);
                ShipIssueWorkers.Add(new ShipIssueWorkerSteer(this, order));
            }

            turrets.Clear();
            turrets.AddRange(CommandedSubmarine.GetItems(alsoFromConnectedSubs: true).FindAll(i => i.HasTag(Tags.Turret) && !i.NonInteractable && !i.HasTag(Tags.Hardpoint))
                .Select(it => it.GetComponent<Turret>()).NotNull());
            foreach (Turret turret in turrets)
            {
                var order = new Order(OrderPrefab.Prefabs["operateweapons"], turret.Item, turret);
                ShipIssueWorkers.Add(new ShipIssueWorkerOperateWeapons(this, order));
            }

            boardingPodLaunchers.Clear();
            boardingPodLaunchers.AddRange(CommandedSubmarine.GetItems(alsoFromConnectedSubs: true).FindAll(i => i.HasTag(BoardingPodLauncherTag) && !i.NonInteractable && !i.HasTag(Tags.Hardpoint))
                .Select(it => it.GetComponent<Turret>()).NotNull());            

            //up to half of the crew can be assigned to fixing leaks
            int fixLeaksOrderCount = Math.Max(crew.Count() / 2, 2);
            // these issueworkers revolve around a singular, shared issue, which is injected into them to prevent redundant calculations
            ShipGlobalIssueFixLeaks shipGlobalIssueFixLeaks = new ShipGlobalIssueFixLeaks(this);
            for (int i = 0; i < fixLeaksOrderCount; i++)
            {
                var order = OrderPrefab.Prefabs["fixleaks"].CreateInstance(OrderPrefab.OrderTargetType.Entity);
                ShipIssueWorkers.Add(new ShipIssueWorkerFixLeaks(this, order, shipGlobalIssueFixLeaks));
            }
            shipGlobalIssues.Add(shipGlobalIssueFixLeaks);

            //up to a third of the crew can be assigned to repairing
            int repairSystemsOrderCount = Math.Max(crew.Count() / 3, 2);
            ShipGlobalIssueRepairSystems shipGlobalIssueRepairSystems = new ShipGlobalIssueRepairSystems(this);
            for (int i = 0; i < repairSystemsOrderCount; i++)
            {
                var order = OrderPrefab.Prefabs["repairsystems"].CreateInstance(OrderPrefab.OrderTargetType.Entity);
                ShipIssueWorkers.Add(new ShipIssueWorkerRepairSystems(this, order, shipGlobalIssueRepairSystems));
            }
            shipGlobalIssues.Add(shipGlobalIssueRepairSystems);
            Enabled = true;
        }
        
        public void Update(Character character, float deltaTime)
        {
            if (!Enabled) { return; }

            if (Commander is { IsIncapacitated: true } or { Removed: true })
            {
                AICharacter newCommander = ChooseNewCommander();
                if (newCommander != null)
                {
                    Commander = newCommander;
                }
            }
            //only the commander gets to do decisions and issue orders
            if (character != Commander)
            {
                return;
            }

            decisionTimer -= deltaTime;
            if (decisionTimer <= 0.0f)
            {
                if (!initialAmmoChecked) { ChooseInitialAmmo(); }
                UpdateCommandDecision(timeSinceLastCommandDecision);
                decisionTimer = decisionInterval * Rand.Range(0.8f, 1.2f);
                timeSinceLastCommandDecision = decisionTimer;
            }

            rammingCooldownTimer -= deltaTime;
            boardingCooldownTimer -= deltaTime;
            navigationTimer -= deltaTime;

            if (navigationTimer <= 0.0f)
            {
                UpdateNavigation(timeSinceLastNavigation);
                navigationTimer = navigationInterval * Rand.Range(0.8f, 1.2f);
                timeSinceLastNavigation = navigationTimer;
            }
        }

        private AICharacter ChooseNewCommander()
        {
            var validCharacters = crew.Where(c => !c.IsIncapacitated && !c.Removed  && c is AICharacter).Cast<AICharacter>();
            if (validCharacters.None()) { return null; }

            return
                validCharacters.FirstOrDefault(character => character.IsCaptain) ??
                validCharacters.MaxBy(character => character.GetSkillLevel(Tags.HelmSkill));
        }


        public static void LogDebugMessage(string text)
        {
#if DEBUG
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                DebugConsole.NewMessage(text);
            }
#endif
        }

        private static bool IsWithinRange(float range, float distanceSquared)
        {
            return distanceSquared < range * range;
        }

        /// <summary>
        /// Finds the best available ammo (based on <see cref="ItemPrefab.BotPriority"/>) in the sub and loads it into the turrets' loaders.
        /// Simplifies things for AI: even though bots can choose the best ammo when reloading an empty loader, they won't attempt to switch the ammo in an already loaded loader -
        /// this takes care of that and allows the AI to start with the best ammo without having to first use up the probably-most-basic ammo that the sub spawned with.
        /// </summary>
        private void ChooseInitialAmmo()
        {
            var allItems = CommandedSubmarine.GetItems(alsoFromConnectedSubs: true);
            foreach (Turret turret in turrets)
            {
                foreach (MapEntity e in turret.Item.linkedTo)
                {
                    if (e is not Item containerItem) { continue; }
                    ItemContainer container = containerItem.GetComponent<ItemContainer>();
                    if (container == null) { continue; }
                    if (container.Inventory.AllItems.FirstOrDefault() is Item containedAmmo &&
                        allItems.FirstOrDefault(it => it.Prefab.BotPriority > containedAmmo.Prefab.BotPriority && container.CanBeContained(it)) is { } betterAmmo)
                    {
                        containedAmmo.Drop(dropper: null);
                        container.Inventory.TryPutItem(betterAmmo, user: null);
                        allItems.Remove(betterAmmo);
                    }
                }
            }
            initialAmmoChecked = true;            
        }

        private void UpdateNavigation(float deltaTime)
        {
            if (steering == null || TargetSubmarines.None()) { return; }
            DecideAttackBehavior();
            switch (NavigationState)
            {
                case NavigationStates.Stationary:
                    UpdateStationary();
                    break;
                case NavigationStates.Patrol:
                    UpdatePatrol();
                    break;
                case NavigationStates.Ram:
                    UpdateRamming(deltaTime);
                    break;
                case NavigationStates.NavigateToFiringPosition:
                    UpdateNavigateToFiringPosition(deltaTime);
                    break;
                case NavigationStates.NavigateToBoardingPodFiringPosition:
                    UpdateNavigateToBoardingPodFiringPosition(deltaTime);
                    break;
                case NavigationStates.ReadyToLaunchBoardingPod:
                    UpdateReadyToLaunchBoardingPod(deltaTime);
                    break;
            }
        }

        private void UpdateStationary()
        {
            StopRamming();
            if (AllowPatrolling)
            {
                NavigationState = NavigationStates.Patrol;
            }
        }

        private void UpdatePatrol()
        {
            if (!AllowPatrolling || PatrolPositions.None())
            {
                NavigationState = NavigationStates.Stationary;
            }
            else
            {
                if (PatrolPositions.Count > 1)
                {
                    float distanceSquaredPatrol = Vector2.DistanceSquared(CommandedSubmarine.WorldPosition, PatrolPositions.First());
                    if (IsWithinRange(CloseEnoughToPatrolPosition, distanceSquaredPatrol))
                    {
                        Vector2 lastPosition = PatrolPositions.First();
                        PatrolPositions.RemoveAt(0);
                        PatrolPositions.Add(lastPosition);
                    }
                }
                steering.SetDestinationTactical(PatrolPositions.First());
            }
        }

        private void DecideAttackBehavior()
        {
            if (NavigationState == NavigationStates.Ram) { return; }
            var targetSubmarine = GetClosestTargetSubmarine();
            if (targetSubmarine == null) { return; }
            //todo: some smarter logic here? When to shoot and when to ram - or do we actually want to ram at all?
            boardingCooldownTimer = 0.0f;
            if (boardingCooldownTimer <= 0.0f && boardingPodLaunchers.Any())
            {
                if (NavigationState != NavigationStates.NavigateToBoardingPodFiringPosition && currentPendingBoarders.None())
                {
                    StartNavigateToBoardingPodFiringPosition();
                }
            }
            else if (turrets.Any(t => t.HasPowerToShoot()))
            {
                NavigationState = NavigationStates.NavigateToFiringPosition;
            }
            else if (targetSubmarine.PhysicsBody.BodyType == BodyType.Dynamic && AllowRamming && rammingCooldownTimer <= 0.0f)
            {
                const float aggroRange = 7000f;
                const float cooldownRange = 40000f;
                float distanceSquaredEnemy = Vector2.DistanceSquared(CommandedSubmarine.WorldPosition, targetSubmarine.WorldPosition);
                if (IsWithinRange(aggroRange, distanceSquaredEnemy))
                {
                    LogDebugMessage("Ship " + CommandedSubmarine + " was within the aggro range of " + targetSubmarine);
                    NavigationState = NavigationStates.Ram;
                }
                else if (IsWithinRange(cooldownRange, distanceSquaredEnemy))
                {
                    NavigationState = AllowPatrolling ? NavigationStates.Patrol : NavigationStates.Stationary;
                }
            }
        }

        private Submarine GetClosestTargetSubmarine()
        {
            //todo: some smarter logic for figuring out which sub to target
            //(e.g. the player's sub should be a higher prio than the outpost if the player is using it to defend an outpost)
            float closestDist = float.PositiveInfinity;
            Submarine closestSub = null;
            foreach (Submarine targetSub in TargetSubmarines)
            {
                if (IsFriendlyTeam(targetSub.TeamID)) { continue; }
                float dist = Vector2.DistanceSquared(targetSub.WorldPosition, CommandedSubmarine.WorldPosition);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestSub = targetSub;
                }
            }
            return closestSub;
        }

        private void UpdateRamming(float deltaTime)
        {
            rammingTarget = GetClosestTargetSubmarine();
            if (!AllowRamming || rammingTarget == null)
            {
                StopRamming();
                NavigationState = NavigationStates.Stationary;
                return;
            }

            steering.SetDestinationTactical(rammingTarget.WorldPosition);
            const float minRammingDistance = 1500f;
            const float maxRammingDistance = 8500f;
            float distanceSquaredEnemy = Vector2.DistanceSquared(CommandedSubmarine.WorldPosition, rammingTarget.WorldPosition);
            // if we are within enemy ship's range for ramTimerMax, try to ram them instead (if we're not already very close)
            if (IsWithinRange(maxRammingDistance, distanceSquaredEnemy) && !IsWithinRange(minRammingDistance, distanceSquaredEnemy))
            {
                if (steering.AIRamTimer > 0f)
                {
                    LogDebugMessage("Ship " + CommandedSubmarine + " was still ramming, " + steering.AIRamTimer + " left");
                }
                else
                {
                    if (rammingCooldownTimer > 0.0f)
                    {
                        StopRamming();
                        NavigationState = NavigationStates.Stationary;
                        return;
                    }

                    timeUntilRam -= deltaTime;
                    LogDebugMessage("Ship " + CommandedSubmarine + " was close enough to ram, " + timeUntilRam + " left until ramming");
                    if (timeUntilRam <= 0f)
                    {
                        LogDebugMessage("Ship " + CommandedSubmarine + " is attempting to ram!");
                        steering.AIRamTimer = 50f;
                        timeUntilRam = RamTimerMax * Rand.Range(0.9f, 1.1f);
                        rammingCooldownTimer = RammingCooldown;
                    }
                }
            }
            else
            {
                StopRamming();
                NavigationState = NavigationStates.Stationary;
            }            
        }


        private float refreshFiringPositionsDelay = 0f;
        private float forceChangeFiringPositionDelay = ForceChangeFiringPositionInterval;

        private FiringPosition currentFiringPosition;

        private readonly HashSet<FiringPosition> previousFiringPositions = [];

        private void UpdateNavigateToFiringPosition(float deltaTime)
        {
            refreshFiringPositionsDelay -= deltaTime;
            if (refreshFiringPositionsDelay < 0.0f)
            {
                bool preparingForBoarding = NavigationState == NavigationStates.NavigateToBoardingPodFiringPosition;
                FindFiringPositions(
                    preparingForBoarding ? boardingPodLaunchers : turrets,
                    prioritizeOwnTurretFiringCoverage: preparingForBoarding);
                refreshFiringPositionsDelay = RefreshFiringPositionsInterval * Rand.Range(1.0f, 1.2f);
            }

            if (potentialFiringPositions.None())
            {
                return;
            }

            //after a certain (longish) interval, force changing firing positions no matter what
            //in theory it would make sense to always go for the absolute best firing position for the given situation,
            //but in practice it's more interesting and harder to defend if the sub moves around
            //this also acts as a failsafe for situations where the sub fails to get to the chosen position for whatever reason
            forceChangeFiringPositionDelay -= deltaTime;
            if (forceChangeFiringPositionDelay < 0.0f)
            {
                if (currentFiringPosition != null)
                {
                    previousFiringPositions.Add(currentFiringPosition);
                }
                forceChangeFiringPositionDelay = ForceChangeFiringPositionInterval * Rand.Range(1.0f, 1.2f);
            }
            else
            {
                //once we get to the chosen firing position, mark it as being already tried (so we'll move to the next position)
                if (currentFiringPosition != null &&
                    Vector2.DistanceSquared(currentFiringPosition.Position, CommandedSubmarine.WorldPosition) < CloseEnoughToFiringPosition * CloseEnoughToFiringPosition)
                {
                    previousFiringPositions.Add(currentFiringPosition);
                }
            }

            float avgFiringPositionPriority = potentialFiringPositions.Average(static f => f.Priority);
            var notTriedFiringPositions = potentialFiringPositions.Where(potentialPos => previousFiringPositions.None(previousPos => previousPos.Identifier == potentialPos.Identifier));

            //reset the tried positions and restart (going back to the highest-prio position if)
            // 1. no position chosen yet
            // 2. we have already tried half of the potential positions
            // 3. the positions we've not yet tried are all worse than average
            // (4. we've tried all positions, should not happen in practice because of points 2 and 3 - we already reset at the point when we've tried half of them)
            if (currentFiringPosition == null ||
                previousFiringPositions.Count > potentialFiringPositions.Count / 2 ||
                notTriedFiringPositions.All(p => p.Priority < avgFiringPositionPriority) ||
                notTriedFiringPositions.None())
            {
                currentFiringPosition = potentialFiringPositions.MaxBy(f => f.Priority);
                previousFiringPositions.Clear();
            }
            //keep going for the best position we've not yet tried
            else
            {
                currentFiringPosition = notTriedFiringPositions.MaxBy(f => f.Priority);
            }
            steering.SetDestinationTactical(currentFiringPosition.Position);

            bool positionInsideSub =
                Math.Abs(currentFiringPosition.Position.X - CommandedSubmarine.WorldPosition.X) < CommandedSubmarine.SubBody.Borders.Width / 2 &&
                Math.Abs(currentFiringPosition.Position.Y - CommandedSubmarine.WorldPosition.Y) < CommandedSubmarine.SubBody.Borders.Height / 2;

            //if we can see the position or are close to it, just maintain pos (= move directly to that position) instead of pathfinding
            if (positionInsideSub ||
                steering.CanSeePosition(currentFiringPosition.Position) ||
                Vector2.DistanceSquared(currentFiringPosition.Position, CommandedSubmarine.WorldPosition) < CloseEnoughToFiringPosition * CloseEnoughToFiringPosition)
            {
                steering.MaintainPos = true;
                steering.PosToMaintain = currentFiringPosition.Position;
            }
        }

        private void StartNavigateToBoardingPodFiringPosition()
        {
            refreshFiringPositionsDelay = 0.0f;
            previousFiringPositions.Clear();
            currentPendingBoarders.Clear();

            var availableBoarders = crew.Where(c => !c.IsDead && !c.Removed && !c.IsIncapacitated && c != Commander && c.Submarine == CommandedSubmarine);
            if (availableBoarders.None())
            {
                LogDebugMessage($"No characters available for boarding in {CommandedSubmarine.Info.DisplayName}, won't attempt boarding.");
                EndBoarding();
                return;
            }

            var availableBoardingPods =
                CommandedSubmarine.GetItems(alsoFromConnectedSubs: true).FindAll(i => i.HasTag(BoardingPodTag) && !i.NonInteractable);
            if (availableBoardingPods.None())
            {
                LogDebugMessage($"No boarding pods available in {CommandedSubmarine.Info.DisplayName}, won't attempt boarding.");
                EndBoarding();
                return;
            }

            //allow a third of the crew to board, up to 3 boarders at a time
            //no boarding if there's less than 3 available boarders
            int maxBoarders = Math.Min(availableBoardingPods.Count, 3);
            int boarderCount = Math.Min(availableBoarders.Count() / 3, maxBoarders);
            if (availableBoarders.Count() < 2)
            {
                LogDebugMessage($"Not enough characters available for boarding in {CommandedSubmarine.Info.DisplayName}, won't attempt boarding.");
                EndBoarding();
                return;
            }

            currentPendingBoarders.AddRange(availableBoarders.OrderByDescending(c => c.GetSkillLevel(Tags.WeaponsSkill)).Take(boarderCount));    
            foreach (var boarder in currentPendingBoarders)
            {
                if (boarder.AIController is HumanAIController humanAi)
                {
                    var prepareOrder = OrderPrefab.Prefabs["prepareforboarding"]
                        .CreateInstance(OrderPrefab.OrderTargetType.Entity)
                        .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                    humanAi.ObjectiveManager.SetOrder(prepareOrder, speak: false);
                }
            }
            NavigationState = NavigationStates.NavigateToBoardingPodFiringPosition;
        }

        private void EndBoarding()
        {
            foreach (var boarder in currentPendingBoarders)
            {
                if (boarder.AIController is HumanAIController humanAi &&
                    humanAi.ObjectiveManager.GetObjective<AIObjectivePrepare>() is { } prepareObjective)
                {
                    prepareObjective.Abandon = true;
                }
            }
            refreshFiringPositionsDelay = 0.0f;
            boardingCooldownTimer = BoardingCooldown;
            currentPendingBoarders.Clear();
            boardingPodLaunchTimer = 0.0f;
        }

        private void UpdateNavigateToBoardingPodFiringPosition(float deltaTime)
        {
            if (currentFiringPosition == null || 
                Vector2.DistanceSquared(currentFiringPosition.Position, CommandedSubmarine.WorldPosition) > CloseEnoughToFiringPosition * CloseEnoughToFiringPosition)
            {            
                UpdateNavigateToFiringPosition(deltaTime);
                return;
            }
            navigationState = NavigationStates.ReadyToLaunchBoardingPod;
            InitiateBoarding();
        }

        private void InitiateBoarding()
        {
            Turret selectedLauncher = null;
            foreach (var myTurret in boardingPodLaunchers)
            {
                if (IsEnemyHullWithinAimingRange(myTurret, out _, checkRange: false))
                {
                    selectedLauncher = myTurret;
                    break;                        
                }
            }
            if (selectedLauncher == null)
            {
                DebugConsole.AddWarning($"{nameof(ShipCommandManager)} failed to find a suitable boarding pod launcher for firing at the target after reaching the desired firing position.");
                EndBoarding();
                return;
            }
            if (currentPendingBoarders.None())
            {
                DebugConsole.AddWarning($"{nameof(ShipCommandManager)} no pending boarders left, aborting boarding.");
                EndBoarding();
                return;
            }
            var loaders = selectedLauncher.GetLinkedLoaders().Where(loader => loader.GetComponent<ItemContainer>() != null);
            var boarder = currentPendingBoarders.First();
            if (boarder.AIController is HumanAIController humanAi && loaders.Any())
            {
                AIObjective prepareBoardingPodObjective;
                Item selectedLoader = loaders.First();
                if (selectedLauncher.GetLoadedProjectiles().None(p => p.Item.HasTag(BoardingPodTag)))
                {
                    prepareBoardingPodObjective = new AIObjectiveContainItem(boarder, BoardingPodTag, selectedLoader.GetComponent<ItemContainer>(), humanAi.ObjectiveManager)
                    {
                        RemoveExisting = true
                    };
                }
                else
                {
                    prepareBoardingPodObjective = new AIObjectiveGoTo(selectedLoader, boarder, humanAi.ObjectiveManager)
                    {
                        OverridePriority = CharacterInfo.HighestManualOrderPriority
                    };
                }
                prepareBoardingPodObjective.Completed += () =>
                {
                    //no hulls within range anymore (sub moved/sank while preparing the pod?) or no pod in the launcher -> abort
                    if (!IsEnemyHullWithinAimingRange(selectedLauncher, out Hull targetHull) ||
                        selectedLoader?.ContainedItems.FirstOrDefault() is not Item boardingPod)
                    {
                        EndBoarding();
                        return;
                    }

                    Submarine targetSub = targetHull?.Submarine;

                    //go inside the boarding pod
                    boardingPod.TryInteract(boarder, forceUseKey: true);
                    //disable AI so the bot doesn't try to get out before launching
                    boarder.AIController.Enabled = false;
                    //1 sec delay to launching the pod to make it easier to see what's going on
                    CoroutineManager.Invoke(() =>
                    {
                        boarder.AIController.Enabled = true;
                        if (selectedLauncher.Item is not { Removed: false } || boarder is not { Removed: false }) { return; }
                        //make sure the boarder is still inside the bot after the delay
                        if (boarder.SelectedItem != boardingPod)
                        {
                            boardingPod.TryInteract(boarder, forceUseKey: true);
                        }
                        LaunchBoardingPod(selectedLauncher, boarder, targetSub);
                    },
                    delay: 1.0f);
                };
                prepareBoardingPodObjective.Abandoned += EndBoarding;
                prepareBoardingPodObjective.BasePriority = AIObjectiveManager.HighestOrderPriority;
                humanAi.ObjectiveManager.AddObjective(prepareBoardingPodObjective);
            }
        }

        private void UpdateReadyToLaunchBoardingPod(float deltaTime)
        {
            //wait at the current firing position for the pod to be launched
            steering.MaintainPos = true;
            steering.PosToMaintain = currentFiringPosition.Position;
            boardingPodLaunchTimer += deltaTime;
            if (boardingPodLaunchTimer >= MaxBoardingPodLaunchWaitTime)
            {
                DebugConsole.AddWarning($"{nameof(ShipCommandManager)} took too long to launch a boarding pod, aborting.");
                EndBoarding();
            }
        }

        private bool IsEnemyHullWithinAimingRange(Turret turret, out Hull enemyHull, bool checkRange = true)
        {
            foreach (var targetSub in targetSubmarines)
            {
                if (IsFriendlyTeam(targetSub.TeamID)) { continue; }
                foreach (var targetHull in targetSub.GetHulls(alsoFromConnectedSubs: false))
                {
                    Rectangle hullRect = targetHull.WorldRect;
                    hullRect.Y -= targetHull.RectHeight;
                    if (checkRange &&
                        !MathUtils.CircleIntersectsRectangle(turret.Item.WorldPosition, turret.AIRange, hullRect))
                    {
                        continue;
                    }
                    if (!turret.IsWithinAimingRadius(targetHull.WorldPosition, targetHull.Size))
                    {
                        continue;
                    }
                    enemyHull = targetHull;
                    return true;
                }
            }
            enemyHull = null;
            return false;
        }

        /// <summary>
        /// Makes the crew stop fighting against the target submarines, and just remain at their current position.
        /// </summary>
        public void StopFighting()
        {
            PatrolPositions.Clear();
            targetSubmarines.Clear();
            NavigationState = NavigationStates.Stationary;
            StopRamming();
            if (steering != null)
            {
                steering.SetMaintainPosition();
                steering.PosToMaintain = CommandedSubmarine.WorldPosition;
            }
        }

        private void LaunchBoardingPod(Turret selectedLauncher, Character boarder, Submarine targetSub)
        {
            //launch the pod: pass null as the character, because normally the launchers aren't directly usable by characters but must be launched via a button/periscope/etc
            selectedLauncher.Use(deltaTime: 1.0f, character: null);
            if (boarder.AIController is HumanAIController humanAi)
            {
                //abandon current order, we don't want the bot to continue doing what they were doing in their sub,
                //but to make sure it switches to the objectives created below
                if (humanAi.ObjectiveManager.CurrentOrder is { } currentOrder)
                {
                    currentOrder.Abandon = true;
                }
                if (humanAi.ObjectiveManager.GetObjective<AIObjectivePrepare>() is { } prepareObjective)
                {
                    prepareObjective.Abandon = true;
                }
                boarder.ForceOffensiveCombatBehavior = true;

                //if we're boarding an outpost, try blowing up the reactor
                Submarine targetOutpost = targetSub.GetConnectedSubs().FirstOrDefault(s => s.Info.Type == SubmarineType.Outpost);
                Reactor targetReactor =
                    targetOutpost?.GetItems(alsoFromConnectedSubs: true)
                        .Find(i => i.HasTag(Tags.Reactor) && !i.NonInteractable)?.GetComponent<Reactor>();

                //check if someone else is already trying to blow up the reactor
                bool isAnotherTargetingReactor = false;
                if (targetReactor != null)
                {
                    foreach (var activeBoarder in activeCharacters)
                    {
                        if (activeBoarder.Submarine == targetSub && !activeBoarder.IsIncapacitated && activeBoarder.AIController is HumanAIController otherHumanAI)
                        {
                            if (humanAi.ObjectiveManager.HasOrder<AIObjectiveOperateItem>(o => o.OperateTarget == targetReactor.Item))
                            {
                                isAnotherTargetingReactor = true;
                                break;
                            }
                        }
                    }
                }
                if (targetReactor != null && !isAnotherTargetingReactor)
                {
                    var blowUpReactorOrder = OrderPrefab.Prefabs["operatereactor"]
                        .CreateInstance(OrderPrefab.OrderTargetType.Entity)
                        .WithManualPriority(CharacterInfo.HighestManualOrderPriority)
                        .WithOption("blowup".ToIdentifier())
                        .WithItemComponent(targetReactor.Item, targetReactor);
                    var blowUpObjective = humanAi.ObjectiveManager.SetOrder(blowUpReactorOrder, speak: false);
                    if (blowUpObjective != null)
                    {
                        //don't abandon if the objective is disallowed (at the moment, e.g. when the bot is not yet inside the target sub)
                        blowUpObjective.AbandonIfDisallowed = false;
                    }
                }
                else
                {
                    //someone already targeting reactor -> just assault enemies instead
                    var assaultOrder = OrderPrefab.Prefabs["assaultenemy"]
                        .CreateInstance(OrderPrefab.OrderTargetType.Entity)
                        .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                    var assaultObjective = humanAi.ObjectiveManager.SetOrder(assaultOrder, speak: false);
                    if (assaultObjective != null)
                    {
                        //don't abandon if the objective is disallowed (at the moment, e.g. when the bot is not yet inside the target sub)
                        assaultObjective.AbandonIfDisallowed = false;
                    }
                }
            }
            currentPendingBoarders.Remove(boarder);
            if (currentPendingBoarders.None())
            {
                EndBoarding();
            }
            else
            {
                //more boarders pending -> initiate again
                InitiateBoarding();
            }
        }

        public void StructureDamaged(MapEntity structure, float damageAmount, Character attacker)
        {
            if (structure.Submarine != CommandedSubmarine) { return; }

            //taking damage, change the position faster
            refreshFiringPositionsDelay -= damageAmount * 0.05f;
        }

        private void FindFiringPositions(IEnumerable<Turret> turrets, bool prioritizeOwnTurretFiringCoverage)
        {           
            //weight given to subs that are the least desirable to shoot at (e.g. a sub with no-one inside)
            const float MinTargetSubWeight = 0.1f;

            potentialFiringPositions.Clear();
            float desiredRange = turrets.None() ? 1000.0f : turrets.Average(t => t.AIRange * 0.8f);

            List<Turret> enemyTurrets = [];
            foreach (var targetSub in targetSubmarines)
            {
                if (IsFriendlyTeam(targetSub.TeamID)) { continue; }
                enemyTurrets.AddRange(
                    targetSub.GetItems(alsoFromConnectedSubs: false)
                            .FindAll(i => i.HasTag(Tags.Turret) && !i.NonInteractable && !i.HasTag(Tags.Hardpoint))
                            .Select(it => it.GetComponent<Turret>()).NotNull());
            }

            List<Hull> targetHulls = [];
            List<FarseerPhysics.Dynamics.Body> ignoredBodies = new List<FarseerPhysics.Dynamics.Body>();
            foreach (var targetSub in targetSubmarines)
            {
                if (IsFriendlyTeam(targetSub.TeamID)) { continue; }

                float targetSubmarineWeight = GetTargetSubmarineWeight(targetSub);

                targetHulls.Clear();
                targetHulls.AddRange(targetSub.GetHulls(alsoFromConnectedSubs: false));

                int maxTestPoints = 20;
                for (int i = 0; i < maxTestPoints; i++)
                {
                    float angle = i / (float)maxTestPoints * MathHelper.TwoPi;
                    Vector2 dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                    Vector2 halfSize = targetSub.Borders.Size.ToVector2() / 2;

                    //calculate a point desiredRange away from the borders of the sub
                    Vector2 potentialFiringPos = 
                        targetSub.WorldPosition
                        + new Vector2(dir.X * halfSize.X, dir.Y * halfSize.Y)
                        + dir * desiredRange;

                    if (Level.Loaded != null)
                    {
                        if (!Level.Loaded.IsPositionWithinBounds(potentialFiringPos)) { continue; }
                        if (Level.Loaded.IsPositionInsideWall(potentialFiringPos)) { continue; }
                    }

                    //inside a sub -> can't be a valid position
                    if (Submarine.FindContaining(potentialFiringPos, excludedSub: CommandedSubmarine) != null) { continue; }

                    float myTurretCoverage = 0;
                    foreach (var myTurret in turrets)
                    {
                        float turretWeight = GetTurretWeight(myTurret);
                        foreach (var targetHull in targetHulls)
                        {
                            //we can't use the pos here directly, because we're not checking how well the turret can fire from it's current position,
                            //but how well it could fire if the sub was at the position we're considering
                            Vector2 targetPositionRelativeToFiringPosition = targetHull.WorldPosition - potentialFiringPos;
                            Vector2 targetPositionTransformedToSubPosition = CommandedSubmarine.WorldPosition + targetPositionRelativeToFiringPosition;
                            if (myTurret.IsWithinAimingRadius(targetPositionTransformedToSubPosition, targetHull.Size))
                            {
                                Vector2 turretOffsetFromSub = myTurret.Item.WorldPosition - CommandedSubmarine.WorldPosition;
                                ignoredBodies.Clear();
                                ignoredBodies.Add(myTurret.Item.Submarine.PhysicsBody.FarseerBody);
                                ignoredBodies.Add(targetSub.PhysicsBody.FarseerBody);
                                if (Submarine.PickBody(
                                    ConvertUnits.ToSimUnits(potentialFiringPos + turretOffsetFromSub), 
                                    ConvertUnits.ToSimUnits(targetHull.WorldPosition),
                                    ignoredBodies,
                                    collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) != null)
                                {
                                    //something blocking the shot, ignore this position
                                    continue;
                                }
                                //take the hull volume into account, so the bots don't prioritize a turret that can fire at 2 tiny hulls rather than 1 big one
                                //or in other words, the turret weight is based on how much area/volume of the target sub it can cover
                                myTurretCoverage += turretWeight * (targetHull.Volume / 100000.0f);
                                //if our own turret coverage should be prioritize, heavily increase the value if the sub is something we prefer to fire at
                                if (prioritizeOwnTurretFiringCoverage && targetSubmarineWeight > MinTargetSubWeight) { myTurretCoverage *= 10.0f; }
                            }
                        }
                    }

                    float enemyTurretDanger = 0;
                    foreach (var enemyTurret in enemyTurrets)
                    {
                        //here we can just use the pos directly - we want to know how well the enemy turret can fire at the firing position we're considering
                        if (enemyTurret.IsWithinAimingRadius(potentialFiringPos))
                        {
                            float distanceFactor = 1.0f - MathHelper.Clamp(Vector2.Distance(enemyTurret.Item.WorldPosition, potentialFiringPos) / enemyTurret.AIRange, 0.0f, 0.9f);
                            enemyTurretDanger += GetTurretWeight(enemyTurret) * distanceFactor;
                        }
                    }

                    potentialFiringPositions.Add(new FiringPosition(
                        potentialFiringPos, 
                        myTurretCoverage * targetSubmarineWeight,
                        enemyTurretDanger,
                        Identifier: $"{targetSub.ID}:{i}".ToIdentifier()));
                }

                //if we found positions where we can probably fire at the enemy, remove the ones where we know we can't
                if (potentialFiringPositions.Any(f => f.MyTurretCoverage > 0))
                {
                    potentialFiringPositions.RemoveAll(f => f.MyTurretCoverage <= 0.0f);
                }
            }

            //how preferrable it is to use this turret, or how much of a threat we consider it to be
            //gives less weight for turrets that have no power, ammo or user
            static float GetTurretWeight(Turret turret)
            {
                float priority = 10.0f;
                if (!turret.HasPowerToShoot())
                {
                    priority *= 0.5f;
                }
                if (turret.GetLoadedProjectiles().None())
                {
                    priority *= 0.5f;
                }
                if (turret.User == null)
                {
                    priority *= 0.5f;
                }
                return priority;
            }

            //how preferrable the sub is as a target
            //prioritizes the subs/outposts with most enemies inside, and gives MinTargetSubWeight to those with no-one inside
            float GetTargetSubmarineWeight(Submarine submarine)
            {
                if (EnemyCharacters.Count == 0) { return MinTargetSubWeight; }
                return MathHelper.Lerp(MinTargetSubWeight, 1.0f, EnemyCharacters.Count(e => e.Submarine == submarine && !e.IsIncapacitated) / EnemyCharacters.Count);
            }
        }

        private bool IsFriendlyTeam(CharacterTeamType otherTeam)
        {
            if (Commander.AIController is HumanAIController humanAI)
            {
                if (humanAI.IsTeamHostile(otherTeam))
                {
                    return false;
                }
            }
            return Character.IsOnFriendlyTeam(Commander.TeamID, otherTeam);
        }

        private void StopRamming()
        {
            if (steering != null) { steering.AIRamTimer = 0f; }
        }

        public bool AbleToTakeOrder(Character character)
        {
            return AbleToContinueOrder(character) && character.Submarine == CommandedSubmarine;
        }

        /// <summary>
        /// Can the character keep doing an order they've already been given? Less strict than <see cref="AbleToTakeOrder"/>,
        /// since the character may be outside (e.g. swimming to repair something) or in a sub connected to the commanded submarine.
        /// </summary>
        public bool AbleToContinueOrder(Character character)
        {
            return
                !character.IsIncapacitated && !character.LockHands &&
                (character.Submarine == null || CommandedSubmarine.IsConnectedTo(character.Submarine));
        }

        private void UpdateCommandDecision(float deltaTime)
        {
            LogDebugMessage("Updating command for character " + Commander);

            shipGlobalIssues.ForEach(c => c.CalculateGlobalIssue());

            activeCharacters.Clear();
            alliedCharacters.Clear();
            EnemyCharacters.Clear();

            bool hostilesOnBoard = false;
            
            foreach (Character character in Character.CharacterList)
            {
                if (HumanAIController.IsActive(character))
                {
                    activeCharacters.Add(character);
                }
            }
            foreach (Character potentialAlly in activeCharacters)
            {
                //only order the characters that are part of the original crew (e.g. not some visitors or other extra NPCs spawned by events)
                if (!crew.Contains(potentialAlly)) { continue; }
                if (HumanAIController.IsFriendly(Commander, potentialAlly, ignoreHuskDisguising: true) &&
                    potentialAlly.AIController is HumanAIController && 
                    AbleToTakeOrder(potentialAlly))
                {
                    alliedCharacters.Add(potentialAlly);
                }
            }
            foreach (Character potentialEnemy in activeCharacters)
            {
                if (!HumanAIController.IsFriendly(Commander, potentialEnemy, ignoreHuskDisguising: true))
                {
                    EnemyCharacters.Add(potentialEnemy);
                    if (potentialEnemy.Submarine == CommandedSubmarine ||
                        CommandedSubmarine.DockedTo.Contains(potentialEnemy.Submarine))
                    {
                        hostilesOnBoard = true;
                    }
                }
            }

            attendedIssues.Clear();
            availableIssues.Clear();

            foreach (ShipIssueWorker shipIssueWorker in ShipIssueWorkers)
            {
                float importance = shipIssueWorker.CalculateImportance(hostilesOnBoard: hostilesOnBoard && StopObjectivesIfHostilesOnBoard);
                if (shipIssueWorker.OrderAttendedTo(deltaTime))
                {
                    LogDebugMessage("Current importance for " + shipIssueWorker + " was " + importance + " and it was already being attended by " + shipIssueWorker.OrderedCharacter);
                    InsertIssue(shipIssueWorker, attendedIssues);
                }
                else
                {
                    LogDebugMessage("Current importance for " + shipIssueWorker + " was " + importance + " and it is not attended to");
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

            List<Character> charactersToAssign = alliedCharacters.ToList();

            //go through all available issues, assigning each to the most suitable character
            while (availableIssues.Any())
            {
                ShipIssueWorker mostImportantIssue = availableIssues.First();
                availableIssues.RemoveAt(0);

                float bestValue = 0f;
                Character bestCharacter = null;

                if (mostImportantIssue is { Importance: >= MinimumIssueThreshold })
                {
                    List<Character> bestCharacters =
                        CrewManager.GetCharactersSortedForOrder(mostImportantIssue.SuggestedOrder, charactersToAssign, Commander, includeSelf: true)
                        .Where(c => !currentPendingBoarders.Contains(c))
                        .ToList();
                    int idleCharacterCount = bestCharacters.Count(c => attendedIssues.None(issue => issue.OrderedCharacter == c));
#if DEBUG
                    if (RequireJobAppropriateOrders && bestCharacters.None(c => c.Info.Job.Prefab.AppropriateOrders.Any(orderId => orderId == mostImportantIssue.SuggestedOrder.Identifier)))
                    {
                        DebugConsole.AddWarning(
                            $"{nameof(ShipCommandManager)} Failed to find an appropriate character to assign on the issue {mostImportantIssue}. " +
                            $"\"{mostImportantIssue.SuggestedOrder.Identifier}\" is not an appropriate order for any of the characters' jobs.");
                    }
#endif
                    foreach (Character orderedCharacter in bestCharacters)
                    {
                        if (RequireJobAppropriateOrders)
                        {
                            if (orderedCharacter.Info.Job.Prefab.AppropriateOrders.None(orderId => orderId == mostImportantIssue.SuggestedOrder.Identifier))
                            {
                                continue;
                            }
                        }
                        //check all the orders instead of just the current objective: the character may temporarily be doing something else
                        //(e.g. an autonomous objective with a higher priority), and a new order would make them lose the existing one
                        if (orderedCharacter.AIController is HumanAIController humanAi &&
                            humanAi.ObjectiveManager.CurrentOrders.Any(o => o.Objective is { AllowReplacementByShipCommandManager: false }))
                        {
                            continue;
                        }

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
                            if (mostImportantIssue.AllowEasySwitching &&
                                occupiedIssue.AllowEasySwitching &&
                                // Only apply switching logic if there isn't many idle npcs, otherwise in subs with a lot of
                                // npcs this can result in only a select few npcs manning every single gun in the ship even
                                // though there's a bunch of other idle npcs that could take the job.
                                idleCharacterCount <= 1)
                            {
                                issueApplicability /= mostImportantIssue.CurrentRedundancy;
                            }

                            // give slight preference if not qualified for current job
                            issueApplicability += occupiedIssue.SuggestedOrder.AppropriateJobs.Contains(orderedCharacter.Info.Job.Prefab.Identifier) ? 0 : 7.5f;

                            // prefer not to switch orders unless considerably more important
                            issueApplicability -= IssueDevotionBuffer;

                            if (issueApplicability < occupiedIssue.Importance)
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
                    LogDebugMessage("Setting " + mostImportantIssue + " for character " + bestCharacter);
                    mostImportantIssue.SetOrder(bestCharacter);
                    charactersToAssign.Remove(bestCharacter);
                    attendedIssues.Add(mostImportantIssue);
                    //the issue is now attended to, so the remaining issues of the same type become less important
                    //(this is handled in CalculateImportance too before the assignment, but we also need to do it here)
                    var similarIssues = availableIssues.Where(i => i.GetType() == mostImportantIssue.GetType()).ToList();
                    foreach (ShipIssueWorker similarIssue in similarIssues)
                    {
                        similarIssue.CurrentRedundancy *= similarIssue.RedundantIssueModifier;
                        similarIssue.Importance *= similarIssue.RedundantIssueModifier;
                        availableIssues.Remove(similarIssue);
                        InsertIssue(similarIssue, availableIssues);
                    }
                }
                else  // if we didn't give an order, let's try to dismiss someone instead
                {
                    foreach (ShipIssueWorker shipIssueWorker in ShipIssueWorkers)
                    {
                        if (shipIssueWorker.Importance <= 0f && shipIssueWorker.OrderAttendedTo())
                        {
                            LogDebugMessage("Dismissing " + shipIssueWorker + " for character " + shipIssueWorker.OrderedCharacter);
                            //only dismiss the order given by this worker, and force it so it works even if the character is too far to hear the commander
                            //(we'd lose track of the order otherwise, leaving the character stuck doing it)
                            shipIssueWorker.OrderedCharacter.SetOrder(shipIssueWorker.CurrentOrder.GetDismissal(), isNewOrder: true, force: true);
                            shipIssueWorker.RemoveOrder();
                            break;
                        }
                    }
                }
            }            
        }
    }
}
