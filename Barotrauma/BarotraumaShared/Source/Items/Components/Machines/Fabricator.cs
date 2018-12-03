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
        public class RequiredItem
        {
            public readonly ItemPrefab ItemPrefab;
            public int Amount;
            public readonly float MinCondition;
            public readonly bool UseCondition;

            public RequiredItem(ItemPrefab itemPrefab, int amount, float minCondition, bool useCondition)
            {
                ItemPrefab = itemPrefab;
                Amount = amount;
                MinCondition = minCondition;
                UseCondition = useCondition;
            }
        }

        public readonly ItemPrefab TargetItem;
        
        public readonly string DisplayName;
        
        public readonly List<RequiredItem> RequiredItems;

        public readonly float RequiredTime;

        public readonly float OutCondition; //Percentage-based from 0 to 1

        public readonly List<Skill> RequiredSkills;

        public FabricableItem(XElement element)
        {
            if (element.Attribute("name") != null)
            {
                string name = element.Attribute("name").Value;
                DebugConsole.ThrowError("Error in fabricable item config (" + name + ") - use item identifiers instead of names");
                TargetItem = MapEntityPrefab.Find(name) as ItemPrefab;
                if (TargetItem == null)
                {
                    DebugConsole.ThrowError("Error in fabricable item config - item prefab \"" + name + "\" not found.");
                    return;
                }
            }
            else
            {
                string identifier = element.GetAttributeString("identifier", "");
                TargetItem = MapEntityPrefab.Find(null, identifier) as ItemPrefab;
                if (TargetItem == null)
                {
                    DebugConsole.ThrowError("Error in fabricable item config - item prefab \"" + identifier + "\" not found.");
                    return;
                }
            }

            string displayName = element.GetAttributeString("displayname", "");
            DisplayName = string.IsNullOrEmpty(displayName) ? TargetItem.Name : TextManager.Get(displayName);

            RequiredSkills = new List<Skill>();
            RequiredTime = element.GetAttributeFloat("requiredtime", 1.0f);
            OutCondition = element.GetAttributeFloat("outcondition", 1.0f);
            RequiredItems = new List<RequiredItem>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requiredskill":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + TargetItem.Name + "! Use skill identifiers instead of names.");
                            continue;
                        }

                        RequiredSkills.Add(new Skill(
                            subElement.GetAttributeString("identifier", ""), 
                            subElement.GetAttributeInt("level", 0)));
                        break;
                    case "item":
                    case "requireditem":
                        string requiredItemIdentifier = subElement.GetAttributeString("identifier", "");
                        if (string.IsNullOrWhiteSpace(requiredItemIdentifier))
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + TargetItem.Name + "! One of the required items has no identifier.");
                            continue;
                        }

                        float minCondition = subElement.GetAttributeFloat("mincondition", 1.0f);
                        //Substract mincondition from required item's condition or delete it regardless?
                        bool useCondition = subElement.GetAttributeBool("usecondition", true);
                        int count = subElement.GetAttributeInt("count", 1);


                        ItemPrefab requiredItem = MapEntityPrefab.Find(null, requiredItemIdentifier.Trim()) as ItemPrefab;
                        if (requiredItem == null)
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + TargetItem.Name + "! Required item \"" + requiredItemIdentifier + "\" not found.");
                            continue;
                        }

                        var existing = RequiredItems.Find(r => r.ItemPrefab == requiredItem);
                        if (existing == null)
                        {
                            RequiredItems.Add(new RequiredItem(requiredItem, count, minCondition, useCondition));
                        }
                        else
                        {

                            RequiredItems.Remove(existing);
                            RequiredItems.Add(new RequiredItem(requiredItem, existing.Amount + count, minCondition, useCondition));
                        }

                        break;
                }
            }

        }
    }

    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        public const float SkillIncreaseMultiplier = 0.5f;

        private List<FabricableItem> fabricableItems;

        private FabricableItem fabricatedItem;
        private float timeUntilReady;

        //used for checking if contained items have changed 
        //(in which case we need to recheck which items can be fabricated)
        private Item[] prevContainedItems;

        private Character user;
        
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
            if (itemList.SelectedComponent != null)
            {
                SelectItem(itemList.SelectedComponent, itemList.SelectedComponent.UserData);                
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
            foreach (GUIComponent child in itemList.Content.Children)
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

#if SERVER
            if (user != null)
            {
                GameServer.Log(user.LogName + " started fabricating " + selectedItem.DisplayName + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif

#if CLIENT
            itemList.Enabled = false;
            activateButton.Text = "Cancel";
#endif

            fabricatedItem = selectedItem;
            IsActive = true;
            this.user = user;

            timeUntilReady = fabricatedItem.RequiredTime;

            var containers = item.GetComponents<ItemContainer>().ToList();
            containers[0].Inventory.Locked = true;
            containers[1].Inventory.Locked = true;

            currPowerConsumption = powerConsumption;
            currPowerConsumption *= MathHelper.Lerp(2.0f, 1.0f, item.Condition / 100.0f);
        }

        private void CancelFabricating(Character user = null)
        {
#if SERVER
            if (fabricatedItem != null && user != null)
            {
                GameServer.Log(user.LogName + " cancelled the fabrication of " + fabricatedItem.DisplayName + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif

            IsActive = false;
            fabricatedItem = null;
            this.user = null;

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

            var containers = item.GetComponents<ItemContainer>().ToList();
            containers[0].Inventory.Locked = false;
            containers[1].Inventory.Locked = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (fabricatedItem == null)
            {
                CancelFabricating();
                return;
            }

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

            var containers = item.GetComponents<ItemContainer>().ToList();
            if (containers.Count < 2)
            {
                DebugConsole.ThrowError("Error while fabricating a new item: fabricators must have two ItemContainer components");
                return;
            }

            foreach (FabricableItem.RequiredItem ingredient in fabricatedItem.RequiredItems)
            {
                for (int i = 0; i < ingredient.Amount; i++)
                {
                    var requiredItem = containers[0].Inventory.Items.FirstOrDefault(it => it != null && it.Prefab == ingredient.ItemPrefab && it.Condition >= ingredient.ItemPrefab.Health * ingredient.MinCondition);
                    if (requiredItem == null) continue;
                    
                    //Item4 = use condition bool
                    if (ingredient.UseCondition && requiredItem.Condition - ingredient.ItemPrefab.Health * ingredient.MinCondition > 0.0f) //Leave it behind with reduced condition if it has enough to stay above 0
                    {
                        requiredItem.Condition -= ingredient.ItemPrefab.Health * ingredient.MinCondition;
                        continue;
                    }
                    Entity.Spawner.AddToRemoveQueue(requiredItem);
                    containers[0].Inventory.RemoveItem(requiredItem);
                }
            }

            if (containers[1].Inventory.Items.All(i => i != null))
            {
                Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, item.Position, item.Submarine, fabricatedItem.TargetItem.Health * fabricatedItem.OutCondition);
            }
            else
            {
                Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, containers[1].Inventory, fabricatedItem.TargetItem.Health * fabricatedItem.OutCondition);
            }

            bool isNotClient = true;
#if CLIENT
            isNotClient = GameMain.Client == null;
#endif

            if (isNotClient && user != null)
            {
                foreach (Skill skill in fabricatedItem.RequiredSkills)
                {
                    user.Info.IncreaseSkillLevel(skill.Identifier, skill.Level / 100.0f * SkillIncreaseMultiplier, user.WorldPosition + Vector2.UnitY * 150.0f);
                }
            }

            CancelFabricating(null);
        }

        private bool CanBeFabricated(FabricableItem fabricableItem, Character user)
        {
            if (fabricableItem == null) return false;

            if (user != null && 
                fabricableItem.RequiredSkills.Any(skill => user.GetSkillLevel(skill.Identifier) < skill.Level))
            {
                return false;
            }
            ItemContainer container = item.GetComponent<ItemContainer>();
            foreach (FabricableItem.RequiredItem ip in fabricableItem.RequiredItems)
            {
                if (Array.FindAll(container.Inventory.Items, it => it != null && it.Prefab == ip.ItemPrefab && it.Condition >= ip.ItemPrefab.Health * ip.MinCondition).Length < ip.Amount) return false;
            }

            return true;
        }
    }
}
