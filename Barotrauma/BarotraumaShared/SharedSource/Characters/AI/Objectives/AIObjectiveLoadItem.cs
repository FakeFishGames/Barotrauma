using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class AIObjectiveLoadItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "load item".ToIdentifier();

        protected override bool RequireOneHandAvailable => true;

        private AIObjectiveLoadItems.ItemCondition TargetItemCondition { get; }
        private Item Container { get; }
        private ItemContainer ItemContainer { get; }
        private ImmutableArray<Identifier> TargetContainerTags { get; }
        private ImmutableHashSet<Identifier> ValidContainableItemIdentifiers { get; set; }
        private static Dictionary<ItemPrefab, ImmutableHashSet<Identifier>> AllValidContainableItemIdentifiers { get; } = new Dictionary<ItemPrefab, ImmutableHashSet<Identifier>>();

        private int itemIndex;
        private AIObjectiveMoveItem moveItemObjective;
        private readonly HashSet<Item> ignoredItems = new HashSet<Item>();
        private Item targetItem;
        private readonly Identifier abandonGetItemDialogueIdentifier = "dialogcannotfindloadable".ToIdentifier();

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
                Identifier optionSpecificDialogueIdentifier = $"{abandonGetItemDialogueIdentifier}.{option}".ToIdentifier();
                if (TextManager.ContainsTag(optionSpecificDialogueIdentifier))
                {
                    abandonGetItemDialogueIdentifier = optionSpecificDialogueIdentifier;
                }
            }
            ValidContainableItemIdentifiers = GetValidContainableItemIdentifiers();
            if (ValidContainableItemIdentifiers.None())
            {
#if DEBUG
                DebugConsole.LogError($"No valid containable item identifiers found for the Load Item objective targeting {Container}");
#endif
                character?.AIController?.SteeringManager?.Reset();
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
            // Only consider containables that bots are allowed to refill (AllowBotRefills="false" excludes e.g. welding fuel in oxygen dispensers).
            // Also applies to the default identifiers used when the status effects don't tell us what to load.
            var refillableContainableIdentifiers = GetAllContainableItems(ItemContainer)
                .Where(c => c.AllowBotRefills)
                .SelectMany(c => c.Identifiers)
                .ToHashSet();
            var potentialContainablePrefabs = MapEntityPrefab.List
                .Where(mep => mep is ItemPrefab ip && refillableContainableIdentifiers.Any(i => i == ip.Identifier || ip.Tags.Contains(i)))
                .Cast<ItemPrefab>()
                .ToList();
            var validContainableItemIdentifiers = new HashSet<Identifier>();
            foreach (var component in Container.Components)
            {
                if (CheckComponent() == CheckStatus.Finished)
                {
                    break;
                }
                CheckStatus CheckComponent()
                {
                    if (CheckStatusEffects(component.GetAllStatusEffects()) == CheckStatus.Finished)
                    {
                        return CheckStatus.Finished;
                    }
                    if (component is ItemContainer itemContainer)
                    {
                        foreach (var containableItem in GetAllContainableItems(itemContainer).Where(c => c.AllowBotRefills))
                        {
                            if (CheckStatusEffects(containableItem.StatusEffects, containableItem.Identifiers) == CheckStatus.Finished)
                            {
                                return CheckStatus.Finished;
                            }
                        }
                    }
                    return CheckStatus.Unfinished;

                    //checks if we can determine what kind of items are supposed to be loaded into the container based on the status effects
                    CheckStatus CheckStatusEffects(IEnumerable<StatusEffect> statusEffects, IEnumerable<Identifier> containableIdentifiers = null)
                    {
                        if (statusEffects == null) { return CheckStatus.Unfinished; }

                        containableIdentifiers ??= Enumerable.Empty<Identifier>();
                        foreach (var statusEffect in statusEffects)
                        {
                            IEnumerable<Identifier> statusEffectTargetIdentifiers =
                                statusEffect.TargetIdentifiers == null ?
                                containableIdentifiers :
                                containableIdentifiers.Concat(statusEffect.TargetIdentifiers);

                            if (statusEffectTargetIdentifiers.None() && !statusEffect.HasConditions) { continue; }
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
                            foreach (Identifier target in statusEffectTargetIdentifiers)
                            {
                                foreach (var prefab in potentialContainablePrefabs)
                                {
                                    if (CheckPrefab(prefab, () => (prefab.Tags.Contains(target) || prefab.Identifier == target) && 
                                        (statusEffect.MatchesTagConditionals(prefab))) == CheckStatus.Finished)
                                    {
                                        return CheckStatus.Finished; 
                                    }
                                }
                            }            
                            CheckStatus CheckPrefab(ItemPrefab prefab, Func<bool> isValid)
                            {
                                if (validContainableItemIdentifiers.Contains(prefab.Identifier)) { return CheckStatus.Unfinished; }
                                if (!isValid()) { return CheckStatus.Unfinished; }
                                validContainableItemIdentifiers.Add(prefab.Identifier);
                                useDefaultContainableItemIdentifiers = false;
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
            AllValidContainableItemIdentifiers.TryAdd(Container.Prefab, identifiers);
            return identifiers;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                HandleDisallowed();
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

                float distanceFactor = 
                    GetDistanceFactor(targetItem.WorldPosition, verticalDistanceMultiplier: 5, maxDistance: 5000, factorAtMinDistance: 0.9f, factorAtMaxDistance: 0);

                bool hasContainable = character.HasItem(targetItem);
                float devotion = (CumulatedDevotion + (hasContainable ? 100 - MaxDevotion : 0)) / 100;
                float max = AIObjectiveManager.LowestOrderPriority - (hasContainable ? 1 : 2);
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (distanceFactor * PriorityModifier), 0, 1));
                if (moveItemObjective != null && targetItem.Container != Container)
                {
                    if (!IsValidContainable(targetItem))
                    {
                        // Target is not valid anymore, abandon the objective
                        moveItemObjective.Abandon = true;
                    }
                    else if (!ItemContainer.Inventory.CanBePut(targetItem) && ItemContainer.Inventory.AllItems.None(i => AIObjectiveLoadItems.ItemMatchesTargetCondition(i, TargetItemCondition)))
                    {
                        // The container is full and there's no item that should be removed, abandon the objective
                        moveItemObjective.Abandon = true;
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

        protected override void Act(float deltaTime)
        {
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
                objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
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
                        DebugConsole.LogError($"Unexpected target condition \"{TargetItemCondition}\" in local function GetConditionBasedProperty");
#endif
                        return 0.0f;
                    }
                }
            }
            else
            {
                if (moveItemObjective == null && !IsValidContainable(targetItem))
                {
                    IgnoreTargetItem();
                    Reset();
                    return;
                }
                TryAddSubObjective(ref moveItemObjective,
                    constructor: () => new AIObjectiveMoveItem(character, targetItem, objectiveManager, targetContainer: ItemContainer, priorityModifier: PriorityModifier)
                    {
                        AbandonGetItemDialogueCondition = () => IsValidContainable(targetItem),
                        AbandonGetItemDialogueIdentifier = abandonGetItemDialogueIdentifier,
                        Equip = true,
                        RemoveExistingWhenNecessary = true,
                        RemoveExistingPredicate = (i) => !ValidContainableItemIdentifiers.Contains(i.Prefab.Identifier) || AIObjectiveLoadItems.ItemMatchesTargetCondition(i, TargetItemCondition),
                        RemoveExistingMax = 1,
                        StoreRemovedItems = true,
                        AllowToFindDivingGear = objectiveManager.HasOrder<AIObjectiveLoadItems>()
                    },
                    onCompleted: () =>
                    {
                        IsCompleted = true;
                        RemoveSubObjective(ref moveItemObjective);
                    },
                    onAbandon: () =>
                    {
                        // Try again
                        IgnoreTargetItem();
                        Reset();
                    });
            }
        }

        /// <summary>
        /// Top-level containables plus the ones defined inside SubContainers.
        /// </summary>
        private static IEnumerable<RelatedItem> GetAllContainableItems(ItemContainer container)
        {
            if (container.ContainableItems != null)
            {
                foreach (var containable in container.ContainableItems)
                {
                    yield return containable;
                }
            }
            if (container.AllSubContainableItems != null)
            {
                foreach (var containable in container.AllSubContainableItems)
                {
                    yield return containable;
                }
            }
        }

        private bool IsValidContainable(Item item)
        {
            if (item == null) { return false; }
            if (item.Removed) { return false; }
            if (!ValidContainableItemIdentifiers.Contains(item.Prefab.Identifier)) { return false; }
            if (ignoredItems.Contains(item)) { return false; }
            if (character.IsOnPlayerTeam && item.Illegitimate) { return false; }
            if (item.GetRootInventoryOwner() is Character owner && owner != character) { return false; }
            Item parentItem = item.Container;
            while (parentItem != null)
            {
                if (parentItem.HasTag(Tags.DontTakeItems) || parentItem.HasTag(Tags.DontTakeItemsUnlessEmergency)) { return false; }
                parentItem = parentItem.Container;
            }
            if (!item.HasAccess(character)) { return false; }
            if (!character.HasItem(item) && !CanEquip(item, allowWearing: false)) { return false; }
            if (!ItemContainer.CanBeContained(item)) { return false; }
            if (AIObjectiveLoadItems.ItemMatchesTargetCondition(item, TargetItemCondition)) { return false; }
            // Ignore items that have had their condition increase recently (= being refilled)
            if (item.ConditionIncreasedRecently) { return false; }
            if (TargetItemCondition == AIObjectiveLoadItems.ItemCondition.Full)
            {
                // Ignore items inside their (condition-restricted) primary containers
                if (item.ParentInventory is ItemInventory itemInventory && item.IsContainerPreferred(itemInventory.Container, out bool _, out bool isSecondary, requireConditionRestriction: true) && !isSecondary) { return false; }
            }
            else if (TargetItemCondition == AIObjectiveLoadItems.ItemCondition.Empty)
            {
                //don't take tanks from diving gear when trying to replace empty tanks in some device - diving gear is (probably) more important to be filled!
                if (item.Container != null && item.Container.HasTag(Tags.DivingGear)) { return false; }
            }
            // Ignore items inside another valid container
            if (AIObjectiveLoadItems.IsValidTarget(item.Container, character, TargetContainerTags)) { return false; }
            return true;
        }

        protected override bool CheckObjectiveState() => IsCompleted;

        public override void Reset()
        {
            base.Reset();
            // Don't reset the target item when resetting the objective because it affects priority calculations
            moveItemObjective = null;
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