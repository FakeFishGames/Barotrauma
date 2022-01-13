using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveGoTo : AIObjective
    {
        public override string Identifier { get; set; } = "go to";

        private AIObjectiveFindDivingGear findDivingGear;
        private readonly bool repeat;
        //how long until the path to the target is declared unreachable
        private float waitUntilPathUnreachable;
        private bool getDivingGearIfNeeded;

        /// <summary>
        /// Doesn't allow the objective to complete if this condition is false
        /// </summary>
        public Func<bool> requiredCondition;
        public Func<PathNode, bool> endNodeFilter;

        public Func<float> PriorityGetter;

        public bool IsFollowOrderObjective;
        public bool Mimic;

        public bool SpeakIfFails { get; set; } = true;
        public bool DebugLogWhenFails { get; set; } = true;
        public bool UsePathingOutside { get; set; } = true;

        public float ExtraDistanceWhileSwimming;
        public float ExtraDistanceOutsideSub;
        private float _closeEnoughMultiplier = 1;
        public float CloseEnoughMultiplier
        {
            get { return _closeEnoughMultiplier; }
            set { _closeEnoughMultiplier = Math.Max(value, 1); }
        }
        private float _closeEnough = 50;
        private readonly float minDistance = 50;
        private readonly float seekGapsInterval = 1;
        private float seekGapsTimer;
        private bool cannotFollow;

        /// <summary>
        /// Display units
        /// </summary>
        public float CloseEnough
        {
            get
            {
                float dist = _closeEnough * CloseEnoughMultiplier;
                float extraMultiplier = Math.Clamp(CloseEnoughMultiplier * 0.6f, 1, 3);
                if (character.AnimController.InWater)
                {
                    dist += ExtraDistanceWhileSwimming * extraMultiplier;
                }
                if (character.CurrentHull == null)
                {
                    dist += ExtraDistanceOutsideSub * extraMultiplier;
                }
                return dist;
            }
            set
            {
                _closeEnough = Math.Max(minDistance, value);
            }
        }

        // TODO: Currently we never check the visibility (to the end node), which is actually unintentional.
        // I don't think it has caused any issues so far, so let's keep defaulting to false for now, because the less we do raycasts the better.
        // However, if there are cases where the bots attempt to go through walls (select the end node that is behind an obstacle), we should set this true.
        public bool CheckVisibility { get; set; }
        public bool IgnoreIfTargetDead { get; set; }
        public bool AllowGoingOutside { get; set; }

        public bool AlwaysUseEuclideanDistance { get; set; } = true;

        /// <summary>
        /// If true, the distance to the destination is calculated from the character's AimSourcePos (= shoulder) instead of the collider's position
        /// </summary>
        public bool UseDistanceRelativeToAimSourcePos { get; set; } = false;

        public override bool AbandonWhenCannotCompleteSubjectives => !repeat;

        public override bool AllowOutsideSubmarine => AllowGoingOutside;
        public override bool AllowInAnySub => true;

        public string DialogueIdentifier { get; set; } = "dialogcannotreachtarget";
        public string TargetName { get; set; }

        public ISpatialEntity Target { get; private set; }

        public float? OverridePriority = null;

        public Func<bool> SpeakCannotReachCondition { get; set; }

        protected override float GetPriority()
        {
            bool isOrder = objectiveManager.IsOrder(this);
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = !isOrder;
                return Priority;
            }
            if (Target == null || Target is Entity e && e.Removed)
            {
                Priority = 0;
                Abandon = !isOrder;
            }
            if (IgnoreIfTargetDead && Target is Character character && character.IsDead)
            {
                Priority = 0;
                Abandon = !isOrder;
            }
            else
            {
                if (PriorityGetter != null)
                {
                    Priority = PriorityGetter();
                }
                else if (OverridePriority.HasValue)
                {
                    Priority = OverridePriority.Value;
                }
                else
                {
                    Priority = isOrder ? objectiveManager.GetOrderPriority(this) : 10;
                }
            }
            return Priority;
        }

        private readonly float avoidLookAheadDistance = 5;
        private readonly float pathWaitingTime = 3;

        public AIObjectiveGoTo(ISpatialEntity target, Character character, AIObjectiveManager objectiveManager, bool repeat = false, bool getDivingGearIfNeeded = true, float priorityModifier = 1, float closeEnough = 0)
            : base(character, objectiveManager, priorityModifier)
        {
            Target = target;
            this.repeat = repeat;
            waitUntilPathUnreachable = pathWaitingTime;
            this.getDivingGearIfNeeded = getDivingGearIfNeeded;
            if (Target is Item i)
            {
                CloseEnough = Math.Max(CloseEnough, i.InteractDistance + Math.Max(i.Rect.Width, i.Rect.Height) / 2);
            }
            else if (Target is Character)
            {
                //if closeEnough value is given, allow setting CloseEnough as low as 50, otherwise above AIObjectiveGetItem.DefaultReach
                CloseEnough = Math.Max(closeEnough, MathUtils.NearlyEqual(closeEnough, 0.0f) ? AIObjectiveGetItem.DefaultReach : minDistance);
            }
            else
            {
                CloseEnough = closeEnough;
            }
        }

        private void SpeakCannotReach()
        {
#if DEBUG
            if (DebugLogWhenFails)
            {
                DebugConsole.NewMessage($"{character.Name}: Cannot reach the target: {Target}", Color.Yellow);
            }
#endif
            if (!character.IsOnPlayerTeam) { return; }
            if (objectiveManager.CurrentOrder != objectiveManager.CurrentObjective) { return; }
            if (DialogueIdentifier == null) { return; }
            if (!SpeakIfFails) { return; }
            if (SpeakCannotReachCondition != null && !SpeakCannotReachCondition()) { return; }
            string msg = TargetName == null ? TextManager.Get(DialogueIdentifier, true) : TextManager.GetWithVariable(DialogueIdentifier, "[name]", TargetName, formatCapitals: !(Target is Character));
            if (msg == null) { return; }
            character.Speak(msg, identifier: DialogueIdentifier, minDurationBetweenSimilar: 20.0f);
        }

        public void ForceAct(float deltaTime) => Act(deltaTime);

        protected override void Act(float deltaTime)
        {
            if (Target == null)
            {
                Abandon = true;
                return;
            }
            if (Target == character || character.SelectedBy != null && HumanAIController.IsFriendly(character.SelectedBy))
            {
                // Wait
                character.AIController.SteeringManager.Reset();
                return;
            }
            if (!character.IsClimbing)
            {
                character.SelectedConstruction = null;
            }
            if (Target is Entity e)
            {
                if (e.Removed)
                {
                    Abandon = true;
                    return;
                }
                else
                {
                    character.AIController.SelectTarget(e.AiTarget);
                }
            }
            Hull targetHull = GetTargetHull();
            if (!IsFollowOrderObjective)
            {
                // Abandon if going through unsafe paths. Note ignores unsafe nodes when following an order or when the objective is set to ignore unsafe hulls.
                bool containsUnsafeNodes = character.IsDismissed && !HumanAIController.ObjectiveManager.CurrentObjective.IgnoreUnsafeHulls
                    && PathSteering != null && PathSteering.CurrentPath != null
                    && PathSteering.CurrentPath.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull));
                if (containsUnsafeNodes || HumanAIController.UnreachableHulls.Contains(targetHull))
                {
                    Abandon = true;
                }
            }
            bool insideSteering = SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.IsPathDirty;
            bool isInside = character.CurrentHull != null;
            bool hasOutdoorNodes = insideSteering && PathSteering.CurrentPath.HasOutdoorsNodes;
            if (isInside && hasOutdoorNodes && !AllowGoingOutside)
            {
                Abandon = true;
            }
            else if (HumanAIController.SteeringManager == PathSteering)
            {
                waitUntilPathUnreachable -= deltaTime;
                if (HumanAIController.IsCurrentPathNullOrUnreachable)
                {
                    SteeringManager.Reset();
                    if (waitUntilPathUnreachable < 0)
                    {
                        waitUntilPathUnreachable = pathWaitingTime;
                        if (repeat)
                        {
                            SpeakCannotReach();
                        }
                        else
                        {
                            Abandon = true;
                        }
                    }
                }
                else if (HumanAIController.HasValidPath(requireNonDirty: true, requireUnfinished: false))
                {
                    waitUntilPathUnreachable = pathWaitingTime;
                }
            }
            if (!Abandon)
            {
                if (getDivingGearIfNeeded && !character.LockHands)
                {
                    Character followTarget = Target as Character;
                    bool needsDivingSuit = (!isInside || hasOutdoorNodes) && character.NeedsAir && !character.HasAbilityFlag(AbilityFlags.ImmuneToPressure);
                    bool needsDivingGear = (needsDivingSuit || HumanAIController.NeedsDivingGear(targetHull, out needsDivingSuit)) && character.NeedsAir;
                    if (Mimic)
                    {
                        if (HumanAIController.HasDivingSuit(followTarget) && character.NeedsAir)
                        {
                            needsDivingGear = true;
                            needsDivingSuit = true;
                        }
                        else if (HumanAIController.HasDivingMask(followTarget) && character.NeedsAir)
                        {
                            needsDivingGear = true;
                        }
                    }
                    bool needsEquipment = false;
                    float minOxygen = AIObjectiveFindDivingGear.GetMinOxygen(character);
                    if (needsDivingSuit)
                    {
                        needsEquipment = !HumanAIController.HasDivingSuit(character, minOxygen);
                    }
                    else if (needsDivingGear)
                    {
                        needsEquipment = !HumanAIController.HasDivingGear(character, minOxygen);
                    }
                    if (needsEquipment)
                    {
                        SteeringManager.Reset();
                        if (findDivingGear != null && !findDivingGear.CanBeCompleted)
                        {
                            TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit: false, objectiveManager),
                                onAbandon: () => Abandon = true,
                                onCompleted: () =>
                                {
                                    cannotFollow = false;
                                    RemoveSubObjective(ref findDivingGear);
                                });
                        }
                        else
                        {
                            TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager),
                                onAbandon: () => Abandon = true,
                                onCompleted: () =>
                                {
                                    cannotFollow = false;
                                    RemoveSubObjective(ref findDivingGear);
                                });
                        }
                        return;
                    }
                    else
                    {
                        cannotFollow = false;
                    }
                }
                if (repeat)
                {
                    if (IsCloseEnough)
                    {
                        if (requiredCondition == null || requiredCondition())
                        {
                            if (character.CanSeeTarget(Target))
                            {
                                OnCompleted();
                                return;
                            }
                        }
                    }
                }
                float maxGapDistance = 500;
                Character targetCharacter = Target as Character;
                if (character.AnimController.InWater)
                {
                    if (character.CurrentHull == null ||
                        IsFollowOrderObjective && 
                        targetCharacter != null && (targetCharacter.CurrentHull == null) != (character.CurrentHull == null) &&
                        Vector2.DistanceSquared(character.WorldPosition, Target.WorldPosition) < maxGapDistance * maxGapDistance)
                    {
                        if (seekGapsTimer > 0)
                        {
                            seekGapsTimer -= deltaTime;
                        }
                        else
                        {
                            bool isRuins = character.Submarine?.Info.IsRuin != null || Target.Submarine?.Info.IsRuin != null;
                            if (!isRuins || !HumanAIController.HasValidPath(requireNonDirty: true, requireUnfinished: true))
                            {
                                SeekGaps(maxGapDistance);
                                seekGapsTimer = seekGapsInterval * Rand.Range(0.1f, 1.1f);
                                if (TargetGap != null)
                                {
                                    // Check that nothing is blocking the way
                                    Vector2 rayStart = character.SimPosition;
                                    Vector2 rayEnd = TargetGap.SimPosition;
                                    if (TargetGap.Submarine != null && character.Submarine == null)
                                    {
                                        rayStart -= TargetGap.Submarine.SimPosition;
                                    }
                                    else if (TargetGap.Submarine == null && character.Submarine != null)
                                    {
                                        rayEnd -= character.Submarine.SimPosition;
                                    }
                                    var closestBody = Submarine.CheckVisibility(rayStart, rayEnd, ignoreSubs: true);
                                    if (closestBody != null)
                                    {
                                        TargetGap = null;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        TargetGap = null;
                    }
                    if (TargetGap != null)
                    {
                        if (TargetGap.FlowTargetHull != null && HumanAIController.SteerThroughGap(TargetGap, IsFollowOrderObjective ? Target.WorldPosition : TargetGap.FlowTargetHull.WorldPosition, deltaTime))
                        {
                            SteeringManager.SteeringAvoid(deltaTime, avoidLookAheadDistance, weight: 1);
                            return;
                        }
                        else
                        {
                            TargetGap = null;
                        }
                    }
                    if (checkScooterTimer <= 0)
                    {
                        useScooter = false;
                        checkScooterTimer = checkScooterTime * Rand.Range(0.75f, 1.25f);
                        string scooterTag = "scooter";
                        string batteryTag = "mobilebattery";
                        Item scooter = null;
                        float closeEnough = 250;
                        float squaredDistance = Vector2.DistanceSquared(character.WorldPosition, Target.WorldPosition);
                        bool shouldUseScooter = squaredDistance > closeEnough * closeEnough && (!Mimic ||
                            (targetCharacter != null && targetCharacter.HasEquippedItem(scooterTag, allowBroken: false)) || squaredDistance > Math.Pow(closeEnough * 2, 2));
                        if (HumanAIController.HasItem(character, scooterTag, out IEnumerable<Item> equippedScooters, recursive: false, requireEquipped: true))
                        {
                            // Currently equipped scooter
                            scooter = equippedScooters.FirstOrDefault();
                        }
                        else if (shouldUseScooter)
                        {
                            var leftHandItem = character.GetEquippedItem(slotType: InvSlotType.LeftHand);
                            var rightHandItem = character.GetEquippedItem(slotType: InvSlotType.RightHand);
                            bool handsFull =
                                (leftHandItem != null && character.Inventory.CheckIfAnySlotAvailable(leftHandItem, inWrongSlot: false) == -1) ||
                                (rightHandItem != null && character.Inventory.CheckIfAnySlotAvailable(rightHandItem, inWrongSlot: false) == -1);
                            if (!handsFull)
                            {
                                bool hasBattery = false;
                                if (HumanAIController.HasItem(character, scooterTag, out IEnumerable<Item> nonEquippedScooters, containedTag: batteryTag, conditionPercentage: 1, requireEquipped: false))
                                {
                                    // Non-equipped scooter with a battery
                                    scooter = nonEquippedScooters.FirstOrDefault();
                                    hasBattery = true;
                                }
                                else if (HumanAIController.HasItem(character, scooterTag, out IEnumerable<Item> _nonEquippedScooters, requireEquipped: false))
                                {
                                    // Non-equipped scooter without a battery
                                    scooter = _nonEquippedScooters.FirstOrDefault();
                                    // Non-recursive so that the bots won't take batteries from other items. Also means that they can't find batteries inside containers. Not sure how to solve this.
                                    hasBattery = HumanAIController.HasItem(character, batteryTag, out _, requireEquipped: false, conditionPercentage: 1, recursive: false);
                                }
                                if (scooter != null && hasBattery)
                                {
                                    // Equip only if we have a battery available
                                    HumanAIController.TakeItem(scooter, character.Inventory, equip: true, dropOtherIfCannotMove: false, allowSwapping: true, storeUnequipped: false);
                                }
                            }
                        }
                        bool isScooterEquipped = scooter != null && character.HasEquippedItem(scooter);
                        if (scooter != null && isScooterEquipped)
                        {
                            if (shouldUseScooter)
                            {
                                useScooter = true;
                                // Check the battery
                                if (scooter.ContainedItems.None(i => i.Condition > 0))
                                {
                                    // Try to switch batteries
                                    if (HumanAIController.HasItem(character, batteryTag, out IEnumerable<Item> batteries, conditionPercentage: 1, recursive: false))
                                    {
                                        scooter.ContainedItems.ForEachMod(emptyBattery => character.Inventory.TryPutItem(emptyBattery, character, CharacterInventory.anySlot));
                                        if (!scooter.Combine(batteries.OrderByDescending(b => b.Condition).First(), character))
                                        {
                                            useScooter = false;
                                        }
                                    }
                                    else
                                    {
                                        useScooter = false;
                                    }
                                }
                            }
                            if (!useScooter)
                            {
                                // Unequip
                                character.Inventory.TryPutItem(scooter, character, CharacterInventory.anySlot);
                            }
                        }
                    }
                    else
                    {
                        checkScooterTimer -= deltaTime;
                    }
                }
                else
                {
                    TargetGap = null;
                    useScooter = false;
                    checkScooterTimer = 0;
                }
                if (SteeringManager == PathSteering)
                {
                    Vector2 targetPos = character.GetRelativeSimPosition(Target);
                    Func<PathNode, bool> nodeFilter = null;
                    if (isInside && !AllowGoingOutside)
                    {
                        nodeFilter = n => n.Waypoint.CurrentHull != null;
                    }
                    else if (!isInside && HumanAIController.UseIndoorSteeringOutside)
                    {
                        nodeFilter = n => n.Waypoint.Submarine == null;
                    }

                    if (!isInside && !UsePathingOutside)
                    {
                        PathSteering.SteeringSeekSimple(character.GetRelativeSimPosition(Target), 10);
                        if (character.AnimController.InWater)
                        {
                            SteeringManager.SteeringAvoid(deltaTime, avoidLookAheadDistance, weight: 15);
                        }
                    }
                    else
                    {
                        PathSteering.SteeringSeek(targetPos, weight: 1,
                            startNodeFilter: n => (n.Waypoint.CurrentHull == null) == (character.CurrentHull == null),
                            endNodeFilter: endNodeFilter,
                            nodeFilter: nodeFilter,
                            checkVisiblity: CheckVisibility);
                    }
                    if (!isInside && (PathSteering.CurrentPath == null || PathSteering.IsPathDirty || PathSteering.CurrentPath.Unreachable))
                    {
                        if (useScooter)
                        {
                            UseScooter(Target.WorldPosition);
                        }
                        else
                        {
                            SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(Target.WorldPosition - character.WorldPosition));
                            if (character.AnimController.InWater)
                            {
                                SteeringManager.SteeringAvoid(deltaTime, avoidLookAheadDistance, weight: 2);
                            }
                        }
                    }
                    else if (useScooter && PathSteering.CurrentPath?.CurrentNode != null)
                    {
                        UseScooter(PathSteering.CurrentPath.CurrentNode.WorldPosition);
                    }
                }
                else
                {
                    if (useScooter)
                    {
                        UseScooter(Target.WorldPosition);
                    }
                    else
                    {
                        SteeringManager.SteeringSeek(character.GetRelativeSimPosition(Target), 10);
                        if (character.AnimController.InWater)
                        {
                            SteeringManager.SteeringAvoid(deltaTime, avoidLookAheadDistance, weight: 15);
                        }
                    }
                }
            }

            void UseScooter(Vector2 targetWorldPos)
            {
                SteeringManager.Reset();
                character.CursorPosition = targetWorldPos;
                if (character.Submarine != null)
                {
                    character.CursorPosition -= character.Submarine.Position;
                }
                Vector2 diff = character.CursorPosition - character.Position;
                Vector2 dir = Vector2.Normalize(diff);
                float sqrDist = diff.LengthSquared();
                if (sqrDist > MathUtils.Pow2(CloseEnough * 1.5f))
                {
                    SteeringManager.SteeringManual(1.0f, dir);
                }
                else
                {
                    float dot = Vector2.Dot(dir, VectorExtensions.Forward(character.AnimController.Collider.Rotation + MathHelper.PiOver2));
                    bool isFacing = dot > 0.9f;
                    if (!isFacing && sqrDist > MathUtils.Pow2(CloseEnough))
                    {
                        SteeringManager.SteeringManual(1.0f, dir);
                    }
                }
                character.SetInput(InputType.Aim, false, true);
                character.SetInput(InputType.Shoot, false, true);
            }
        }

        private bool useScooter;
        private float checkScooterTimer;
        private readonly float checkScooterTime = 0.5f;

        public Hull GetTargetHull() => GetTargetHull(Target);

        public static Hull GetTargetHull(ISpatialEntity target)
        {
            if (target is Hull h)
            {
                return h;
            }
            else if (target is Item i)
            {
                return i.CurrentHull;
            }
            else if (target is Character c)
            {
                return c.CurrentHull;
            }
            else if (target is Gap g)
            {
                return g.FlowTargetHull;
            }
            else if (target is WayPoint wp)
            {
                return wp.CurrentHull;
            }
            else if (target is FireSource fs)
            {
                return fs.Hull;
            }
            else if (target is OrderTarget ot)
            {
                return ot.Hull;
            }
            return null;
        }

        public Gap TargetGap { get; private set; }
        private void SeekGaps(float maxDistance)
        {
            Gap selectedGap = null;
            float selectedDistance = -1;
            Vector2 toTargetNormalized = Vector2.Normalize(Target.WorldPosition - character.WorldPosition);
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.Open < 1) { continue; }
                if (gap.Submarine == null) { continue; }
                if (!IsFollowOrderObjective)
                {
                    if (gap.FlowTargetHull == null) { continue; }
                    if (gap.Submarine != Target.Submarine) { continue; }
                }
                Vector2 toGap = gap.WorldPosition - character.WorldPosition;
                if (Vector2.Dot(Vector2.Normalize(toGap), toTargetNormalized) < 0) { continue; }
                float squaredDistance = toGap.LengthSquared();
                if (squaredDistance > maxDistance * maxDistance) { continue; }
                if (selectedGap == null || squaredDistance < selectedDistance)
                {
                    selectedGap = gap;
                    selectedDistance = squaredDistance;
                }
            }
            TargetGap = selectedGap;
        }

        public bool IsCloseEnough
        {
            get
            {
                if (SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.CurrentPath.Finished && PathSteering.IsCurrentNodeLadder)
                {
                    // Climbing a ladder
                    if (Target.WorldPosition.Y > character.WorldPosition.Y)
                    {
                        // The target is still above us
                        return false;
                    }
                    if (!character.AnimController.IsAboveFloor)
                    {
                        // Going through a hatch
                        return false;
                    }
                }
                if (!AlwaysUseEuclideanDistance && !character.AnimController.InWater)
                {
                    float yDist = Math.Abs(Target.WorldPosition.Y - character.WorldPosition.Y);
                    if (yDist > CloseEnough) { return false; }
                    float xDist = Math.Abs(Target.WorldPosition.X - character.WorldPosition.X);
                    return xDist <= CloseEnough;
                }
                Vector2 sourcePos = UseDistanceRelativeToAimSourcePos ? character.AnimController.AimSourceWorldPos : character.WorldPosition;
                return Vector2.DistanceSquared(Target.WorldPosition, sourcePos) < CloseEnough * CloseEnough;
            }
        }

        protected override bool CheckObjectiveSpecific()
        {
            if (IsCompleted) { return true; }
            // First check the distance and then if can interact (heaviest)
            if (Target == null)
            {
                Abandon = true;
                return false;
            }
            if (repeat)
            {
                return false;
            }
            else
            {
                if (IsCloseEnough)
                {
                    if (requiredCondition == null || requiredCondition())
                    {
                        if (Target is Item item)
                        {
                            if (!character.IsClimbing && character.CanInteractWith(item, out _, checkLinked: false)) { IsCompleted = true; }
                        }
                        else if (Target is Character targetCharacter)
                        {
                            character.SelectCharacter(targetCharacter);
                            if (character.CanInteractWith(targetCharacter, skipDistanceCheck: true)) { IsCompleted = true; }
                            character.DeselectCharacter();
                        }
                        else
                        {
                            IsCompleted = true;
                        }
                    }
                }
            }
            return IsCompleted;
        }

        protected override void OnAbandon()
        {
            StopMovement();
            if (SteeringManager == PathSteering)
            {
                PathSteering.ResetPath();
            }
            SpeakCannotReach();
            base.OnAbandon();
        }

        private void StopMovement()
        {
            SteeringManager.Reset();
            if (Target != null)
            {
                character.AnimController.TargetDir = Target.WorldPosition.X > character.WorldPosition.X ? Direction.Right : Direction.Left;
            }
        }

        protected override void OnCompleted()
        {
            StopMovement();
            HumanAIController.FaceTarget(Target);
            base.OnCompleted();
        }

        public override void Reset()
        {
            base.Reset();
            findDivingGear = null;
            seekGapsTimer = 0;
            TargetGap = null;
            cannotFollow = false;
        }
    }
}
