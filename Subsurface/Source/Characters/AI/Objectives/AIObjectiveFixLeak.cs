using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveFixLeak : AIObjective
    {
        Gap leak;

        public AIObjectiveFixLeak(Gap leak, Character character)
            :base (character)
        {
            this.leak = leak;
        }

        public override float GetPriority(Character character)
        {
            return leak.isHorizontal ? leak.Rect.Height * leak.Open : leak.Rect.Width * leak.Open;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveFixLeak fixLeak = otherObjective as AIObjectiveFixLeak;
            if (fixLeak == null) return false;
            return fixLeak.leak == leak;
        }

        protected override void Act(float deltaTime)
        {
            var weldingTool = character.Inventory.FindItem("Welding Tool");

            if (weldingTool == null)
            {
                subObjectives.Add(new AIObjectiveGetItem(character, "Welding Tool"));
            }
            else
            {
                if (Vector2.Distance(character.Position, leak.Position)>10.0f)
                {
                    subObjectives.Add(new AIObjectiveGoTo(leak.Position,character));
                }
            }
        }
    }
}
