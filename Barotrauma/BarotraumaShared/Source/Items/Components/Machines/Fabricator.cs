using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{   

    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        public const float SkillIncreaseMultiplier = 0.5f;

        private readonly List<FabricationRecipe> fabricationRecipes = new List<FabricationRecipe>();

        private FabricationRecipe fabricatedItem;
        private float timeUntilReady;
        private float requiredTime;

        private bool hasPower;

        private Character user;

        private ItemContainer inputContainer, outputContainer;

        public ItemContainer InputContainer
        {
            get { return inputContainer; }
        }

        public ItemContainer OutputContainer
        {
            get { return outputContainer; }
        }

        private float progressState;

        public Fabricator(Item item, XElement element)
            : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "fabricableitem")
                {
                    DebugConsole.ThrowError("Error in item " + item.Name + "! Fabrication recipes should be defined in the craftable item's xml, not in the fabricator.");
                    break;
                }            
            }

            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                foreach (FabricationRecipe recipe in itemPrefab.FabricationRecipes)
                {
                    if (recipe.SuitableFabricatorIdentifiers.Length > 0)
                    {
                        if (!recipe.SuitableFabricatorIdentifiers.Any(i => item.prefab.Identifier == i || item.HasTag(i)))
                        {
                            continue;
                        }
                    }
                    fabricationRecipes.Add(recipe);
                }
            }

            InitProjSpecific();
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            var containers = item.GetComponents<ItemContainer>().ToList();
            if (containers.Count < 2)
            {
                DebugConsole.ThrowError("Error in item \"" + item.Name + "\": Fabricators must have two ItemContainer components!");
                return;
            }

            inputContainer = containers[0];
            outputContainer = containers[1];
                        
            foreach (var recipe in fabricationRecipes)
            {
                int ingredientCount = recipe.RequiredItems.Sum(it => it.Amount);
                if (ingredientCount > inputContainer.Capacity)
                {
                    DebugConsole.ThrowError("Error in item \"" + item.Name + "\": There's not enough room in the input inventory for the ingredients of \"" + recipe.TargetItem.Name + "\"!");
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

        public void RemoveFabricationRecipes(List<string> allowedIdentifiers)
        {
            for (int i = 0; i < fabricationRecipes.Count; i++)
            {
                if (!allowedIdentifiers.Contains(fabricationRecipes[i].TargetItem.Identifier))
                {
                    fabricationRecipes.RemoveAt(i);
                    i--;
                }
            }

            CreateRecipes();
        }

        partial void CreateRecipes();

        private void StartFabricating(FabricationRecipe selectedItem, Character user)
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

            IsActive = true;
            this.user = user;
            fabricatedItem = selectedItem;
            MoveIngredientsToInputContainer(selectedItem);
            
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
            if (fabricatedItem != null)
            {
                if (user != null) 
                {
                    GameServer.Log(user.LogName + " cancelled the fabrication of " + fabricatedItem.DisplayName + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
                }
                item.CreateServerEvent(this);
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

            hasPower = Voltage >= MinVoltage;
            if (!hasPower) { return; }
            
            var repairable = item.GetComponent<Repairable>();
            if (repairable != null)
            {
                repairable.LastActiveTime = (float)Timing.TotalTime + 10.0f;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerConsumption <= 0) { Voltage = 1.0f; }

            timeUntilReady -= deltaTime * Voltage;

            if (timeUntilReady > 0.0f) { return; }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                var availableIngredients = GetAvailableIngredients();
                foreach (FabricationRecipe.RequiredItem ingredient in fabricatedItem.RequiredItems)
                {
                    for (int i = 0; i < ingredient.Amount; i++)
                    {
                        var availableItem = availableIngredients.FirstOrDefault(it => it != null && it.Prefab == ingredient.ItemPrefab && it.Condition >= ingredient.ItemPrefab.Health * ingredient.MinCondition);
                        if (availableItem == null) { continue; }
                                            
                        //Item4 = use condition bool
                        if (ingredient.UseCondition && availableItem.Condition - ingredient.ItemPrefab.Health * ingredient.MinCondition > 0.0f) //Leave it behind with reduced condition if it has enough to stay above 0
                        {
                            availableItem.Condition -= ingredient.ItemPrefab.Health * ingredient.MinCondition;
                            continue;
                        }
                        availableIngredients.Remove(availableItem);
                        Entity.Spawner.AddToRemoveQueue(availableItem);
                        inputContainer.Inventory.RemoveItem(availableItem);
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
            
                if (user != null && !user.Removed)
                {
                    foreach (Skill skill in fabricatedItem.RequiredSkills)
                    {
                        user.Info.IncreaseSkillLevel(skill.Identifier, skill.Level / 100.0f * SkillIncreaseMultiplier, user.WorldPosition + Vector2.UnitY * 150.0f);
                    }
                }

                CancelFabricating(null);
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
        }

        private bool CanBeFabricated(FabricationRecipe fabricableItem)
        {
            if (fabricableItem == null) { return false; }
            List<Item> availableIngredients = GetAvailableIngredients();
            return CanBeFabricated(fabricableItem, availableIngredients);
        }

        private bool CanBeFabricated(FabricationRecipe fabricableItem, IEnumerable<Item> availableIngredients)
        {
            if (fabricableItem == null) { return false; }            
            foreach (FabricationRecipe.RequiredItem requiredItem in fabricableItem.RequiredItems)
            {
                if (availableIngredients.Count(it => IsItemValidIngredient(it, requiredItem)) < requiredItem.Amount)
                {
                    return false;
                }
            }
            return true;
        }

        private float GetRequiredTime(FabricationRecipe fabricableItem, Character user)
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
#if CLIENT
            if (Character.Controlled?.Inventory != null)
            {
                availableIngredients.AddRange(Character.Controlled.Inventory.Items.Distinct().Where(it => it != null));
            }
#else
            if (user?.Inventory != null)
            {
                availableIngredients.AddRange(user.Inventory.Items.Distinct().Where(it => it != null));
            }
#endif

            return availableIngredients;
        }

        /// <summary>
        /// Move the items required for fabrication into the input container.
        /// The method assumes that all the required ingredients are available either in the input container or linked containers.
        /// </summary>
        private void MoveIngredientsToInputContainer(FabricationRecipe targetItem)
        {
            //required ingredients that are already present in the input container
            List<Item> usedItems = new List<Item>();

            bool isClient = GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

            var availableIngredients = GetAvailableIngredients();
            foreach (var requiredItem in targetItem.RequiredItems)
            {
                for (int i = 0; i < requiredItem.Amount; i++)
                {
                    var matchingItem = availableIngredients.Find(it => !usedItems.Contains(it) && IsItemValidIngredient(it, requiredItem));
                    if (matchingItem == null) { continue; }

                    availableIngredients.Remove(matchingItem);
                    
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
                            unneededItem?.Drop(null, createNetworkEvent: !isClient);
                        }
                        inputContainer.Inventory.TryPutItem(matchingItem, user: null, createNetworkEvent: !isClient);
                    }                    
                }
            }
        }

        private bool IsItemValidIngredient(Item item, FabricationRecipe.RequiredItem requiredItem)
        {
            return 
                item != null && 
                item.prefab == requiredItem.ItemPrefab && 
                item.Condition / item.Prefab.Health >= requiredItem.MinCondition;
        }
    }
}
