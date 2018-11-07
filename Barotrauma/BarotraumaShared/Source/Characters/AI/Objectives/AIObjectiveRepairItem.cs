using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

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
            //the worse the condition of the item, the higher the priority to repair
            float priority = 100.0f - item.Condition;
            foreach (Repairable repairable in item.Repairables)
            {
                if (item.Condition > repairable.ShowRepairUIThreshold) continue;
                //preference over items this character is good at fixing
                priority *= Math.Max(repairable.DegreeOfSuccess(character), 0.1f);
            }

            //prefer nearby items
            priority /= Math.Max(Vector2.DistanceSquared(character.WorldPosition, item.WorldPosition), 1.0f);

            return priority;
        }

        public override bool IsCompleted()
        {
            foreach (Repairable repairable in item.GetComponents<Repairable>())
            {
                if (item.Condition < repairable.ShowRepairUIThreshold) return false;
            }
            
            character?.Speak(TextManager.Get("DialogItemRepaired").Replace("[itemname]", item.Name), null, 0.0f, "itemrepaired", 10.0f);
            return true;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveRepairItem repairObjective && repairObjective.item == item;
        }

        protected override void Act(float deltaTime)
        {
            foreach (Repairable repairable in item.Repairables)
            {
                if (item.Condition > repairable.ShowRepairUIThreshold) continue;
                
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
                    if (item.Condition > repairable.ShowRepairUIThreshold) continue;
                    if (character.SelectedConstruction != item) item.TryInteract(character, true, true);
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
