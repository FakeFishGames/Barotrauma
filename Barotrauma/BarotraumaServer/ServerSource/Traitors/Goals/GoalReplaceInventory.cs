using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalReplaceInventory : HumanoidGoal
        {
            private readonly HashSet<string> sabotageContainerIds = new HashSet<string>();
            private readonly HashSet<string> validReplacementIds = new HashSet<string>();

            private readonly float replaceAmount;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override IEnumerable<string> StatusTextKeys => base.StatusTextKeys.Concat(new string[] { "[percentage]" });
            public override IEnumerable<string> StatusTextValues(Traitor traitor) => base.StatusTextValues(traitor).Concat(new string[] { string.Format("{0:0}", replaceAmount * 100.0f) });

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                int totalAmount = 0, replacedAmount = 0;
                foreach (var item in Item.ItemList)
                {
                    if (item.Submarine == null || Traitors.All(traitor => item.Submarine.TeamID != traitor.Character.TeamID))
                    {
                        continue;
                    }
                    if (item.FindParentInventory(inventory => inventory.Owner is Character) != null)
                    {
                        continue;
                    }
                    if (sabotageContainerIds.Contains(item.prefab.Identifier))
                    {
                        ++totalAmount;
                        if (item.OwnInventory.AllItems.All(containedItem => !validReplacementIds.Contains(containedItem.Prefab.Identifier)))
                        {
                            continue;
                        }
                        ++replacedAmount;
                    }
                }
                isCompleted = replacedAmount >= (int)(replaceAmount * totalAmount + 0.5f);
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                if (sabotageContainerIds.Count <= 0 || validReplacementIds.Count <= 0)
                {
                    return false;
                }
                return true;
            }

            public GoalReplaceInventory(string[] containerIds, string[] replacementIds, float replaceAmount)
            {
                sabotageContainerIds.UnionWith(containerIds);
                validReplacementIds.UnionWith(replacementIds);
                this.replaceAmount = replaceAmount;
            }
        }
    }
}
