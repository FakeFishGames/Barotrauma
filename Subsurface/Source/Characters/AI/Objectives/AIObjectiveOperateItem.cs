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
        private ItemComponent itemController;

        private bool isCompleted;



        public AIObjectiveOperateItem(ItemComponent item, Character character, string option)
            :base (character, option)
        {
            targetItem = item;

            var controllers = item.Item.GetConnectedComponents<Controller>();
            if (controllers.Any()) itemController = controllers[0];
        }

        protected override void Act(float deltaTime)
        {
            ItemComponent target = itemController == null ? targetItem: itemController;

            if (Vector2.Distance(character.SimPosition, target.Item.SimPosition) < target.Item.PickDistance
                || target.Item.IsInsideTrigger(character.Position))
            {
                if (character.SelectedConstruction != target.Item && target.CanBeSelected)
                {
                    target.Item.Pick(character, false, true);
                }

                if (targetItem.AIOperate(deltaTime, character, this)) isCompleted = true;
                return;
            }

            subObjectives.Add(new AIObjectiveGoTo(target.Item, character));
        }

        public override bool IsCompleted()
        {
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveOperateItem operateItem = otherObjective as AIObjectiveOperateItem;
            if (operateItem == null) return false;

            return (operateItem.targetItem == targetItem);
        }
    }
}
