using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

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

        public Func<bool> requiredCondition;
        public Func<PathNode, bool> startNodeFilter;
        public Func<PathNode, bool> endNodeFilter;

        public bool followControlledCharacter;
        public bool mimic;

        private float _closeEnough = 50;
        /// <summary>
        /// Display units
        /// </summary>
        public float CloseEnough
        {
            get { return _closeEnough; }
            set
            {
                _closeEnough = Math.Max(_closeEnough, value);
            }
        }
        public bool IgnoreIfTargetDead { get; set; }
        public bool AllowGoingOutside { get; set; }

        public ISpatialEntity Target { get; private set; }

        public override float GetPriority()
        {
            if (followControlledCharacter && Character.Controlled == null) { return 0.0f; }
            if (Target is Entity e && e.Removed) { return 0.0f; }
            if (IgnoreIfTargetDead && Target is Character character && character.IsDead) { return 0.0f; }                     
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return 1.0f;
        }

        public AIObjectiveGoTo(ISpatialEntity target, Character character, AIObjectiveManager objectiveManager, bool repeat = false, bool getDivingGearIfNeeded = true, float priorityModifier = 1, float closeEnough = 0) 
            : base (character, objectiveManager, priorityModifier)
        {
            this.Target = target;
            this.repeat = repeat;
            waitUntilPathUnreachable = 3.0f;
            this.getDivingGearIfNeeded = getDivingGearIfNeeded;
            CloseEnough = closeEnough;
            if (Target is Item i)
            {
                CloseEnough = Math.Max(CloseEnough, i.InteractDistance + Math.Max(i.Rect.Width, i.Rect.Height) / 2);
            }
        }

        protected override void Act(float deltaTime)
        {
            if (followControlledCharacter)
            {
                if (Character.Controlled == null)
                {
                    abandon = true;
                    return;
                }
                Target = Character.Controlled;
            }
            if (Target == character)
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
                    abandon = true;
                }
                else
                {
                    character.AIController.SelectTarget(e.AiTarget);
                }
            }
            var targetHull = Target is Hull h ? h : Target is Item i ? i.CurrentHull : Target is Character c ? c.CurrentHull : character.CurrentHull;
            // Abandon if going through unsafe paths. Note ignores unsafe nodes when following an order or when the objective is set to ignore unsafe hulls.
            bool containsUnsafeNodes = HumanAIController.CurrentOrder == null && !HumanAIController.ObjectiveManager.CurrentObjective.IgnoreUnsafeHulls
                && PathSteering != null && PathSteering.CurrentPath != null
                && PathSteering.CurrentPath.Nodes.Any(n => HumanAIController.UnsafeHulls.Contains(n.CurrentHull));
            if (containsUnsafeNodes || HumanAIController.UnreachableHulls.Contains(targetHull))
            {
                abandon = true;
                SteeringManager.Reset();
                return;
            }
            bool insideSteering = SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.IsPathDirty;
            bool isInside = character.CurrentHull != null;
            bool targetIsOutside = (Target != null && targetHull == null) || (insideSteering && PathSteering.CurrentPath.HasOutdoorsNodes);
            if (isInside && targetIsOutside && !AllowGoingOutside)
            {
                abandon = true;
            }
            else if (waitUntilPathUnreachable < 0)
            {
                if (SteeringManager == PathSteering && PathSteering.CurrentPath != null && PathSteering.CurrentPath.Unreachable)
                {
                    if (repeat)
                    {
                        SteeringManager.Reset();
                    }
                    else
                    {
                        abandon = true;
                        if (targetHull != null)
                        {
                            HumanAIController.UnreachableHulls.Add(targetHull);
                        }
                    }
                }
            }
            if (abandon)
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Cannot reach the target: {Target.ToString()}", Color.Yellow);
#endif
                if (objectiveManager.CurrentOrder != null)
                {
                    character.Speak(TextManager.Get("DialogCannotReach"), identifier: "cannotreach", minDurationBetweenSimilar: 10.0f);
                }
                SteeringManager.Reset();
            }
            else
            {
                if (getDivingGearIfNeeded)
                {
                    Character followTarget = Target as Character;
                    bool needsDivingGear = HumanAIController.NeedsDivingGear(character, targetHull, out bool needsDivingSuit);
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
                        needsEquipment = !HumanAIController.HasDivingSuit(character);
                    }
                    else if (needsDivingGear)
                    {
                        needsEquipment = !HumanAIController.HasDivingMask(character) && !HumanAIController.HasDivingSuit(character);
                    }
                    if (needsEquipment)
                    {
                        TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager));
                        return;
                    }
                }
                if (repeat && IsCloseEnough)
                {
                    OnCompleted();
                    return;
                }
                Vector2 currTargetSimPos = Vector2.Zero;
                currTargetSimPos = Target.SimPosition;
                // Take the sub position into account in the sim pos
                if (SteeringManager != PathSteering && character.Submarine == null && Target.Submarine != null)
                {
                    currTargetSimPos += Target.Submarine.SimPosition;
                }
                else if (character.Submarine != null && Target.Submarine == null)
                {
                    currTargetSimPos -= character.Submarine.SimPosition;
                }
                else if (character.Submarine != Target.Submarine)
                {
                    if (character.Submarine != null && Target.Submarine != null)
                    {
                        Vector2 diff = character.Submarine.SimPosition - Target.Submarine.SimPosition;
                        currTargetSimPos -= diff;
                    }
                }
                if (PathSteering != null)
                {
                    PathSteering.startNodeFilter = startNodeFilter;
                    PathSteering.endNodeFilter = endNodeFilter;
                }
                SteeringManager.SteeringSeek(currTargetSimPos);
                if (SteeringManager != PathSteering)
                {
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: 5, weight: 1, heading: VectorExtensions.Forward(character.AnimController.Collider.Rotation));
                }
            }
        }

        private bool IsCloseEnough
        {
            get
            {
                bool closeEnough = Vector2.DistanceSquared(Target.WorldPosition, character.WorldPosition) < CloseEnough * CloseEnough;
                if (closeEnough)
                {
                    closeEnough = !(Target is Character) || Target is Character c && c.CurrentHull == character.CurrentHull;
                }
                return closeEnough;
            }
        }

        protected override bool Check()
        {
            if (isCompleted) { return true; }
            // First check the distance
            // Then the custom condition
            // And finally check if can interact (heaviest)
            if (Target == null)
            {
                abandon = true;
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
                            if (character.CanInteractWith(item, out _, checkLinked: false)) { isCompleted = true; }
                        }
                        else if (Target is Character targetCharacter)
                        {
                            if (character.CanInteractWith(targetCharacter, CloseEnough)) { isCompleted = true; }
                        }
                        else
                        {
                            isCompleted = true;
                        }
                    }
                }
            }
            return isCompleted;
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
            base.OnCompleted();
        }
    }
}
