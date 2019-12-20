using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalFindItem : HumanoidGoal
        {
            private readonly TraitorMission.CharacterFilter filter;
            private readonly string identifier;
            private readonly bool preferNew;
            private readonly bool allowNew;
            private readonly bool allowExisting;
            private readonly HashSet<string> allowedContainerIdentifiers = new HashSet<string>();

            private ItemPrefab targetPrefab;
            private ItemPrefab containedPrefab;
            private Item targetContainer;
            private Item target;
            private HashSet<Item> existingItems = new HashSet<Item>();
            private string targetNameText;
            private string targetContainerNameText;
            private string targetHullNameText;
            private float percentage;
            private int spawnAmount = 1;

            private const string itemContainerId = "toolbox";

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[identifier]", "[target]", "[targethullname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { targetNameText ?? "", targetContainerNameText ?? "", targetHullNameText ?? "" });

            public override bool IsCompleted => target != null && Traitors.Any(traitor => traitor.Character.HasItem(target));
            public override bool CanBeCompleted(ICollection<Traitor> traitors)
            {
                if (!base.CanBeCompleted(traitors))
                {
                    return false;
                }
                if (target == null)
                {
                    var targetPrefabCandidate = FindItemPrefab(identifier);
                    return targetPrefabCandidate != null && FindTargetContainer(traitors, targetPrefabCandidate) != null;
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
                    if (Traitors.All(traitor => target.Submarine.TeamID != traitor.Character.TeamID))
                    {
                        return false;
                    }
                }
                return true;
            }

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || (target != null && target.FindParentInventory(inventory => inventory == character.Inventory) != null);

            protected ItemPrefab FindItemPrefab(string identifier)
            {
                return (ItemPrefab)MapEntityPrefab.List.FirstOrDefault(prefab => prefab is ItemPrefab && prefab.Identifier == identifier);
            }

            protected Item FindRandomContainer(ICollection<Traitor> traitors, ItemPrefab targetPrefabCandidate, bool includeNew, bool includeExisting)
            {
                List<Item> suitableItems = new List<Item>();
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null || traitors.All(traitor => item.Submarine.TeamID != traitor.Character.TeamID))
                    {
                        continue;
                    }
                    if (item.GetComponent<ItemContainer>() != null && allowedContainerIdentifiers.Contains(item.prefab.Identifier))
                    {
                        if ((includeNew && !item.OwnInventory.IsFull()) || (includeExisting && item.OwnInventory.FindItemByIdentifier(targetPrefabCandidate.Identifier) != null))
                        {
                            suitableItems.Add(item);
                        }
                    }
                }

                if (suitableItems.Count == 0) { return null; }
                return suitableItems[TraitorManager.RandomInt(suitableItems.Count)];
            }

            protected Item FindTargetContainer(ICollection<Traitor> traitors, ItemPrefab targetPrefabCandidate)
            {
                Item result = null;
                if (preferNew)
                {
                    result = FindRandomContainer(traitors, targetPrefabCandidate, true, false);
                }
                if (result == null)
                {
                    result = FindRandomContainer(traitors, targetPrefabCandidate, allowNew, allowExisting);
                }
                if (result == null)
                {
                    return null;
                }
                if (allowNew && !result.OwnInventory.IsFull())
                {
                    return result;
                }
                if (allowExisting && result.OwnInventory.FindItemByIdentifier(targetPrefabCandidate.Identifier) != null)
                {
                    return result;
                }
                return null;
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                if (targetPrefab != null)
                {
                    return true;
                }

                string targetPrefabTextId;

                if (percentage > 0f)
                {
                    spawnAmount = (int)Math.Floor(Character.CharacterList.FindAll(c => c.TeamID == traitor.Character.TeamID && c != traitor.Character && !c.IsDead && (filter == null || filter(c))).Count * percentage);
                }

                if (spawnAmount > 1 && allowNew)
                {
                    containedPrefab = FindItemPrefab(identifier);
                    targetPrefab = FindItemPrefab(itemContainerId);

                    if (containedPrefab == null || targetPrefab == null)
                    {
                        return false;
                    }

                    targetPrefabTextId = containedPrefab.GetItemNameTextId();
                }
                else
                {
                    spawnAmount = 1;
                    containedPrefab = null;
                    targetPrefab = FindItemPrefab(identifier);

                    if (targetPrefab == null)
                    {
                        return false;
                    }

                    targetPrefabTextId = targetPrefab.GetItemNameTextId();
                }

                targetNameText = targetPrefabTextId != null ? TextManager.FormatServerMessage(targetPrefabTextId) : targetPrefab.Name;
                targetContainer = FindTargetContainer(Traitors, targetPrefab);
                if (targetContainer == null)
                {
                    targetPrefab = null;
                    targetContainer = null;
                    return false;
                }
                var containerPrefabTextId = targetContainer.Prefab.GetItemNameTextId();
                targetContainerNameText = containerPrefabTextId != null ? TextManager.FormatServerMessage(containerPrefabTextId) : targetContainer.Prefab.Name;
                var targetHullTextId = targetContainer.CurrentHull?.prefab.GetHullNameTextId();
                targetHullNameText = targetHullTextId != null ? TextManager.FormatServerMessage(targetHullTextId) : targetContainer?.CurrentHull?.DisplayName ?? "";
                if (allowNew && !targetContainer.OwnInventory.IsFull())
                {
                    existingItems.Clear();
                    foreach (var item in targetContainer.OwnInventory.Items)
                    {
                        existingItems.Add(item);
                    }
                    Entity.Spawner.AddToSpawnQueue(targetPrefab, targetContainer.OwnInventory);
                    target = null;
                }
                else if (allowExisting)
                {
                    target = targetContainer.OwnInventory.FindItemByIdentifier(targetPrefab.Identifier);
                }
                else
                {
                    targetPrefab = null;
                    targetContainer = null;
                    return false;
                }
                return true;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (target == null)
                {
                    target = targetContainer.OwnInventory.Items.FirstOrDefault(item => item != null && item.Prefab.Identifier == (containedPrefab != null ? itemContainerId : identifier) && !existingItems.Contains(item));
                    if (target != null)
                    {
                        if (containedPrefab != null)
                        {
                            for (int i = 0; i < spawnAmount; i++)
                            {
                                Entity.Spawner.AddToSpawnQueue(containedPrefab, target.OwnInventory);
                            }
                        }
                        existingItems.Clear();
                    }
                }
            }

            public GoalFindItem(TraitorMission.CharacterFilter filter, string identifier, bool preferNew, bool allowNew, bool allowExisting, float percentage, params string[] allowedContainerIdentifiers)
            {
                this.filter = filter;
                this.identifier = identifier;
                this.preferNew = preferNew;
                this.allowNew = allowNew;
                this.allowExisting = allowExisting;
                this.percentage = percentage / 100f;
                this.allowedContainerIdentifiers.UnionWith(allowedContainerIdentifiers);
            }
        }
    }
}
