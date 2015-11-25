using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        private Item targetItem;

        public AIObjectiveOperateItem(Item item, Character character)
            :base (character)
        {
            targetItem = item;
        }

        protected override void Act(float deltaTime)
        {
            //item.AIOperate(float deltaTime, Character character) or something
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveOperateItem operateItem = otherObjective as AIObjectiveOperateItem;
            if (operateItem == null) return false;

            return (operateItem.targetItem == targetItem);
        }
    }
}
