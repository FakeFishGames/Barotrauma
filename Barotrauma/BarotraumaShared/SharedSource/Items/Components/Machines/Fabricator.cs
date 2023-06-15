﻿using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Abilities;

namespace Barotrauma.Items.Components
{   
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private ImmutableDictionary<uint, FabricationRecipe> fabricationRecipes; //this is not readonly because tutorials fuck this up!!!!

        private const int MaxAmountToFabricate = 99;

        private FabricationRecipe fabricatedItem;
        private float timeUntilReady;
        private float requiredTime;

        private string savedFabricatedItem;
        private float savedTimeUntilReady, savedRequiredTime;

        private readonly Dictionary<Identifier, List<Item>> availableIngredients = new Dictionary<Identifier, List<Item>>();

        const float RefreshIngredientsInterval = 1.0f;
        private float refreshIngredientsTimer;

        private bool hasPower;

        private Character user;

        private ItemContainer inputContainer, outputContainer;
        
        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float FabricationSpeed { get; set; }
        
        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float SkillRequirementMultiplier { get; set; }

        private int amountToFabricate;
        [Serialize(1, IsPropertySaveable.Yes)]
        public int AmountToFabricate 
        {
            get { return amountToFabricate; }
            set { amountToFabricate = MathHelper.Clamp(value, 1, MaxAmountToFabricate); }
        }

        private int amountRemaining;

        private const float TinkeringSpeedIncrease = 2.5f;

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

        private float progressState;

        private readonly Dictionary<uint, int> fabricationLimits = new Dictionary<uint, int>();

        public Fabricator(Item item, ContentXElement element)
            : base(item, element)
        {
            foreach (var subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("fabricableitem", StringComparison.OrdinalIgnoreCase))
                {
                    DebugConsole.ThrowError("Error in item " + item.Name + "! Fabrication recipes should be defined in the craftable item's xml, not in the fabricator.");
                    break;
                }
            }

            var fabricationRecipes = new Dictionary<uint, FabricationRecipe>();
            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                foreach (FabricationRecipe recipe in itemPrefab.FabricationRecipes.Values)
                {
                    if (recipe.SuitableFabricatorIdentifiers.Length > 0)
                    {
                        if (!recipe.SuitableFabricatorIdentifiers.Any(i => item.Prefab.Identifier == i || item.HasTag(i)))
                        {
                            continue;
                        }
                    }

                    bool recipeInvalid = false;
                    foreach (var requiredItem in recipe.RequiredItems)
                    {
                        if (requiredItem.ItemPrefabs.None())
                        {
                            DebugConsole.ThrowError($"Error in the fabrication recipe for \"{itemPrefab.Name}\". Could not find the ingredient \"{requiredItem}\".");
                            recipeInvalid = true;
                        }
                    }
                    if (recipeInvalid) { continue; }

                    fabricationRecipes.Add(recipe.RecipeHash, recipe);
                    if (recipe.FabricationLimitMax >= 0)
                    {
                        fabricationLimits.Add(recipe.RecipeHash, Rand.Range(recipe.FabricationLimitMin, recipe.FabricationLimitMax + 1));
                    }
                }
            }
            this.fabricationRecipes = fabricationRecipes.ToImmutableDictionary();

            state = FabricatorState.Stopped;
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

            foreach (var recipe in fabricationRecipes.Values)
            {
                if (recipe.RequiredItems.Length > inputContainer.Capacity)
                {
                    DebugConsole.ThrowError("Error in item \"" + item.Name + "\": There's not enough room in the input inventory for the ingredients of \"" + recipe.TargetItem.Name + "\"!");
                }
            }

            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific();

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

        public void RemoveFabricationRecipes(IEnumerable<Identifier> allowedIdentifiers)
        {
            fabricationRecipes = fabricationRecipes
                .Where(kvp => allowedIdentifiers.Contains(kvp.Value.TargetItemPrefabIdentifier))
                .ToImmutableDictionary();

            CreateRecipes();
        }

        partial void CreateRecipes();

        private void StartFabricating(FabricationRecipe selectedItem, Character user, bool addToServerLog = true)
        {
            if (selectedItem == null) { return; }
            if (!outputContainer.Inventory.CanBePut(selectedItem.TargetItem, selectedItem.OutCondition * selectedItem.TargetItem.Health)) { return; }

            IsActive = true;
            this.user = user;
            fabricatedItem = selectedItem;
            RefreshAvailableIngredients();

#if CLIENT
            itemList.Enabled = false;
            if (amountInput != null)
            {
                amountInput.Enabled = false;
            }
            RefreshActivateButtonText();
#endif

            bool isClient = GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;
            if (!isClient)
            {
                MoveIngredientsToInputContainer(selectedItem);
            }

            requiredTime = GetRequiredTime(fabricatedItem, user);
            timeUntilReady = requiredTime;
            
            inputContainer.Inventory.Locked = true;
            outputContainer.Inventory.Locked = true;

            if (GameMain.NetworkMember?.IsServer ?? true)
            {
                State = FabricatorState.Active;
            }
#if SERVER
            if (user != null && addToServerLog && selectedItem.RequiredMoney == 0)
            {
                if (selectedItem.RequiredMoney > 0)
                {
                    GameServer.Log($"{GameServer.CharacterLogName(user)} bought {selectedItem.DisplayName.Value} for {selectedItem.RequiredMoney} mk from {item.Name}", ServerLog.MessageType.Money);
                }
                else
                {
                    GameServer.Log($"{GameServer.CharacterLogName(user)} started fabricating {selectedItem.DisplayName.Value} in {item.Name}", ServerLog.MessageType.ItemInteraction);
                }
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
            UpdateRequiredTimeProjSpecific();
            inputContainer.Inventory.Locked = false;
            outputContainer.Inventory.Locked = false;

            if (GameMain.NetworkMember?.IsServer ?? true)
            {
                State = FabricatorState.Stopped;
            }

            if (fabricatedItem == null) { return; }
#if SERVER
            if (user != null)
            {
                GameServer.Log(GameServer.CharacterLogName(user) + " cancelled the fabrication of " + fabricatedItem.DisplayName.Value + " in " + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#elif CLIENT
            itemList.Enabled = true;
            if (amountInput != null)
            {
                amountInput.Enabled = amountTextMax.Enabled;
            }
            RefreshActivateButtonText();
#endif
            fabricatedItem = null;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (refreshIngredientsTimer <= 0.0f)
            {
                RefreshAvailableIngredients();
                refreshIngredientsTimer = RefreshIngredientsInterval;
            }
            refreshIngredientsTimer -= deltaTime;

            bool isClient = GameMain.NetworkMember?.IsClient ?? false;

            if (!isClient)
            {
                if (fabricatedItem == null || !CanBeFabricated(fabricatedItem, availableIngredients, user))
                {
                    CancelFabricating();
                    return;
                }
            }

            progressState = fabricatedItem == null ? 0.0f : (requiredTime - timeUntilReady) / requiredTime;

            if (isClient)
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
            
            float tinkeringStrength = 0f;
            var repairable = item.GetComponent<Repairable>();
            if (repairable != null)
            {
                repairable.LastActiveTime = (float)Timing.TotalTime + 10.0f;
                if (repairable.IsTinkering)
                {
                    tinkeringStrength = repairable.TinkeringStrength;
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);


            float fabricationSpeedIncrease = 1f + tinkeringStrength * TinkeringSpeedIncrease;

            timeUntilReady -= deltaTime * fabricationSpeedIncrease * Math.Min(powerConsumption <= 0 ? 1 : Voltage, MaxOverVoltageFactor);

            UpdateRequiredTimeProjSpecific();

            if (timeUntilReady <= 0.0f) 
            {
                Fabricate();
            }
        }

        private Client GetUsingClient()
        {
#if SERVER
            return GameMain.Server.ConnectedClients.Find(c => c.Character == user);
#elif CLIENT
            return null;
#endif
        }

        private void Fabricate()
        {
            RefreshAvailableIngredients();
            if (fabricatedItem == null || !CanBeFabricated(fabricatedItem, availableIngredients, user))
            {
                CancelFabricating();
                return;
            }

            if (fabricatedItem.RequiredMoney > 0)
            {
                if (user == null) { return; }
                if (GameMain.GameSession?.GameMode is MultiPlayerCampaign mpCampaign)
                {
#if SERVER
                    if (GetUsingClient() is { } client)
                    {
                        mpCampaign.TryPurchase(client, fabricatedItem.RequiredMoney);
                    }
                    else
                    {
                        user.Wallet.Deduct(fabricatedItem.RequiredMoney);
                    }
#endif
                }
                else if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    campaign.Bank.Deduct(fabricatedItem.RequiredMoney);
                }
            }

            bool ingredientsStolen = false;
            bool ingredientsAllowStealing = true;

            if (GameMain.NetworkMember is null || GameMain.NetworkMember.IsServer)
            {
                List<Item> foundAvailableItems = new List<Item>();
                foreach (FabricationRecipe.RequiredItem requiredItem in fabricatedItem.RequiredItems)
                {
                    for (int usedPrefabsAmount = 0; usedPrefabsAmount < requiredItem.Amount; usedPrefabsAmount++)
                    {
                        foreach (ItemPrefab requiredPrefab in requiredItem.ItemPrefabs)
                        {
                            if (!availableIngredients.ContainsKey(requiredPrefab.Identifier)) { continue; }

                            var availableItems = availableIngredients[requiredPrefab.Identifier];
                            var availableItem = availableItems.FirstOrDefault(potentialPrefab => requiredItem.IsConditionSuitable(potentialPrefab.ConditionPercentage));

                            if (availableItem == null) { continue; }

                            ingredientsStolen |= availableItem.StolenDuringRound;
                            if (!availableItem.AllowStealing)
                            {
                                ingredientsAllowStealing = false;
                            }

                            //Leave it behind with reduced condition if it has enough to stay above 0
                            if (requiredItem.UseCondition && availableItem.ConditionPercentage - requiredItem.MinCondition * 100 > 0.0f)
                            {
                                availableItem.Condition -= availableItem.Prefab.Health * requiredItem.MinCondition;
                                continue;
                            }
                            if (availableItem.OwnInventory != null)
                            {
                                foreach (Item containedItem in availableItem.OwnInventory.AllItemsMod)
                                {
                                    if (availableItem.GetComponent<ItemContainer>()?.RemoveContainedItemsOnDeconstruct ?? false)
                                    {
                                        Entity.Spawner.AddItemToRemoveQueue(containedItem);
                                    }
                                    else
                                    {
                                        containedItem.Drop(dropper: null);
                                    }
                                }
                            }

                            foundAvailableItems.Add(availableItem);
                            availableItems.Remove(availableItem);
                            break;
                        }
                    }
                }

                var fabricationIngredients = new AbilityFabricationItemIngredients(foundAvailableItems);
                user?.CheckTalents(AbilityEffectType.OnItemFabricatedIngredients, fabricationIngredients);

                foreach (Item availableItem in fabricationIngredients.Items)
                {
                    Entity.Spawner.AddItemToRemoveQueue(availableItem);
                    inputContainer.Inventory.RemoveItem(availableItem);
                }

                int amountFittingContainer = outputContainer.Inventory.HowManyCanBePut(fabricatedItem.TargetItem, fabricatedItem.OutCondition * fabricatedItem.TargetItem.Health);

                var fabricationitemAmount = new AbilityFabricationItemAmount(fabricatedItem.TargetItem, fabricatedItem.Amount);

                int quality = 0;
                if (fabricatedItem.Quality.HasValue)
                {
                    quality = fabricatedItem.Quality.Value;
                }
                else if (user?.Info != null)
                {
                    foreach (Character character in Character.GetFriendlyCrew(user))
                    {
                        character.CheckTalents(AbilityEffectType.OnAllyItemFabricatedAmount, fabricationitemAmount);
                    }
                    user.CheckTalents(AbilityEffectType.OnItemFabricatedAmount, fabricationitemAmount);                    
                    quality = GetFabricatedItemQuality(fabricatedItem, user);
                }

                int amount = (int)fabricationitemAmount.Value;
                if (fabricationLimits.ContainsKey(fabricatedItem.RecipeHash))
                {
                    if (amount > fabricationLimits[fabricatedItem.RecipeHash])
                    {
                        amount = fabricationLimits[fabricatedItem.RecipeHash];
                        fabricationLimits[fabricatedItem.RecipeHash] = 0;
                    }
                    else
                    {
                        fabricationLimits[fabricatedItem.RecipeHash] -= amount;
                    }
                }

                var tempUser = user;
                for (int i = 0; i < amount; i++)
                {
                    float outCondition = fabricatedItem.OutCondition;
                    GameAnalyticsManager.AddDesignEvent("ItemFabricated:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "none") + ":" + fabricatedItem.TargetItem.Identifier);
                    if (i < amountFittingContainer)
                    {
                        Entity.Spawner.AddItemToSpawnQueue(fabricatedItem.TargetItem, outputContainer.Inventory, fabricatedItem.TargetItem.Health * outCondition, quality,
                            onSpawned: (Item spawnedItem) =>
                            {
                                onItemSpawned(spawnedItem, tempUser);
                                spawnedItem.Quality = quality;
                                spawnedItem.StolenDuringRound = ingredientsStolen;
                                spawnedItem.AllowStealing = ingredientsAllowStealing;
                                //reset the condition in case the max condition is higher than the prefab's due to e.g. quality modifiers
                                spawnedItem.Condition = spawnedItem.MaxCondition * outCondition;
                            });
                    }
                    else
                    {
                        Entity.Spawner.AddItemToSpawnQueue(fabricatedItem.TargetItem, item.Position, item.Submarine, fabricatedItem.TargetItem.Health * outCondition, quality,
                            onSpawned: (Item spawnedItem) =>
                            {
                                onItemSpawned(spawnedItem, tempUser);
                                spawnedItem.Quality = quality;
                                spawnedItem.StolenDuringRound = ingredientsStolen;
                                spawnedItem.AllowStealing = ingredientsAllowStealing;
                                //reset the condition in case the max condition is higher than the prefab's due to e.g. quality modifiers
                                spawnedItem.Condition = spawnedItem.MaxCondition * outCondition;
                            });
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
                if (user?.Info != null && !user.Removed)
                {
                    foreach (Skill skill in fabricatedItem.RequiredSkills)
                    {
                        float userSkill = user.GetSkillLevel(skill.Identifier);
                        float addedSkill = skill.Level * SkillSettings.Current.SkillIncreasePerFabricatorRequiredSkill / Math.Max(userSkill, 1.0f);
                        var addedSkillValue = new AbilityFabricatorSkillGain(skill.Identifier, addedSkill);
                        user.CheckTalents(AbilityEffectType.OnItemFabricationSkillGain, addedSkillValue);

                        user.Info.IncreaseSkillLevel(
                            skill.Identifier,
                            addedSkillValue.Value);
                    }
                }

                var prevFabricatedItem = fabricatedItem;
                var prevUser = user;
                CancelFabricating();

                amountRemaining--; 
                if (amountRemaining > 0 && CanBeFabricated(prevFabricatedItem, availableIngredients, prevUser))
                {
                    //keep fabricating if we can fabricate more
                    StartFabricating(prevFabricatedItem, prevUser, addToServerLog: false);
                }
            }

        }

        /// <summary>
        /// Power consumption of the fabricator. Only consume power when active and adjust consumption based on condition.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            //No consumption if not powerin or is off
            if (connection != this.powerIn || !IsActive)
            {
                return 0;
            }

            currPowerConsumption = PowerConsumption;
            item.GetComponent<Repairable>()?.AdjustPowerConsumption(ref currPowerConsumption);

            return currPowerConsumption;
        }

        private static int GetFabricatedItemQuality(FabricationRecipe fabricatedItem, Character user)
        {
            if (user?.Info == null) { return 0; }
            if (fabricatedItem.TargetItem.ConfigElement.GetChildElement("Quality") == null) { return 0; }
            int quality = 0;
            float floatQuality = 0.0f;
            floatQuality += user.GetStatValue(StatTypes.IncreaseFabricationQuality, includeSaved: false);
            foreach (var tag in fabricatedItem.TargetItem.Tags)
            {
                floatQuality += user.Info.GetSavedStatValue(StatTypes.IncreaseFabricationQuality, tag);
            }
            if (!fabricatedItem.TargetItem.Tags.Contains(fabricatedItem.TargetItem.Identifier))
            {
                floatQuality += user.Info.GetSavedStatValue(StatTypes.IncreaseFabricationQuality, fabricatedItem.TargetItem.Identifier);
            }
            quality = (int)floatQuality;

            const int MaxCraftingSkill = 100;

            //having a higher-than-100 skill (e.g. due to talents) gives +1 quality
            quality += fabricatedItem.RequiredSkills.All(s => user.GetSkillLevel(s.Identifier) >= MaxCraftingSkill) ? 1 : 0;
            foreach (var skill in fabricatedItem.RequiredSkills)
            {
                //+1 quality if the character's skill level is >20% from the min requirement towards max skill
                //e.g. if the skill requirement is 10 -> 28
                //40 -> 52
                //90 -> 92
                float skillRequirement = MathHelper.Lerp(skill.Level, MaxCraftingSkill, 0.2f);
                if (user.GetSkillLevel(skill.Identifier) > skillRequirement)
                {
                    quality += 1;
                }
            }
            return quality;
        }

        partial void UpdateRequiredTimeProjSpecific();

        private static bool AnyOneHasRecipeForItem(Character user, ItemPrefab item)
        {
            return 
                (user != null && user.HasRecipeForItem(item.Identifier)) ||
                GameSession.GetSessionCrewCharacters(CharacterType.Bot).Any(c => c.HasRecipeForItem(item.Identifier));
        }
        
        private bool CanBeFabricated(FabricationRecipe fabricableItem, IReadOnlyDictionary<Identifier, List<Item>> availableIngredients, Character character)
        {
            if (fabricableItem == null) { return false; }
            if (fabricableItem.RequiresRecipe) 
            {
                if (character == null) { return false; }
                if (!AnyOneHasRecipeForItem(character, fabricableItem.TargetItem))
                {
                    return false; 
                }
            }

            if (fabricableItem.RequiredMoney > 0)
            {
                switch (GameMain.GameSession?.GameMode)
                {
                    case MultiPlayerCampaign mpCampaign:
                    {
                        if (!mpCampaign.CanAfford(fabricableItem.RequiredMoney, GetUsingClient())) { return false; }

                        break;
                    }
                    case CampaignMode campaign:
                    {
                        if (campaign.Bank.Balance < fabricableItem.RequiredMoney) { return false; }

                        break;
                    }
                    default:
                        return false;
                }
            }

            if (fabricationLimits.TryGetValue(fabricableItem.RecipeHash, out int amount) && amount <= 0)
            {
                return false;
            }

            return fabricableItem.RequiredItems.All(requiredItem =>
            {
                int availablePrefabsAmount = 0;
                foreach (ItemPrefab requiredPrefab in requiredItem.ItemPrefabs)
                {
                    if (!availableIngredients.ContainsKey(requiredPrefab.Identifier)) { continue; }

                    var availablePrefabs = availableIngredients[requiredPrefab.Identifier];
                    foreach (Item availablePrefab in availablePrefabs)
                    {
                        if (requiredItem.IsConditionSuitable(availablePrefab.ConditionPercentage))
                        {
                            availablePrefabsAmount++;
                        }

                        if (availablePrefabsAmount >= requiredItem.Amount)
                        {
                            return true;
                        }
                    }
                }

                return false;
            });
        }

        private float GetRequiredTime(FabricationRecipe fabricableItem, Character user)
        {
            float degreeOfSuccess = FabricationDegreeOfSuccess(user, fabricableItem.RequiredSkills);

            float t = degreeOfSuccess < 0.5f ? degreeOfSuccess * degreeOfSuccess : degreeOfSuccess * 2;

            //fabricating takes 100 times longer if degree of success is close to 0
            //characters with a higher skill than required can fabricate up to 100% faster
            float time = fabricableItem.RequiredTime / item.StatManager.GetAdjustedValue(ItemTalentStats.FabricationSpeed, FabricationSpeed) / MathHelper.Clamp(t, 0.01f, 2.0f);

            if (user?.Info is { } info && fabricableItem.TargetItem is { } it)
            {
                time /= 1f + it.Tags.Sum(tag => info.GetSavedStatValue(StatTypes.FabricationSpeed, tag));
            }
            return time;
        }

        public float FabricationDegreeOfSuccess(Character character, ImmutableArray<Skill> skills)
        {
            if (skills.Length == 0) { return 1.0f; }
            if (character == null) { return 0.0f; }

            float minDegreeOfSuccess = 1.0f;
            foreach (var skill in skills)
            {
                float characterLevel = character.GetSkillLevel(skill.Identifier);
                minDegreeOfSuccess = Math.Min(minDegreeOfSuccess, (characterLevel - (skill.Level * SkillRequirementMultiplier) + 100.0f) / 2.0f / 100.0f);
            }
            return minDegreeOfSuccess;
        }

        public override float GetSkillMultiplier()
        {
            return SkillRequirementMultiplier;
        }


        private readonly HashSet<Inventory> linkedInventories = new HashSet<Inventory>();

        private void RefreshAvailableIngredients()
        {
            Character user = this.user;
#if CLIENT
            user ??= Character.Controlled;
#endif
            linkedInventories.Clear();
            List<Item> itemList = new List<Item>();
            itemList.AddRange(inputContainer.Inventory.AllItems);
            foreach (MapEntity linkedTo in item.linkedTo)
            {
                if (linkedTo is Item linkedItem)
                {
                    var itemContainer = linkedItem.GetComponent<ItemContainer>();
                    if (itemContainer == null) { continue; }
                    if (user != null)
                    {
                        if (!itemContainer.HasRequiredItems(user, addMessage: false)) { continue; }
                    }

                    var deconstructor = linkedItem.GetComponent<Deconstructor>();
                    if (deconstructor != null)
                    {
                        itemContainer = deconstructor.OutputContainer;
                    }

                    linkedInventories.Add(itemContainer.Inventory);
                    itemList.AddRange(itemContainer.Inventory.AllItems);
                }
            }
            for (int i = 0; i < itemList.Count; i++)
            {
                var container = itemList[i].GetComponent<ItemContainer>();
                if (container != null)
                {
                    itemList.AddRange(container.Inventory.AllItems);
                }
            }
            if (user?.Inventory != null && user.SelectedItem == item)
            {
                itemList.AddRange(user.Inventory.AllItems);
                linkedInventories.Add(user.Inventory);
            }
            availableIngredients.Clear();
            foreach (Item item in itemList)
            {
                var itemIdentifier = item.Prefab.Identifier;
                if (!availableIngredients.ContainsKey(itemIdentifier))
                {
                    availableIngredients[itemIdentifier] = new List<Item>(itemList.Count);
                }
                //order by condition (prefer using worst-condition items)
                int index = 0;
                while (index < availableIngredients[itemIdentifier].Count &&
                    compare(item, availableIngredients[itemIdentifier][index], inputContainer.Inventory) < 0)
                {
                    index++;
                }

                static int compare(Item item1, Item item2, Inventory inputInventory)
                {
                    bool item1InInputInventory = item1.ParentInventory == inputInventory;
                    bool item2InInputInventory = item2.ParentInventory == inputInventory;
                    //prefer items in the input inventory
                    if (item1InInputInventory != item2InInputInventory)
                    {
                        return item1InInputInventory ? 1 : -1;
                    }
                    else
                    {
                        float condition1 = MathUtils.IsValid(item1.Condition) ? item1.Condition : 0;
                        float condition2 = MathUtils.IsValid(item2.Condition) ? item2.Condition : 0;
                        //prefer items in worse condition
                        return Math.Sign(condition2 - condition1);
                    }
                }

                availableIngredients[itemIdentifier].Insert(index, item);
            }
        }

        /// <summary>
        /// Move the items required for fabrication into the input container.
        /// The method assumes that all the required ingredients are available either in the input container or linked containers.
        /// </summary>
        private void MoveIngredientsToInputContainer(FabricationRecipe targetItem)
        {
            //required ingredients that are already present in the input container
            List<Item> usedItems = new List<Item>();

            targetItem.RequiredItems.ForEach(requiredItem => {
                for (int i = 0; i < requiredItem.Amount; i++)
                {
                    foreach (ItemPrefab requiredPrefab in requiredItem.ItemPrefabs)
                    {
                        if (!availableIngredients.ContainsKey(requiredPrefab.Identifier)) { continue; }

                        var availablePrefabs = availableIngredients[requiredPrefab.Identifier];
                        var availablePrefab = availablePrefabs.FirstOrDefault(potentialPrefab =>
                        {
                            return !usedItems.Contains(potentialPrefab) && requiredItem.IsConditionSuitable(potentialPrefab.ConditionPercentage);
                        });
                        if (availablePrefab == null) { continue; }

                        availablePrefabs.Remove(availablePrefab);

                        if (availablePrefab.ParentInventory == inputContainer.Inventory)
                        {
                            //already in input container, all good
                            usedItems.Add(availablePrefab);
                        }
                        else //in another inventory, we need to move the item
                        {
                            if (!inputContainer.Inventory.CanBePut(availablePrefab))
                            {
                                var unneededItem = inputContainer.Inventory.AllItems.FirstOrDefault(it => !usedItems.Contains(it));
                                unneededItem?.Drop(null);
                            }
                            inputContainer.Inventory.TryPutItem(availablePrefab, user: null);
                        }
                        break;
                    }
                }
            });
            RefreshAvailableIngredients();
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

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap)
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
            
            var recipe = fabricationRecipes.Values.FirstOrDefault(r => r.TargetItem.Identifier == savedFabricatedItem);
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
        class AbilityFabricatorSkillGain : AbilityObject, IAbilityValue, IAbilitySkillIdentifier
        {
            public AbilityFabricatorSkillGain(Identifier skillIdentifier, float skillAmount)
            {
                SkillIdentifier = skillIdentifier;
                Value = skillAmount;
            }
            public float Value { get; set; }
            public Identifier SkillIdentifier { get; set; }
        }

        class AbilityFabricationItemAmount : AbilityObject, IAbilityValue, IAbilityItemPrefab
        {
            public AbilityFabricationItemAmount(ItemPrefab itemPrefab, float itemAmount)
            {
                ItemPrefab = itemPrefab;
                Value = itemAmount;
            }
            public float Value { get; set; }
            public ItemPrefab ItemPrefab { get; set; }
        }

        internal sealed class AbilityFabricationItemIngredients : AbilityObject
        {
            public List<Item> Items { get; set; }

            public AbilityFabricationItemIngredients(List<Item> items)
            {
                Items = items;
            }
        }
    }
}
