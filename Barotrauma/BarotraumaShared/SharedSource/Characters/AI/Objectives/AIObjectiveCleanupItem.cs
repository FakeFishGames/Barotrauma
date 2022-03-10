using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveCleanupItem : AIObjective
    {
        public override string Identifier { get; set; } = "cleanup item";
        public override bool KeepDivingGearOn => true;
        public override bool AllowAutomaticItemUnequipping => false;

        public readonly Item item;
        public bool IsPriority { get; set; }

        private readonly List<Item> ignoredContainers = new List<Item>();
        private AIObjectiveDecontainItem decontainObjective;
        private int itemIndex = 0;

        public AIObjectiveCleanupItem(Item item, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.item = item;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
                return Priority;
            }
            else
            {
                float distanceFactor = 0.9f;
                if (!IsPriority && item.CurrentHull != character.CurrentHull)
                {
                    float yDist = Math.Abs(character.WorldPosition.Y - item.WorldPosition.Y);
                    yDist = yDist > 100 ? yDist * 5 : 0;
                    float dist = Math.Abs(character.WorldPosition.X - item.WorldPosition.X) + yDist;
                    distanceFactor = MathHelper.Lerp(0.9f, 0, MathUtils.InverseLerp(0, 5000, dist));
                }
                bool isSelected = character.HasItem(item);
                float selectedBonus = isSelected ? 100 - MaxDevotion : 0;
                float devotion = (CumulatedDevotion + selectedBonus) / 100;
                float reduction = IsPriority ? 1 : isSelected ? 2 : 3;
                float max = AIObjectiveManager.LowestOrderPriority - reduction;
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (distanceFactor * PriorityModifier), 0, 1));
                if (decontainObjective == null)
                {
                    // Halve the priority until there's a decontain objective (a valid container was found).
                    Priority /= 2;
                }
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            if (item.IgnoreByAI(character))
            {
                Abandon = true;
                return;
            }
            if (item.ParentInventory != null)
            {
                if (item.Container != null && !AIObjectiveCleanupItems.IsValidContainer(item.Container, character, allowUnloading: objectiveManager.HasOrder<AIObjectiveCleanupItems>()))
                {
                    // Target was picked up or moved by someone.
                    Abandon = true;
                    return;
                }
            }
            // Only continue when the get item sub objectives have been completed.
            if (subObjectives.Any()) { return; }
            if (HumanAIController.FindSuitableContainer(character, item, ignoredContainers, ref itemIndex, out Item suitableContainer))
            {
                itemIndex = 0;
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
                            s == InvSlotType.HealthInterface);

                    TryAddSubObjective(ref decontainObjective, () => new AIObjectiveDecontainItem(character, item, objectiveManager, targetContainer: suitableContainer.GetComponent<ItemContainer>())
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
                        if (decontainObjective != null && decontainObjective.ContainObjective != null && decontainObjective.ContainObjective.CanBeCompleted)
                        {
                            ignoredContainers.Add(suitableContainer);
                        }
                        else
                        {
                            Abandon = true;
                        }
                    });
                }
                else
                {
                    Abandon = true;
                }
            }
            else
            {
                objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
            }
        }

        protected override bool CheckObjectiveSpecific() => IsCompleted;

        public override void Reset()
        {
            base.Reset();
            ignoredContainers.Clear();
            itemIndex = 0;
            decontainObjective = null;
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
