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
                return container.Inventory.Items.Contains(item);
            }
            else
            {
                int containedItemCount = 0;
                foreach (Item i in container.Inventory.Items)
                {
                    if (i != null && CheckItem(i))
                    {
                        containedItemCount++;
                    }
                }
                return containedItemCount >= targetItemCount;
            }
        }

        private bool CheckItem(Item i) => itemIdentifiers.Any(id => i.Prefab.Identifier == id || i.HasTag(id)) && i.ConditionPercentage >= ConditionLevel;

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
                        foreach (var emptyItem in container.Inventory.Items)
                        {
                            if (emptyItem == null) { continue; }
                            if (emptyItem.Condition <= 0)
                            {
                                emptyItem.Drop(character);
                            }
                        }
                    }
                    // Contain the item
                    if (ItemToContain.ParentInventory == character.Inventory)
                    {
                        if (!container.Inventory.CanBePut(ItemToContain))
                        {
                            Abandon = true;
                        }
                        else
                        {
                            character.Inventory.RemoveItem(ItemToContain);
                            if (container.Inventory.TryPutItem(ItemToContain, null))
                            {
                                IsCompleted = true;
                            }
                            else
                            {
                                ItemToContain.Drop(character);
                                Abandon = true;
                            }
                        }
                    }
                    else
                    {
                        if (container.Combine(ItemToContain, character))
                        {
                            IsCompleted = true;
                        }
                        else
                        {
                            Abandon = true;
                        }
                    }
                }
                else
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager, getDivingGearIfNeeded: AllowToFindDivingGear)
                    {
                        DialogueIdentifier = "dialogcannotreachtarget",
                        TargetName = container.Item.Name,
                        abortCondition = () => !ItemToContain.IsOwnedBy(character)
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
