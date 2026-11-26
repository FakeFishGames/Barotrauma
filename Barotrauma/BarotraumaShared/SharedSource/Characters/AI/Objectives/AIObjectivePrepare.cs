using Barotrauma.Extensions;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    public class AIObjectivePrepare : AIObjective
    {
        public override Identifier Identifier { get; set; } = "prepare".ToIdentifier();
        public override string DebugTag => $"{Identifier}";
        public override bool KeepDivingGearOn => true;
        public override bool KeepDivingGearOnAlsoWhenInactive => true;
        public override bool PrioritizeIfSubObjectivesActive => true;
        protected override bool RequireOneHandAvailable => true;

        private AIObjectiveGetItem getSingleItemObjective;
        private AIObjectiveGetItems getAllItemsObjective;
        private AIObjectiveGetItems getMultipleItemsObjective;
        private bool subObjectivesCreated;
        private readonly Item targetItem;
        private readonly ImmutableArray<Identifier> requiredItems;
        private readonly ImmutableArray<Identifier> optionalItems;
        public bool KeepActiveWhenReady { get; set; }
        public bool CheckInventory { get; set; }

        /// <summary>
        /// Should the objective try to find items matching all of the specified tags, or one item that matches them?
        /// </summary>
        public bool FindAllItems { get; set; }
        public bool Equip { get; set; }
        public bool Wear { get; set; }
        public bool EvaluateCombatPriority { get; set; }
        /// <summary>
        /// When enabled, the bot will try to evaluate the suitability of all the items it has access to and fetch the best one.
        /// Can be used in conjunction with <see cref="EvaluateCombatPriority"/> to make the bot get the best weapon they have access to. 
        /// The bot will also consider items in their inventory, and not try to fetch a weapon if they can't find anything better than their current one.
        /// </summary>
        public bool EvaluateAllItems { get; set; }
        public bool RequireNonEmpty { get; set; }
        public bool SpeakIfFails { get; set; }

        private AIObjective GetSubObjective()
        {
            if (getSingleItemObjective != null) { return getSingleItemObjective; }
            if (getAllItemsObjective == null || getAllItemsObjective.IsCompleted)
            {
                return getMultipleItemsObjective;
            }
            return getAllItemsObjective;
        }
        public AIObjectivePrepare(Character character, AIObjectiveManager objectiveManager, Item targetItem, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetItem = targetItem;
        }

        public AIObjectivePrepare(Character character, AIObjectiveManager objectiveManager, IEnumerable<Identifier> optionalItems, IEnumerable<Identifier> requiredItems = null, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.optionalItems = optionalItems.ToImmutableArray();
            if (requiredItems != null)
            {
                this.requiredItems = requiredItems.ToImmutableArray();
            }
        }

        protected override bool CheckObjectiveState() => IsCompleted;

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                HandleDisallowed();
                return Priority;
            }
            Priority = objectiveManager.GetOrderPriority(this);
            var subObjective = GetSubObjective();
            if (subObjective is { IsCompleted: true })
            {
                Priority = 0;
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            if (!subObjectivesCreated)
            {
                if (FindAllItems && targetItem == null)
                {
                    getMultipleItemsObjective = CreateObjectives(optionalItems, requireAll: false);
                    if (requiredItems != null && requiredItems.Any())
                    {
                        getAllItemsObjective = CreateObjectives(requiredItems, requireAll: true);
                    }
                    AIObjectiveGetItems CreateObjectives(IEnumerable<Identifier> itemTags, bool requireAll)
                    {
                        AIObjectiveGetItems objectiveReference = null;
                        if (!TryAddSubObjective(ref objectiveReference, () =>
                        {
                            var getItems = new AIObjectiveGetItems(character, objectiveManager, itemTags)
                            {
                                CheckInventory = CheckInventory,
                                Equip = Equip,
                                Wear = Wear,
                                EvaluateCombatPriority = EvaluateCombatPriority,
                                RequireNonEmpty = RequireNonEmpty,
                                RequireAllItems = requireAll,
                            };

                            if (itemTags.Contains(Tags.HeavyDivingGear))
                            {
                                getItems.ItemFilter = (Item it, Identifier tag) =>
                                {
                                    if (tag == Tags.HeavyDivingGear)
                                    {
                                        return AIObjectiveFindDivingGear.IsSuitablePressureProtection(it, tag, character);
                                    }
                                    return true;
                                };
                            }
                            return getItems;
                        },
                        onCompleted: () =>
                        {
                            if (!KeepActiveWhenReady &&
                                //both the optional and mandatory items must be retrieved to consider this objective completed
                                getMultipleItemsObjective is { IsCompleted: true } && 
                                getAllItemsObjective is { IsCompleted: true })
                            {
                                IsCompleted = true;
                            }
                        },
                        onAbandon: () => Abandon = true))
                        {
                            Abandon = true;
                        }
                        return objectiveReference;
                    }
                }
                else
                {
                    Func<AIObjectiveGetItem> getItemConstructor;
                    if (targetItem != null)
                    {
                        getItemConstructor = () => new AIObjectiveGetItem(character, targetItem, objectiveManager, equip: Equip)
                        {
                            SpeakIfFails = SpeakIfFails
                        };
                    }
                    else
                    {
                        IEnumerable<Identifier> allItems = optionalItems;
                        if (requiredItems != null && requiredItems.Any())
                        {
                            allItems = requiredItems;
                        }
                        getItemConstructor = () => new AIObjectiveGetItem(character, allItems, objectiveManager, equip: Equip, checkInventory: CheckInventory)
                        {
                            EvaluateCombatPriority = EvaluateCombatPriority,
                            EvaluateAllItems = EvaluateAllItems,
                            SpeakIfFails = SpeakIfFails,
                            RequireNonEmpty = RequireNonEmpty
                        };
                    }
                    if (!TryAddSubObjective(ref getSingleItemObjective, getItemConstructor,
                        onCompleted: () =>
                        {
                            if (!KeepActiveWhenReady)
                            {
                                IsCompleted = true;
                            }
                        }, 
                        onAbandon: () => Abandon = true))
                    {
                        Abandon = true;
                    }
                    if (getSingleItemObjective != null && EvaluateCombatPriority && EvaluateAllItems)
                    {
                        getSingleItemObjective.EvaluateAllItems = true;
                        float bestInventoryItemPriority = character.Inventory.AllItems.Select(getItemCombatPriority).DefaultIfEmpty(0f).Max();
                        getSingleItemObjective.ItemFilter = (Item it) =>
                        {
                            //disregard any items that are worse than the best weapon in the character's inventory
                            return getItemCombatPriority(it) > bestInventoryItemPriority;
                        };
                        float getItemCombatPriority(Item it)
                        {
                            return it.Components.Max(c => c.IsEmpty(character) ? 0 : c.CombatPriority);
                        }
                    }
                }
                subObjectivesCreated = true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            subObjectivesCreated = false;
            getMultipleItemsObjective = null;
            getSingleItemObjective = null;
            getAllItemsObjective = null;
        }
    }
}
