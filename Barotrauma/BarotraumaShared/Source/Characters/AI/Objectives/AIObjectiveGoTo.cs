using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class AIObjectiveGoTo : AIObjective
    {
        private Vector2 targetPos;

        private bool repeat;

        //how long until the path to the target is declared unreachable
        private float waitUntilPathUnreachable;

        private bool getDivingGearIfNeeded;

        public float CloseEnough = 0.5f;

        public bool IgnoreIfTargetDead;

        public bool AllowGoingOutside = false;

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (Target != null && Target.Removed) return 0.0f;
            if (IgnoreIfTargetDead && Target is Character character && character.IsDead) return 0.0f;
                        
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }

        public override bool CanBeCompleted
        {
            get
            {
                if (Target != null && Target.Removed) return false;

                if (repeat || waitUntilPathUnreachable > 0.0f) return true;
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;

                //path doesn't exist (= hasn't been searched for yet), assume for now that the target is reachable
                if (pathSteering?.CurrentPath == null) return true;

                if (!AllowGoingOutside && pathSteering.CurrentPath.HasOutdoorsNodes) return false;

                return !pathSteering.CurrentPath.Unreachable;
            }
        }

        public Entity Target { get; private set; }

        public AIObjectiveGoTo(Entity target, Character character, bool repeat = false, bool getDivingGearIfNeeded = true)
            : base (character, "")
        {
            this.Target = target;
            this.repeat = repeat;

            waitUntilPathUnreachable = 5.0f;
            this.getDivingGearIfNeeded = getDivingGearIfNeeded;
        }


        public AIObjectiveGoTo(Vector2 simPos, Character character, bool repeat = false, bool getDivingGearIfNeeded = true)
            : base(character, "")
        {
            this.targetPos = simPos;
            this.repeat = repeat;

            waitUntilPathUnreachable = 5.0f;
            this.getDivingGearIfNeeded = getDivingGearIfNeeded;
        }

        protected override void Act(float deltaTime)
        {
            if (Target == character)
            {
                character.AIController.SteeringManager.Reset();
                return;
            }

            waitUntilPathUnreachable -= deltaTime;

            if (character.SelectedConstruction != null && character.SelectedConstruction.GetComponent<Ladder>() == null)
            {
                character.SelectedConstruction = null;
            }

            if (Target != null) character.AIController.SelectTarget(Target.AiTarget);

            Vector2 currTargetPos = Vector2.Zero;

            if (Target == null)
            {
                currTargetPos = targetPos;
            }
            else
            {
                currTargetPos = Target.SimPosition;
                
                //if character is inside the sub and target isn't, transform the position
                if (character.Submarine != null && Target.Submarine == null)
                {
                    currTargetPos -= character.Submarine.SimPosition;
                }
            }

            if (Vector2.DistanceSquared(currTargetPos, character.SimPosition) < CloseEnough * CloseEnough)
            {
                character.AIController.SteeringManager.Reset();
                character.AnimController.TargetDir = currTargetPos.X > character.SimPosition.X ? Direction.Right : Direction.Left;
            }
            else
            {
                character.AIController.SteeringManager.SteeringSeek(currTargetPos);
                if (getDivingGearIfNeeded && Target?.Submarine == null && AllowGoingOutside)
                {
                    AddSubObjective(new AIObjectiveFindDivingGear(character, true));
                }
                else if (character.AIController.SteeringManager is IndoorsSteeringManager indoorsSteering)
                {
                    if (indoorsSteering.CurrentPath == null || indoorsSteering.CurrentPath.Unreachable)
                    {
                        indoorsSteering.SteeringWander();
                    }
                    else if (AllowGoingOutside && 
                        getDivingGearIfNeeded && 
                        indoorsSteering.CurrentPath != null && 
                        indoorsSteering.CurrentPath.HasOutdoorsNodes)
                    {
                        AddSubObjective(new AIObjectiveFindDivingGear(character, true));
                    }
                }
            }
        }

        public override bool IsCompleted()
        {
            if (repeat) return false;

            bool completed = false;

            float allowedDistance = 0.5f;

            if (Target is Item item)
            {
                allowedDistance = Math.Max(ConvertUnits.ToSimUnits(item.InteractDistance), allowedDistance);
                if (item.IsInsideTrigger(character.WorldPosition)) completed = true;
            }
            else if (Target is Character targetCharacter)
            {
                if (character.CanInteractWith(targetCharacter)) completed = true;
            }

            completed = completed || Vector2.DistanceSquared(Target != null ? Target.SimPosition : targetPos, character.SimPosition) < allowedDistance * allowedDistance;

            if (completed) character.AIController.SteeringManager.Reset();

            return completed;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveGoTo objective = otherObjective as AIObjectiveGoTo;
            if (objective == null) return false;

            if (objective.Target == Target) return true;

            return (objective.targetPos == targetPos);
        }
    }
}
