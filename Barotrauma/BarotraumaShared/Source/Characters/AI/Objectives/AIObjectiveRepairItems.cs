using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjective
    {
        public override string DebugTag => "repair items";

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character)
            : base(character, "")
        {
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            GetBrokenItems();
            if (subObjectives.Count > 0 && objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }
                
        public override bool IsCompleted()
        {
            return false;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;
        }

        protected override void Act(float deltaTime)
        {
            GetBrokenItems();
        }

        private void GetBrokenItems()
        {
            foreach (Item item in Item.ItemList)
            {
                // Ignore items that are in full condition
                if (item.IsFullCondition) { continue; }
                foreach (Repairable repairable in item.Repairables)
                {
                    // Ignore ones that are already fixed
                    if (item.Condition > repairable.ShowRepairUIThreshold) { continue; }
                    // Ignore items that are already being repaired by someone else
                    if (item.Repairables.Any(r => r.CurrentFixer != null)) { continue; }

                    if (RequireAdequateSkills)
                    {
                        if (!repairable.HasRequiredSkills(character)) { continue; }
                    }

                    // TODO: don't create duplicates, because this is called so frequently
                    AddSubObjective(new AIObjectiveRepairItem(character, item));
                    break;
                }
            }
        }
    }
}
