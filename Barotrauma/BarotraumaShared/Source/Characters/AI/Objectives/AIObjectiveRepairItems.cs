using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjective
    {
        public override string DebugTag => "repair items";

        private Dictionary<Item, AIObjectiveRepairItem> repairObjectives = new Dictionary<Item, AIObjectiveRepairItem>();
        private readonly List<Item> itemList = new List<Item>();

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character) : base(character, "")
        {
            itemList = character.Submarine.GetItems(true);
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            GetBrokenItems();
            if (repairObjectives.None()) { return 0; }
            else if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            // Don't use the itemlist, because it can be huge.
            float avg = repairObjectives.Average(ro => 100 - ro.Key.ConditionPercentage);
            return MathHelper.Lerp(0, 50, avg / 100);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

        protected override void Act(float deltaTime) => GetBrokenItems();

        private void GetBrokenItems()
        {
            SyncRemovedObjectives(repairObjectives, itemList);
            foreach (Item item in itemList)
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
