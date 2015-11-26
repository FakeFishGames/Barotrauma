using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        private ItemComponent targetItem;

        public AIObjectiveOperateItem(ItemComponent item, Character character)
            :base (character)
        {
            targetItem = item;
        }

        protected override void Act(float deltaTime)
        {
            if (Vector2.Distance(character.SimPosition, targetItem.Item.SimPosition) < targetItem.Item.PickDistance)
            {
                //targetItem.Pick(character, false, true);
                return;
            }

            subObjectives.Add(new AIObjectiveGoTo(targetItem.Item.SimPosition, character));
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveOperateItem operateItem = otherObjective as AIObjectiveOperateItem;
            if (operateItem == null) return false;

            return (operateItem.targetItem == targetItem);
        }
    }
}
