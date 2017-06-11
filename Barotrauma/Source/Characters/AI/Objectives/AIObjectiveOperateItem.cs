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
        private ItemComponent component, controller;

        private Entity operateTarget;

        private bool isCompleted;

        private bool canBeCompleted;

        public override bool CanBeCompleted
        {
            get
            {
                return canBeCompleted;
            }
        }

        public Entity OperateTarget
        {
            get { return operateTarget; }
        }

        public AIObjectiveOperateItem(ItemComponent item, Character character, string option, Entity operateTarget = null, bool useController = false)
            :base (character, option)
        {
            this.component = item;

            this.operateTarget = operateTarget;

            if (useController)
            {
                var controllers = item.Item.GetConnectedComponents<Controller>();
                if (controllers.Any()) controller = controllers[0];
            }


            canBeCompleted = true;
        }

        protected override void Act(float deltaTime)
        {
            ItemComponent target = controller == null ? component : controller;

            if (target.CanBeSelected)
            { 
                if (Vector2.Distance(character.Position, target.Item.Position) < target.Item.PickDistance
                    || target.Item.IsInsideTrigger(character.WorldPosition))
                {
                    if (character.SelectedConstruction != target.Item && target.CanBeSelected)
                    {
                        target.Item.Pick(character, false, true);
                    }

                    if (component.AIOperate(deltaTime, character, this)) isCompleted = true;
                    return;
                }

                AddSubObjective(new AIObjectiveGoTo(target.Item, character));
            }
            else
            {
                if (!character.Inventory.Items.Contains(component.Item))
                {
                    AddSubObjective(new AIObjectiveGetItem(character, component.Item, true));
                }
                else
                {
                    if (component.AIOperate(deltaTime, character, this)) isCompleted = true;
                }
            }
        }

        public override bool IsCompleted()
        {
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveOperateItem operateItem = otherObjective as AIObjectiveOperateItem;
            if (operateItem == null) return false;

            return (operateItem.component == component);
        }
    }
}
