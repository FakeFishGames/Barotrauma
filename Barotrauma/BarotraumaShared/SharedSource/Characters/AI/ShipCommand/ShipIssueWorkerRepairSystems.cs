using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ShipGlobalIssueRepairSystems : ShipGlobalIssue
    {
        readonly List<Item> itemsNeedingRepair = new List<Item>();

        public ShipGlobalIssueRepairSystems(ShipCommandManager shipCommandManager) : base(shipCommandManager) { }

        public override void CalculateGlobalIssue()
        {
            itemsNeedingRepair.Clear();

            foreach (Item item in shipCommandManager.CommandedSubmarine.GetItems(true))
            {
                if (!AIObjectiveRepairItems.ViableForRepair(item, shipCommandManager.character, shipCommandManager.character.AIController as HumanAIController)) { continue; }
                if (AIObjectiveRepairItems.NearlyFullCondition(item)) { continue; }
                itemsNeedingRepair.Add(item);
                // merged this logic with AIObjectiveRepairItems 
            }

            if (itemsNeedingRepair.Any())
            {
                itemsNeedingRepair.Sort((x, y) => y.ConditionPercentage.CompareTo(x.ConditionPercentage));
                float modifiedPercentage = itemsNeedingRepair.TakeLast(3).Average(x => x.ConditionPercentage) * 0.6f + itemsNeedingRepair.TakeLast(10).Average(x => x.ConditionPercentage) * 0.4f;
                // calculate a modified percentage with the most damaged items, with 60% the weight given to the top 3 damaged and the remaining given to top 10
                GlobalImportance = 100 - modifiedPercentage;
            }
            // this system works reasonably well, though it could give extra importance to repairing critical items like reactors and junction boxes
        }
    }

    class ShipIssueWorkerRepairSystems : ShipIssueWorkerGlobal // this class could be removed, but it might need special behavior later
    {
        public ShipIssueWorkerRepairSystems(ShipCommandManager shipCommandManager, Order order, ShipGlobalIssueRepairSystems shipGlobalIssueRepairSystems) : base(shipCommandManager, order, shipGlobalIssueRepairSystems) 
        {
        }
    }
}
