using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    partial class HumanAIController : AIController
    {
        public static bool debugai;
        public static bool DisableCrewAI;

        private readonly AIObjectiveManager objectiveManager;
        
        public float SortTimer { get; set; }
        private float crouchRaycastTimer;
        private float reactTimer;
        private float unreachableClearTimer;
        private bool shouldCrouch;
        public bool IsInsideCave { get; private set; }
        /// <summary>
        /// Resets each frame
        /// </summary>
        public bool AutoFaceMovement = true;

        const float reactionTime = 0.3f;
        const float crouchRaycastInterval = 1;
        const float sortObjectiveInterval = 1;
        const float clearUnreachableInterval = 30;

        private float flipTimer;
        private const float FlipInterval = 0.5f;

        public const float HULL_SAFETY_THRESHOLD = 40;
        public const float HULL_LOW_OXYGEN_PERCENTAGE = 30;

        private static readonly float characterWaitOnSwitch = 5;

        public readonly HashSet<Hull> UnreachableHulls = new HashSet<Hull>();
        public readonly HashSet<Hull> UnsafeHulls = new HashSet<Hull>();
        public readonly List<Item> IgnoredItems = new List<Item>();

        private float respondToAttackTimer;
        private const float RespondToAttackInterval = 1.0f;
        private bool wasConscious;

        private bool freezeAI;

        private readonly float maxSteeringBuffer = 5000;
        private readonly float minSteeringBuffer = 500;
        private readonly float steeringBufferIncreaseSpeed = 100;
        private float steeringBuffer;

        private readonly float obstacleRaycastIntervalShort = 1, obstacleRaycastIntervalLong = 5;
        private float obstacleRaycastTimer;

        private readonly float enemyCheckInterval = 0.2f;
        private readonly float enemySpotDistanceOutside = 1500;
        private readonly float enemySpotDistanceInside = 1000;
        private float enemycheckTimer;

        /// <summary>
        /// How far other characters can hear reports done by this character (e.g. reports for fires, intruders). Defaults to infinity.
        /// </summary>
        public float ReportRange { get; set; } = float.PositiveInfinity;

        private float _aimSpeed = 1;
        public float AimSpeed
        {
            get { return _aimSpeed; }
            set { _aimSpeed = Math.Max(value, 0.01f); }
        }

        private float _aimAccuracy = 1;
        public float AimAccuracy
        {
            get { return _aimAccuracy; }
            set { _aimAccuracy = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>
        /// List of previous attacks done to this character
        /// </summary>
        private readonly Dictionary<Character, AttackResult> previousAttackResults = new Dictionary<Character, AttackResult>();

        private readonly SteeringManager outsideSteering, insideSteering;

        public bool UseIndoorSteeringOutside { get; set; } = false;

        public IndoorsSteeringManager PathSteering => insideSteering as IndoorsSteeringManager;
        public HumanoidAnimController AnimController => Character.AnimController as HumanoidAnimController;

        public AIObjectiveManager ObjectiveManager => objectiveManager;

        public float CurrentHullSafety { get; private set; } = 100;

        private readonly Dictionary<Character, float> structureDamageAccumulator = new Dictionary<Character, float>();
        private readonly Dictionary<Hull, HullSafety> knownHulls = new Dictionary<Hull, HullSafety>();
        private class HullSafety
        {
            public float safety;
            public float timer;

            public bool IsStale => timer <= 0;

            public HullSafety(float safety)
            {
                Reset(safety);
            }

            public void Reset(float safety)
            {
                this.safety = safety;
                // How long before the hull safety is considered stale
                timer = 0.5f;
            }

            /// <summary>
            /// Returns true when the safety is stale
            /// </summary>
            public bool Update(float deltaTime)
            {
                timer = Math.Max(timer - deltaTime, 0);
                return IsStale;
            }
        }

        public MentalStateManager MentalStateManager { get; private set; }

        public void InitMentalStateManager()
        {
            if (MentalStateManager == null)
            {
                MentalStateManager = new MentalStateManager(Character, this);
            }
            MentalStateManager.Active = true;
        }

        public override bool IsMentallyUnstable => 
            MentalStateManager == null ? false :
            MentalStateManager.CurrentMentalType != MentalStateManager.MentalType.Normal && 
            MentalStateManager.CurrentMentalType != MentalStateManager.MentalType.Confused;

        public ShipCommandManager ShipCommandManager { get; private set; }

        public void InitShipCommandManager()
        {
            if (ShipCommandManager == null)
            {
                ShipCommandManager = new ShipCommandManager(Character);
            }
            ShipCommandManager.Active = true;
        }

        public HumanAIController(Character c) : base(c)
        {
            if (!c.IsHuman)
            {
                throw new Exception($"Tried to create a human ai controller for a non-human: {c.SpeciesName}!");
            }
            insideSteering = new IndoorsSteeringManager(this, true, false);
            outsideSteering = new SteeringManager(this);
            objectiveManager = new AIObjectiveManager(c);
            reactTimer = GetReactionTime();
            SortTimer = Rand.Range(0f, sortObjectiveInterval);
        }

        public override void Update(float deltaTime)
        {
            if (DisableCrewAI || Character.Removed) { return; }

            bool isIncapacitated = Character.IsIncapacitated;
            if (freezeAI && !isIncapacitated)
            {
                freezeAI = false;
            }
            if (isIncapacitated) { return; }

            wasConscious = true;

            respondToAttackTimer -= deltaTime;
            if (respondToAttackTimer <= 0.0f)
            {
                foreach (var previousAttackResult in previousAttackResults)
                {
                    RespondToAttack(previousAttackResult.Key, previousAttackResult.Value);
                }
                previousAttackResults.Clear();
                respondToAttackTimer = RespondToAttackInterval;
            }

            base.Update(deltaTime);

            foreach (var values in knownHulls)
            {
                HullSafety hullSafety = values.Value;
                hullSafety.Update(deltaTime);
            }

            if (unreachableClearTimer > 0)
            {
                unreachableClearTimer -= deltaTime;
            }
            else
            {
                unreachableClearTimer = clearUnreachableInterval;
                UnreachableHulls.Clear();
                IgnoredItems.Clear();
            }

            bool IsCloseEnoughToTarget(float threshold, bool useTargetSub = true)
            {
                Entity target = SelectedAiTarget?.Entity;
                if (target == null)
                {
                    return false;
                }
                if (useTargetSub)
                {
                    if (target.Submarine is Submarine sub)
                    {
                        target = sub;
                        threshold += Math.Max(sub.Borders.Size.X, sub.Borders.Size.Y) / 2;
                    }
                    else
                    {
                        return false;
                    }
                }
                return Vector2.DistanceSquared(Character.WorldPosition, target.WorldPosition) < MathUtils.Pow(threshold, 2);
            }

            bool hasValidPath = HasValidPath();

            if (Character.Submarine == null)
            {
                // When the character is outside, far enough from the target, and the direct route is blocked,
                // use the indoor steering with the main and side path waypoints to help avoid getting stuck in level walls
                if (SelectedAiTarget?.Entity != null && !IsCloseEnoughToTarget(2000, useTargetSub: false))
                {
                    obstacleRaycastTimer -= deltaTime;
                    if (obstacleRaycastTimer <= 0)
                    {
                        obstacleRaycastTimer = obstacleRaycastIntervalLong;
                        Vector2 rayEnd = SelectedAiTarget.Entity.SimPosition;
                        if (SelectedAiTarget.Entity.Submarine != null)
                        {
                            rayEnd += SelectedAiTarget.Entity.Submarine.SimPosition;
                        }
                        UseIndoorSteeringOutside = Submarine.PickBody(SimPosition, rayEnd, collisionCategory: Physics.CollisionLevel) != null;
                    }
                }
                else
                {
                    UseIndoorSteeringOutside = false;
                    if (hasValidPath)
                    {
                        obstacleRaycastTimer -= deltaTime;
                        if (obstacleRaycastTimer <= 0)
                        {
                            obstacleRaycastTimer = obstacleRaycastIntervalShort;
                            // Swimming outside and using the path finder -> check that the path is not blocked with anything (the path finder doesn't know about other subs).
                            foreach (var connectedSub in Submarine.MainSub.GetConnectedSubs())
                            {
                                if (connectedSub == Submarine.MainSub) { continue; }
                                Vector2 rayStart = SimPosition - connectedSub.SimPosition;
                                Vector2 dir = PathSteering.CurrentPath.CurrentNode.WorldPosition - WorldPosition;
                                Vector2 rayEnd = rayStart + dir.ClampLength(Character.AnimController.Collider.GetLocalFront().Length() * 5);
                                if (Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true) != null)
                                {
                                    PathSteering.CurrentPath.Unreachable = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                UseIndoorSteeringOutside = false;
            }
            
            if (Character.Submarine == null || !IsOnFriendlyTeam(Character.TeamID, Character.Submarine.TeamID) && !Character.IsEscorted)
            {
                // Spot enemies while staying outside or inside an enemy ship.
                // does not apply for escorted characters, such as prisoners or terrorists who have their own behavior
                enemycheckTimer -= deltaTime;
                if (enemycheckTimer < 0)
                {
                    enemycheckTimer = enemyCheckInterval * Rand.Range(0.75f, 1.25f);
                    if (!objectiveManager.IsCurrentObjective<AIObjectiveCombat>())
                    {
                        float closestDistance = 0;
                        Character closestEnemy = null;
                        foreach (Character c in Character.CharacterList)
                        {
                            if (c.Submarine != Character.Submarine) { continue; }
                            if (c.Removed || c.IsDead || c.IsIncapacitated) { continue; }
                            if (IsFriendly(c)) { continue; }
                            Vector2 toTarget = c.WorldPosition - WorldPosition;
                            float dist = toTarget.LengthSquared();
                            float maxDistance = Character.Submarine == null ? enemySpotDistanceOutside : enemySpotDistanceInside;
                            if (dist > maxDistance * maxDistance) { continue; }
                            Vector2 forward = VectorExtensions.Forward(Character.AnimController.Collider.Rotation);
                            forward.X *= Character.AnimController.Dir;
                            if (Vector2.Dot(toTarget, forward) < 0.2f) { continue; }
                            if (!Character.CanSeeCharacter(c)) { continue; }
                            if (dist < closestDistance || closestEnemy == null)
                            {
                                closestEnemy = c;
                                closestDistance = dist;
                            }
                        }
                        if (closestEnemy != null)
                        {
                            AddCombatObjective(AIObjectiveCombat.CombatMode.Defensive, closestEnemy);
                        }
                    }
                }
            }

            // Check whether the character is inside a cave
            if (IsInsideCave)
            {
                // If the character was inside a cave, require them to move a bit further from the area to set the field back to false
                // This is to avoid any twitchy behavior with the steering managers
                IsInsideCave = Character.CurrentHull == null && Level.Loaded?.Caves.FirstOrDefault(c =>
                {
                    var area = c.Area;
                    area.Inflate(new Vector2(100));
                    return area.Contains(Character.WorldPosition);
                }) is Level.Cave;
            }
            else
            {
                IsInsideCave = Character.CurrentHull == null && Level.Loaded?.Caves.FirstOrDefault(c => c.Area.Contains(Character.WorldPosition)) is Level.Cave;
            }

            if (UseIndoorSteeringOutside || IsInsideCave || Character.Submarine != null || hasValidPath && IsCloseEnoughToTarget(maxSteeringBuffer) || IsCloseEnoughToTarget(steeringBuffer))
            {
                if (steeringManager != insideSteering)
                {
                    insideSteering.Reset();
                    steeringManager = insideSteering;
                }
                steeringBuffer += steeringBufferIncreaseSpeed * deltaTime;
            }
            else
            {
                if (steeringManager != outsideSteering)
                {
                    outsideSteering.Reset();
                    steeringManager = outsideSteering;
                }
                steeringBuffer = minSteeringBuffer;
            }
            steeringBuffer = Math.Clamp(steeringBuffer, minSteeringBuffer, maxSteeringBuffer);

            AnimController.Crouching = shouldCrouch;
            CheckCrouching(deltaTime);
            Character.ClearInputs();
            
            if (SortTimer > 0.0f)
            {
                SortTimer -= deltaTime;
            }
            else
            {
                objectiveManager.SortObjectives();
                SortTimer = sortObjectiveInterval;
            }
            objectiveManager.UpdateObjectives(deltaTime);

            if (reactTimer > 0.0f)
            {
                reactTimer -= deltaTime;
                if (findItemState != FindItemState.None)
                {
                    // Update every frame only when seeking items
                    UnequipUnnecessaryItems();
                }
            }
            else
            {
                Character.UpdateTeam();

                if (Character.CurrentHull != null)
                {
                    if (Character.IsOnPlayerTeam)
                    {
                        VisibleHulls.ForEach(h => PropagateHullSafety(Character, h));
                    }
                    else
                    {
                        // Outpost npcs don't inform each other about threats, like crew members do.
                        VisibleHulls.ForEach(h => RefreshHullSafety(h));
                    }
                }
                if (Character.SpeechImpediment < 100.0f)
                {
                    if (Character.Submarine != null && (Character.Submarine.TeamID == Character.TeamID || Character.IsEscorted) && !Character.Submarine.Info.IsWreck)
                    {
                        ReportProblems();
                    }
                    UpdateSpeaking();
                }
                UnequipUnnecessaryItems();
                reactTimer = GetReactionTime();
            }

            if (objectiveManager.CurrentObjective == null) { return; }

            objectiveManager.DoCurrentObjective(deltaTime);
            bool run = objectiveManager.CurrentObjective.ForceRun || !objectiveManager.CurrentObjective.ForceWalk && objectiveManager.GetCurrentPriority() > AIObjectiveManager.RunPriority;
            if (ObjectiveManager.CurrentObjective is AIObjectiveGoTo goTo && goTo.Target != null)
            {
                if (Character.CurrentHull == null)
                {
                    run = Vector2.DistanceSquared(Character.WorldPosition, goTo.Target.WorldPosition) > 300 * 300;
                }
                else
                {
                    float yDiff = goTo.Target.WorldPosition.Y - Character.WorldPosition.Y;
                    if (Math.Abs(yDiff) > 100)
                    {
                        run = true;
                    }
                    else
                    {
                        float xDiff = goTo.Target.WorldPosition.X - Character.WorldPosition.X;
                        run = Math.Abs(xDiff) > 500;
                    }
                }
            }
            steeringManager.Update(Character.AnimController.GetCurrentSpeed(run && Character.CanRun));

            bool ignorePlatforms = Character.AnimController.TargetMovement.Y < -0.5f && (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));
            if (steeringManager == insideSteering)
            {
                var currPath = PathSteering.CurrentPath;
                if (currPath != null && currPath.CurrentNode != null)
                {
                    if (currPath.CurrentNode.SimPosition.Y < Character.AnimController.GetColliderBottom().Y)
                    {
                        // Don't allow to jump from too high.
                        float allowedJumpHeight = Character.AnimController.ImpactTolerance / 2;
                        float height = Math.Abs(currPath.CurrentNode.SimPosition.Y - Character.SimPosition.Y);
                        ignorePlatforms = height < allowedJumpHeight;
                    }
                }
                if (Character.IsClimbing && PathSteering.IsNextLadderSameAsCurrent)
                {
                    Character.AnimController.TargetMovement = new Vector2(0.0f, Math.Sign(Character.AnimController.TargetMovement.Y));
                }
            }
            Character.AnimController.IgnorePlatforms = ignorePlatforms;

            Vector2 targetMovement = AnimController.TargetMovement;
            if (!Character.AnimController.InWater)
            {
                targetMovement = new Vector2(Character.AnimController.TargetMovement.X, MathHelper.Clamp(Character.AnimController.TargetMovement.Y, -1.0f, 1.0f));
            }
            Character.AnimController.TargetMovement = Character.ApplyMovementLimits(targetMovement, AnimController.GetCurrentSpeed(run));

            flipTimer -= deltaTime;
            if (flipTimer <= 0.0f)
            {
                Direction newDir = Character.AnimController.TargetDir;
                if (Character.IsKeyDown(InputType.Aim))
                {
                    var cursorDiffX = Character.CursorPosition.X - Character.Position.X;
                    if (cursorDiffX > 10.0f)
                    {
                        newDir = Direction.Right;
                    }
                    else if (cursorDiffX < -10.0f)
                    {
                        newDir = Direction.Left;
                    }
                    if (Character.SelectedConstruction != null)
                    {
                        Character.SelectedConstruction.SecondaryUse(deltaTime, Character);
                    }
                }
                else if (AutoFaceMovement && Math.Abs(Character.AnimController.TargetMovement.X) > 0.1f && !Character.AnimController.InWater)
                {
                    newDir = Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
                }
                if (newDir != Character.AnimController.TargetDir)
                {
                    Character.AnimController.TargetDir = newDir;
                    flipTimer = FlipInterval;
                }
            }
            AutoFaceMovement = true;

            MentalStateManager?.Update(deltaTime);
            ShipCommandManager?.Update(deltaTime);
        }

        private void UnequipUnnecessaryItems()
        {
            if (Character.LockHands) { return; }
            if (ObjectiveManager.CurrentObjective == null) { return; }
            if (Character.CurrentHull == null) { return; }
            bool oxygenLow = !Character.AnimController.HeadInWater && Character.OxygenAvailable < CharacterHealth.LowOxygenThreshold;
            bool isCarrying = ObjectiveManager.HasActiveObjective<AIObjectiveContainItem>() || ObjectiveManager.HasActiveObjective<AIObjectiveDecontainItem>();

            bool NeedsDivingGearOnPath(AIObjectiveGoTo gotoObjective)
            {
                bool insideSteering = SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.IsPathDirty;
                Hull targetHull = gotoObjective.GetTargetHull();
                return gotoObjective.Target != null && targetHull == null ||
                    NeedsDivingGear(targetHull, out _) ||
                    insideSteering && (PathSteering.CurrentPath.HasOutdoorsNodes || PathSteering.CurrentPath.Nodes.Any(n => NeedsDivingGear(n.CurrentHull, out _)));
            }

            if (isCarrying)
            {
                if (findItemState != FindItemState.OtherItem)
                {
                    var decontain = ObjectiveManager.GetActiveObjectives<AIObjectiveDecontainItem>().LastOrDefault();
                    if (decontain != null && decontain.TargetItem != null && decontain.TargetItem.HasTag(AIObjectiveFindDivingGear.HEAVY_DIVING_GEAR) &&
                        ObjectiveManager.GetActiveObjective() is AIObjectiveGoTo gotoObjective && NeedsDivingGearOnPath(gotoObjective))
                    {
                        // Don't try to put the diving suit in a locker if the suit would be needed in any hull in the path to the locker.
                        gotoObjective.Abandon = true;
                    }
                }
                if (!oxygenLow)
                {
                    return;
                }
            }

            // Diving gear
            if (oxygenLow || findItemState != FindItemState.OtherItem)
            {
                bool needsGear = NeedsDivingGear(Character.CurrentHull, out _);
                if (!needsGear || oxygenLow)
                {
                    bool isCurrentObjectiveFindSafety = ObjectiveManager.IsCurrentObjective<AIObjectiveFindSafety>();
                    bool shouldKeepTheGearOn =
                        isCurrentObjectiveFindSafety ||
                        Character.AnimController.InWater ||
                        Character.AnimController.HeadInWater ||
                        Character.Submarine == null ||
                        (Character.Submarine.TeamID != Character.TeamID && !Character.IsEscorted) ||
                        ObjectiveManager.CurrentObjective.GetSubObjectivesRecursive(true).Any(o => o.KeepDivingGearOn) ||
                        Character.CurrentHull.OxygenPercentage < HULL_LOW_OXYGEN_PERCENTAGE + 10;
                    bool IsOrderedToWait() => Character.IsOnPlayerTeam && ObjectiveManager.CurrentOrder is AIObjectiveGoTo goTo && goTo.Target == Character;
                    bool removeDivingSuit = !shouldKeepTheGearOn && !IsOrderedToWait();
                    if (oxygenLow && Character.CurrentHull.Oxygen > 0 && (!isCurrentObjectiveFindSafety || Character.OxygenAvailable < 1))
                    {
                        shouldKeepTheGearOn = false;
                        // Remove the suit before we pass out
                        removeDivingSuit = true;
                    }
                    bool takeMaskOff = !shouldKeepTheGearOn;
                    if (!shouldKeepTheGearOn && !oxygenLow)
                    {
                        if (ObjectiveManager.IsCurrentObjective<AIObjectiveIdle>())
                        {
                            removeDivingSuit = true;
                            takeMaskOff = true;
                        }
                        else
                        {
                            bool removeSuit = false;
                            bool removeMask = false;
                            foreach (var objective in ObjectiveManager.CurrentObjective.GetSubObjectivesRecursive(includingSelf: true))
                            {
                                if (objective is AIObjectiveGoTo gotoObjective)
                                {
                                    if (NeedsDivingGearOnPath(gotoObjective))
                                    {
                                        removeDivingSuit = false;
                                        takeMaskOff = false;
                                        break;
                                    }
                                    else if (gotoObjective.mimic)
                                    {
                                        if (!removeSuit)
                                        {
                                            removeDivingSuit = !HasDivingSuit(gotoObjective.Target as Character);
                                            if (removeDivingSuit)
                                            {
                                                removeSuit = true;
                                            }
                                        }
                                        if (!removeMask)
                                        {
                                            takeMaskOff = !HasDivingMask(gotoObjective.Target as Character);
                                            if (takeMaskOff)
                                            {
                                                removeMask = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (removeDivingSuit)
                    {
                        var divingSuit = Character.Inventory.FindItemByTag(AIObjectiveFindDivingGear.HEAVY_DIVING_GEAR);
                        if (divingSuit != null)
                        {
                            if (oxygenLow || Character.Submarine?.TeamID != Character.TeamID || ObjectiveManager.GetCurrentPriority() >= AIObjectiveManager.RunPriority)
                            {
                                divingSuit.Drop(Character);
                                HandleRelocation(divingSuit);
                            }
                            else if (findItemState == FindItemState.None || findItemState == FindItemState.DivingSuit)
                            {
                                findItemState = FindItemState.DivingSuit;
                                if (FindSuitableContainer(divingSuit, out Item targetContainer))
                                {
                                    findItemState = FindItemState.None;
                                    itemIndex = 0;
                                    if (targetContainer != null)
                                    {
                                        var decontainObjective = new AIObjectiveDecontainItem(Character, divingSuit, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>())
                                        {
                                            DropIfFails = false
                                        };
                                        decontainObjective.Abandoned += () =>
                                        {
                                            ReequipUnequipped();
                                            IgnoredItems.Add(targetContainer);
                                        };
                                        decontainObjective.Completed += () => ReequipUnequipped();
                                        ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                        return;
                                    }
                                    else
                                    {
                                        divingSuit.Drop(Character);
                                        HandleRelocation(divingSuit);
                                    }
                                }
                            }
                        }
                    }
                    if (takeMaskOff)
                    {
                        if (Character.HasEquippedItem(AIObjectiveFindDivingGear.LIGHT_DIVING_GEAR))
                        {
                            var mask = Character.Inventory.FindItemByTag(AIObjectiveFindDivingGear.LIGHT_DIVING_GEAR);
                            if (mask != null)
                            {
                                if (!mask.AllowedSlots.Contains(InvSlotType.Any) || !Character.Inventory.TryPutItem(mask, Character, new List<InvSlotType>() { InvSlotType.Any }))
                                {
                                    if (Character.Submarine?.TeamID != Character.TeamID || ObjectiveManager.GetCurrentPriority() >= AIObjectiveManager.RunPriority)
                                    {
                                        mask.Drop(Character);
                                        HandleRelocation(mask);
                                    }
                                    else if (findItemState == FindItemState.None || findItemState == FindItemState.DivingMask)
                                    {
                                        findItemState = FindItemState.DivingMask;
                                        if (FindSuitableContainer(mask, out Item targetContainer))
                                        {
                                            findItemState = FindItemState.None;
                                            itemIndex = 0;
                                            if (targetContainer != null)
                                            {
                                                var decontainObjective = new AIObjectiveDecontainItem(Character, mask, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                                decontainObjective.Abandoned += () =>
                                                {
                                                    ReequipUnequipped();
                                                    IgnoredItems.Add(targetContainer);
                                                };
                                                decontainObjective.Completed += () => ReequipUnequipped();
                                                ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                                return;
                                            }
                                            else
                                            {
                                                mask.Drop(Character);
                                                HandleRelocation(mask);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    ReequipUnequipped();
                                }
                            }
                        }
                    }
                }
            }
            // Other items
            if (isCarrying) { return; }
            if (!ObjectiveManager.CurrentObjective.AllowAutomaticItemUnequipping || !ObjectiveManager.GetActiveObjective().AllowAutomaticItemUnequipping) { return; }

            if (findItemState == FindItemState.None || findItemState == FindItemState.OtherItem)
            {
                for (int i = 0; i < 2; i++)
                {
                    var hand = i == 0 ? InvSlotType.RightHand : InvSlotType.LeftHand;
                    Item item = Character.Inventory.GetItemInLimbSlot(hand);
                    if (item == null) { continue; }

                    if (!item.AllowedSlots.Contains(InvSlotType.Any) || !Character.Inventory.TryPutItem(item, Character, new List<InvSlotType>() { InvSlotType.Any }) && Character.Submarine?.TeamID == Character.TeamID )
                    {
                        findItemState = FindItemState.OtherItem;
                        if (FindSuitableContainer(item, out Item targetContainer))
                        {
                            findItemState = FindItemState.None;
                            itemIndex = 0;
                            if (targetContainer != null)
                            {
                                var decontainObjective = new AIObjectiveDecontainItem(Character, item, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                decontainObjective.Abandoned += () =>
                                {
                                    ReequipUnequipped();
                                    IgnoredItems.Add(targetContainer);
                                };
                                ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                return;
                            }
                            else
                            {
                                item.Drop(Character);
                                HandleRelocation(item);
                            }
                        }
                    }
                }
            }
        }

        private readonly HashSet<Item> itemsToRelocate = new HashSet<Item>();

        private void HandleRelocation(Item item)
        {
            if (item.Submarine?.TeamID == CharacterTeamType.FriendlyNPC)
            {
                if (itemsToRelocate.Contains(item)) { return; }
                itemsToRelocate.Add(item);
                if (item.Submarine.ConnectedDockingPorts.TryGetValue(Submarine.MainSub, out DockingPort myPort))
                {
                    myPort.OnUnDocked += Relocate;
                }
                var campaign = GameMain.GameSession.Campaign;
                if (campaign != null)
                {
                    // In the campaign mode, undocking happens after leaving the outpost, so we can't use that.
                    campaign.BeforeLevelLoading += Relocate;
                }
            }

            void Relocate()
            {
                if (item == null || item.Removed) { return; }
                if (!itemsToRelocate.Contains(item)) { return; }
                var mainSub = Submarine.MainSub;
                if (item.ParentInventory != null)
                {
                    if (item.ParentInventory.Owner is Character c)
                    {
                        if (c.TeamID == CharacterTeamType.Team1 || c.TeamID == CharacterTeamType.Team2)
                        {
                            // Taken by a player/bot (if npc or monster would take the item, we'd probably still want it to spawn back to the main sub.
                            return;
                        }
                    }
                    else if (item.ParentInventory.Owner.Submarine == mainSub)
                    {
                        // Placed inside an inventory that's already in the main sub.
                        return;
                    }
                }
                // Laying on ground inside the main sub.
                if (item.Submarine == mainSub)
                {
                    return;
                }
                WayPoint wp = WayPoint.GetRandom(SpawnType.Cargo, null, mainSub);
                if (wp != null)
                {
                    item.Submarine = mainSub;
                    item.SetTransform(wp.SimPosition, 0.0f);
                }
                itemsToRelocate.Remove(item);
            }
        }

        private enum FindItemState
        {
            None,
            DivingSuit,
            DivingMask,
            OtherItem
        }
        private FindItemState findItemState;
        private int itemIndex;

        public bool FindSuitableContainer(Item containableItem, out Item suitableContainer) => FindSuitableContainer(Character, containableItem, IgnoredItems, ref itemIndex, out suitableContainer);

        public static bool FindSuitableContainer(Character character, Item containableItem, List<Item> ignoredItems, ref int itemIndex, out Item suitableContainer)
        {
            suitableContainer = null;
            if (character.FindItem(ref itemIndex, out Item targetContainer, ignoredItems: ignoredItems, positionalReference: containableItem, customPriorityFunction: i =>
            {
                if (i.IsThisOrAnyContainerIgnoredByAI(character)) { return 0; }
                var container = i.GetComponent<ItemContainer>();
                if (container == null) { return 0; }
                if (!container.HasAccess(character)) { return 0; }
                if (!container.Inventory.CanBePut(containableItem)) { return 0; }
                if (container.ShouldBeContained(containableItem, out bool isRestrictionsDefined))
                {
                    if (isRestrictionsDefined)
                    {
                        return 10;
                    }
                    else
                    {
                        if (containableItem.IsContainerPreferred(container, out bool isPreferencesDefined, out bool isSecondary))
                        {
                            return isPreferencesDefined ? isSecondary ? 2 : 5 : 1;
                        }
                        else
                        {
                            return isPreferencesDefined ? 0 : 1;
                        }
                    }
                }
                else
                {
                    return 0;
                }
            }))
            {
                suitableContainer = targetContainer;
                return true;
            }
            return false;
        }

        protected void ReportProblems()
        {
            Order newOrder = null;
            Hull targetHull = null;
            bool speak = true;
            if (Character.CurrentHull != null)
            {
                bool isFighting = ObjectiveManager.HasActiveObjective<AIObjectiveCombat>();
                bool isFleeing = ObjectiveManager.HasActiveObjective<AIObjectiveFindSafety>();
                foreach (var hull in VisibleHulls)
                {
                    foreach (Character target in Character.CharacterList)
                    {
                        if (target.CurrentHull != hull || !target.Enabled) { continue; }
                        if (AIObjectiveFightIntruders.IsValidTarget(target, Character))
                        {
                            if (AddTargets<AIObjectiveFightIntruders, Character>(Character, target) && newOrder == null)
                            {
                                var orderPrefab = Order.GetPrefab("reportintruders");
                                newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                                targetHull = hull;
                                if (target.IsEscorted)
                                {
                                    if (!Character.IsPrisoner && target.IsPrisoner)
                                    {
                                        string msg = TextManager.GetWithVariables("orderdialog.prisonerescaped", new string[] { "[roomname]" }, new string[] { targetHull.DisplayName }, new bool[] { false, true }, true);
                                        Character.Speak(msg, ChatMessageType.Order);
                                        speak = false;
                                    }
                                    else if (!IsMentallyUnstable && target.AIController.IsMentallyUnstable)
                                    {
                                        string msg = TextManager.GetWithVariables("orderdialog.mentalcase", new string[] { "[roomname]" }, new string[] { targetHull.DisplayName }, new bool[] { false, true }, true);
                                        Character.Speak(msg, ChatMessageType.Order);
                                        speak = false;
                                    }
                                }
                            }
                        }
                    }
                    if (AIObjectiveExtinguishFires.IsValidTarget(hull, Character))
                    {
                        if (AddTargets<AIObjectiveExtinguishFires, Hull>(Character, hull) && newOrder == null)
                        {
                            var orderPrefab = Order.GetPrefab("reportfire");
                            newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                            targetHull = hull;
                        }
                    }
                    if (IsBallastFloraNoticeable(Character, hull) && newOrder == null)
                    {
                        var orderPrefab = Order.GetPrefab("reportballastflora");
                        newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                        targetHull = hull;
                    }
                    if (!isFighting)
                    {
                        foreach (var gap in hull.ConnectedGaps)
                        {
                            if (AIObjectiveFixLeaks.IsValidTarget(gap, Character))
                            {
                                if (AddTargets<AIObjectiveFixLeaks, Gap>(Character, gap) && newOrder == null && !gap.IsRoomToRoom)
                                {
                                    var orderPrefab = Order.GetPrefab("reportbreach");
                                    newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                                    targetHull = hull;
                                }
                            }
                        }
                        if (!isFleeing)
                        {
                            foreach (Character target in Character.CharacterList)
                            {
                                if (target.CurrentHull != hull) { continue; }
                                if (AIObjectiveRescueAll.IsValidTarget(target, Character))
                                {
                                    if (AddTargets<AIObjectiveRescueAll, Character>(Character, target) && newOrder == null && !ObjectiveManager.HasActiveObjective<AIObjectiveRescue>())
                                    {
                                        var orderPrefab = Order.GetPrefab("requestfirstaid");
                                        newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                                        targetHull = hull;
                                    }
                                }
                            }
                            foreach (Item item in Item.ItemList)
                            {
                                if (item.CurrentHull != hull) { continue; }
                                if (AIObjectiveRepairItems.IsValidTarget(item, Character))
                                {
                                    if (item.Repairables.All(r => item.ConditionPercentage > r.RepairIconThreshold)) { continue; }
                                    if (AddTargets<AIObjectiveRepairItems, Item>(Character, item) && newOrder == null && !ObjectiveManager.HasActiveObjective<AIObjectiveRepairItem>())
                                    {
                                        var orderPrefab = Order.GetPrefab("reportbrokendevices");
                                        newOrder = new Order(orderPrefab, hull, item.Repairables?.FirstOrDefault(), orderGiver: Character);
                                        targetHull = hull;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (newOrder != null && speak)
            {
                // for now, escorted characters use the report system to get targets but do not speak. escort-character specific dialogue could be implemented
                if (!Character.IsEscorted)
                {
                    if (Character.TeamID == CharacterTeamType.FriendlyNPC)
                    {
                        Character.Speak(newOrder.GetChatMessage("", targetHull?.DisplayName, givingOrderToSelf: false), ChatMessageType.Default,
                            identifier: newOrder.Prefab.Identifier + (targetHull?.DisplayName ?? "null"),
                            minDurationBetweenSimilar: 60.0f);
                    }
                    else if (Character.IsOnPlayerTeam && GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime))
                    {
                        Character.Speak(newOrder.GetChatMessage("", targetHull?.DisplayName, givingOrderToSelf: false), ChatMessageType.Order);
#if SERVER
                        GameMain.Server.SendOrderChatMessage(new OrderChatMessage(newOrder, "", CharacterInfo.HighestManualOrderPriority, targetHull, null, Character));
#endif
                    }
                }
            }
        }

        public static bool IsBallastFloraNoticeable(Character character, Hull hull)
        {
            foreach (var ballastFlora in MapCreatures.Behavior.BallastFloraBehavior.EntityList)
            {
                if (ballastFlora.Parent?.Submarine != character.Submarine) { continue; }
                if (!ballastFlora.HasBrokenThrough) { continue; }
                // Don't react to the first two branches, because they are usually in the very edges of the room.
                if (ballastFlora.Branches.Count(b => !b.Removed && b.Health > 0 && b.CurrentHull == hull) > 2)
                {
                    return true;
                }
            }
            return false;
        }

        public static void ReportProblem(Character reporter, Order order, Hull targetHull = null)
        {
            if (reporter == null || order == null) { return; }
            var visibleHulls = targetHull is null ? new List<Hull>(reporter.GetVisibleHulls()) : new List<Hull> { targetHull };
            foreach (var hull in visibleHulls)
            {
                PropagateHullSafety(reporter, hull);
                RefreshTargets(reporter, order, hull);
            }
        }

        private void UpdateSpeaking()
        {
            if (!Character.IsOnPlayerTeam) { return; }

            if (Character.Oxygen < 20.0f)
            {
                Character.Speak(TextManager.Get("DialogLowOxygen"), null, Rand.Range(0.5f, 5.0f), "lowoxygen", 30.0f);
            }

            if (Character.Bleeding > 2.0f)
            {
                Character.Speak(TextManager.Get("DialogBleeding"), null, Rand.Range(0.5f, 5.0f), "bleeding", 30.0f);
            }

            if (Character.PressureTimer > 50.0f && Character.CurrentHull?.DisplayName != null)
            {
                Character.Speak(TextManager.GetWithVariable("DialogPressure", "[roomname]", Character.CurrentHull.DisplayName, true), null, Rand.Range(0.5f, 5.0f), "pressure", 30.0f);
            }
        }

        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            // The attack incapacitated/killed the character: respond immediately to trigger nearby characters because the update loop no longer runs
            if (wasConscious && (Character.IsIncapacitated || Character.Stun > 0.0f))
            {
                RespondToAttack(attacker, attackResult);
                wasConscious = false;
                return;
            }
            if (Character.IsDead) { return; }
            if (attacker == null || Character.IsPlayer)
            {
                // The player characters need to "respond" to the attack always, because the update loop doesn't run for them.
                // Otherwise other NPCs totally ignore when player characters are attacked.
                RespondToAttack(attacker, attackResult);
                return;
            }
            if (previousAttackResults.ContainsKey(attacker))
            {
                if (attackResult.Afflictions != null)
                {
                    foreach (Affliction newAffliction in attackResult.Afflictions)
                    {
                        var matchingAffliction = previousAttackResults[attacker].Afflictions.Find(a => a.Prefab == newAffliction.Prefab && a.Source == newAffliction.Source);
                        if (matchingAffliction == null)
                        {
                            previousAttackResults[attacker].Afflictions.Add(newAffliction);
                        }
                        else
                        {
                            matchingAffliction.Strength += newAffliction.Strength;
                        }
                    }
                }
                previousAttackResults[attacker] = new AttackResult(previousAttackResults[attacker].Afflictions, previousAttackResults[attacker].HitLimb);
            }
            else
            {
                previousAttackResults.Add(attacker, attackResult);
            }
        }

        private void RespondToAttack(Character attacker, AttackResult attackResult)
        { 
            // excluding poisons etc
            float realDamage = attackResult.Damage;
            // including poisons etc
            float totalDamage = realDamage;
            if (attackResult.Afflictions != null)
            {
                foreach (Affliction affliction in attackResult.Afflictions)
                {
                    totalDamage -= affliction.Prefab.KarmaChangeOnApplied * affliction.Strength;
                }
            }
            if (totalDamage <= 0.01f) { return; }
            if (Character.IsBot)
            {
                if (!freezeAI && !Character.IsDead && Character.IsIncapacitated)
                {
                    // Removes the combat objective and resets all objectives.
                    objectiveManager.CreateAutonomousObjectives();
                    objectiveManager.SortObjectives();
                    freezeAI = true;
                }
            }
            if (attacker == null || attacker.IsDead || attacker.Removed)
            {
                // Don't react to the damage if there's no attacker.
                // We might consider launching the retreat combat objective in some cases, so that the bot does not just stand somewhere getting damaged and dying.
                // But fires and enemies should already be handled by the FindSafetyObjective.
                return;
                // Ignore damage from falling etc that we shouldn't react to.
                //if (Character.LastDamageSource == null) { return; }
                //AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
            }
            if (realDamage <= 0 && (attacker.IsBot || attacker.TeamID == Character.TeamID))
            {
                // Don't react to damage that is entirely based on karma penalties (medics, poisons etc), unless applier is player
                return;
            }
            if (attacker.Submarine == null && Character.Submarine != null)
            {
                // Don't react to attackers that are outside of the sub (e.g. AoE attacks)
                return;
            }
            if (IsFriendly(attacker))
            {
                if (attacker.AnimController.Anim == Barotrauma.AnimController.Animation.CPR && attacker.SelectedCharacter == Character)
                {
                    // Don't attack characters that damage you while doing cpr, because let's assume that they are helping you.
                    // Should not cancel any existing ai objectives (so that if the character attacked you and then helped, we still would want to retaliate).
                    return;
                }
                float cumulativeDamage = GetDamageDoneByAttacker(attacker);
                bool isAccidental = attacker.IsBot && !IsMentallyUnstable && !attacker.AIController.IsMentallyUnstable && Character.CombatAction == null;
                if (isAccidental)
                {
                    if (!Character.IsSecurity && cumulativeDamage > 1)
                    {
                        AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, attacker);
                    }
                }
                else
                {
                    (GameMain.GameSession?.GameMode as CampaignMode)?.OutpostNPCAttacked(Character, attacker, attackResult);
                    // Inform other NPCs
                    if (cumulativeDamage > 1 || totalDamage >= 10)
                    {
                        InformOtherNPCs(cumulativeDamage);
                    }
                    if (Character.IsBot)
                    {
                        if (ObjectiveManager.CurrentObjective is AIObjectiveFightIntruders) { return; }
                        if (attacker.IsPlayer)
                        {
                            if (Character.IsSecurity)
                            {
                                if (attacker.TeamID != Character.TeamID && cumulativeDamage > 1 || cumulativeDamage > 10)
                                {
                                    Character.Speak(TextManager.Get("dialogattackedbyfriendlysecurityarrest"), null, 0.50f, "attackedbyfriendlysecurityarrest", minDurationBetweenSimilar: 30.0f);
                                }
                                else
                                {
                                    Character.Speak(TextManager.Get("dialogattackedbyfriendlysecurityresponse"), null, 0.50f, "attackedbyfriendlysecurityresponse", minDurationBetweenSimilar: 30.0f);
                                }
                            }
                            else if (!Character.IsInstigator && cumulativeDamage > 1)
                            {
                                Character.Speak(TextManager.Get("DialogAttackedByFriendly"), null, 0.50f, "attackedbyfriendly", minDurationBetweenSimilar: 30.0f);
                            }
                        }
                        if (cumulativeDamage > 1 && attacker.TeamID != Character.TeamID)
                        {
                            // If the attacker is using a low damage and high frequency weapon like a repair tool, we shouldn't use any delay.
                            AddCombatObjective(DetermineCombatMode(Character, cumulativeDamage), attacker, delay: realDamage > 1 ? GetReactionTime() : 0);
                        }
                        else
                        {
                            // Don't react to minor (accidental) dmg done by characters that are in the same team
                            if (cumulativeDamage < 10)
                            {
                                if (!Character.IsSecurity && cumulativeDamage > 1)
                                {
                                    AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, attacker);
                                }
                            }
                            else
                            {
                                AddCombatObjective(DetermineCombatMode(Character, cumulativeDamage, dmgThreshold: 50), attacker, GetReactionTime() * 2);
                            }
                        }
                    }
                }
            }
            else
            {
                if (Character.Submarine != null && Character.Submarine.GetConnectedSubs().Contains(attacker.Submarine))
                {
                    // Non-friendly
                    InformOtherNPCs(GetDamageDoneByAttacker(attacker));
                }
                if (Character.IsBot)
                {
                    AddCombatObjective(DetermineCombatMode(Character, cumulativeDamage: realDamage), attacker);
                }
            }

            void InformOtherNPCs(float cumulativeDamage)
            {
                foreach (Character otherCharacter in Character.CharacterList)
                {
                    if (otherCharacter == Character || otherCharacter.IsUnconscious || otherCharacter.Removed) { continue; }
                    if (otherCharacter.Submarine != Character.Submarine) { continue; }
                    if (otherCharacter.Submarine != attacker.Submarine) { continue; }
                    if (otherCharacter.Info?.Job == null || otherCharacter.IsInstigator) { continue; }
                    if (otherCharacter.IsPlayer) { continue; }
                    if (!(otherCharacter.AIController is HumanAIController otherHumanAI)) { continue; }
                    if (!otherHumanAI.IsFriendly(Character)) { continue; }
                    bool isWitnessing = otherHumanAI.VisibleHulls.Contains(Character.CurrentHull) || otherHumanAI.VisibleHulls.Contains(attacker.CurrentHull);
                    if (!isWitnessing && !CheckReportRange(Character, otherCharacter, ReportRange)) { continue; }
                    var combatMode = DetermineCombatMode(otherCharacter, cumulativeDamage, isWitnessing, dmgThreshold: attacker.TeamID == Character.TeamID ? 50 : 10);
                    float delay = isWitnessing ? GetReactionTime() : Rand.Range(2.0f, 5.0f, Rand.RandSync.Unsynced);
                    otherHumanAI.AddCombatObjective(combatMode, attacker, delay);
                }
            }

            AIObjectiveCombat.CombatMode DetermineCombatMode(Character c, float cumulativeDamage, bool isWitnessing = false, float dmgThreshold = 10, bool allowOffensive = true)
            {
                if (!IsFriendly(attacker))
                {
                    if (Character.Submarine == null)
                    {
                        // Outside -> don't react.
                        return AIObjectiveCombat.CombatMode.None;
                    }
                    if (!Character.Submarine.GetConnectedSubs().Contains(attacker.Submarine))
                    {
                        // Attacked from an unconnected submarine.
                        return Character.SelectedConstruction?.GetComponent<Turret>() != null ? AIObjectiveCombat.CombatMode.None : AIObjectiveCombat.CombatMode.Retreat;
                    }
                    return c.AIController is HumanAIController humanAI &&
                        (humanAI.ObjectiveManager.IsCurrentOrder<AIObjectiveFightIntruders>() || humanAI.ObjectiveManager.Objectives.Any(o => o is AIObjectiveFightIntruders)) 
                        ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Defensive;
                }
                else
                {
                    if (Character.Submarine == null || !Character.Submarine.GetConnectedSubs().Contains(attacker.Submarine))
                    {
                        // Outside or attacked from an unconnected submarine -> don't react.
                        return AIObjectiveCombat.CombatMode.None;
                    }
                    // If there are any enemies around, just ignore the friendly fire
                    if (Character.CharacterList.Any(ch => ch.Submarine == Character.Submarine && !ch.Removed && !ch.IsDead && !ch.IsIncapacitated && !IsFriendly(ch) && VisibleHulls.Contains(ch.CurrentHull)))
                    {
                        return AIObjectiveCombat.CombatMode.None;
                    }
                    else if (isWitnessing && Character.CombatAction != null && !c.IsSecurity)
                    {
                        return Character.CombatAction.WitnessReaction;
                    }
                    else if (Character.IsInstigator && attacker.IsPlayer)
                    {
                        // The guards don't react when the player attacks instigators.
                        return c.IsSecurity ? AIObjectiveCombat.CombatMode.None : (Character.CombatAction != null ? Character.CombatAction.WitnessReaction : AIObjectiveCombat.CombatMode.Retreat);
                    }
                    else if (attacker.TeamID == CharacterTeamType.FriendlyNPC && !(attacker.AIController.IsMentallyUnstable || attacker.AIController.IsMentallyUnstable))
                    {
                        if (c.IsSecurity)
                        {
                            return Character.CombatAction != null ? Character.CombatAction.GuardReaction : AIObjectiveCombat.CombatMode.None;
                        }
                        else
                        {
                            return Character.CombatAction != null ? Character.CombatAction.WitnessReaction : AIObjectiveCombat.CombatMode.None;
                        }
                    }
                    else
                    {
                        if (c.AIController is HumanAIController humanAI && humanAI.ObjectiveManager.GetActiveObjective<AIObjectiveCombat>()?.Enemy == attacker)
                        {
                            // Already targeting the attacker -> treat as a more serious threat.
                            cumulativeDamage *= 2;
                        }
                        if (attackResult.Afflictions != null && attackResult.Afflictions.Any(a => a is AfflictionHusk))
                        {
                            cumulativeDamage = 100;
                        }
                        if (cumulativeDamage > dmgThreshold)
                        {
                            if (c.IsSecurity)
                            {
                                return c.IsSecurity && allowOffensive ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Arrest;
                            }
                            else
                            {
                                return c == Character ? AIObjectiveCombat.CombatMode.Defensive : AIObjectiveCombat.CombatMode.Retreat;
                            }
                        }
                        else
                        {
                            return c.IsSecurity ? AIObjectiveCombat.CombatMode.Arrest : AIObjectiveCombat.CombatMode.Retreat;
                        }
                    }
                }
            }
        }

        public void AddCombatObjective(AIObjectiveCombat.CombatMode mode, Character target, float delay = 0, Func<AIObjective, bool> abortCondition = null, Action onAbort = null, Action onCompleted = null, bool allowHoldFire = false)
        {
            if (mode == AIObjectiveCombat.CombatMode.None) { return; }
            if (Character.IsDead || Character.IsIncapacitated || Character.Removed) { return; }
            if (!Character.IsBot) { return; }
            if (ObjectiveManager.Objectives.FirstOrDefault(o => o is AIObjectiveCombat) is AIObjectiveCombat combatObjective)
            {
                // Don't replace offensive mode with something else
                if (combatObjective.Mode == AIObjectiveCombat.CombatMode.Offensive && mode != AIObjectiveCombat.CombatMode.Offensive) { return; }
                if (combatObjective.Mode != mode || combatObjective.Enemy != target || (combatObjective.Enemy == null && target == null))
                {
                    // Replace the old objective with the new.
                    ObjectiveManager.Objectives.Remove(combatObjective);
                    ObjectiveManager.AddObjective(CreateCombatObjective());
                }
            }
            else
            {
                if (delay > 0)
                {
                    ObjectiveManager.AddObjective(CreateCombatObjective(), delay);
                }
                else
                {
                    ObjectiveManager.AddObjective(CreateCombatObjective());
                }
            }

            AIObjectiveCombat CreateCombatObjective()
            {
                var objective = new AIObjectiveCombat(Character, target, mode, objectiveManager)
                {
                    HoldPosition = Character.Info?.Job?.Prefab.Identifier == "watchman",
                    AbortCondition = abortCondition,
                    allowHoldFire = allowHoldFire,
                };
                if (onAbort != null)
                {
                    objective.Abandoned += onAbort;
                }
                if (onCompleted != null)
                {
                    objective.Completed += onCompleted;
                }
                return objective;
            }
        }

        public void SetOrder(Order order, string option, int priority, Character orderGiver, bool speak = true)
        {
            objectiveManager.SetOrder(order, option, priority, orderGiver, speak);
        }

        public void SetForcedOrder(Order order, string option, Character orderGiver)
        {
            var objective = ObjectiveManager.CreateObjective(order, option, orderGiver);
            ObjectiveManager.SetForcedOrder(objective);
        }

        public void ClearForcedOrder()
        {
            ObjectiveManager.ClearForcedOrder();
        }

        public override void SelectTarget(AITarget target)
        {
            SelectedAiTarget = target;
        }

        public override void Reset()
        {
            base.Reset();
            objectiveManager.SortObjectives();
            SortTimer = sortObjectiveInterval;
            float waitDuration = characterWaitOnSwitch;
            if (ObjectiveManager.IsCurrentObjective<AIObjectiveIdle>())
            {
                waitDuration *= 2;
            }
            ObjectiveManager.WaitTimer = waitDuration;
        }

        public override void Escape(float deltaTime)
        {
            UpdateEscape(deltaTime, canAttackDoors: false);
        }

        private void CheckCrouching(float deltaTime)
        {
            crouchRaycastTimer -= deltaTime;
            if (crouchRaycastTimer > 0.0f) { return; }

            crouchRaycastTimer = crouchRaycastInterval;

            //start the raycast in front of the character in the direction it's heading to
            Vector2 startPos = Character.SimPosition;
            startPos.X += MathHelper.Clamp(Character.AnimController.TargetMovement.X, -1.0f, 1.0f);

            //do a raycast upwards to find any walls
            float minCeilingDist = Character.AnimController.Collider.height / 2 + Character.AnimController.Collider.radius + 0.1f;

            shouldCrouch = Submarine.PickBody(startPos, startPos + Vector2.UnitY * minCeilingDist, null, Physics.CollisionWall, customPredicate: (fixture) => { return !(fixture.Body.UserData is Submarine); }) != null;
        }

        public bool AllowCampaignInteraction()
        {
            if (Character == null || Character.Removed || Character.IsIncapacitated) { return false; }

            switch (ObjectiveManager.CurrentObjective)
            {
                case AIObjectiveCombat _:
                case AIObjectiveFindSafety _:
                case AIObjectiveExtinguishFires _:
                case AIObjectiveFightIntruders _:
                case AIObjectiveFixLeaks _:
                    return false;
            }
            return true;
        }

        public static bool NeedsDivingGear(Hull hull, out bool needsSuit)
        {
            needsSuit = false;
            if (hull == null || 
                hull.WaterPercentage > 90 || 
                hull.LethalPressure > 0 || 
                hull.ConnectedGaps.Any(gap => !gap.IsRoomToRoom && gap.Open > 0.5f))
            {
                needsSuit = true;
                return true;
            }
            if (hull.WaterPercentage > 60 || hull.OxygenPercentage < HULL_LOW_OXYGEN_PERCENTAGE + 1)
            {
                return true;
            }
            return false;
        }

        public static bool HasDivingGear(Character character, float conditionPercentage = 0) => HasDivingSuit(character, conditionPercentage) || HasDivingMask(character, conditionPercentage);

        /// <summary>
        /// Check whether the character has a diving suit in usable condition plus some oxygen.
        /// </summary>
        public static bool HasDivingSuit(Character character, float conditionPercentage = 0) 
            => HasItem(character, AIObjectiveFindDivingGear.HEAVY_DIVING_GEAR, out _, AIObjectiveFindDivingGear.OXYGEN_SOURCE, conditionPercentage, requireEquipped: true,
                predicate: (Item item) => character.HasEquippedItem(item, InvSlotType.OuterClothes));

        /// <summary>
        /// Check whether the character has a diving mask in usable condition plus some oxygen.
        /// </summary>
        public static bool HasDivingMask(Character character, float conditionPercentage = 0) 
            => HasItem(character, AIObjectiveFindDivingGear.LIGHT_DIVING_GEAR, out _, AIObjectiveFindDivingGear.OXYGEN_SOURCE, conditionPercentage, requireEquipped: true);

        private static List<Item> matchingItems = new List<Item>();

        /// <summary>
        /// Note: uses a single list for matching items. The item is reused each time when the method is called. So if you use the method twice, and then refer to the first items, you'll actually get the second. 
        /// To solve this, create a copy of the collection or change the code so that you first handle the first items and only after that query for the next items.
        /// </summary>
        public static bool HasItem(Character character, string tagOrIdentifier, out IEnumerable<Item> items, string containedTag = null, float conditionPercentage = 0, bool requireEquipped = false, bool recursive = true, Func<Item, bool> predicate = null)
        {
            matchingItems.Clear();
            items = matchingItems;
            if (character == null) { return false; }
            if (character.Inventory == null) { return false; }
            matchingItems = character.Inventory.FindAllItems(i => (i.Prefab.Identifier == tagOrIdentifier || i.HasTag(tagOrIdentifier)) &&
                i.ConditionPercentage >= conditionPercentage &&
                (!requireEquipped || character.HasEquippedItem(i)) &&
                (predicate == null || predicate(i)), recursive, matchingItems);
            items = matchingItems;
            return matchingItems.Any(i => i != null && (containedTag == null || i.ContainedItems.Any(it => it.HasTag(containedTag) && it.ConditionPercentage > conditionPercentage)));
        }

        public static void StructureDamaged(Structure structure, float damageAmount, Character character)
        {
            const float MaxDamagePerSecond = 5.0f;
            const float MaxDamagePerFrame = MaxDamagePerSecond * (float)Timing.Step;

            const float WarningThreshold = 5.0f;
            const float ArrestThreshold = 20.0f;
            const float KillThreshold = 50.0f;

            if (character == null || damageAmount <= 0.0f) { return; }
            if (structure?.Submarine == null || !structure.Submarine.Info.IsOutpost || character.TeamID == structure.Submarine.TeamID) { return; }
            //structure not indestructible = something that's "meant" to be destroyed, like an ice wall in mines
            if (!structure.Prefab.IndestructibleInOutposts) { return; }

            bool someoneSpoke = false;
            float maxAccumulatedDamage = 0.0f;
            foreach (Character otherCharacter in Character.CharacterList)
            {
                if (otherCharacter == character || otherCharacter.TeamID == character.TeamID || otherCharacter.IsDead ||
                    otherCharacter.Info?.Job == null ||
                    !(otherCharacter.AIController is HumanAIController otherHumanAI) ||
                    !otherHumanAI.VisibleHulls.Contains(character.CurrentHull))
                {
                    continue;
                }
                if (!otherCharacter.CanSeeCharacter(character)) { continue; }

                if (!otherHumanAI.structureDamageAccumulator.ContainsKey(character)) { otherHumanAI.structureDamageAccumulator.Add(character, 0.0f); }
                float prevAccumulatedDamage = otherHumanAI.structureDamageAccumulator[character];
                otherHumanAI.structureDamageAccumulator[character] += MathHelper.Clamp(damageAmount, -MaxDamagePerFrame, MaxDamagePerFrame);
                float accumulatedDamage = Math.Max(otherHumanAI.structureDamageAccumulator[character], maxAccumulatedDamage);
                maxAccumulatedDamage = Math.Max(accumulatedDamage, maxAccumulatedDamage);

                if (GameMain.GameSession?.Campaign?.Map?.CurrentLocation != null)
                {
                    var reputationLoss = damageAmount * Reputation.ReputationLossPerWallDamage;
                    GameMain.GameSession.Campaign.Map.CurrentLocation.Reputation.AddReputation(-reputationLoss);
                }

                if (accumulatedDamage <= WarningThreshold) { return; }

                if (accumulatedDamage > WarningThreshold && prevAccumulatedDamage <= WarningThreshold &&
                    !someoneSpoke && !character.IsIncapacitated && character.Stun <= 0.0f)
                {
                    //if the damage is still fairly low, wait and see if the character keeps damaging the walls to the point where we need to intervene
                    if (accumulatedDamage < ArrestThreshold)
                    {
                        if (otherHumanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveIdle>())
                        {
                            (otherHumanAI.ObjectiveManager.CurrentObjective as AIObjectiveIdle)?.FaceTargetAndWait(character, 5.0f);
                        }
                    }
                    otherCharacter.Speak(TextManager.Get("dialogdamagewallswarning"), null, Rand.Range(0.5f, 1.0f), "damageoutpostwalls", 10.0f);
                    someoneSpoke = true;
                }
                // React if we are security
                if ((accumulatedDamage > ArrestThreshold && prevAccumulatedDamage <= ArrestThreshold) ||
                    (accumulatedDamage > KillThreshold && prevAccumulatedDamage <= KillThreshold))
                {
                    var combatMode = accumulatedDamage > KillThreshold ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Arrest;
                    if (!TriggerSecurity(otherHumanAI, combatMode))
                    {
                        // Else call the others
                        foreach (Character security in Character.CharacterList.Where(c => c.TeamID == otherCharacter.TeamID).OrderByDescending(c => Vector2.DistanceSquared(character.WorldPosition, c.WorldPosition)))
                        {
                            if (!TriggerSecurity(security.AIController as HumanAIController, combatMode))
                            {
                                // Only alert one guard at a time
                                return;
                            }
                        }
                    }
                }
            }

            bool TriggerSecurity(HumanAIController humanAI, AIObjectiveCombat.CombatMode combatMode)
            {
                if (humanAI == null) { return false; }
                if (!humanAI.Character.IsSecurity) { return false; }
                if (humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveCombat>()) { return false; }
                humanAI.AddCombatObjective(combatMode, character, delay: GetReactionTime(), allowHoldFire: true, onCompleted: () => 
                { 
                    //if the target is arrested successfully, reset the damage accumulator
                    foreach (Character anyCharacter in Character.CharacterList)
                    {
                        if (anyCharacter.AIController is HumanAIController anyAI)
                        {
                            anyAI.structureDamageAccumulator?.Remove(character);
                        }
                    }
                });
                return true;
            }
        }

        public static void ItemTaken(Item item, Character character)
        {
            if (item == null || character == null || item.GetComponent<LevelResource>() != null) { return; }
            Character thief = character;
            bool someoneSpoke = false;

            bool stolenItemsInside = item.OwnInventory?.FindAllItems(it => it.SpawnedInOutpost && !it.AllowStealing, recursive: true).Any() ?? false;

            if ((item.SpawnedInOutpost && !item.AllowStealing || stolenItemsInside) && thief.TeamID != CharacterTeamType.FriendlyNPC && !item.HasTag("handlocker"))
            {
                foreach (Character otherCharacter in Character.CharacterList)
                {
                    if (otherCharacter == thief || otherCharacter.TeamID == thief.TeamID || otherCharacter.IsDead ||
                        otherCharacter.Info?.Job == null ||
                        !(otherCharacter.AIController is HumanAIController otherHumanAI) ||
                        !otherHumanAI.VisibleHulls.Contains(thief.CurrentHull))
                    {
                        continue;
                    }
                    //if (!otherCharacter.IsFacing(thief.WorldPosition)) { continue; }
                    if (!otherCharacter.CanSeeCharacter(thief)) { continue; }
                    // Don't react if the player is taking an extinguisher and there's any fires on the sub, or diving gear when the sub is flooding
                    // -> allow them to use the emergency items
                    if (character.Submarine != null)
                    {
                        var connectedHulls = character.Submarine.GetHulls(alsoFromConnectedSubs: true);
                        if (item.HasTag("fireextinguisher") && connectedHulls.Any(h => h.FireSources.Any())) { continue; }
                        if (item.HasTag("diving") && connectedHulls.Any(h => h.ConnectedGaps.Any(g => AIObjectiveFixLeaks.IsValidTarget(g, thief)))) { continue; }
                    }
                    if (!someoneSpoke && !character.IsIncapacitated && character.Stun <= 0.0f)
                    {
                        if (!item.StolenDuringRound && GameMain.GameSession?.Campaign?.Map?.CurrentLocation != null)
                        {
                            var reputationLoss = MathHelper.Clamp(
                                (item.Prefab.GetMinPrice() ?? 0) * Reputation.ReputationLossPerStolenItemPrice, 
                                Reputation.MinReputationLossPerStolenItem, Reputation.MaxReputationLossPerStolenItem);
                            GameMain.GameSession.Campaign.Map.CurrentLocation.Reputation.AddReputation(-reputationLoss);
                        }
                        item.StolenDuringRound = true;
                        otherCharacter.Speak(TextManager.Get("dialogstealwarning"), null, Rand.Range(0.5f, 1.0f), "thief", 10.0f);
                        someoneSpoke = true;
#if CLIENT
                        HintManager.OnStoleItem(thief, item);
#endif
                    }
                    // React if we are security
                    if (!TriggerSecurity(otherHumanAI))
                    {
                        // Else call the others
                        foreach (Character security in Character.CharacterList.Where(c => c.TeamID == otherCharacter.TeamID).OrderByDescending(c => Vector2.DistanceSquared(thief.WorldPosition, c.WorldPosition)))
                        {
                            if (TriggerSecurity(security.AIController as HumanAIController))
                            {
                                // Only alert one guard at a time
                                break;
                            }
                        }
                    }
                }
            }
            else if (item.OwnInventory?.FindItem(it => it.SpawnedInOutpost && !item.AllowStealing, true) is { } foundItem)
            {
                ItemTaken(foundItem, character);
            }

            bool TriggerSecurity(HumanAIController humanAI)
            {
                if (humanAI == null) { return false; }
                if (!humanAI.Character.IsSecurity) { return false; }
                if (humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveCombat>()) { return false; }
                humanAI.AddCombatObjective(AIObjectiveCombat.CombatMode.Arrest, thief, delay: GetReactionTime(),
                    abortCondition: obj => thief.Inventory.FindItem(it => it != null && it.StolenDuringRound, true) == null,
                    onAbort: () =>
                    {
                        if (item != null && !item.Removed && humanAI != null && !humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveGetItem>())
                        {
                            humanAI.ObjectiveManager.AddObjective(new AIObjectiveGetItem(humanAI.Character, item, humanAI.ObjectiveManager, equip: false)
                            {
                                BasePriority = 10
                            });
                        }
                    },
                    allowHoldFire: true);
                return true;
            }
        }

        // 0.225 - 0.375
        private static float GetReactionTime() => reactionTime * Rand.Range(0.75f, 1.25f);

        /// <summary>
        /// Updates the hull safety for all ai characters in the team. The idea is that the crew communicates (magically) via radio about the threads.
        /// The safety levels need to be calculated for each bot individually, because the formula takes into account things like current orders.
        /// There's now a cached value per each hull, which should prevent too frequent calculations.
        /// </summary>
        public static void PropagateHullSafety(Character character, Hull hull)
        {
            DoForEachCrewMember(character, (humanAi) => humanAi.RefreshHullSafety(hull));
        }

        private void RefreshHullSafety(Hull hull)
        {
            if (GetHullSafety(hull, Character, VisibleHulls) > HULL_SAFETY_THRESHOLD)
            {
                UnsafeHulls.Remove(hull);
            }
            else
            {
                UnsafeHulls.Add(hull);
            }
        }

        public static void RefreshTargets(Character character, Order order, Hull hull)
        {
            switch (order.Identifier)
            {
                case "reportfire":
                    AddTargets<AIObjectiveExtinguishFires, Hull>(character, hull);
                    break;
                case "reportbreach":
                    foreach (var gap in hull.ConnectedGaps)
                    {
                        if (AIObjectiveFixLeaks.IsValidTarget(gap, character))
                        {
                            AddTargets<AIObjectiveFixLeaks, Gap>(character, gap);
                        }
                    }
                    break;
                case "reportbrokendevices":
                    foreach (var item in Item.ItemList)
                    {
                        if (item.CurrentHull != hull) { continue; }
                        if (AIObjectiveRepairItems.IsValidTarget(item, character))
                        {
                            if (item.Repairables.All(r => item.ConditionPercentage >= r.RepairThreshold)) { continue; }
                            AddTargets<AIObjectiveRepairItems, Item>(character, item);
                        }
                    }
                    break;
                case "reportintruders":
                    foreach (var enemy in Character.CharacterList)
                    {
                        if (enemy.CurrentHull != hull) { continue; }
                        if (AIObjectiveFightIntruders.IsValidTarget(enemy, character))
                        {
                            AddTargets<AIObjectiveFightIntruders, Character>(character, enemy);
                        }
                    }
                    break;
                case "requestfirstaid":
                    foreach (var c in Character.CharacterList)
                    {
                        if (c.CurrentHull != hull) { continue; }
                        if (AIObjectiveRescueAll.IsValidTarget(c, character))
                        {
                            AddTargets<AIObjectiveRescueAll, Character>(character, c);
                        }
                    }
                    break;
                default:
#if DEBUG
                    DebugConsole.ThrowError(order.Identifier + " not implemented!");
#endif
                    break;
            }
        }

        private static bool AddTargets<T1, T2>(Character caller, T2 target) where T1 : AIObjectiveLoop<T2>
        {
            bool targetAdded = false;
            DoForEachCrewMember(caller, humanAI =>
            {
                var objective = humanAI.ObjectiveManager.GetObjective<T1>();
                if (objective != null)
                {
                    if (!targetAdded && objective.AddTarget(target))
                    {
                        targetAdded = true;
                    }
                }
            }, range: (caller.AIController as HumanAIController)?.ReportRange ?? float.PositiveInfinity);
            return targetAdded;
        }

        public static void RemoveTargets<T1, T2>(Character caller, T2 target) where T1 : AIObjectiveLoop<T2>
        {
            DoForEachCrewMember(caller, humanAI =>
                humanAI.ObjectiveManager.GetObjective<T1>()?.ReportedTargets.Remove(target));
        }

        public float GetDamageDoneByAttacker(Character otherCharacter)
        {
            float dmg = 0;
            Character.Attacker attacker = Character.LastAttackers.LastOrDefault(a => a.Character == otherCharacter);
            if (attacker != null)
            {
                dmg = attacker.Damage;
            }
            return dmg;
        }

        private void StoreHullSafety(Hull hull, HullSafety safety)
        {
            if (knownHulls.ContainsKey(hull))
            {
                // Update existing. Shouldn't currently happen, but things might change.
                knownHulls[hull] = safety;
            }
            else
            {
                // Add new
                knownHulls.Add(hull, safety);
            }
        }

        private float CalculateHullSafety(Hull hull, Character character, IEnumerable<Hull> visibleHulls = null)
        {
            bool isCurrentHull = character == Character && character.CurrentHull == hull;
            if (hull == null)
            {
                if (isCurrentHull)
                {
                    CurrentHullSafety = 0;
                }
                return CurrentHullSafety;
            }
            if (isCurrentHull && visibleHulls == null)
            {
                // Use the cached visible hulls
                visibleHulls = VisibleHulls;
            }
            bool ignoreFire = objectiveManager.CurrentOrder is AIObjectiveExtinguishFires extinguishOrder && extinguishOrder.Priority > 0 || objectiveManager.HasActiveObjective<AIObjectiveExtinguishFire>();
            bool ignoreWater = HasDivingSuit(character);
            bool ignoreOxygen = ignoreWater || HasDivingMask(character);
            bool ignoreEnemies = ObjectiveManager.IsCurrentOrder<AIObjectiveFightIntruders>() || ObjectiveManager.Objectives.Any(o => o is AIObjectiveFightIntruders);
            float safety = CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies);
            if (isCurrentHull)
            {
                CurrentHullSafety = safety;
            }
            return safety;
        }

        private static float CalculateHullSafety(Hull hull, IEnumerable<Hull> visibleHulls, Character character, bool ignoreWater = false, bool ignoreOxygen = false, bool ignoreFire = false, bool ignoreEnemies = false)
        {
            if (hull == null) { return 0; }
            if (hull.LethalPressure > 0 && character.PressureProtection <= 0) { return 0; }
            // Oxygen factor should be 1 with 70% oxygen or more and 0.1 when the oxygen level is 30% or lower.
            // With insufficient oxygen, the safety of the hull should be 39, all the other factors aside. So, just below the HULL_SAFETY_THRESHOLD.
            float oxygenFactor = ignoreOxygen ? 1 : MathHelper.Lerp((HULL_SAFETY_THRESHOLD - 1) / 100, 1, MathUtils.InverseLerp(HULL_LOW_OXYGEN_PERCENTAGE, 100 - HULL_LOW_OXYGEN_PERCENTAGE, hull.OxygenPercentage));
            float waterFactor = ignoreWater ? 1 : MathHelper.Lerp(1, HULL_SAFETY_THRESHOLD / 2 / 100, hull.WaterPercentage / 100);
            if (!character.NeedsAir)
            {
                oxygenFactor = 1;
                waterFactor = 1;
            }
            float fireFactor = 1;
            if (!ignoreFire)
            {
                float calculateFire(Hull h) => h.FireSources.Count * 0.5f + h.FireSources.Sum(fs => fs.DamageRange) / h.Size.X;
                // Even the smallest fire reduces the safety by 50%
                float fire = visibleHulls == null ? calculateFire(hull) : visibleHulls.Sum(h => calculateFire(h));
                fireFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(fire, 0, 1));
            }
            float enemyFactor = 1;
            if (!ignoreEnemies)
            {
                bool isValidTarget(Character e) => IsActive(e) && !IsFriendly(character, e);
                int enemyCount = visibleHulls == null ?
                    Character.CharacterList.Count(e => isValidTarget(e) && e.CurrentHull == hull) :
                    Character.CharacterList.Count(e => isValidTarget(e) && visibleHulls.Contains(e.CurrentHull));
                // The hull safety decreases 90% per enemy up to 100% (TODO: test smaller percentages)
                enemyFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(enemyCount * 0.9f, 0, 1));
            }
            float dangerousItemsFactor = 1f;
            foreach (Item item in Item.ItemList)
            {
                if (item.CurrentHull != hull) { continue; }
                if (item.Prefab != null && item.Prefab.IsDangerous)
                {
                    dangerousItemsFactor = 0;
                }
            }
            float safety = oxygenFactor * waterFactor * fireFactor * enemyFactor * dangerousItemsFactor;
            return MathHelper.Clamp(safety * 100, 0, 100);
        }

        public float GetHullSafety(Hull hull, Character character, IEnumerable<Hull> visibleHulls = null)
        {
            if (!knownHulls.TryGetValue(hull, out HullSafety hullSafety))
            {
                hullSafety = new HullSafety(CalculateHullSafety(hull, character, visibleHulls));
                StoreHullSafety(hull, hullSafety);
            }
            else if (hullSafety.IsStale)
            {
                hullSafety.Reset(CalculateHullSafety(hull, character, visibleHulls));
            }
            return hullSafety.safety;
        }

        public static float GetHullSafety(Hull hull, IEnumerable<Hull> visibleHulls, Character character, bool ignoreWater = false, bool ignoreOxygen = false, bool ignoreFire = false, bool ignoreEnemies = false)
        {
            HullSafety hullSafety;
            if (character.AIController is HumanAIController controller)
            {
                if (!controller.knownHulls.TryGetValue(hull, out hullSafety))
                {
                    hullSafety = new HullSafety(CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies));
                    controller.StoreHullSafety(hull, hullSafety);
                }
                else if (hullSafety.IsStale)
                {
                    hullSafety.Reset(CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies));
                }
            }
            else
            {
#if DEBUG
                DebugConsole.ThrowError("Cannot store the hull safety, because was unable to cast the AIController as HumanAIController. This should never happen!");
#endif
                return CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies);
            }
            return hullSafety.safety;
        }

        public static bool IsFriendly(Character me, Character other, bool onlySameTeam = false)
        {
            bool sameTeam = me.TeamID == other.TeamID;
            bool friendlyTeam = IsOnFriendlyTeam(me, other);
            bool teamGood = sameTeam || friendlyTeam && !onlySameTeam;
            if (!teamGood) { return false; }
            bool speciesGood = other.SpeciesName == me.SpeciesName || other.Params.CompareGroup(me.Params.Group);
            if (!speciesGood) { return false; }
            if (me.TeamID == CharacterTeamType.FriendlyNPC && other.TeamID == CharacterTeamType.Team1 && GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                var reputation = campaign.Map?.CurrentLocation?.Reputation;
                if (reputation != null && reputation.NormalizedValue < Reputation.HostileThreshold)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsOnFriendlyTeam(CharacterTeamType myTeam, CharacterTeamType otherTeam)
        {
            if (myTeam == otherTeam) { return true; }

            switch (myTeam)
            {
                case CharacterTeamType.None:
                case CharacterTeamType.Team1:
                case CharacterTeamType.Team2:
                    // Only friendly to the same team and friendly NPCs
                    return otherTeam == CharacterTeamType.FriendlyNPC;
                case CharacterTeamType.FriendlyNPC:
                    // Friendly NPCs are friendly to both teams
                    return otherTeam == CharacterTeamType.Team1 || otherTeam == CharacterTeamType.Team2;
                default:
                    return true;
            }
        }

        public static bool IsOnFriendlyTeam(Character me, Character other) => IsOnFriendlyTeam(me.TeamID, other.TeamID);

        public static bool IsActive(Character other) => other != null && !other.Removed && !other.IsDead && !other.IsUnconscious;

        public static bool IsTrueForAllCrewMembers(Character character, Func<HumanAIController, bool> predicate)
        {
            if (character == null) { return false; }
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c))
                {
                    if (!predicate(c.AIController as HumanAIController))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool IsTrueForAnyCrewMember(Character character, Func<HumanAIController, bool> predicate)
        {
            if (character == null) { return false; }
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c))
                {
                    if (predicate(c.AIController as HumanAIController))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static int CountCrew(Character character, Func<HumanAIController, bool> predicate = null, bool onlyActive = true, bool onlyBots = false)
        {
            if (character == null) { return 0; }
            int count = 0;
            foreach (var other in Character.CharacterList)
            {
                if (onlyActive && !IsActive(other))
                {
                    continue;
                }
                if (onlyBots && other.IsPlayer)
                {
                    continue;
                }
                if (FilterCrewMember(character, other))
                {
                    if (predicate == null || predicate(other.AIController as HumanAIController))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public static void DoForEachCrewMember(Character character, Action<HumanAIController> action, float range = float.PositiveInfinity)
        {
            if (character == null) { return; }
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c) && CheckReportRange(character, c, range))
                {
                    action(c.AIController as HumanAIController);
                }
            }
        }

        private static bool CheckReportRange(Character character, Character target, float range)
        {
            if (float.IsPositiveInfinity(range)) { return true; }
            if (character.CurrentHull == null || target.CurrentHull == null)
            {
                return Vector2.DistanceSquared(character.WorldPosition, target.WorldPosition) <= range * range;
            }
            else
            {
                return character.CurrentHull.GetApproximateDistance(character.Position, target.Position, target.CurrentHull, range, distanceMultiplierPerClosedDoor: 2) <= range;
            }
        }

        private static bool FilterCrewMember(Character self, Character other) => other != null && !other.IsDead && !other.Removed && other.AIController is HumanAIController humanAi && humanAi.IsFriendly(self);

        public static bool IsItemTargetedBySomeone(ItemComponent target, CharacterTeamType team, out Character operatingCharacter)
        {
            operatingCharacter = null;
            float highestPriority = -1.0f;
            float highestPriorityModifier = -1.0f;
            foreach (Character c in Character.CharacterList)
            {
                if (c.Removed) { continue; }
                if (c.TeamID != team) { continue; }
                if (c.IsIncapacitated) { continue; }
                if (c.SelectedConstruction == target.Item)
                {
                    operatingCharacter = c;
                    return true;
                }
                if (c.AIController is HumanAIController humanAI)
                {
                    foreach (var objective in humanAI.ObjectiveManager.Objectives)
                    {
                        if (!(objective is AIObjectiveOperateItem operateObjective)) { continue; }
                        if (operateObjective.Component.Item != target.Item) { continue; }
                        if (operateObjective.Priority < highestPriority) { continue; }
                        if (operateObjective.PriorityModifier < highestPriorityModifier) { continue; }
                        operatingCharacter = c;
                        highestPriority = operateObjective.Priority;
                        highestPriorityModifier = operateObjective.PriorityModifier;
                    }
                }
            }
            return operatingCharacter != null;
        }

        // There's some duplicate logic in the two methods below, but making them use the same code would require some changes in the target classes so that we could use exactly the same checks.
        // And even then there would be some differences that could end up being confusing (like the exception for steering).
        public bool IsItemOperatedByAnother(ItemComponent target, out Character other)
        {
            other = null;
            if (target?.Item == null) { return false; }
            bool isOrder = IsOrderedToOperateThis(Character.AIController);
            foreach (Character c in Character.CharacterList)
            {
                if (c == Character) { continue; }
                if (c.Removed) { continue; }
                if (c.TeamID != Character.TeamID) { continue; }
                if (c.IsIncapacitated) { continue; }
                if (c.IsPlayer)
                {
                    if (c.SelectedConstruction == target.Item)
                    {
                        // If the other character is player, don't try to operate
                        other = c;
                        break;
                    }
                }
                else if (c.AIController is HumanAIController operatingAI)
                {
                    if (operatingAI.ObjectiveManager.Objectives.None(o => o is AIObjectiveOperateItem operateObjective && operateObjective.Component.Item == target.Item))
                    {
                        // Not targeting the same item.
                        continue;
                    }
                    bool isTargetOrdered = IsOrderedToOperateThis(c.AIController);
                    if (!isOrder && isTargetOrdered)
                    {
                        // If the other bot is ordered to operate the item, let him do it, unless we are ordered too
                        other = c;
                        break;
                    }
                    else
                    {
                        if (isOrder && !isTargetOrdered)
                        {
                            // We are ordered and the target is not -> allow to operate
                            continue;
                        }
                        else
                        {
                            if (!isTargetOrdered && operatingAI.ObjectiveManager.CurrentOrder != operatingAI.ObjectiveManager.CurrentObjective)
                            {
                                // The other bot is ordered to do something else
                                continue;
                            }
                            if (target is Steering)
                            {
                                // Steering is hard-coded -> cannot use the required skills collection defined in the xml
                                if (Character.GetSkillLevel("helm") <= c.GetSkillLevel("helm"))
                                {
                                    other = c;
                                    break;
                                }
                            }
                            else if (target.DegreeOfSuccess(Character) <= target.DegreeOfSuccess(c))
                            {
                                other = c;
                                break;
                            }
                        }
                    }
                }
            }
            return other != null;
            bool IsOrderedToOperateThis(AIController ai) => ai is HumanAIController humanAI && humanAI.ObjectiveManager.CurrentOrder is AIObjectiveOperateItem operateOrder && operateOrder.Component.Item == target.Item;
        }

        public bool IsItemRepairedByAnother(Item target, out Character other)
        {
            other = null;
            if (Character == null) { return false; }
            if (target == null) { return false; }
            bool isOrder = IsOrderedToRepairThis(Character.AIController as HumanAIController);
            foreach (var c in Character.CharacterList)
            {
                if (c == Character) { continue; }
                if (c.TeamID != Character.TeamID) { continue; }
                if (c.IsIncapacitated) { continue; }
                other = c;
                if (c.IsPlayer)
                {
                    if (target.Repairables.Any(r => r.CurrentFixer == c))
                    {
                        // If the other character is player, don't try to repair
                        return true;
                    }
                }
                else if (c.AIController is HumanAIController operatingAI)
                {
                    var repairItemsObjective = operatingAI.ObjectiveManager.GetObjective<AIObjectiveRepairItems>();
                    if (repairItemsObjective == null) { continue; }
                    if (!(repairItemsObjective.SubObjectives.FirstOrDefault(o => o is AIObjectiveRepairItem) is AIObjectiveRepairItem activeObjective) || activeObjective.Item != target)
                    {
                        // Not targeting the same item.
                        continue;
                    }
                    bool isTargetOrdered = IsOrderedToRepairThis(operatingAI);
                    if (!isOrder && isTargetOrdered)
                    {
                        // If the other bot is ordered to repair the item, let him do it, unless we are ordered too
                        return true;
                    }
                    else
                    {
                        if (isOrder && !isTargetOrdered)
                        {
                            // We are ordered and the target is not -> allow to repair
                            continue;
                        }
                        else
                        {
                            if (!isTargetOrdered && operatingAI.ObjectiveManager.CurrentOrder != operatingAI.ObjectiveManager.CurrentObjective)
                            {
                                // The other bot is ordered to do something else
                                continue;
                            }
                            return target.Repairables.Max(r => r.DegreeOfSuccess(Character)) <= target.Repairables.Max(r => r.DegreeOfSuccess(c));
                        }
                    }
                }
            }
            return false;
            bool IsOrderedToRepairThis(HumanAIController ai) => ai.ObjectiveManager.CurrentOrder is AIObjectiveRepairItems repairOrder && repairOrder.PrioritizedItem == target;
        }

        #region Wrappers
        public bool IsFriendly(Character other) => IsFriendly(Character, other);
        public void DoForEachCrewMember(Action<HumanAIController> action) => DoForEachCrewMember(Character, action);
        public bool IsTrueForAnyCrewMember(Func<HumanAIController, bool> predicate) => IsTrueForAnyCrewMember(Character, predicate);
        public bool IsTrueForAllCrewMembers(Func<HumanAIController, bool> predicate) => IsTrueForAllCrewMembers(Character, predicate);
        public int CountCrew(Func<HumanAIController, bool> predicate = null, bool onlyActive = true, bool onlyBots = false) => CountCrew(Character, predicate, onlyActive, onlyBots);
        #endregion
    }
}
