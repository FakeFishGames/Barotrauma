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

        private Character character;

        public AIObjectiveGoTo(AITarget target, Character character)
        {
            this.character = character;
            this.target = target;

        }

        protected override void Act(float deltaTime, Character character)
        {
            if (target == null) return;

            character.AIController.SelectTarget(target);

            character.AIController.SteeringManager.SteeringSeek(ConvertUnits.ToDisplayUnits(target.Position));
        }
    }
}
