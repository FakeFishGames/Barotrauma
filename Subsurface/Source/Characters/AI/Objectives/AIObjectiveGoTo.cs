using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveGoTo : AIObjective
    {
        Entity target;

        Vector2 targetPos;

        bool repeat;

        //how long until the path to the target is declared unreachable
        private float waitUntilPathUnreachable;

        public override bool CanBeCompleted
        {
            get
            {
                if (repeat || waitUntilPathUnreachable > 0.0f) return true;
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;

                //path doesn't exist (= hasn't been searched for yet), assume for now that the target is reachable
                if (pathSteering.CurrentPath == null) return true;

                return (!pathSteering.CurrentPath.Unreachable);
            }
        }

        public Entity Target
        {
            get { return target; }
        }

        public AIObjectiveGoTo(Entity target, Character character, bool repeat = false)
            : base (character, "")
        {
            this.target = target;
            this.repeat = repeat;

            waitUntilPathUnreachable = 5.0f;
        }


        public AIObjectiveGoTo(Vector2 simPos, Character character, bool repeat = false)
            : base(character, "")
        {
            this.targetPos = simPos;
            this.repeat = repeat;

            waitUntilPathUnreachable = 5.0f;
        }

        protected override void Act(float deltaTime)
        {
            waitUntilPathUnreachable -= deltaTime;

            if (character.SelectedConstruction!=null && character.SelectedConstruction.GetComponent<Ladder>()==null)
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
                
                //if character is outside the sub and target isn't, transform the position
                if (character.Submarine == null && target.Submarine != null)
                {
                    //currTargetPos += target.Submarine.SimPosition;
                }
                else if (target.Submarine==null)
                {
                    currTargetPos -= Submarine.Loaded.SimPosition;
                }
            }

            character.AIController.SteeringManager.SteeringSeek(currTargetPos);

            if (Vector2.Distance(currTargetPos, character.SimPosition) < 1.0f)
            {
                character.AnimController.TargetDir = currTargetPos.X > character.SimPosition.X ? Direction.Right : Direction.Left;
            }
        }

        public override bool IsCompleted()
        {
            if (repeat) return false;

            bool completed = false;

            float allowedDistance = 0.5f;
            var item = target as Item;

            if (item != null)
            {
                allowedDistance = Math.Max(item.PickDistance, allowedDistance);
                if (item.IsInsideTrigger(character.WorldPosition)) completed = true;
            }

            completed = completed || Vector2.Distance(target != null ? target.SimPosition : targetPos, character.SimPosition) < allowedDistance;

            if (completed) character.AIController.SteeringManager.SteeringManual(0.0f, -character.AIController.Steering);

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
