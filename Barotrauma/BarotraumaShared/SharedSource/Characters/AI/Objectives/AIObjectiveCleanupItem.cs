using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class AIObjectiveCleanupItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "cleanup item".ToIdentifier();
        public override bool KeepDivingGearOn => true;
        public override bool AllowAutomaticItemUnequipping => false;
        protected override bool RequireBothHandsAvailable => true;

        public readonly Item item;
        public bool IsPriority { get; set; }

        public override bool AllowMultipleInstances => true;

        public override bool IsDuplicate<T>(T otherObjective) => otherObjective is AIObjectiveCleanupItem otherCleanup && otherCleanup.item == item;

        /// <summary>
        /// Optional preferred container where the bot will try to put the item if possible.
        /// </summary>
        public ItemContainer PreferredContainer;

        private readonly List<Item> ignoredContainers = new List<Item>();
        private AIObjectiveMoveItem moveItemObjective;
        private int itemIndex = 0;

        /// <summary>
        /// Allows <see cref="moveItemObjective"/> to be interrupted if this objective gets abandoned (e.g. due to the item no longer being eligible for cleanup)
        /// </summary>
        protected override bool ConcurrentObjectives => true;

        public AIObjectiveCleanupItem(Item item, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.item = item;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                HandleDisallowed();
                return Priority;
            }
            else
            {
                float distanceFactor = 0.9f;
                if (!IsPriority && item.CurrentHull != character.CurrentHull)
                {
                    distanceFactor = GetDistanceFactor(item.WorldPosition, verticalDistanceMultiplier: 5, maxDistance: 5000,  
                        factorAtMinDistance: 0.9f, factorAtMaxDistance: 0);
                }
                bool isSelected = character.HasItem(item);
                float selectedBonus = isSelected ? 100 - MaxDevotion : 0;
                float devotion = (CumulatedDevotion + selectedBonus) / 100;
                float reduction = IsPriority ? 1 : isSelected ? 2 : 3;
                float max = AIObjectiveManager.LowestOrderPriority - reduction;
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (distanceFactor * PriorityModifier), 0, 1));
                if (moveItemObjective == null)
                {
                    // Halve the priority until there's a moveItemObjective (a valid container was found).
                    Priority /= 2;
                }
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            if (subObjectives.Any()) { return; }

            Item suitableContainer = null;
            if (PreferredContainer != null &&
                !ignoredContainers.Contains(PreferredContainer.Item) &&
                PreferredContainer.Item.HasAccess(character) &&
                PreferredContainer.Inventory.CanBePut(item))
            {
                suitableContainer = PreferredContainer.Item;
            }
            else
            {
                //FindSuitableContainer returns true when the search is finished.
                //If the suitable container is still null, we couldn't find anything and need to abandon.
                if (HumanAIController.FindSuitableContainer(character, item, ignoredContainers, ref itemIndex, out suitableContainer) && 
                    suitableContainer == null)
                {
                    Abandon = true;
                    return;                    
                }
            }


            if (suitableContainer != null)
            {
                bool equip = item.GetComponent<Holdable>() != null ||
                    item.AllowedSlots.Any(s => s != InvSlotType.Any) &&
                    item.AllowedSlots.None(s =>
                        s == InvSlotType.Card ||
                        s == InvSlotType.Head ||
                        s == InvSlotType.Headset ||
                        s == InvSlotType.InnerClothes ||
                        s == InvSlotType.OuterClothes ||
                        CharacterInventory.HealthInterfaceSlots.HasFlag(s));

                TryAddSubObjective(ref moveItemObjective, () => new AIObjectiveMoveItem(character, item, objectiveManager, targetContainer: suitableContainer.GetComponent<ItemContainer>())
                {
                    Equip = equip,
                    TakeWholeStack = true,
                    DropIfFails = true
                }, 
                onCompleted: () =>
                {
                    if (equip)
                    {
                        HumanAIController.ReequipUnequipped();
                    }
                    IsCompleted = true;
                }, 
                onAbandon: () =>
                {
                    if (equip)
                    {
                        HumanAIController.ReequipUnequipped();
                    }
                    if (moveItemObjective is { ContainObjective.CanBeCompleted: true })
                    {
                        ignoredContainers.Add(suitableContainer);
                    }
                    else
                    {
                        Abandon = true;
                    }
                });
            }
            
            objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
        }

        protected override bool CheckObjectiveState()
        {
            if (item.IgnoreByAI(character) || Item.DeconstructItems.Contains(item))
            {
                Abandon = true;
            }
            else if (item.ParentInventory != null && item.GetRootInventoryOwner() != character)
            {
                if (!objectiveManager.HasOrder<AIObjectiveCleanupItems>())
                {
                    // Don't allow taking items from containers in the idle state.
                    Abandon = true;
                }
                else if (item.Container != null && !AIObjectiveCleanupItems.IsValidContainer(item.Container, character))
                {
                    // Target was picked up or moved by someone.
                    Abandon = true;
                }
            }
            return !Abandon && IsCompleted;
        }

        public override void Reset()
        {
            base.Reset();
            ignoredContainers.Clear();
            itemIndex = 0;
            moveItemObjective = null;
        }

        public void DropTarget()
        {
            if (item != null && character.HasItem(item))
            {
                item.Drop(character);
            }
        }
    }
}
