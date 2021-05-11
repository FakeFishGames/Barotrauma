using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CargoMission : Mission
    {
        private readonly XElement itemConfig;

        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<Item, UInt16> parentInventoryIDs = new Dictionary<Item, UInt16>();
        private readonly Dictionary<Item, byte> parentItemContainerIndices = new Dictionary<Item, byte>();

        private int requiredDeliveryAmount;

        private readonly List<(XElement element, ItemContainer container)> itemsToSpawn = new List<(XElement element, ItemContainer container)>();
        private int? rewardPerCrate;
        private int calculatedReward;
        private int maxItemCount;

        private Submarine sub;

        public CargoMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            this.sub = sub;
            itemConfig = prefab.ConfigElement.Element("Items");
            requiredDeliveryAmount = prefab.ConfigElement.GetAttributeInt("requireddeliveryamount", 0);
            DetermineCargo();
        }

        private void DetermineCargo()
        {
            if (this.sub == null || itemConfig == null)
            {
                calculatedReward = Prefab.Reward;
                return;
            }

            itemsToSpawn.Clear();
            List<(ItemContainer container, int freeSlots)> containers = sub.GetCargoContainers();
            containers.Sort((c1, c2) => { return c2.container.Capacity.CompareTo(c1.container.Capacity); });

            maxItemCount = 0;
            foreach (XElement subElement in itemConfig.Elements())
            {
                int maxCount = subElement.GetAttributeInt("maxcount", 10);
                maxItemCount += maxCount;
            }

            for (int i = 0; i < containers.Count; i++)
            {
                foreach (XElement subElement in itemConfig.Elements())
                {
                    int maxCount = subElement.GetAttributeInt("maxcount", 10);
                    if (itemsToSpawn.Count(it => it.element == subElement) >= maxCount) { continue; }
                    ItemPrefab itemPrefab = FindItemPrefab(subElement);
                    while (containers[i].freeSlots > 0 && containers[i].container.Inventory.CanBePut(itemPrefab))
                    {
                        containers[i] = (containers[i].container, containers[i].freeSlots - 1);
                        itemsToSpawn.Add((subElement, containers[i].container));
                        if (itemsToSpawn.Count(it => it.element == subElement) >= maxCount) { break; }
                    }
                }
            }

            if (!itemsToSpawn.Any())
            {
                itemsToSpawn.Add((itemConfig.Elements().First(), null));
            }

            calculatedReward = 0;
            foreach (var itemToSpawn in itemsToSpawn)
            {
                int price = itemToSpawn.element.GetAttributeInt("reward", Prefab.Reward / itemsToSpawn.Count);
                if (rewardPerCrate.HasValue)
                {
                    if (price != rewardPerCrate.Value) { rewardPerCrate = -1; }
                }
                else
                {
                    rewardPerCrate = price;
                }
                calculatedReward += price;
            }
            if (rewardPerCrate.HasValue && rewardPerCrate < 0) { rewardPerCrate = null; }

            string rewardText = $"‖color:gui.orange‖{string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", GetReward(sub))}‖end‖";
            if (descriptionWithoutReward != null) { description = descriptionWithoutReward.Replace("[reward]", rewardText); }
        }

        public override int GetReward(Submarine sub)
        {
            if (sub != this.sub)
            {
                this.sub = sub;
                DetermineCargo();
            }
            return calculatedReward;
        }

        private void InitItems()
        {
            this.sub = Submarine.MainSub;
            DetermineCargo();

            items.Clear();
            parentInventoryIDs.Clear();
            parentItemContainerIndices.Clear();

            if (itemConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize items for cargo mission (itemConfig == null)");
                return;
            }

            foreach (var (element, container) in itemsToSpawn)
            {
                LoadItemAsChild(element, container?.Item);
            }

            if (requiredDeliveryAmount == 0) { requiredDeliveryAmount = items.Count; }
            if (requiredDeliveryAmount > items.Count)
            {
                DebugConsole.AddWarning($"Error in mission \"{Prefab.Identifier}\". Required delivery amount is {requiredDeliveryAmount} but there's only {items.Count} items to deliver.");
                requiredDeliveryAmount = items.Count;
            }
        }

        private ItemPrefab FindItemPrefab(XElement element)
        {
            ItemPrefab itemPrefab;
            if (element.Attribute("name") != null)
            {
                DebugConsole.ThrowError("Error in cargo mission \"" + Name + "\" - use item identifiers instead of names to configure the items.");
                string itemName = element.GetAttributeString("name", "");
                itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Couldn't spawn item for cargo mission: item prefab \"" + itemName + "\" not found");
                }
            }
            else
            {
                string itemIdentifier = element.GetAttributeString("identifier", "");
                itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Couldn't spawn item for cargo mission: item prefab \"" + itemIdentifier + "\" not found");
                }
            }
            return itemPrefab;
        }


        private void LoadItemAsChild(XElement element, Item parent)
        {
            ItemPrefab itemPrefab = FindItemPrefab(element);

            WayPoint cargoSpawnPos = WayPoint.GetRandom(SpawnType.Cargo, null, Submarine.MainSub, useSyncedRand: true);
            if (cargoSpawnPos == null)
            {
                DebugConsole.ThrowError("Couldn't spawn items for cargo mission, cargo spawnpoint not found");
                return;
            }

            var cargoRoom = cargoSpawnPos.CurrentHull;
            if (cargoRoom == null)
            {
                DebugConsole.ThrowError("A waypoint marked as Cargo must be placed inside a room!");
                return;
            }

            Vector2 position = new Vector2(
                cargoSpawnPos.Position.X + Rand.Range(-20.0f, 20.0f, Rand.RandSync.Server),
                cargoRoom.Rect.Y - cargoRoom.Rect.Height + itemPrefab.Size.Y / 2);

            var item = new Item(itemPrefab, position, cargoRoom.Submarine)
            {
                SpawnedInOutpost = true,
                AllowStealing = false
            };
            item.FindHull();
            items.Add(item);

            if (parent != null && parent.GetComponent<ItemContainer>() != null) 
            {
                parentInventoryIDs.Add(item, parent.ID);
                parentItemContainerIndices.Add(item, (byte)parent.GetComponentIndex(parent.GetComponent<ItemContainer>()));
                parent.Combine(item, user: null);
            }
            
            foreach (XElement subElement in element.Elements())
            {
                int amount = subElement.GetAttributeInt("amount", 1);
                for (int i = 0; i < amount; i++)
                {
                    LoadItemAsChild(subElement, item);
                }                    
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
            items.Clear();
            parentInventoryIDs.Clear();

            if (!IsClient)
            {
                InitItems();
            }
        }

        public override void End()
        {
            if (Submarine.MainSub != null && Submarine.MainSub.AtEndExit)
            {
                int deliveredItemCount = items.Count(i => i.CurrentHull != null && !i.Removed && i.Condition > 0.0f);
                if (deliveredItemCount >= requiredDeliveryAmount)
                {
                    GiveReward();
                    completed = true;
                    if (Prefab.LocationTypeChangeOnCompleted != null)
                    {
                        ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                    }
                }
            }

            foreach (Item item in items)
            {
                if (!item.Removed) { item.Remove(); }
            }
            items.Clear();
            failed = !completed;
        }
    }
}
