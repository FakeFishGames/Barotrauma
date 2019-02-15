using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveDecontainItem : AIObjective
    {
        public override string DebugTag => "decontain item";

        //can either be a tag or an identifier
        private string[] itemIdentifiers;

        private ItemContainer container;

        private bool isCompleted;

        public Func<Item, float> GetItemPriority;

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;
        private Item targetItem;

        public AIObjectiveDecontainItem(Character character, Item targetItem, ItemContainer container)
        : base(character, "")
        {
            this.targetItem = targetItem;
            this.container = container;
        }


        public AIObjectiveDecontainItem(Character character, string itemIdentifier, ItemContainer container)
            : this(character, new string[] { itemIdentifier }, container)
        {
        }

        public AIObjectiveDecontainItem(Character character, string[] itemIdentifiers, ItemContainer container)
            : base(character, "")
        {
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }

            this.container = container;
        }

        public override bool IsCompleted()
        {
            return isCompleted;
        }

        public override bool CanBeCompleted
        {
            get
            {
                if (goToObjective != null)
                {
                    return goToObjective.CanBeCompleted;
                }

                return getItemObjective == null || getItemObjective.CanBeCompleted;
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }

        protected override void Act(float deltaTime)
        {
            if (isCompleted) return;

            Item itemToDecontain = null;

            //get the item that should be de-contained
            if (targetItem == null)
            {
                if (itemIdentifiers != null)
                {
                    foreach (string identifier in itemIdentifiers)
                    {
                        itemToDecontain = container.Inventory.FindItemByIdentifier(identifier) ?? container.Inventory.FindItemByTag(identifier);
                        if (itemToDecontain != null) break;
                    }
                }
            }
            else
            {
                itemToDecontain = targetItem;
            }

            if (itemToDecontain == null || itemToDecontain.Container != container.Item) // Item not found or already de-contained, consider complete
            {
                isCompleted = true;
                return;
            }

            if (itemToDecontain.OwnInventory != character.Inventory && itemToDecontain.ParentInventory != character.Inventory)
            {
                if (Vector2.Distance(character.Position, container.Item.Position) > container.Item.InteractDistance
                && !container.Item.IsInsideTrigger(character.WorldPosition))
                {
                    goToObjective = new AIObjectiveGoTo(container.Item, character);
                    AddSubObjective(goToObjective);
                    return;
                }
            }

            itemToDecontain.Drop(character);
            isCompleted = true;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveDecontainItem decontainItem = otherObjective as AIObjectiveDecontainItem;
            if (decontainItem == null) return false;
            if (decontainItem.itemIdentifiers != null && itemIdentifiers != null)
            {
                if (decontainItem.itemIdentifiers.Length != itemIdentifiers.Length) return false;
                for (int i = 0; i < decontainItem.itemIdentifiers.Length; i++)
                {
                    if (decontainItem.itemIdentifiers[i] != itemIdentifiers[i]) return false;
                }
                return true;
            }
            else if (decontainItem.itemIdentifiers == null && itemIdentifiers == null)
            {
                return decontainItem.targetItem == targetItem;
            }

            return false;
        }
    }
}
