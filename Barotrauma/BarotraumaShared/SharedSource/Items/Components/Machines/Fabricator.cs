using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{   
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private readonly List<FabricationRecipe> fabricationRecipes = new List<FabricationRecipe>();

        private FabricationRecipe fabricatedItem;
        private float timeUntilReady;
        private float requiredTime;

        private string savedFabricatedItem;
        private float savedTimeUntilReady, savedRequiredTime;

        private bool hasPower;

        private Character user;

        private ItemContainer inputContainer, outputContainer;
        
        [Serialize(1.0f, true)]
        public float FabricationSpeed { get; set; }
        
        [Serialize(1.0f, true)]
        public float SkillRequirementMultiplier { get; set; }

        private enum FabricatorState
        {
            Active = 1,
            Paused = 2,
            Stopped = 0
        }

        private FabricatorState state;
        private FabricatorState State
        {
            get
            {
                return state;
            }
            set
            {
                if (state == value) { return; }
                state = value;
#if SERVER
                serverEventId++;
                item.CreateServerEvent(this);
#endif
            }
        }

        public ItemContainer InputContainer
        {
            get { return inputContainer; }
        }

        public ItemContainer OutputContainer
        {
            get { return outputContainer; }
        }

        public override bool RecreateGUIOnResolutionChange => true;

        private float progressState;

        public Fabricator(Item item, XElement element)
            : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("fabricableitem", StringComparison.OrdinalIgnoreCase))
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

            state = FabricatorState.Stopped;

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
            return picker != null;
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

        private void StartFabricating(FabricationRecipe selectedItem, Character user, bool addToServerLog = true)
        {
            if (selectedItem == null) { return; }
            if (!outputContainer.Inventory.CanBePut(selectedItem.TargetItem)) { return; }

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
            item.GetComponent<Repairable>()?.AdjustPowerConsumption(ref currPowerConsumption);

            if (GameMain.NetworkMember?.IsServer ?? true)
            {
                State = FabricatorState.Active;
            }
#if SERVER
            if (user != null && addToServerLog)
            {
                GameServer.Log(GameServer.CharacterLogName(user) + " started fabricating " + selectedItem.DisplayName + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif
        }

        private void CancelFabricating(Character user = null)
        {
            IsActive = false;
            this.user = null;
            currPowerConsumption = 0.0f;

            progressState = 0.0f;
            timeUntilReady = 0.0f;
            inputContainer.Inventory.Locked = false;
            outputContainer.Inventory.Locked = false;

            if (GameMain.NetworkMember?.IsServer ?? true)
            {
                State = FabricatorState.Stopped;
            }

            if (fabricatedItem == null) { return; }
            fabricatedItem = null;

#if CLIENT
            itemList.Enabled = true;
            if (activateButton != null)
            {
                activateButton.Text = TextManager.Get("FabricatorCreate");
            }
#endif
#if SERVER
            if (user != null)
            {
                GameServer.Log(GameServer.CharacterLogName(user) + " cancelled the fabrication of " + fabricatedItem.DisplayName + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (fabricatedItem == null || !CanBeFabricated(fabricatedItem))
            {
                CancelFabricating();
                return;
            }

            progressState = fabricatedItem == null ? 0.0f : (requiredTime - timeUntilReady) / requiredTime;

            if (GameMain.NetworkMember?.IsClient ?? false)
            {
                hasPower = State != FabricatorState.Paused;
                if (!hasPower)
                {
                    return;
                }
            }
            else
            {
                hasPower = Voltage >= MinVoltage;

                if (!hasPower)
                {
                    State = FabricatorState.Paused;
                    return;
                }
                State = FabricatorState.Active;
            }
            
            var repairable = item.GetComponent<Repairable>();
            if (repairable != null)
            {
                repairable.LastActiveTime = (float)Timing.TotalTime + 10.0f;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerConsumption <= 0) { Voltage = 1.0f; }

            timeUntilReady -= deltaTime * Math.Min(Voltage, 1.0f);

            if (timeUntilReady > 0.0f) { return; }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                var availableIngredients = GetAvailableIngredients();
                foreach (FabricationRecipe.RequiredItem ingredient in fabricatedItem.RequiredItems)
                {
                    for (int i = 0; i < ingredient.Amount; i++)
                    {
                        var availableItem = availableIngredients.FirstOrDefault(it => it != null && ingredient.ItemPrefabs.Contains(it.Prefab) && it.ConditionPercentage >= ingredient.MinCondition * 100.0f);
                        if (availableItem == null) { continue; }
                                            
                        //Item4 = use condition bool
                        if (ingredient.UseCondition && availableItem.ConditionPercentage - ingredient.MinCondition * 100 > 0.0f) //Leave it behind with reduced condition if it has enough to stay above 0
                        {
                            availableItem.Condition -= availableItem.Prefab.Health * ingredient.MinCondition;
                            continue;
                        }
                        availableIngredients.Remove(availableItem);
                        Entity.Spawner.AddToRemoveQueue(availableItem);
                        inputContainer.Inventory.RemoveItem(availableItem);
                    }
                }

                Character tempUser = user;
                int amountFittingContainer = outputContainer.Inventory.HowManyCanBePut(fabricatedItem.TargetItem);
                for (int i = 0; i < fabricatedItem.Amount; i++)
                {
                    if (i < amountFittingContainer)
                    {
                        Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, outputContainer.Inventory, fabricatedItem.TargetItem.Health * fabricatedItem.OutCondition,
                            onSpawned: (Item spawnedItem) => { onItemSpawned(spawnedItem, tempUser); });
                    }
                    else
                    {
                        Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, item.Position, item.Submarine, fabricatedItem.TargetItem.Health * fabricatedItem.OutCondition,
                            onSpawned: (Item spawnedItem) => { onItemSpawned(spawnedItem, tempUser); });
                    }
                }

                static void onItemSpawned(Item spawnedItem, Character user)
                {
                    if (user != null && user.TeamID != CharacterTeamType.None)
                    {
                        foreach (WifiComponent wifiComponent in spawnedItem.GetComponents<WifiComponent>())
                        {
                            wifiComponent.TeamID = user.TeamID;
                        }
                    }
                }
            
                if (user != null && !user.Removed)
                {
                    foreach (Skill skill in fabricatedItem.RequiredSkills)
                    {
                        float userSkill = user.GetSkillLevel(skill.Identifier);
                        user.Info.IncreaseSkillLevel(
                            skill.Identifier,
                            skill.Level * SkillSettings.Current.SkillIncreasePerFabricatorRequiredSkill / Math.Max(userSkill, 1.0f),
                            user.Position + Vector2.UnitY * 150.0f);
                    }
                }

                //disabled "continuous fabrication" for now
                //before we enable it, there should be some UI controls for fabricating a specific number of items

                /*var prevFabricatedItem = fabricatedItem;
                var prevUser = user;
                CancelFabricating();
                if (CanBeFabricated(prevFabricatedItem))
                {
                    //keep fabricating if we can fabricate more
                    StartFabricating(prevFabricatedItem, prevUser, addToServerLog: false);
                }*/


                CancelFabricating();
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
            float degreeOfSuccess = FabricationDegreeOfSuccess(user, fabricableItem.RequiredSkills);

            float t = degreeOfSuccess < 0.5f ? degreeOfSuccess * degreeOfSuccess : degreeOfSuccess * 2;

            //fabricating takes 100 times longer if degree of success is close to 0
            //characters with a higher skill than required can fabricate up to 100% faster
            return fabricableItem.RequiredTime / FabricationSpeed / MathHelper.Clamp(t, 0.01f, 2.0f);
        }
        
        public float FabricationDegreeOfSuccess(Character character, List<Skill> skills)
        {
            if (skills.Count == 0) { return 1.0f; }
            if (character == null) { return 0.0f; }

            float skillSum = (from t in skills let characterLevel = character.GetSkillLevel(t.Identifier) select (characterLevel - (t.Level * SkillRequirementMultiplier))).Sum();
            float average = skillSum / skills.Count;

            return (average + 100.0f) / 2.0f / 100.0f;
        }

        public override float GetSkillMultiplier()
        {
            return SkillRequirementMultiplier;
        }

        /// <summary>
        /// Get a list of all items available in the input container and linked containers
        /// </summary>
        /// <returns></returns>
        private List<Item> GetAvailableIngredients()
        {
            List<Item> availableIngredients = new List<Item>();
            availableIngredients.AddRange(inputContainer.Inventory.AllItems);
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

                    availableIngredients.AddRange(itemContainer.Inventory.AllItems);
                }
            }
#if CLIENT
            if (Character.Controlled?.Inventory != null)
            {
                availableIngredients.AddRange(Character.Controlled.Inventory.AllItems);
            }
#else
            if (user?.Inventory != null)
            {
                availableIngredients.AddRange(user.Inventory.AllItems);
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
                        if (!inputContainer.Inventory.CanBePut(matchingItem))
                        {
                            var unneededItem = inputContainer.Inventory.AllItems.FirstOrDefault(it => !usedItems.Contains(it));
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
                requiredItem.ItemPrefabs.Contains(item.prefab) && 
                item.Condition / item.Prefab.Health >= requiredItem.MinCondition;
        }

        public override XElement Save(XElement parentElement)
        {
            var componentElement = base.Save(parentElement);
            if (fabricatedItem != null)
            {
                componentElement.Add(new XAttribute("fabricateditemidentifier", fabricatedItem.TargetItem.Identifier));
                componentElement.Add(new XAttribute("savedtimeuntilready", timeUntilReady.ToString("G", CultureInfo.InvariantCulture)));
                componentElement.Add(new XAttribute("savedrequiredtime", requiredTime.ToString("G", CultureInfo.InvariantCulture)));

            }
            return componentElement;
        }

        public override void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            savedFabricatedItem = componentElement.GetAttributeString("fabricateditemidentifier", "");
            savedTimeUntilReady = componentElement.GetAttributeFloat("savedtimeuntilready", 0.0f);
            savedRequiredTime = componentElement.GetAttributeFloat("savedrequiredtime", 0.0f);
        }

        public override void OnMapLoaded()
        {
            if (string.IsNullOrEmpty(savedFabricatedItem)) { return; }

            inputContainer?.OnMapLoaded();
            outputContainer?.OnMapLoaded();
            
            var recipe = fabricationRecipes.Find(r => r.TargetItem.Identifier == savedFabricatedItem);
            if (recipe == null)
            {
                DebugConsole.ThrowError("Error while loading a fabricator. Can't continue fabricating \"" + savedFabricatedItem + "\" (matching recipe not found).");
            }
            else
            {
#if CLIENT
                SelectItem(null, recipe, savedRequiredTime);
#endif
                StartFabricating(recipe, user: null);
                timeUntilReady = savedTimeUntilReady;
                requiredTime = savedRequiredTime;
            }
            savedFabricatedItem = null;
        }
    }
}
