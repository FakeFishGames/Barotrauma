using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class AIObjectiveGoTo : AIObjective
    {
        private Entity target;

        private Vector2 targetPos;

        private bool repeat;

        //how long until the path to the target is declared unreachable
        private float waitUntilPathUnreachable;

        private bool getDivingGearIfNeeded;

        public float CloseEnough = 0.5f;

        public bool IgnoreIfTargetDead;

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (target != null && target.Removed) return 0.0f;
            if (IgnoreIfTargetDead && target is Character character && character.IsDead) return 0.0f;
                        
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
                if (target != null && target.Removed) return false;

                if (repeat || waitUntilPathUnreachable > 0.0f) return true;
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;

                //path doesn't exist (= hasn't been searched for yet), assume for now that the target is reachable
                if (pathSteering?.CurrentPath == null) return true;

                return (!pathSteering.CurrentPath.Unreachable);
            }
        }

        public Entity Target
        {
            get { return target; }
        }

        public AIObjectiveGoTo(Entity target, Character character, bool repeat = false, bool getDivingGearIfNeeded = true)
            : base (character, "")
        {
            this.target = target;
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
            if (target == character)
            {
                character.AIController.SteeringManager.Reset();
                return;
            }

            waitUntilPathUnreachable -= deltaTime;

            if (character.SelectedConstruction != null && character.SelectedConstruction.GetComponent<Ladder>() == null)
            {
                character.SelectedConstruction = null;
            }

            if (target != null) character.AIController.SelectTarget(target.AiTarget);

            Vector2 currTargetPos = Vector2.Zero;

            if (target == null)
            {
                currTargetPos = targetPos;
            }
            else
            {
                currTargetPos = target.SimPosition;
                
                //if character is inside the sub and target isn't, transform the position
                if (character.Submarine != null && target.Submarine == null)
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
                if (getDivingGearIfNeeded && target?.Submarine == null)
                {
                    AddSubObjective(new AIObjectiveFindDivingGear(character, true));
                }
                else if (character.AIController.SteeringManager is IndoorsSteeringManager indoorsSteering)
                {
                    if (indoorsSteering.CurrentPath == null || indoorsSteering.CurrentPath.Unreachable)
                    {
                        indoorsSteering.SteeringWander();
                    }
                    else if (getDivingGearIfNeeded && indoorsSteering.CurrentPath != null && indoorsSteering.CurrentPath.HasOutdoorsNodes)
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

            if (target is Item item)
            {
                allowedDistance = Math.Max(ConvertUnits.ToSimUnits(item.InteractDistance), allowedDistance);
                if (item.IsInsideTrigger(character.WorldPosition)) completed = true;
            }

            completed = completed || Vector2.DistanceSquared(target != null ? target.SimPosition : targetPos, character.SimPosition) < allowedDistance * allowedDistance;

            if (completed) character.AIController.SteeringManager.Reset();

            return completed;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveGoTo objective = otherObjective as AIObjectiveGoTo;
            if (objective == null) return false;

            if (objective.target == target) return true;

            return (objective.targetPos == targetPos);
        }
    }
}
