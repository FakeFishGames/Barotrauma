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
        public override string DebugTag => "cleanup item";
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

        public override float GetPriority()
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
                float max = MathHelper.Min(AIObjectiveManager.OrderPriority - reduction, 90);
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (distanceFactor * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            // Only continue when the get item sub objectives have been completed.
            if (subObjectives.Any()) { return; }
            if (HumanAIController.FindSuitableContainer(character, item, ignoredContainers, ref itemIndex, out Item suitableContainer))
            {
                itemIndex = 0;
                if (suitableContainer != null)
                {
                    bool equip = item.HasTag(AIObjectiveFindDivingGear.HEAVY_DIVING_GEAR) || (
                            item.GetComponent<Wearable>() == null &&
                            item.AllowedSlots.None(s =>
                                s == InvSlotType.Card ||
                                s == InvSlotType.Head ||
                                s == InvSlotType.Headset ||
                                s == InvSlotType.InnerClothes ||
                                s == InvSlotType.OuterClothes));
                    TryAddSubObjective(ref decontainObjective, () => new AIObjectiveDecontainItem(character, item, objectiveManager, targetContainer: suitableContainer.GetComponent<ItemContainer>())
                    {
                        Equip = equip,
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

        protected override bool Check() => IsCompleted;

        public override void Reset()
        {
            base.Reset();
            ignoredContainers.Clear();
            itemIndex = 0;
            decontainObjective = null;
        }
    }
}
