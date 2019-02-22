using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjective
    {
        public override string DebugTag => "repair items";

        private Dictionary<Item, AIObjectiveRepairItem> repairObjectives = new Dictionary<Item, AIObjectiveRepairItem>();

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character) : base(character, "") { }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            GetBrokenItems();
            if (subObjectives.Any() && objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            // Don't use the itemlist, because it can be huge.
            float avg = repairObjectives.Average(ro => 100 - ro.Key.ConditionPercentage);
            return MathHelper.Lerp(0, 50, avg / 100);
        }

        public override bool IsCompleted() => false;

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

        protected override void Act(float deltaTime) => GetBrokenItems();

        private void GetBrokenItems()
        {
            SyncRemovedObjectives(repairObjectives, Item.ItemList);
            foreach (Item item in Item.ItemList)
            {
                if (!item.IsFullCondition)
                {
                    foreach (Repairable repairable in item.Repairables)
                    {
                        if (item.Condition > repairable.ShowRepairUIThreshold) { continue; }
                        if (RequireAdequateSkills && !repairable.HasRequiredSkills(character)) { continue; }
                        if (!repairObjectives.TryGetValue(item, out AIObjectiveRepairItem objective))
                        {
                            objective = new AIObjectiveRepairItem(character, item);
                            repairObjectives.Add(item, objective);
                            AddSubObjective(objective);
                        }
                        break;
                    }
                }
            }
        }
    }
}
