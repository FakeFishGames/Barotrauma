using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveGoTo : AIObjective
    {
        public override string DebugTag => "go to";

        private AIObjectiveFindDivingGear findDivingGear;
        private readonly bool repeat;
        //how long until the path to the target is declared unreachable
        private float waitUntilPathUnreachable;
        private bool getDivingGearIfNeeded;

        /// <summary>
        /// Doesn't allow the objective to complete if this condition is false
        /// </summary>
        public Func<bool> requiredCondition;
        /// <summary>
        /// Aborts the objective when this condition is true
        /// </summary>
        public Func<bool> abortCondition;
        public Func<PathNode, bool> endNodeFilter;

        public Func<float> priorityGetter;

        public bool followControlledCharacter;
        public bool mimic;

        private float _closeEnough = 50;
        private readonly float minDistance = 50;
        /// <summary>
        /// Display units
        /// </summary>
        public float CloseEnough
        {
            get { return _closeEnough; }
            set
            {
                _closeEnough = Math.Max(minDistance, value);
            }
        }

        public bool CheckVisibility { get; set; }
        public bool IgnoreIfTargetDead { get; set; }
        public bool AllowGoingOutside { get; set; }

        public override bool AbandonWhenCannotCompleteSubjectives => !repeat;

        public override bool AllowOutsideSubmarine => AllowGoingOutside;
        public override bool AllowInAnySub => true;

        public string DialogueIdentifier { get; set; }
        public string TargetName { get; set; }

        public ISpatialEntity Target { get; private set; }

        public float? OverridePriority = null;

        public override float GetPriority()
        {
            bool isOrder = objectiveManager.CurrentOrder == this;
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = !isOrder;
                return Priority;
            }
            if (followControlledCharacter && Character.Controlled == null)
            {
                Priority = 0;
                Abandon = !isOrder;
            }
            if (Target is Entity e && e.Removed)
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
                if (priorityGetter != null)
                {
                    Priority = priorityGetter();
                }
                else if (OverridePriority.HasValue)
                {
                    Priority = OverridePriority.Value;
                }
                else
                {
                    Priority = isOrder ? AIObjectiveManager.OrderPriority : 10;
                }
            }
            return Priority;
        }

        public AIObjectiveGoTo(ISpatialEntity target, Character character, AIObjectiveManager objectiveManager, bool repeat = false, bool getDivingGearIfNeeded = true, float priorityModifier = 1, float closeEnough = 0)
            : base(character, objectiveManager, priorityModifier)
        {
            Target = target;
            this.repeat = repeat;
            waitUntilPathUnreachable = 3.0f;
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
            DebugConsole.NewMessage($"{character.Name}: Cannot reach the target: {Target}", Color.Yellow);
#endif
            if (objectiveManager.CurrentOrder != null && DialogueIdentifier != null)
            {
                string msg = TargetName == null ? TextManager.Get(DialogueIdentifier, true) : TextManager.GetWithVariable(DialogueIdentifier, "[name]", TargetName, formatCapitals: !(Target is Character));
                if (msg != null)
                {
                    character.Speak(msg, identifier: DialogueIdentifier, minDurationBetweenSimilar: 20.0f);
                }
            }
        }

        protected override void Act(float deltaTime)
        {
            if (followControlledCharacter)
            {
                if (Character.Controlled == null)
                {
                    Abandon = true;
                    SteeringManager.Reset();
                    return;
                }
                Target = Character.Controlled;
            }
            if (Target == character || character.SelectedBy != null && HumanAIController.IsFriendly(character.SelectedBy))
            {
                // Wait
                character.AIController.SteeringManager.Reset();
                return;
            }
            waitUntilPathUnreachable -= deltaTime;
            if (!character.IsClimbing)
            {
                character.SelectedConstruction = null;
            }
            if (Target is Entity e)
            {
                if (e.Removed)
                {
                    Abandon = true;
                    SteeringManager.Reset();
                    return;
                }
                else
                {
                    character.AIController.SelectTarget(e.AiTarget);
                }
            }
            Hull targetHull = GetTargetHull();
            if (!followControlledCharacter)
            {
                // Abandon if going through unsafe paths. Note ignores unsafe nodes when following an order or when the objective is set to ignore unsafe hulls.
                bool containsUnsafeNodes = HumanAIController.CurrentOrder == null && !HumanAIController.ObjectiveManager.CurrentObjective.IgnoreUnsafeHulls
                    && PathSteering != null && PathSteering.CurrentPath != null
                    && PathSteering.CurrentPath.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull));
                if (containsUnsafeNodes || HumanAIController.UnreachableHulls.Contains(targetHull))
                {
                    Abandon = true;
                }
            }
            bool insideSteering = SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.IsPathDirty;
            bool isInside = character.CurrentHull != null;
            bool targetIsOutside = (Target != null && targetHull == null) || (insideSteering && PathSteering.CurrentPath.HasOutdoorsNodes);
            if (isInside && targetIsOutside && !AllowGoingOutside)
            {
                Abandon = true;
            }
            else if (waitUntilPathUnreachable < 0)
            {
                if (SteeringManager == PathSteering && PathSteering.CurrentPath != null && PathSteering.CurrentPath.Unreachable && !PathSteering.IsPathDirty)
                {
                    if (repeat)
                    {
                        SpeakCannotReach();
                        SteeringManager.Reset();
                    }
                    else
                    {
                        Abandon = true;
                    }
                }
            }
            if (Abandon)
            {
                SpeakCannotReach();
                SteeringManager.Reset();
            }
            else
            {
                if (getDivingGearIfNeeded && !character.LockHands)
                {
                    Character followTarget = Target as Character;
                    bool needsDivingSuit = targetIsOutside;
                    bool needsDivingGear = needsDivingSuit || HumanAIController.NeedsDivingGear(targetHull, out needsDivingSuit);
                    if (!needsDivingGear && mimic)
                    {
                        if (HumanAIController.HasDivingSuit(followTarget))
                        {
                            needsDivingGear = true;
                            needsDivingSuit = true;
                        }
                        else if (HumanAIController.HasDivingMask(followTarget))
                        {
                            needsDivingGear = true;
                        }
                    }
                    bool needsEquipment = false;
                    if (needsDivingSuit)
                    {
                        needsEquipment = !HumanAIController.HasDivingSuit(character, AIObjectiveFindDivingGear.MIN_OXYGEN);
                    }
                    else if (needsDivingGear)
                    {
                        needsEquipment = !HumanAIController.HasDivingGear(character, AIObjectiveFindDivingGear.MIN_OXYGEN);
                    }
                    if (needsEquipment)
                    {
                        if (findDivingGear != null && !findDivingGear.CanBeCompleted)
                        {
                            TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit: false, objectiveManager),
                                onAbandon: () => Abandon = true,
                                onCompleted: () => RemoveSubObjective(ref findDivingGear));
                        }
                        else
                        {
                            TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager),
                                onCompleted: () => RemoveSubObjective(ref findDivingGear));
                        }
                        return;
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
                if (!character.AnimController.InWater)
                {
                    useScooter = false;
                    checkScooterTimer = 0;
                }
                else if (checkScooterTimer <= 0)
                {
                    useScooter = false;
                    checkScooterTimer = checkScooterTime;
                    string scooterTag = "scooter";
                    string batteryTag = "mobilebattery";
                    Item scooter = null;
                    bool isScooterEquipped = false;
                    float closeEnough = 250;
                    float squaredDistance = Vector2.DistanceSquared(character.WorldPosition, Target.WorldPosition);
                    bool shouldUseScooter = squaredDistance > closeEnough * closeEnough && (!mimic ||
                        (Target is Character targetCharacter && targetCharacter.HasEquippedItem(scooterTag, allowBroken: false)) || squaredDistance > Math.Pow(closeEnough * 2, 2));
                    if (HumanAIController.HasItem(character, scooterTag, out IEnumerable<Item> equippedScooters, batteryTag, requireEquipped: true))
                    {
                        scooter = equippedScooters.FirstOrDefault();
                        isScooterEquipped = scooter != null;
                    }
                    else if (shouldUseScooter && HumanAIController.HasItem(character, scooterTag, out IEnumerable<Item> scooters, batteryTag, requireEquipped: false))
                    {
                        scooter = scooters.FirstOrDefault();
                        if (scooter != null)
                        {
                            isScooterEquipped = HumanAIController.TakeItem(scooter, character.Inventory, equip: true, dropOtherIfCannotMove: false, allowSwapping: true, storeUnequipped: false);
                        }
                    }
                    if (scooter != null && isScooterEquipped)
                    {
                        if (shouldUseScooter)
                        {
                            useScooter = true;
                        }
                        else
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
                if (SteeringManager == PathSteering)
                {
                    Func<PathNode, bool> nodeFilter = null;
                    if (isInside && !AllowGoingOutside)
                    {
                        nodeFilter = node => node.Waypoint.CurrentHull != null;
                    }
                    PathSteering.SteeringSeek(character.GetRelativeSimPosition(Target), 1, n =>
                    {
                        if (n.Waypoint.isObstructed) { return false; }
                        return (n.Waypoint.CurrentHull == null) == (character.CurrentHull == null);
                    }, endNodeFilter, nodeFilter, CheckVisibility);
                    if (!isInside && PathSteering.CurrentPath == null || PathSteering.IsPathDirty || PathSteering.CurrentPath.Unreachable)
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
                                SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: 5, weight: 2);
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
                            SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: 5, weight: 15);
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
                Vector2 dir = Vector2.Normalize(character.CursorPosition - character.Position);
                if (!MathUtils.IsValid(dir)) { dir = Vector2.UnitY; }
                SteeringManager.SteeringManual(1.0f, dir);
                character.SetInput(InputType.Aim, false, true);
                character.SetInput(InputType.Shoot, false, true);
            }
        }

        private bool useScooter;
        private float checkScooterTimer;
        private readonly float checkScooterTime = 0.2f;

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

        public bool IsCloseEnough
        {
            get
            {
                if (SteeringManager == PathSteering && PathSteering.CurrentPath?.CurrentNode?.Ladders != null)
                {
                    //don't consider the character to be close enough to the target while climbing ladders,
                    //UNLESS the last node in the path has been reached
                    //otherwise characters can let go of the ladders too soon once they're close enough to the target
                    if (PathSteering.CurrentPath.NextNode != null) { return false; }
                }
                return Vector2.DistanceSquared(Target.WorldPosition, character.WorldPosition) < CloseEnough * CloseEnough;
            }
        }

        protected override bool Check()
        {
            if (IsCompleted) { return true; }
            // First check the distance
            // Then the custom condition
            // And finally check if can interact (heaviest)
            if (Target == null)
            {
                Abandon = true;
                return false;
            }
            if (abortCondition != null && abortCondition())
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
            base.OnAbandon();
        }

        private void StopMovement()
        {
            character.AIController.SteeringManager.Reset();
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
        }
    }
}
