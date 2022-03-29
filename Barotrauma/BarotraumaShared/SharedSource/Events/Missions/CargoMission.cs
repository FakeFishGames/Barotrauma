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
        private readonly ContentXElement itemConfig;

        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<Item, UInt16> parentInventoryIDs = new Dictionary<Item, UInt16>();
        private readonly Dictionary<Item, int> inventorySlotIndices = new Dictionary<Item, int>();
        private readonly Dictionary<Item, byte> parentItemContainerIndices = new Dictionary<Item, byte>();

        private float requiredDeliveryAmount;

        private readonly List<(ContentXElement element, ItemContainer container)> itemsToSpawn = new List<(ContentXElement element, ItemContainer container)>();
        private int? rewardPerCrate;
        private int calculatedReward;
        private int maxItemCount;

        private Submarine sub;
        
        private readonly List<CargoMission> previouslySelectedMissions = new List<CargoMission>();

        public override LocalizedString Description
        {
            get
            {
                if (Submarine.MainSub != sub)
                {
                    string rewardText = $"‖color:gui.orange‖{string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", GetReward(Submarine.MainSub))}‖end‖";
                    if (descriptionWithoutReward != null) { description = descriptionWithoutReward.Replace("[reward]", rewardText); }
                }
                return description;
            }
        }

        public CargoMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            this.sub = sub;
            itemConfig = prefab.ConfigElement.GetChildElement("Items");
            requiredDeliveryAmount = Math.Min(prefab.ConfigElement.GetAttributeFloat("requireddeliveryamount", 0.98f), 1.0f);
            //this can get called between rounds when the client receives a campaign save
            //don't attempt to determine cargo if the sub hasn't been fully loaded
            if (sub == null || sub.Loading || sub.Removed || Submarine.Unloading || !Submarine.Loaded.Contains(sub))
            {
                return;
            }
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

            previouslySelectedMissions.Clear();
            if (GameMain.GameSession?.StartLocation?.SelectedMissions != null)
            {
                bool isPriorMission = true;
                foreach (Mission mission in GameMain.GameSession.StartLocation.SelectedMissions)
                {
                    if (!(mission is CargoMission otherMission)) { continue; }
                    if (mission == this) { isPriorMission = false; }
                    previouslySelectedMissions.Add(otherMission);                    
                    if (!isPriorMission) { continue; }
                    foreach (var (element, container) in otherMission.itemsToSpawn)
                    {
                        for (int i = 0; i < containers.Count; i++)
                        {
                            if (containers[i].container == container)
                            {
                                containers[i] = (containers[i].container, containers[i].freeSlots - 1);
                                break;
                            }
                        }
                    }
                }
            }

            maxItemCount = 0;
            foreach (var subElement in itemConfig.Elements())
            {
                int maxCount = subElement.GetAttributeInt("maxcount", 10);
                maxItemCount += maxCount;
            }

            for (int i = 0; i < containers.Count; i++)
            {
                foreach (var subElement in itemConfig.Elements())
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
            foreach (var (element, container) in itemsToSpawn)
            {
                int price = element.GetAttributeInt("reward", Prefab.Reward / itemsToSpawn.Count);
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
            // If we are not at the location of the mission, skip the calculation of the reward
            if (GameMain.GameSession?.StartLocation != Locations[0])
            {
                return calculatedReward;
            }

            bool missionsChanged = false;
            if (GameMain.GameSession?.StartLocation?.SelectedMissions != null)
            {
                List<Mission> currentMissions = GameMain.GameSession.StartLocation.SelectedMissions.Where(m => m is CargoMission).ToList();
                if (currentMissions.Count != previouslySelectedMissions.Count)
                {
                    missionsChanged = true;
                }
                else
                {
                    for (int i = 0; i < previouslySelectedMissions.Count; i++)
                    {
                        if (previouslySelectedMissions[i] != currentMissions[i])
                        {
                            missionsChanged = true;
                            break;
                        }
                    }
                }
            }

            if (sub != this.sub || missionsChanged)
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
            inventorySlotIndices.Clear();

            if (itemConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize items for cargo mission (itemConfig == null)");
                return;
            }

            foreach (var (element, container) in itemsToSpawn)
            {
                LoadItemAsChild(element, container?.Item);
            }

            if (requiredDeliveryAmount <= 0.0f) { requiredDeliveryAmount = 1.0f; }
        }

        private void LoadItemAsChild(ContentXElement element, Item parent)
        {
            ItemPrefab itemPrefab = FindItemPrefab(element);

            Vector2? position = GetCargoSpawnPosition(itemPrefab, out Submarine cargoRoomSub);
            if (!position.HasValue) { return; }

            var item = new Item(itemPrefab, position.Value, cargoRoomSub)
            {
                SpawnedInCurrentOutpost = true,
                AllowStealing = false
            };
            item.FindHull();
            items.Add(item);

            if (parent != null && parent.GetComponent<ItemContainer>() != null) 
            {
                parentInventoryIDs.Add(item, parent.ID);
                parentItemContainerIndices.Add(item, (byte)parent.GetComponentIndex(parent.GetComponent<ItemContainer>()));
                parent.Combine(item, user: null);
                inventorySlotIndices.Add(item, item.ParentInventory?.FindIndex(item) ?? -1);
            }
            
            foreach (var subElement in element.Elements())
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
                if (deliveredItemCount / (float)items.Count >= requiredDeliveryAmount)
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
