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
        private float requiredTime;
        
        private Character user;

        private ItemContainer inputContainer, outputContainer;

        private float progressState;

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

        public override void OnItemLoaded()
        {
            var containers = item.GetComponents<ItemContainer>().ToList();
            if (containers.Count < 2)
            {
                DebugConsole.ThrowError("Error in item \"" + item.Name + "\": Fabricators must have two ItemContainer components!");
                return;
            }

            inputContainer = containers[0];
            outputContainer = containers[1];

            foreach (FabricableItem fabricableItem in fabricableItems)
            {
                int ingredientCount = fabricableItem.RequiredItems.Sum(it => it.Amount);
                if (ingredientCount > inputContainer.Capacity)
                {
                    DebugConsole.ThrowError("Error in item \"" + item.Name + "\": There's not enough room in the input inventory for the ingredients of \"" + fabricableItem.TargetItem.Name + "\"!");
                }
            }

            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific();


        partial void InitProjSpecific();

        public override bool Select(Character character)
        {
            SelectProjSpecific(character);
            return base.Select(character);
        }

        partial void SelectProjSpecific(Character character);

        public override bool Pick(Character picker)
        {
            return (picker != null);
        }
        
        private void StartFabricating(FabricableItem selectedItem, Character user)
        {
            if (selectedItem == null) return;
            if (!outputContainer.Inventory.IsEmpty()) return;

#if SERVER
            if (user != null)
            {
                GameServer.Log(user.LogName + " started fabricating " + selectedItem.DisplayName + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif

#if CLIENT
            itemList.Enabled = false;
            activateButton.Text = TextManager.Get("FabricatorCancel");
#endif

            MoveIngredientsToInputContainer(selectedItem);

            fabricatedItem = selectedItem;
            IsActive = true;
            this.user = user;
            
            requiredTime = GetRequiredTime(fabricatedItem, user);
            timeUntilReady = requiredTime;
            
            inputContainer.Inventory.Locked = true;
            outputContainer.Inventory.Locked = true;

            currPowerConsumption = powerConsumption;
            currPowerConsumption *= MathHelper.Lerp(2.0f, 1.0f, item.Condition / item.MaxCondition);
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
                activateButton.Text = TextManager.Get("FabricatorCreate");
            }
#endif
            progressState = 0.0f;

            timeUntilReady = 0.0f;

            inputContainer.Inventory.Locked = false;
            outputContainer.Inventory.Locked = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (fabricatedItem == null || !CanBeFabricated(fabricatedItem))
            {
                CancelFabricating();
                return;
            }

            progressState = fabricatedItem == null ? 0.0f : (requiredTime - timeUntilReady) / requiredTime;

            if (voltage < minVoltage) { return; }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerConsumption <= 0) { voltage = 1.0f; }

            timeUntilReady -= deltaTime * voltage;
            voltage -= deltaTime * 10.0f;

            if (timeUntilReady > 0.0f) { return; }

            var availableIngredients = GetAvailableIngredients();
            foreach (FabricableItem.RequiredItem ingredient in fabricatedItem.RequiredItems)
            {
                for (int i = 0; i < ingredient.Amount; i++)
                {
                    var requiredItem = inputContainer.Inventory.Items.FirstOrDefault(it => it != null && it.Prefab == ingredient.ItemPrefab && it.Condition >= ingredient.ItemPrefab.Health * ingredient.MinCondition);
                    if (requiredItem == null) continue;
                    
                    //Item4 = use condition bool
                    if (ingredient.UseCondition && requiredItem.Condition - ingredient.ItemPrefab.Health * ingredient.MinCondition > 0.0f) //Leave it behind with reduced condition if it has enough to stay above 0
                    {
                        requiredItem.Condition -= ingredient.ItemPrefab.Health * ingredient.MinCondition;
                        continue;
                    }
                    Entity.Spawner.AddToRemoveQueue(requiredItem);
                    inputContainer.Inventory.RemoveItem(requiredItem);
                }
            }

            if (outputContainer.Inventory.Items.All(i => i != null))
            {
                Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, item.Position, item.Submarine, fabricatedItem.TargetItem.Health * fabricatedItem.OutCondition);
            }
            else
            {
                Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, outputContainer.Inventory, fabricatedItem.TargetItem.Health * fabricatedItem.OutCondition);
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

        private bool CanBeFabricated(FabricableItem fabricableItem)
        {
            if (fabricableItem == null) { return false; }
            List<Item> availableIngredients = GetAvailableIngredients();
            return CanBeFabricated(fabricableItem, availableIngredients);
        }

        private bool CanBeFabricated(FabricableItem fabricableItem, IEnumerable<Item> availableIngredients)
        {
            if (fabricableItem == null) { return false; }            
            foreach (FabricableItem.RequiredItem requiredItem in fabricableItem.RequiredItems)
            {
                if (availableIngredients.Count(it => IsItemValidIngredient(it, requiredItem)) < requiredItem.Amount)
                {
                    return false;
                }
            }
            return true;
        }

        private float GetRequiredTime(FabricableItem fabricableItem, Character user)
        {
            float degreeOfSuccess = DegreeOfSuccess(user, fabricableItem.RequiredSkills);

            float t = degreeOfSuccess < 0.5f ? degreeOfSuccess * degreeOfSuccess : degreeOfSuccess * 2;

            //fabricating takes 100 times longer if degree of success is close to 0
            //characters with a higher skill than required can fabricate up to 100% faster
            return fabricableItem.RequiredTime / MathHelper.Clamp(t, 0.01f, 2.0f);
        }

        /// <summary>
        /// Get a list of all items available in the input container and linked containers
        /// </summary>
        /// <returns></returns>
        private List<Item> GetAvailableIngredients()
        {
            List<Item> availableIngredients = new List<Item>();
            availableIngredients.AddRange(inputContainer.Inventory.Items.Where(it => it != null));
            foreach (MapEntity linkedTo in item.linkedTo)
            {
                if (linkedTo is Item linkedItem)
                {
                    var itemContainer = linkedItem.GetComponent<ItemContainer>();
                    if (itemContainer == null) { continue; }

                    var deconstructor = linkedItem.GetComponent<Deconstructor>();
                    if (deconstructor != null)
                    {
                        itemContainer = deconstructor.OutputContainer;
                    }

                    availableIngredients.AddRange(itemContainer.Inventory.Items.Where(it => it != null));
                }
            }
            return availableIngredients;
        }

        /// <summary>
        /// Move the items required for fabrication into the input container.
        /// The method assumes that all the required ingredients are available either in the input container or linked containers.
        /// </summary>
        private void MoveIngredientsToInputContainer(FabricableItem targetItem)
        {
            //required ingredients that are already present in the input container
            List<Item> usedItems = new List<Item>();

            var availableIngredients = GetAvailableIngredients();
            foreach (var requiredItem in targetItem.RequiredItems)
            {
                for (int i = 0; i < requiredItem.Amount; i++)
                {
                    var matchingItem = availableIngredients.Find(it => !usedItems.Contains(it) && IsItemValidIngredient(it, requiredItem));
                    if (matchingItem == null) { continue; }
                    
                    if (matchingItem.ParentInventory == inputContainer.Inventory)
                    {
                        //already in input container, all good
                        usedItems.Add(matchingItem);
                    }
                    else //in another inventory, we need to move the item
                    {
                        if (inputContainer.Inventory.Items.All(it => it != null))
                        {
                            var unneededItem = inputContainer.Inventory.Items.FirstOrDefault(it => !usedItems.Contains(it));
                            unneededItem?.Drop();
                        }
                        inputContainer.Inventory.TryPutItem(matchingItem, user: null, createNetworkEvent: true);
                    }                    
                }
            }
        }

        private bool IsItemValidIngredient(Item item, FabricableItem.RequiredItem requiredItem)
        {
            return 
                item != null && 
                item.prefab == requiredItem.ItemPrefab && 
                item.Condition / item.Prefab.Health >= requiredItem.MinCondition;
        }
    }
}
