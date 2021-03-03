using Barotrauma.Items.Components;
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

        //if the item can't be found, spawn it in the character's inventory (used by outpost NPCs)
        private bool spawnItemIfNotFound = false;

        //can either be a tag or an identifier
        public readonly string[] itemIdentifiers;
        public readonly ItemContainer container;
        private readonly Item item;
        public Item ItemToContain { get; private set; }

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;

        private readonly HashSet<Item> containedItems = new HashSet<Item>();

        public bool AllowToFindDivingGear { get; set; } = true;
        public bool AllowDangerousPressure { get; set; }
        public float ConditionLevel { get; set; } = 1;
        public bool Equip { get; set; }
        public bool RemoveEmpty { get; set; } = true;

        public bool MoveWholeStack { get; set; }


        public AIObjectiveContainItem(Character character, Item item, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.container = container;
            this.item = item;
        }

        public AIObjectiveContainItem(Character character, string itemIdentifier, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool spawnItemIfNotFound = false)
            : this(character, new string[] { itemIdentifier }, container, objectiveManager, priorityModifier, spawnItemIfNotFound) { }

        public AIObjectiveContainItem(Character character, string[] itemIdentifiers, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool spawnItemIfNotFound = false) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.itemIdentifiers = itemIdentifiers;
            this.spawnItemIfNotFound = spawnItemIfNotFound;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }
            this.container = container;
        }

        protected override bool Check()
        {
            if (IsCompleted) { return true; }
            if (container == null || (container.Item != null && container.Item.IsThisOrAnyContainerIgnoredByAI()))
            {
                Abandon = true;
                return false;
            }
            if (item != null)
            {
                return container.Inventory.Contains(item);
            }
            else
            {
                int containedItemCount = 0;
                foreach (Item it in container.Inventory.AllItems)
                {
                    if (CheckItem(it))
                    {
                        containedItemCount++;
                    }
                }
                return containedItemCount >= targetItemCount;
            }
        }

        private bool CheckItem(Item i) => itemIdentifiers.Any(id => i.Prefab.Identifier == id || i.HasTag(id)) && i.ConditionPercentage >= ConditionLevel && !i.IsThisOrAnyContainerIgnoredByAI();

        protected override void Act(float deltaTime)
        {
            if (container == null || (container.Item != null && container.Item.IsThisOrAnyContainerIgnoredByAI()))
            {
                Abandon = true;
                return;
            }
            ItemToContain = item ?? character.Inventory.FindItem(i => CheckItem(i) && i.Container != container.Item, recursive: true);
            if (ItemToContain != null)
            {
                if (!character.CanInteractWith(ItemToContain, checkLinked: false))
                {
                    Abandon = true;
                    return;
                }
                if (character.CanInteractWith(container.Item, checkLinked: false))
                {
                    if (RemoveEmpty)
                    {
                        HumanAIController.UnequipEmptyItems(container.Item);
                    }
                    Inventory originalInventory = ItemToContain.ParentInventory;
                    var slots = originalInventory?.FindIndices(ItemToContain);
                    if (container.Inventory.TryPutItem(ItemToContain, null))
                    {
                        if (MoveWholeStack && slots != null)
                        {
                            foreach (int slot in slots)
                            {
                                foreach (Item item in originalInventory.GetItemsAt(slot).ToList())
                                {
                                    container.Inventory.TryPutItem(item, null);
                                }
                            }

                            IsCompleted = true;
                        }
                    }
                    else
                    {
                        if (ItemToContain.ParentInventory == character.Inventory)
                        {
                            ItemToContain.Drop(character);
                        }
                        Abandon = true;
                    }
                }
                else
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager, getDivingGearIfNeeded: AllowToFindDivingGear)
                    {
                        DialogueIdentifier = "dialogcannotreachtarget",
                        TargetName = container.Item.Name,
                        abortCondition = obj => !ItemToContain.IsOwnedBy(character)
                    },
                    onAbandon: () => Abandon = true,
                    onCompleted: () => RemoveSubObjective(ref goToObjective));
                }
            }
            else
            {
                if (character.Submarine == null)
                {
                    Abandon = true;
                }
                else
                {
                    // No matching items in the inventory, try to get an item
                    TryAddSubObjective(ref getItemObjective, () =>
                        new AIObjectiveGetItem(character, itemIdentifiers, objectiveManager, equip: Equip, checkInventory: checkInventory, spawnItemIfNotFound: spawnItemIfNotFound)
                        {
                            GetItemPriority = GetItemPriority,
                            ignoredContainerIdentifiers = ignoredContainerIdentifiers,
                            ignoredItems = containedItems,
                            AllowToFindDivingGear = AllowToFindDivingGear,
                            AllowDangerousPressure = AllowDangerousPressure,
                            TargetCondition = ConditionLevel
                        }, onAbandon: () =>
                        {
                            Abandon = true;
                        }, onCompleted: () =>
                        {
                            if (getItemObjective?.TargetItem != null)
                            {
                                containedItems.Add(getItemObjective.TargetItem);
                            }
                            RemoveSubObjective(ref getItemObjective);
                        });
                }
            }
        }

        public override void Reset()
        {
            base.Reset();
            getItemObjective = null;
            goToObjective = null;
            containedItems.Clear();
        }
    }
}
