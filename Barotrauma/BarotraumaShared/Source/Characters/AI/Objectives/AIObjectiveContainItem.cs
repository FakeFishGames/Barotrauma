using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        public override string DebugTag => "contain item";

        public Func<Item, float> GetItemPriority;

        public int targetItemCount = 1;
        public string[] ignoredContainerIdentifiers;
        public bool checkInventory = true;

        //can either be a tag or an identifier
        public readonly string[] itemIdentifiers;
        public readonly ItemContainer container;

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;

        private readonly HashSet<Item> containedItems = new HashSet<Item>();

        public bool AllowToFindDivingGear { get; set; }

        public AIObjectiveContainItem(Character character, string itemIdentifier, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : this(character, new string[] { itemIdentifier }, container, objectiveManager, priorityModifier) { }

        public AIObjectiveContainItem(Character character, string[] itemIdentifiers, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base (character, objectiveManager, priorityModifier)
        {
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }

            this.container = container;
        }

        protected override bool Check()
        {
            int containedItemCount = 0;
            foreach (Item item in container.Inventory.Items)
            {
                if (item != null && itemIdentifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id)))
                {
                    containedItemCount++;
                }
            }
            return containedItemCount >= targetItemCount;
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
            Item itemToContain = character.Inventory.FindItem(i => itemIdentifiers.Any(id => id == i.Prefab.Identifier || i.HasTag(id)) && i.Condition > 0 && i.Container != container.Item, true);
            if (itemToContain != null)
            {
                // Contain the item
                if (container.Item.ParentInventory == character.Inventory)
                {
                    character.Inventory.RemoveItem(itemToContain);
                    container.Inventory.TryPutItem(itemToContain, null);
                }
                else
                {
                    if (!character.CanInteractWith(container.Item, out _, checkLinked: false))
                    {
                        TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager, getDivingGearIfNeeded: AllowToFindDivingGear), 
                            onAbandon: () => Abandon = true,
                            onCompleted: () => RemoveSubObjective(ref goToObjective));
                        return;
                    }
                    container.Combine(itemToContain);
                }
            }
            else
            {
                // Not in the inventory, try to get the item
                TryAddSubObjective(ref getItemObjective, () =>
                    new AIObjectiveGetItem(character, itemIdentifiers, objectiveManager, equip: false, checkInventory: checkInventory)
                    {
                        GetItemPriority = GetItemPriority,
                        ignoredContainerIdentifiers = ignoredContainerIdentifiers,
                        ignoredItems = containedItems,
                        AllowToFindDivingGear = this.AllowToFindDivingGear
                    }, onAbandon: () =>
                    {
                        Abandon = true;
                    }, onCompleted: () =>
                    {
                        if (getItemObjective.TargetItem != null)
                        {
                            containedItems.Add(getItemObjective.TargetItem);
                        }
                        else
                        {
                            if (container.Inventory.FindItem(i => itemIdentifiers.Any(id => i.Prefab.Identifier == id || i.HasTag(id)), false) != null)
                            {
                                IsCompleted = true;
                            }
                            else
                            {
                                Abandon = true;
                            }
                        }
                        RemoveSubObjective(ref getItemObjective);
                    });
            }
        }  
    }
}
