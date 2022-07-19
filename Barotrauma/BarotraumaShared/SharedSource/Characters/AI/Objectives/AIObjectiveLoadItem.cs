using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveLoadItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "load item".ToIdentifier();
        public override bool IsLoop
        {
            get => true;
            set => throw new Exception("Trying to set the value for AIObjectiveLoadItem.IsLoop from: " + Environment.StackTrace.CleanupStackTrace());
        }

        private AIObjectiveLoadItems.ItemCondition TargetItemCondition { get; }
        private Item Container { get; }
        private ItemContainer ItemContainer { get; }
        private ImmutableArray<Identifier> TargetContainerTags { get; }
        private ImmutableHashSet<Identifier> ValidContainableItemIdentifiers { get; }
        private static Dictionary<ItemPrefab, ImmutableHashSet<Identifier>> AllValidContainableItemIdentifiers { get; } = new Dictionary<ItemPrefab, ImmutableHashSet<Identifier>>();

        private int itemIndex = 0;
        private AIObjectiveDecontainItem decontainObjective;
        private readonly HashSet<Item> ignoredItems = new HashSet<Item>();
        private Item targetItem;
        private readonly string abandonGetItemDialogueIdentifier = "dialogcannotfindloadable";

        public AIObjectiveLoadItem(Item container, ImmutableArray<Identifier> targetTags, AIObjectiveLoadItems.ItemCondition targetCondition, Identifier option, Character character, AIObjectiveManager objectiveManager, float priorityModifier)
            : base(character, objectiveManager, priorityModifier)
        {
            Container = container;
            ItemContainer = container?.GetComponent<ItemContainer>();
            if (ItemContainer?.Inventory == null)
            {
                Abandon = true;
                return;
            }
            TargetContainerTags = targetTags;
            TargetItemCondition = targetCondition;
            if (!option.IsEmpty)
            {
                string optionSpecificDialogueIdentifier = $"{abandonGetItemDialogueIdentifier}.{option}";
                if (TextManager.ContainsTag(optionSpecificDialogueIdentifier))
                {
                    abandonGetItemDialogueIdentifier = optionSpecificDialogueIdentifier;
                }
            }
            ValidContainableItemIdentifiers = GetValidContainableItemIdentifiers();
            if (ValidContainableItemIdentifiers.None())
            {
#if DEBUG
                DebugConsole.ShowError($"No valid containable item identifiers found for the Load Item objective targeting {Container}");
#endif
                Abandon = true;
                return;
            }
        }

        private enum CheckStatus { Unfinished, Finished }

        private ImmutableHashSet<Identifier> GetValidContainableItemIdentifiers()
        {
            if (AllValidContainableItemIdentifiers.TryGetValue(Container.Prefab, out var existingIdentifiers))
            {
                return existingIdentifiers;
            }
            // Status effects are often used to alter item condition so using the Containable Item Identifiers directly can lead to unwanted results
            // For example, placing welding fuel tanks inside oxygen tank shelves
            bool useDefaultContainableItemIdentifiers = true;
            var potentialContainablePrefabs = MapEntityPrefab.List
                .Where(mep => mep is ItemPrefab ip && ItemContainer.ContainableItemIdentifiers.Any(i => i == ip.Identifier || ip.Tags.Contains(i)))
                .Cast<ItemPrefab>();
            var validContainableItemIdentifiers = new HashSet<Identifier>();
            foreach (var component in Container.Components)
            {
                if (CheckComponent() == CheckStatus.Finished)
                {
                    break;
                }
                CheckStatus CheckComponent()
                {
                    if (component.statusEffectLists != null)
                    {
                        foreach (var (_, statusEffects) in component.statusEffectLists)
                        {
                            if (CheckStatusEffects(statusEffects) == CheckStatus.Finished)
                            {
                                return CheckStatus.Finished;
                            }
                        }
                    }
                    if (component is ItemContainer itemContainer && itemContainer.ContainableItems != null)
                    {
                        foreach (var item in itemContainer.ContainableItems)
                        {
                            if (CheckStatusEffects(item.statusEffects) == CheckStatus.Finished)
                            {
                                return CheckStatus.Finished;
                            }
                        }
                    }
                    return CheckStatus.Unfinished;
                    CheckStatus CheckStatusEffects(IEnumerable<StatusEffect> statusEffects)
                    {
                        if (statusEffects == null) { return CheckStatus.Unfinished; }
                        foreach (var statusEffect in statusEffects)
                        {
                            if ((statusEffect.TargetIdentifiers == null || statusEffect.TargetIdentifiers.None()) && !statusEffect.HasConditions) { continue; }
                            switch (TargetItemCondition)
                            {
                                case AIObjectiveLoadItems.ItemCondition.Empty:
                                    if (!statusEffect.ReducesItemCondition()) { continue; }
                                    break;
                                case AIObjectiveLoadItems.ItemCondition.Full:
                                    if (!statusEffect.IncreasesItemCondition()) { continue; }
                                    break;
                                default:
                                    continue;
                            }
                            useDefaultContainableItemIdentifiers = false;
                            if (statusEffect.TargetIdentifiers != null)
                            {
                                foreach (Identifier target in statusEffect.TargetIdentifiers)
                                {
                                    foreach (var prefab in potentialContainablePrefabs)
                                    {
                                        if (CheckPrefab(prefab, () => prefab.Tags.Contains(target)) == CheckStatus.Finished) { return CheckStatus.Finished; }
                                    }
                                }
                            }
                            foreach (var prefab in potentialContainablePrefabs)
                            {
                                if (CheckPrefab(prefab, () => statusEffect.MatchesTagConditionals(prefab)) == CheckStatus.Finished) { return CheckStatus.Finished; }
                            }
                            CheckStatus CheckPrefab(ItemPrefab prefab, Func<bool> isValid)
                            {
                                if (validContainableItemIdentifiers.Contains(prefab.Identifier)) { return CheckStatus.Unfinished; }
                                if (!isValid()) { return CheckStatus.Unfinished; }
                                validContainableItemIdentifiers.Add(prefab.Identifier);
                                if (potentialContainablePrefabs.Any(p => !validContainableItemIdentifiers.Contains(p.Identifier))) { return CheckStatus.Unfinished; }
                                return CheckStatus.Finished;
                            }
                        }
                        return CheckStatus.Unfinished;
                    }
                }
            }
            var identifiers = useDefaultContainableItemIdentifiers ?
                potentialContainablePrefabs.Select(p => p.Identifier).ToImmutableHashSet() :
                validContainableItemIdentifiers.ToImmutableHashSet();
            AllValidContainableItemIdentifiers.Add(Container.Prefab, identifiers);
            return identifiers;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
                return Priority;
            }
            else if (!AIObjectiveLoadItems.IsValidTarget(Container, character, targetCondition: TargetItemCondition))
            {
                // Reduce priority to 0 if the this isn't a valid container right now
                Priority = 0;
            }
            else if (targetItem == null)
            {
                Priority = 0;
            }
            else
            {
                float dist = 0.0f;
                if (character.CurrentHull != targetItem.CurrentHull)
                {
                    AddDistance(character.WorldPosition, targetItem.WorldPosition);
                }
                if (targetItem.CurrentHull != Container.CurrentHull)
                {
                    AddDistance(targetItem.WorldPosition, Container.WorldPosition);
                }
                void AddDistance(Vector2 startPos, Vector2 targetPos)
                {
                    float yDist = Math.Abs(startPos.Y - targetPos.Y);
                    // If we're on the same level with the target, we'll disregard the vertical distance
                    if (yDist > 100) { dist += yDist * 5; }
                    dist += Math.Abs(character.WorldPosition.X - targetPos.X);
                }
                float distanceFactor = dist > 0.0f ? MathHelper.Lerp(0.9f, 0, MathUtils.InverseLerp(0, 5000, dist)) : 0.9f;
                bool hasContainable = character.HasItem(targetItem);
                float devotion = (CumulatedDevotion + (hasContainable ? 100 - MaxDevotion : 0)) / 100;
                float max = AIObjectiveManager.LowestOrderPriority - (hasContainable ? 1 : 2);
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (distanceFactor * PriorityModifier), 0, 1));
                if (decontainObjective != null && targetItem.Container != Container)
                {
                    if (!IsValidContainable(targetItem))
                    {
                        // Target is not valid anymore, abandon the objective
                        decontainObjective.Abandon = true;
                    }
                    else if (!ItemContainer.Inventory.CanBePut(targetItem) && ItemContainer.Inventory.AllItems.None(i => AIObjectiveLoadItems.ItemMatchesTargetCondition(i, TargetItemCondition)))
                    {
                        // The container is full and there's no item that should be removed, abandon the objective
                        decontainObjective.Abandon = true;
                    }
                }
                if (ItemContainer.Inventory.IsFull())
                {
                    // Prioritize containers that still have empty space by lowering the priority of objectives with a full target container
                    Priority /= 4;
                }
            }
            return Priority;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (targetItem == null)
            {
                if (character.FindItem(ref itemIndex, out Item item, identifiers: ValidContainableItemIdentifiers, ignoreBroken: false, customPredicate: IsValidContainable, customPriorityFunction: GetPriority))
                {
                    if (item == null)
                    {
                        // No possible containables found, abandon the objective
                        Abandon = true;
                    }
                    targetItem = item;
                }
                float GetPriority(Item item)
                {
                    try
                    {
                        // Prefer items closer to full condition when target condition is Empty, and vice versa
                        float conditionBasedPriority = TargetItemCondition switch
                        {
                            AIObjectiveLoadItems.ItemCondition.Full => MathUtils.InverseLerp(100.0f, 0.0f, item.ConditionPercentage),
                            AIObjectiveLoadItems.ItemCondition.Empty => MathUtils.InverseLerp(0.0f, 100.0f, item.ConditionPercentage),
                            _ => throw new NotImplementedException()
                        };
                        // Prefer items that have the same identifier as one of the already contained items
                        return ItemContainer.ContainsItemsWithSameIdentifier(item) ? conditionBasedPriority : conditionBasedPriority / 2;
                    }
                    catch (NotImplementedException)
                    {
#if DEBUG
                        DebugConsole.ShowError($"Unexpected target condition \"{TargetItemCondition}\" in local function GetConditionBasedProperty");
#endif
                        return 0.0f;
                    }
                }
            }
        }

        protected override void Act(float deltaTime)
        {
            if (targetItem != null)
            {
                if(decontainObjective == null && !IsValidContainable(targetItem))
                {
                    IgnoreTargetItem();
                    Reset();
                    return;
                }
                TryAddSubObjective(ref decontainObjective,
                    constructor: () => new AIObjectiveDecontainItem(character, targetItem, objectiveManager, targetContainer: ItemContainer, priorityModifier: PriorityModifier)
                    {
                        AbandonGetItemDialogueCondition = () => IsValidContainable(targetItem),
                        AbandonGetItemDialogueIdentifier = abandonGetItemDialogueIdentifier,
                        Equip = true,
                        RemoveExistingWhenNecessary = true,
                        RemoveExistingPredicate = (i) => !ValidContainableItemIdentifiers.Contains(i.Prefab.Identifier) || AIObjectiveLoadItems.ItemMatchesTargetCondition(i, TargetItemCondition),
                        RemoveExistingMax = 1
                    },
                    onCompleted: () =>
                    {
                        IsCompleted = true;
                        RemoveSubObjective(ref decontainObjective);
                    },
                    onAbandon: () =>
                    {
                        // Try again
                        IgnoreTargetItem();
                        Reset();
                    });
            }
            else
            {
                objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
            }
        }

        private bool IsValidContainable(Item item)
        {
            if (item == null) { return false; }
            if (item.Removed) { return false; }
            if (!ValidContainableItemIdentifiers.Contains(item.Prefab.Identifier)) { return false; }
            if (ignoredItems.Contains(item)) { return false; }
            if ((item.SpawnedInCurrentOutpost && !item.AllowStealing) == character.IsOnPlayerTeam) { return false; }
            var rootInventoryOwner = item.GetRootInventoryOwner();
            if (rootInventoryOwner is Character owner && owner != character) { return false; }
            if (rootInventoryOwner is Item parentItem)
            {
                if (parentItem.HasTag("donttakeitems")) { return false; }
            }
            if (!item.HasAccess(character)) { return false; }
            if (!character.HasItem(item) && !CanEquip(item)) { return false; }
            if (!ItemContainer.CanBeContained(item)) { return false; }
            if (AIObjectiveLoadItems.ItemMatchesTargetCondition(item, TargetItemCondition)) { return false; }
            if (TargetItemCondition == AIObjectiveLoadItems.ItemCondition.Full)
            {
                // Ignore items that have had their condition increase recently
                if (TargetItemCondition == AIObjectiveLoadItems.ItemCondition.Full && item.ConditionIncreasedRecently) { return false; }
                // Ignore items inside their (condition-restricted) primary containers
                if (item.ParentInventory is ItemInventory itemInventory && item.IsContainerPreferred(itemInventory.Container, out bool _, out bool isSecondary, requireConditionRestriction: true) && !isSecondary) { return false; }
            }
            // Ignore items inside another valid container
            if (AIObjectiveLoadItems.IsValidTarget(item.Container, character, TargetContainerTags)) { return false; }
            return true;
        }

        protected override bool CheckObjectiveSpecific() => IsCompleted;

        public override void Reset()
        {
            base.Reset();
            // Don't reset the target item when resetting the objective because it affects priority calculations
            decontainObjective = null;
            itemIndex = 0;
        }

        private void IgnoreTargetItem()
        {
            if(targetItem == null) { return; }
            ignoredItems.Add(targetItem);
            targetItem = null;
        }
    }
}