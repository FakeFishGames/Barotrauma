using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class AIObjectiveDecontainItem : AIObjective
    {
        public override string DebugTag => "decontain item";

        public Func<Item, float> GetItemPriority;

        //can either be a tag or an identifier
        private readonly string[] itemIdentifiers;
        private readonly ItemContainer container;
        private readonly Item targetItem;

        private AIObjectiveGoTo goToObjective;

        public AIObjectiveDecontainItem(Character character, Item targetItem, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetItem = targetItem;
            this.container = container;
        }


        public AIObjectiveDecontainItem(Character character, string itemIdentifier, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : this(character, new string[] { itemIdentifier }, container, objectiveManager, priorityModifier) { }

        public AIObjectiveDecontainItem(Character character, string[] itemIdentifiers, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }
            this.container = container;
        }

        protected override bool Check() => IsCompleted;

        public override float GetPriority()
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return 1.0f;
        }

        protected override void Act(float deltaTime)
        {
            if (IsCompleted) { return; }
            Item itemToDecontain = null;
            //get the item that should be de-contained
            if (targetItem == null)
            {
                if (itemIdentifiers != null)
                {
                    foreach (string identifier in itemIdentifiers)
                    {
                        itemToDecontain = container.Inventory.FindItemByIdentifier(identifier, true) ?? container.Inventory.FindItemByTag(identifier, true);
                        if (itemToDecontain != null) { break; }
                    }
                }
            }
            else
            {
                itemToDecontain = targetItem;
            }
            if (itemToDecontain == null || itemToDecontain.Container != container.Item) // Item not found or already de-contained, consider complete
            {
                IsCompleted = true;
                return;
            }
            if (itemToDecontain.OwnInventory != character.Inventory && itemToDecontain.ParentInventory != character.Inventory)
            {
                if (!character.CanInteractWith(container.Item, out _, checkLinked: false))
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager));
                    return;
                }
            }
            itemToDecontain.Drop(character);
            IsCompleted = true;
        }
    }
}
