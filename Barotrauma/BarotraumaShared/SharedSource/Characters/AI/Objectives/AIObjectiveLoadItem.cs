using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class AIObjectiveLoadItem : AIObjective
    {
        public override string Identifier { get; set; } = "load item";
        public override bool IsLoop
        {
            get => true;
            set => throw new Exception("Trying to set the value for AIObjectiveLoadItem.IsLoop from: " + Environment.StackTrace.CleanupStackTrace());
        }

        private AIObjectiveLoadItems.ItemCondition TargetItemCondition { get; }
        private Item Container { get; }
        private ItemContainer ItemContainer { get; }
        private ImmutableArray<string> TargetContainerTags { get; }

        private int itemIndex = 0;
        private AIObjectiveDecontainItem decontainObjective;
        private readonly HashSet<Item> ignoredItems = new HashSet<Item>();
        private Item targetItem;
        private readonly string abandonGetItemDialogueIdentifier = "dialogcannotfindloadable";

        public AIObjectiveLoadItem(Item container, ImmutableArray<string> targetTags, AIObjectiveLoadItems.ItemCondition targetCondition, string option, Character character, AIObjectiveManager objectiveManager, float priorityModifier)
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
            if (!string.IsNullOrEmpty(option))
            {
                string optionSpecificDialogueIdentifier = $"{abandonGetItemDialogueIdentifier}.{option}";
                if (TextManager.ContainsTag(optionSpecificDialogueIdentifier))
                {
                    abandonGetItemDialogueIdentifier = optionSpecificDialogueIdentifier;
                }
            }
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
                if (character.FindItem(ref itemIndex, out Item item, identifiers: ItemContainer.ContainableItemIdentifiers, ignoreBroken: false, customPredicate: IsValidContainable, customPriorityFunction: GetConditionBasedPriority))
                {
                    if (item == null)
                    {
                        // No possible containables found, abandon the objective
                        Abandon = true;
                    }
                    targetItem = item;
                }
                // Prefer items closer to full condition when target condition is Empty, and vice versa
                float GetConditionBasedPriority(Item item)
                {
                    try
                    {
                        return TargetItemCondition switch
                        {
                            AIObjectiveLoadItems.ItemCondition.Full => MathUtils.InverseLerp(100.0f, 0.0f, item.ConditionPercentage),
                            AIObjectiveLoadItems.ItemCondition.Empty => MathUtils.InverseLerp(0.0f, 100.0f, item.ConditionPercentage),
                            _ => throw new NotImplementedException()
                        };
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
                        AbandonGetItemDialogueIdentifier = abandonGetItemDialogueIdentifier,
                        Equip = true,
                        RemoveExistingWhenNecessary = true,
                        RemoveExistingPredicate = (i) => AIObjectiveLoadItems.ItemMatchesTargetCondition(i, TargetItemCondition),
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
            if (ignoredItems.Contains(item)) { return false; }
            if ((item.SpawnedInCurrentOutpost && !item.AllowStealing) == character.IsOnPlayerTeam) { return false; }
            var rootInventoryOwner = item.GetRootInventoryOwner();
            if (rootInventoryOwner is Character owner && owner != character) { return false; }
            if (rootInventoryOwner is Item parentItem)
            {
                if (parentItem.HasTag("donttakeitems")) { return false; }
                if (!(parentItem.GetComponent<ItemContainer>()?.HasAccess(character) ?? true)) { return false; }
            }
            if (item.IsThisOrAnyContainerIgnoredByAI(character)) { return false; }
            if (!character.HasItem(item) && !CanEquip(item)) { return false; }
            if (!ItemContainer.HasAccess(character)) { return false; }
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