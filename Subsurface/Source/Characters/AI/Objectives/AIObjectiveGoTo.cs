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
        AITarget target;

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

        public AITarget Target
        {
            get { return target; }
        }

        public AIObjectiveGoTo(AITarget target, Character character, bool repeat = false)
            : base (character)
        {
            this.target = target;
            this.repeat = false;
        }


        public AIObjectiveGoTo(Vector2 targetPos, Character character)
            : base(character)
        {
            this.targetPos = targetPos;
        }

        protected override void Act(float deltaTime)
        {            
            character.AIController.SelectTarget(target);

            character.AIController.SteeringManager.SteeringSeek(
                target != null ? target.SimPosition : targetPos);
        }

        public override bool IsCompleted()
        {
            if (repeat) return false;
            return Vector2.Distance(target != null ? target.SimPosition : ConvertUnits.ToDisplayUnits(targetPos), character.SimPosition) < 0.5f;
        }
    }
}
