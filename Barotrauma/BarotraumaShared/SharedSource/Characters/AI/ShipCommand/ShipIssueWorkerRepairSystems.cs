using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ShipGlobalIssueRepairSystems : ShipGlobalIssue
    {
        readonly List<Item> itemsNeedingRepair = [];

        public ShipGlobalIssueRepairSystems(ShipCommandManager shipCommandManager) : base(shipCommandManager) { }

        public override void CalculateGlobalIssue()
        {
            itemsNeedingRepair.Clear();

            foreach (Item item in shipCommandManager.CommandedSubmarine.GetItems(true))
            {
                if (!AIObjectiveRepairItems.ViableForRepair(item, shipCommandManager.Commander, shipCommandManager.Commander.AIController as HumanAIController)) 
                { 
                    continue; 
                }
                if (AIObjectiveRepairItems.NearlyFullCondition(item)) { continue; }
                itemsNeedingRepair.Add(item);
                int i = 0;
                while (i < itemsNeedingRepair.Count)
                {
                    if (itemsNeedingRepair[i].ConditionPercentage > item.ConditionPercentage)
                    {
                        break;
                    }
                    i++;
                }
                itemsNeedingRepair.Insert(i, item);
            }

            GlobalImportance = 0.0f;
            if (itemsNeedingRepair.Any())
            {
                const int MaxItemsToConsider = 10;

                for (int i = 0; i < MaxItemsToConsider && i < itemsNeedingRepair.Count; i++)
                {
                    Item item = itemsNeedingRepair[i];

                    float itemPriority;
                    //highest prio to junction boxes and reactors, keeping the sub/outpost powerer is important
                    if (item.GetComponent<PowerTransfer>() != null ||
                        item.GetComponent<Reactor>() != null)
                    {
                        itemPriority = 50.0f;
                    }
                    //medium prio for loaders, so the sub/outpost can keep firing
                    else if (item.HasTag(Tags.TurretAmmoSource))
                    {
                        itemPriority = 30.0f;
                    }
                    //somewhat high prio to batteries and supercapacitors, needed by weapons and potentially other systems
                    else if (item.GetComponent<PowerContainer>() != null)
                    {
                        itemPriority = 25.0f;
                    }
                    //repairing doors between rooms isn't that urgent
                    //(and there seem to often be lots of broken doors during outpost or sub-vs-sub combat when piercing ammo is involved)
                    else if (item.GetComponent<Door>() is { LinkedGap.IsRoomToRoom: true })
                    {
                        itemPriority = 2.0f;
                    }
                    else
                    {
                        itemPriority = 10.0f;
                    }
                    GlobalImportance += MathHelper.Lerp(itemPriority, 0.0f, item.ConditionPercentage / 100.0f);
                }
            }
        }
    }

    class ShipIssueWorkerRepairSystems : ShipIssueWorkerGlobal // this class could be removed, but it might need special behavior later
    {
        public ShipIssueWorkerRepairSystems(ShipCommandManager shipCommandManager, Order order, ShipGlobalIssueRepairSystems shipGlobalIssueRepairSystems) : base(shipCommandManager, order, shipGlobalIssueRepairSystems) 
        {
        }
    }
}
