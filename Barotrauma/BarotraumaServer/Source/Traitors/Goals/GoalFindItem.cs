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
            private HashSet<Item> existingItems = new HashSet<Item>();
            private string targetNameText;
            private string targetContainerNameText;
            private string targetHullNameText;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[identifier]", "[target]", "[targethullname]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { targetNameText, targetContainerNameText, targetHullNameText });

            public override bool IsCompleted => target != null && target.ParentInventory == Traitor.Character.Inventory;
            public override bool CanBeCompleted {
                get
                {
                    if (!base.CanBeCompleted)
                    {
                        return false;
                    }
                    if (target == null)
                    {
                        return true;
                    }
                    if (target.Removed)
                    {
                        return false;
                    }
                    if (target.Submarine == null)
                    {
                        if (!(target.ParentInventory?.Owner is Character))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (target.Submarine.TeamID != Traitor.Character.TeamID)
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (target != null && target.ParentInventory == character.Inventory);

            protected ItemPrefab FindItemPrefab(string identifier)
            {
                return (ItemPrefab)MapEntityPrefab.List.Find(prefab => prefab is ItemPrefab && prefab.Identifier == identifier);
            }

            protected Item FindRandomContainer()
            {
                int itemsCount = Item.ItemList.Count;
                int startIndex = TraitorMission.Random(itemsCount);
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

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                targetPrefab = FindItemPrefab(identifier);
                if (targetPrefab == null)
                {
                    return false;
                }
                var targetPrefabTextId = targetPrefab.GetItemNameTextId();
                targetNameText = targetPrefabTextId != null ? TextManager.FormatServerMessage(targetPrefabTextId) : targetPrefab.Name;
                targetContainer = FindRandomContainer();
                if (targetContainer == null)
                {
                    return false;
                }
                var containerPrefabTextId = targetContainer.Prefab.GetItemNameTextId();
                targetContainerNameText = containerPrefabTextId != null ? TextManager.FormatServerMessage(containerPrefabTextId) : targetContainer.Prefab.Name;
                var targetHullTextId = targetContainer.CurrentHull != null ? targetContainer.CurrentHull.prefab.GetHullNameTextId() : null;
                targetHullNameText = targetHullTextId != null ? TextManager.FormatServerMessage(targetHullTextId) : targetContainer?.CurrentHull?.DisplayName ?? "";
                existingItems.Clear();
                foreach (var item in targetContainer.OwnInventory.Items)
                {
                    existingItems.Add(item);
                }
                Entity.Spawner.AddToSpawnQueue(targetPrefab, targetContainer.OwnInventory);
                target = null;
                return true;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (target != null)
                {
                    target = targetContainer.OwnInventory.Items.FirstOrDefault(item => item != null && item.Prefab.Identifier == identifier && !existingItems.Contains(item));
                    if (target != null)
                    {
                        existingItems.Clear();
                    }
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
