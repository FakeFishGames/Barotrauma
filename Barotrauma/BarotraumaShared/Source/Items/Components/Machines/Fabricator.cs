using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class FabricableItem
    {
        public readonly ItemPrefab TargetItem;

        public readonly List<Tuple<ItemPrefab, int>> RequiredItems;

        public readonly float RequiredTime;

        public readonly List<Skill> RequiredSkills;

        public FabricableItem(XElement element)
        {
            string name = element.GetAttributeString("name", "");

            TargetItem = MapEntityPrefab.Find(name) as ItemPrefab;
            
            if (TargetItem == null)
            {
                return;
            }

            RequiredSkills = new List<Skill>();
            RequiredTime = element.GetAttributeFloat("requiredtime", 1.0f);
            RequiredItems = new List<Tuple<ItemPrefab, int>>();
            
            string[] requiredItemNames = element.GetAttributeString("requireditems", "").Split(',');
            foreach (string requiredItemName in requiredItemNames)
            {
                if (string.IsNullOrWhiteSpace(requiredItemName)) continue;

                ItemPrefab requiredItem = MapEntityPrefab.Find(requiredItemName.Trim()) as ItemPrefab;
                if (requiredItem == null)
                {
                    DebugConsole.ThrowError("Error in fabricable item " + name + "! Required item \"" + requiredItemName + "\" not found.");
                    continue;
                }

                var existing = RequiredItems.Find(r => r.Item1 == requiredItem);
                if (existing == null)
                {
                    RequiredItems.Add(new Tuple<ItemPrefab, int>(requiredItem, 1));
                }
                else
                {
                    RequiredItems.Remove(existing);
                    RequiredItems.Add(new Tuple<ItemPrefab, int>(requiredItem, existing.Item2 + 1));
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requiredskill":
                        RequiredSkills.Add(new Skill(
                            subElement.GetAttributeString("name", ""), 
                            subElement.GetAttributeInt("level", 0)));
                        break;
                }
            }

        }
    }

    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private List<FabricableItem> fabricableItems;

        private FabricableItem fabricatedItem;
        private float timeUntilReady;

        //used for checking if contained items have changed 
        //(in which case we need to recheck which items can be fabricated)
        private Item[] prevContainedItems;
        
        public Fabricator(Item item, XElement element) 
            : base(item, element)
        {
            fabricableItems = new List<FabricableItem>();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "fabricableitem") continue;

                FabricableItem fabricableItem = new FabricableItem(subElement);
                if (fabricableItem.TargetItem == null)
                {
                    DebugConsole.ThrowError("Error in item " + item.Name + "! Fabricable item \"" + subElement.GetAttributeString("name", "") + "\" not found.");
                }
                else
                {
                    fabricableItems.Add(fabricableItem);
                }             
            }

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override bool Select(Character character)
        {
            CheckFabricableItems(character);
#if CLIENT
            if (itemList.Selected != null)
            {
                SelectItem(itemList.Selected, itemList.Selected.UserData);                
            }
#endif


            return base.Select(character);
        }

        public override bool Pick(Character picker)
        {
            return (picker != null);
        }

        /// <summary>
        /// check which of the items can be fabricated by the character
        /// and update the text colors of the item list accordingly
        /// </summary>
        private void CheckFabricableItems(Character character)
        {
#if CLIENT
            foreach (GUIComponent child in itemList.children)
            {
                var itemPrefab = child.UserData as FabricableItem;
                if (itemPrefab == null) continue;

                bool canBeFabricated = CanBeFabricated(itemPrefab, character);


                child.GetChild<GUITextBlock>().TextColor = Color.White * (canBeFabricated ? 1.0f : 0.5f);
                child.GetChild<GUIImage>().Color = itemPrefab.TargetItem.SpriteColor * (canBeFabricated ? 1.0f : 0.5f);

            }
#endif

            var itemContainer = item.GetComponent<ItemContainer>();
            prevContainedItems = new Item[itemContainer.Inventory.Items.Length];
            itemContainer.Inventory.Items.CopyTo(prevContainedItems, 0);
        }

        private void StartFabricating(FabricableItem selectedItem, Character user = null)
        {
            if (selectedItem == null) return;

            if (user != null)
            {
                GameServer.Log(user.Name + " started fabricating " + selectedItem.TargetItem.Name + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }

#if CLIENT
            itemList.Enabled = false;

            activateButton.Text = "Cancel";
#endif

            fabricatedItem = selectedItem;
            IsActive = true;

            timeUntilReady = fabricatedItem.RequiredTime;

            var containers = item.GetComponents<ItemContainer>();
            containers[0].Inventory.Locked = true;
            containers[1].Inventory.Locked = true;

            currPowerConsumption = powerConsumption;
        }

        private void CancelFabricating(Character user = null)
        {
            if (fabricatedItem != null && user != null)
            {
                GameServer.Log(user.Name + " cancelled the fabrication of " + fabricatedItem.TargetItem.Name + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }

            IsActive = false;
            fabricatedItem = null;

            currPowerConsumption = 0.0f;

#if CLIENT
            itemList.Enabled = true;
            if (activateButton != null)
            {
                activateButton.Text = "Create";
            }
            if (progressBar != null) progressBar.BarSize = 0.0f;
#endif

            timeUntilReady = 0.0f;

            var containers = item.GetComponents<ItemContainer>();
            containers[0].Inventory.Locked = false;
            containers[1].Inventory.Locked = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
#if CLIENT
            if (progressBar != null)
            {
                progressBar.BarSize = fabricatedItem == null ? 0.0f : (fabricatedItem.RequiredTime - timeUntilReady) / fabricatedItem.RequiredTime;
            }
#endif

            if (voltage < minVoltage) return;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerConsumption == 0) voltage = 1.0f;

            timeUntilReady -= deltaTime*voltage;

            voltage -= deltaTime * 10.0f;

            if (timeUntilReady > 0.0f) return;

            var containers = item.GetComponents<ItemContainer>();
            if (containers.Count < 2)
            {
                DebugConsole.ThrowError("Error while fabricating a new item: fabricators must have two ItemContainer components");
                return;
            }

            foreach (Tuple<ItemPrefab, int> ip in fabricatedItem.RequiredItems)
            {
                for (int i = 0; i < ip.Item2; i++)
                {
                    var requiredItem = containers[0].Inventory.Items.FirstOrDefault(it => it != null && it.Prefab == ip.Item1);
                    if (requiredItem == null) continue;

                    Entity.Spawner.AddToRemoveQueue(requiredItem);
                    containers[0].Inventory.RemoveItem(requiredItem);
                }
            }

            if (containers[1].Inventory.Items.All(i => i != null))
            {
                Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, item.Position, item.Submarine);
            }
            else
            {
                Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, containers[1].Inventory);
            }

            CancelFabricating(null);
        }

        private bool CanBeFabricated(FabricableItem fabricableItem, Character user)
        {
            if (fabricableItem == null) return false;

            if (user != null && 
                fabricableItem.RequiredSkills.Any(skill => user.GetSkillLevel(skill.Name) < skill.Level))
            {
                return false;
            }

            ItemContainer container = item.GetComponent<ItemContainer>();
            foreach (Tuple<ItemPrefab, int> ip in fabricableItem.RequiredItems)
            {
                if (Array.FindAll(container.Inventory.Items, it => it != null && it.Prefab == ip.Item1).Length < ip.Item2) return false;
            }

            return true;
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            int itemIndex = msg.ReadRangedInteger(-1, fabricableItems.Count - 1);

            item.CreateServerEvent(this);

            if (!item.CanClientAccess(c)) return;

            if (itemIndex == -1)
            {
                CancelFabricating(c.Character);
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricableItems.IndexOf(fabricatedItem) == itemIndex) return;
                if (itemIndex < 0 || itemIndex >= fabricableItems.Count) return;

#if CLIENT
                SelectItem(null, fabricableItems[itemIndex]);
#endif
                StartFabricating(fabricableItems[itemIndex], c.Character);
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            int itemIndex = fabricatedItem == null ? -1 : fabricableItems.IndexOf(fabricatedItem);
            msg.WriteRangedInteger(-1, fabricableItems.Count - 1, itemIndex);
        }

    }
}
