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

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (target != null && target.ParentInventory == character.Inventory);

            protected ItemPrefab FindItemPrefab(string identifier)
            {
                return (ItemPrefab)MapEntityPrefab.List.Find(prefab => prefab is ItemPrefab && prefab.Identifier == identifier);
            }

            protected Item FindRandomContainer()
            {
                int itemsCount = Item.ItemList.Count;
                int startIndex = Rand.Int(itemsCount);
                for (int i = 0; i < itemsCount; ++i)
                {
                    var item = Item.ItemList[(i + startIndex) % itemsCount];
                    if (item.Submarine == null || item.Submarine.TeamID != Traitor.Character.TeamID)
                    {
                        continue;
                    }
                    if (item.GetComponent<ItemContainer>() != null && !item.OwnInventory.IsFull() && allowedContainerIdentifiers.Contains(item.prefab.Identifier))
                    {
                        return item;
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
                targetPrefab = FindItemPrefab(identifier);
                if (targetPrefab == null)
                {
                    return false;
                }
                targetContainer = FindRandomContainer();
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
