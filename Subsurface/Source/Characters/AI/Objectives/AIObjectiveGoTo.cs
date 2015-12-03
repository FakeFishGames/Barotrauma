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
                return (pathSteering.CurrentPath == null || !pathSteering.CurrentPath.Unreachable);
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
        }

        public override bool IsCompleted()
        {
            if (repeat) return false;

            float allowedDistance = 0.5f;
            var item = target as Item;
            if (item != null) allowedDistance = Math.Max(item.PickDistance,allowedDistance);

            return Vector2.Distance(target != null ? target.SimPosition : targetPos, character.SimPosition) < allowedDistance;
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
