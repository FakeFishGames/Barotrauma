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

        public override bool CanBeCompleted
        {
            get
            {
                if (repeat) return true;
                var pathSteering = character.AIController.SteeringManager as IndoorsSteeringManager;

                //path doesn't exist (= hasn't been searched for yet), assume for now that the target is reachable
                if (pathSteering.CurrentPath == null) return true;
                //steeringmanager is still targeting some other position, assume for now that the target is reachable
                if ((target != null && Vector2.Distance(target.Position, targetPos) > 5.0f) ||
                    Vector2.Distance(pathSteering.CurrentTarget, targetPos) > 5.0f) return true;

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
        }


        public AIObjectiveGoTo(Vector2 simPos, Character character, bool repeat = false)
            : base(character, "")
        {
            this.targetPos = simPos;
            this.repeat = repeat;
        }

        protected override void Act(float deltaTime)
        {            
            if (character.SelectedConstruction!=null)
            {
                character.SelectedConstruction = null;
            }

            if (target!=null) character.AIController.SelectTarget(target.AiTarget);

            character.AIController.SteeringManager.SteeringSeek(
                target != null ? target.SimPosition : targetPos);

            Vector2 currTargetPos = target != null ? target.SimPosition : targetPos;
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
