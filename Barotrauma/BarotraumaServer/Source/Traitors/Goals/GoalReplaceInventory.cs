using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalReplaceInventory : Goal
        {
            private readonly HashSet<string> sabotageContainerIds = new HashSet<string>();
            private readonly HashSet<string> validReplacementIds = new HashSet<string>();

            private readonly float replaceAmount;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override IEnumerable<string> StatusTextKeys => base.StatusTextKeys.Concat(new string[] { "[percentage]" });
            public override IEnumerable<string> StatusTextValues => base.StatusTextValues.Concat(new string[] { string.Format("{0:0}", replaceAmount * 100.0f) });

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                int totalAmount = 0, replacedAmount = 0;
                foreach (var item in Item.ItemList)
                {
                    if (item.Submarine == null || item.Submarine.TeamID != Traitor.Character.TeamID)
                    {
                        continue;
                    }
                    if (item.ParentInventory?.Owner is Character)
                    {
                        continue;
                    }
                    if (sabotageContainerIds.Contains(item.prefab.Identifier))
                    {
                        ++totalAmount;
                        if (item.OwnInventory.Items.Length <= 0 || item.OwnInventory.Items.All(containedItem => containedItem != null && !validReplacementIds.Contains(containedItem.Prefab.Identifier)))
                        {
                            continue;
                        }
                        ++replacedAmount;
                    }
                }
                isCompleted = replacedAmount >= (int)(replaceAmount * totalAmount + 0.5f);
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
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
