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

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                foreach (var item in Item.ItemList)
                {
                    if (item == null || item.Prefab == null)
                    {
                        continue;
                    }
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
                        if (item.OwnInventory.Items.Length <= 0) {
                            isCompleted = false;
                            return;
                        }
                        foreach (var containedItem in item.OwnInventory.Items) {
                            if (!validReplacementIds.Contains(containedItem.ContainerIdentifier)) {
                                isCompleted = false;
                                return;
                            }
                        }
                    }
                }
                isCompleted = true;
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

            public GoalReplaceInventory(string[] containerIds, string[] replacementIds)
            {
                sabotageContainerIds.UnionWith(containerIds);
                validReplacementIds.UnionWith(replacementIds);
            }
        }
    }
}
