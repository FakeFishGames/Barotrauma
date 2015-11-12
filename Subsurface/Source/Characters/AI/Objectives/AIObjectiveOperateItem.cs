using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        private Item targetItem;

        public AIObjectiveOperateItem(Item item)
        {
            targetItem = item;
        }

        protected override void Act(float deltaTime, Character character)
        {
            //item.AIOperate(float deltaTime, Character character) or something
        }
    }
}
