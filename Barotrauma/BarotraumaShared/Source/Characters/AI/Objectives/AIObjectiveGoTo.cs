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

        /// <summary>
        /// Doesn't allow the objective to complete if this condition is false
        /// </summary>
        public Func<bool> requiredCondition;
        /// <summary>
        /// Aborts the objective when this condition is true
        /// </summary>
        public Func<bool> abortCondition;
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

        public override bool AbandonWhenCannotCompleteSubjectives => !repeat;
        public string DialogueIdentifier { get; set; }
        public string TargetName { get; set; }

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

        private void SpeakCannotReach()
        {
#if DEBUG
            DebugConsole.NewMessage($"{character.Name}: Cannot reach the target: {Target.ToString()}", Color.Yellow);
#endif
            if (objectiveManager.CurrentOrder != null && DialogueIdentifier != null)
            {
                string msg = TargetName == null ? TextManager.Get(DialogueIdentifier, true) : TextManager.GetWithVariable(DialogueIdentifier, "[name]", TargetName, true);
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
                    bool needsDivingGear = needsDivingSuit || HumanAIController.NeedsDivingGear(character, targetHull, out needsDivingSuit);
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
                        needsEquipment = !HumanAIController.HasDivingSuit(character, AIObjectiveFindDivingGear.lowOxygenThreshold);
                    }
                    else if (needsDivingGear)
                    {
                        needsEquipment = !HumanAIController.HasDivingGear(character, AIObjectiveFindDivingGear.lowOxygenThreshold);
                    }
                    if (needsEquipment)
                    {
                        TryAddSubObjective(ref findDivingGear, () => new AIObjectiveFindDivingGear(character, needsDivingSuit, objectiveManager), 
                            onAbandon: () => Abandon = true,
                            onCompleted: () => RemoveSubObjective(ref findDivingGear));
                        return;
                    }
                }
                if (repeat && IsCloseEnough)
                {
                    OnCompleted();
                    return;
                }
                if (SteeringManager == PathSteering)
                {
                    Func<PathNode, bool> nodeFilter = null;
                    if (isInside && !AllowGoingOutside)
                    {
                        nodeFilter = node => node.Waypoint.CurrentHull != null;
                    }
                    PathSteering.SteeringSeek(character.GetRelativeSimPosition(Target), 1, startNodeFilter, endNodeFilter, nodeFilter);
                }
                else
                {
                    SteeringManager.SteeringSeek(character.GetRelativeSimPosition(Target), 10);
                }
                if (!insideSteering && character.CurrentHull == null)
                {
                    SteeringManager.SteeringAvoid(deltaTime, lookAheadDistance: 5, weight: 15);
                }
            }
        }

        private Hull GetTargetHull()
        {
            if (Target is Hull h)
            {
                return h;
            }
            else if (Target is Item i)
            {
                return i.CurrentHull;
            }
            else if (Target is Character c)
            {
                return c.CurrentHull;
            }
            else if (Target is Gap g)
            {
                return g.FlowTargetHull;
            }
            else if (Target is WayPoint wp)
            {
                return wp.CurrentHull;
            }
            else if (Target is FireSource fs)
            {
                return fs.Hull;
            }
            return null;
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
                            if (character.CanInteractWith(item, out _, checkLinked: false)) { IsCompleted = true; }
                        }
                        else if (Target is Character targetCharacter)
                        {
                            if (character.CanInteractWith(targetCharacter, CloseEnough)) { IsCompleted = true; }
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
    }
}
