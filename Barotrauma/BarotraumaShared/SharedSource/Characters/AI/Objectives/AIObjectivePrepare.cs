using Barotrauma.Extensions;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class AIObjectivePrepare : AIObjective
    {
        public override Identifier Identifier { get; set; } = "prepare".ToIdentifier();
        public override string DebugTag => $"{Identifier}";
        public override bool KeepDivingGearOn => true;
        public override bool KeepDivingGearOnAlsoWhenInactive => true;
        public override bool PrioritizeIfSubObjectivesActive => true;
        protected override bool AllowWhileHandcuffed => false;

        private AIObjectiveGetItem getSingleItemObjective;
        private AIObjectiveGetItems getAllItemsObjective;
        private AIObjectiveGetItems getMultipleItemsObjective;
        private bool subObjectivesCreated;
        private readonly Item targetItem;
        private readonly ImmutableArray<Identifier> requiredItems;
        private readonly ImmutableArray<Identifier> optionalItems;
        public bool KeepActiveWhenReady { get; set; }
        public bool CheckInventory { get; set; }
        public bool FindAllItems { get; set; }
        public bool Equip { get; set; }
        public bool EvaluateCombatPriority { get; set; }
        public bool RequireNonEmpty { get; set; }

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

        protected override bool CheckObjectiveSpecific() => IsCompleted;

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
                                EvaluateCombatPriority = EvaluateCombatPriority,
                                RequireNonEmpty = RequireNonEmpty,
                                RequireAllItems = requireAll
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
                            if (!KeepActiveWhenReady)
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
                            SpeakIfFails = true
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
                            SpeakIfFails = true,
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
