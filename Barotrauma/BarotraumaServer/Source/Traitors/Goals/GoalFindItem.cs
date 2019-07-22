using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalFindItem : Goal
        {
            private readonly string identifier;
            private readonly HashSet<string> allowedContainerIdentifiers = new HashSet<string>();

            private ItemPrefab targetPrefab;
            private Item targetContainer;
            private Item target;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[identifier]", "[target]", "[targethullname]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { targetPrefab.Name, targetContainer.Prefab.Name, targetContainer.CurrentHull.DisplayName });

            public override bool IsCompleted => target != null && target.ParentInventory == Traitor.Character.Inventory;

            protected ItemPrefab FindRandomItemPrefab(string identifier)
            {
                var prefabsCount = MapEntityPrefab.List.Count;
                var startIndex = Rand.Int(prefabsCount);
                for(int i = 0; i < prefabsCount; ++i) {
                    var prefab = MapEntityPrefab.List[(startIndex + i) % prefabsCount];
                    if (prefab is ItemPrefab && prefab.Identifier == identifier)
                    {
                        return (ItemPrefab)prefab;
                    }
                }
                return null;
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                targetPrefab = FindRandomItemPrefab(identifier);
                if (targetPrefab == null)
                {
                    return false;
                }
                targetContainer = Item.ItemList.Find(item => item.GetComponent<ItemContainer>() != null && !item.OwnInventory.IsFull() && allowedContainerIdentifiers.Contains(item.prefab.Identifier));
                if (targetContainer == null)
                {
                    return false;
                }
                Entity.Spawner.AddToSpawnQueue(targetPrefab, targetContainer.OwnInventory);
                target = null;
                return true;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (target == null)
                {
                    target = targetContainer.OwnInventory.FindItemByIdentifier(identifier);
                }
            }

            public GoalFindItem(string identifier, params string[] allowedContainerIdentifiers)
            {
                this.identifier = identifier;
                this.allowedContainerIdentifiers.UnionWith(allowedContainerIdentifiers);
            }
        }
    }
}
