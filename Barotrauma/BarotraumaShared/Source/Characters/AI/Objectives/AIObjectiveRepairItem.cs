using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveRepairItem : AIObjective
    {
        private Item item;

        public AIObjectiveRepairItem(Character character, Item item)
            : base(character, "")
        {
            this.item = item;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            return 100.0f / Math.Max(Vector2.DistanceSquared(character.WorldPosition, item.WorldPosition), 1.0f);
        }

        public override bool IsCompleted()
        {
            if (item.Condition > 0.0f) return true;
            foreach (FixRequirement fixRequirement in item.FixRequirements)
            {
                if (fixRequirement.Fixed || !fixRequirement.HasRequiredSkills(character)) continue;
                return false;
            }

            return true;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveRepairItem repairObjective = otherObjective as AIObjectiveRepairItem;
            return repairObjective != null && repairObjective.item == item;
        }

        protected override void Act(float deltaTime)
        {
            foreach (FixRequirement fixRequirement in item.FixRequirements)
            {
                if (fixRequirement.Fixed || !fixRequirement.HasRequiredSkills(character)) continue;
                
                //make sure we have all the items required to fix the target item
                foreach (string requiredItem in fixRequirement.RequiredItems)
                {
                    if (character.Inventory.FindItem(requiredItem) == null)
                    {
                        AddSubObjective(new AIObjectiveGetItem(character, requiredItem));
                        return;
                    }
                }
            }

            if (character.CanInteractWith(item))
            {
                foreach (FixRequirement fixRequirement in item.FixRequirements)
                {
                    if (fixRequirement.CanBeFixed(character))
                    {
                        fixRequirement.Fixed = true;
                        if (item.FixRequirements.All(fr => fr.Fixed))
                        {
                            character.Speak(TextManager.Get("DialogItemRepaired").Replace("[itemname]", item.Name), null, 0.0f, "itemrepaired", 10.0f);
                            item.Condition = 100.0f;
                        }
                    }
                }
            }
            else
            {
                AddSubObjective(new AIObjectiveGoTo(item, character));
            }
        }
    }
}
