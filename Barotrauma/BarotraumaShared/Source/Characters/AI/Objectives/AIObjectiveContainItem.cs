using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        public override string DebugTag => "contain item";

        public int MinContainedAmount = 1;

        //can either be a tag or an identifier
        private string[] itemIdentifiers;

        private ItemContainer container;

        public Func<Item, float> GetItemPriority;

        public string[] ignoredContainerIdentifiers;

        //can either be a tag or an identifier
        private readonly string[] itemIdentifiers;
        private readonly ItemContainer container;

        private bool isCompleted;
        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;
        
        public AIObjectiveContainItem(Character character, string itemIdentifier, ItemContainer container)
            : this(character, new string[] { itemIdentifier }, container)
        {
        }

        public AIObjectiveContainItem(Character character, string[] itemIdentifiers, ItemContainer container)
            : base (character, "")
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
            if (isCompleted) { return true; }
            int containedItemCount = 0;
            foreach (Item item in container.Inventory.Items)
            {
                if (item != null && itemIdentifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id))) containedItemCount++;
            }

            return containedItemCount >= MinContainedAmount;
        }

        public override bool CanBeCompleted
        {
            get
            {
                if (goToObjective != null)
                {
                    containedItemCount++;
                }

                return getItemObjective == null || getItemObjective.CanBeCompleted;
            }
            return containedItemCount >= MinContainedAmount;
        }

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
            if (isCompleted) { return; }
            //get the item that should be contained
            Item itemToContain = null;
            foreach (string identifier in itemIdentifiers)
            {
                itemToContain = character.Inventory.FindItemByIdentifier(identifier) ?? character.Inventory.FindItemByTag(identifier);
                if (itemToContain != null && itemToContain.Condition > 0.0f) break;
            }
            
            if (itemToContain == null)
            {
                getItemObjective = new AIObjectiveGetItem(character, itemIdentifiers)
                {
                    GetItemPriority = GetItemPriority,
                    ignoredContainerIdentifiers = ignoredContainerIdentifiers
                };
                AddSubObjective(getItemObjective);
                return;
            }
            if (container.Item.ParentInventory == character.Inventory)
            {
                var containedItems = container.Inventory.Items;
                //if there's already something in the mask (empty oxygen tank?), drop it
                var existingItem = containedItems.FirstOrDefault(i => i != null);
                if (existingItem != null)
                {
                    existingItem.Drop(character);
                }              
                character.Inventory.RemoveItem(itemToContain);
                container.Inventory.TryPutItem(itemToContain, null);
            }
            else
            {
                if (container.Item.CurrentHull != character.CurrentHull || (Vector2.Distance(character.Position, container.Item.Position) > container.Item.InteractDistance && !container.Item.IsInsideTrigger(character.WorldPosition)))
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager));
                    return;
                }
                container.Combine(itemToContain);
            }
            isCompleted = true;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveContainItem objective = otherObjective as AIObjectiveContainItem;
            if (objective == null) return false;
            if (objective.container != container) return false;
            if (objective.itemIdentifiers.Length != itemIdentifiers.Length) return false;

            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                if (objective.itemIdentifiers[i] != itemIdentifiers[i]) return false;
            }
            return true;
        }    
    }
}
