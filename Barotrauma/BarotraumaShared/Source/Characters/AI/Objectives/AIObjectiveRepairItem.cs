using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItem : AIObjective
    {
        public override string DebugTag => "repair item";

        private Item item;

        public AIObjectiveRepairItem(Character character, Item item)
            : base(character, "")
        {
            this.item = item;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (item.Repairables.None()) { return 0; }
            base.GetPriority(objectiveManager);
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
            float dist = Math.Abs(character.WorldPosition.X - item.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - item.WorldPosition.Y) * 2.0f;
            float distanceFactor = MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 10000, dist));
            float damagePriority = MathHelper.Lerp(1, 0, item.Condition / item.MaxCondition);
            float successFactor = MathHelper.Lerp(0, 1, item.Repairables.Average(r => r.DegreeOfSuccess(character)));
            float isSelected = character.SelectedConstruction == item ? 50 : 1;
            return MathHelper.Clamp((priority + isSelected) * damagePriority * distanceFactor * successFactor, 0, 100);
        }

        public override bool CanBeCompleted
        {
            get
            {
                // If the current condition is not more than the previous condition, we can't repair the target. It probably is deteriorating at a greater speed than we can repair it.
                bool canRepair = base.CanBeCompleted && item.Condition > previousCondition;
                if (!canRepair)
                {
                    character?.Speak(TextManager.Get("DialogCannotRepair").Replace("[itemname]", item.Name), null, 0.0f, "cannotrepair", 10.0f);
                }
                return canRepair;
            }
        }

        public override bool IsCompleted()
        {
            bool isCompleted = item.IsFullCondition;
            if (isCompleted)
            {
                character?.Speak(TextManager.Get("DialogItemRepaired").Replace("[itemname]", item.Name), null, 0.0f, "itemrepaired", 10.0f);
            }
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveRepairItem repairObjective && repairObjective.item == item;
        }

        private float previousCondition = -1;
        protected override void Act(float deltaTime)
        {
            foreach (Repairable repairable in item.Repairables)
            {
                //make sure we have all the items required to fix the target item
                foreach (var kvp in repairable.requiredItems)
                {
                    foreach (RelatedItem requiredItem in kvp.Value)
                    {
                        if (!character.Inventory.Items.Any(it => it != null && requiredItem.MatchesItem(it)))
                        {
                            AddSubObjective(new AIObjectiveGetItem(character, requiredItem.Identifiers, true));
                            return;
                        }
                    }
                }
            }

            if (character.CanInteractWith(item))
            {
                foreach (Repairable repairable in item.Repairables)
                {
                    if (character.SelectedConstruction != item)
                    {
                        previousCondition = item.Condition;
                        item.TryInteract(character, true, true);
                    }
                    repairable.CurrentFixer = character;
                    break;
                }
            }
            else
            {
                AddSubObjective(new AIObjectiveGoTo(item, character));
            }
        }
    }
}
