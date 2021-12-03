using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Abilities;

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

        private readonly Dictionary<string, List<Item>> availableIngredients = new Dictionary<string, List<Item>>();

        const float RefreshIngredientsInterval = 1.0f;
        private float refreshIngredientsTimer;

        private bool hasPower;

        private Character user;

        private ItemContainer inputContainer, outputContainer;
        
        [Serialize(1.0f, true)]
        public float FabricationSpeed { get; set; }
        
        [Serialize(1.0f, true)]
        public float SkillRequirementMultiplier { get; set; }

        private const float TinkeringSpeedIncrease = 1.5f;

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
            fabricationRecipes.Sort((r1, r2) =>
            {
                int hash1 = (int)r1.TargetItem.UIntIdentifier;
                int hash2 = (int)r2.TargetItem.UIntIdentifier;
                return hash1 - hash2;
            });

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
                if (recipe.RequiredItems.Count > inputContainer.Capacity)
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
            if (!outputContainer.Inventory.CanBePut(selectedItem.TargetItem, selectedItem.OutCondition * selectedItem.TargetItem.Health)) { return; }

            RefreshAvailableIngredients();

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
            UpdateRequiredTimeProjSpecific();
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
            if (refreshIngredientsTimer <= 0.0f)
            {
                RefreshAvailableIngredients();
                refreshIngredientsTimer = RefreshIngredientsInterval;
            }
            refreshIngredientsTimer -= deltaTime;

            if (fabricatedItem == null || !CanBeFabricated(fabricatedItem, availableIngredients, user))
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

            if (powerConsumption <= 0) { Voltage = 1.0f; }

            float fabricationSpeedIncrease = 1f + tinkeringStrength * TinkeringSpeedIncrease;

            timeUntilReady -= deltaTime * fabricationSpeedIncrease * Math.Min(Voltage, 1.0f);

            UpdateRequiredTimeProjSpecific();

            if (timeUntilReady <= 0.0f) 
            {
                Fabricate();
            }
        }

        private void Fabricate()
        {
            RefreshAvailableIngredients();
            if (fabricatedItem == null || !CanBeFabricated(fabricatedItem, availableIngredients, user))
            {
                CancelFabricating();
                return;
            }

            bool ingredientsStolen = false;
            bool ingredientsAllowStealing = true;

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                fabricatedItem.RequiredItems.ForEach(requiredItem =>
                {
                    for (int usedPrefabsAmount = 0; usedPrefabsAmount < requiredItem.Amount; usedPrefabsAmount++)
                    {
                        foreach (ItemPrefab requiredPrefab in requiredItem.ItemPrefabs)
                        {
                            if (!availableIngredients.ContainsKey(requiredPrefab.Identifier)) { continue; }

                            var availableItems = availableIngredients[requiredPrefab.Identifier];
                            var availableItem = availableItems.FirstOrDefault(potentialPrefab =>
                            {
                                return potentialPrefab.ConditionPercentage >= requiredItem.MinCondition * 100.0f &&
                                       potentialPrefab.ConditionPercentage <= requiredItem.MaxCondition * 100.0f;
                            });

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
                                    containedItem.Drop(dropper: null);
                                }
                            }

                            availableItems.Remove(availableItem);
                            Entity.Spawner.AddToRemoveQueue(availableItem);
                            inputContainer.Inventory.RemoveItem(availableItem);
                        }
                    }
                });

                int amountFittingContainer = outputContainer.Inventory.HowManyCanBePut(fabricatedItem.TargetItem, fabricatedItem.OutCondition * fabricatedItem.TargetItem.Health);

                var fabricationValueItem = new AbilityValueItem(fabricatedItem.Amount, fabricatedItem.TargetItem);

                int quality = 0;
                if (user?.Info != null)
                {
                    foreach (Character character in Character.GetFriendlyCrew(user))
                    {
                        character.CheckTalents(AbilityEffectType.OnAllyItemFabricatedAmount, fabricationValueItem);
                    }
                    user.CheckTalents(AbilityEffectType.OnItemFabricatedAmount, fabricationValueItem);

                    quality = GetFabricatedItemQuality(fabricatedItem, user);
                }

                var tempUser = user;
                for (int i = 0; i < (int)fabricationValueItem.Value; i++)
                {
                    float outCondition = fabricatedItem.OutCondition;
                    if (i < amountFittingContainer)
                    {
                        Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, outputContainer.Inventory, fabricatedItem.TargetItem.Health * outCondition, quality,
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
                        Entity.Spawner.AddToSpawnQueue(fabricatedItem.TargetItem, item.Position, item.Submarine, fabricatedItem.TargetItem.Health * outCondition, quality,
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
                        var addedSkillValue = new AbilityValueString(addedSkill, skill.Identifier);
                        user.CheckTalents(AbilityEffectType.OnItemFabricationSkillGain, addedSkillValue);

                        user.Info.IncreaseSkillLevel(
                            skill.Identifier,
                            addedSkillValue.Value);
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

        private int GetFabricatedItemQuality(FabricationRecipe fabricatedItem, Character user)
        {
            if (user == null) { return 0; }
            if (fabricatedItem.TargetItem.ConfigElement.GetChildElement("Quality") == null) { return 0; }
            int quality = 0;
            float floatQuality = 0.0f;
            foreach (string tag in fabricatedItem.TargetItem.Tags)
            {
                floatQuality += user.Info.GetSavedStatValue(StatTypes.IncreaseFabricationQuality, tag);
            }
            quality = (int)floatQuality;

            const int MaxCraftingSkill = 100;

            quality += fabricatedItem.RequiredSkills.All(s => user.GetSkillLevel(s.Identifier) >= MaxCraftingSkill) ? 1 : 0;
            quality += FabricationDegreeOfSuccess(user, fabricatedItem.RequiredSkills) >= 0.5f ? 1 : 0;
            return quality;
        }

        partial void UpdateRequiredTimeProjSpecific();

        private bool CanBeFabricated(FabricationRecipe fabricableItem, Dictionary<string, List<Item>> availableIngredients, Character character)
        {
            if (fabricableItem == null) { return false; }
            if (fabricableItem.RequiresRecipe && (character == null || !character.HasRecipeForItem(fabricableItem.TargetItem.Identifier))) { return false; }

            return fabricableItem.RequiredItems.All(requiredItem =>
            {
                int availablePrefabsAmount = 0;
                foreach (ItemPrefab requiredPrefab in requiredItem.ItemPrefabs)
                {
                    if (!availableIngredients.ContainsKey(requiredPrefab.Identifier)) { continue; }

                    var availablePrefabs = availableIngredients[requiredPrefab.Identifier];
                    foreach (Item availablePrefab in availablePrefabs)
                    {
                        if (availablePrefab.ConditionPercentage / 100.0f >= requiredItem.MinCondition &&
                            availablePrefab.ConditionPercentage / 100.0f <= requiredItem.MaxCondition)
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

        private void RefreshAvailableIngredients()
        {
            List<Item> itemList = new List<Item>();
            itemList.AddRange(inputContainer.Inventory.AllItems);
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
#if CLIENT
            if (Character.Controlled?.Inventory != null)
            {
                itemList.AddRange(Character.Controlled.Inventory.AllItems);
            }
#else
            if (user?.Inventory != null)
            {
                itemList.AddRange(user.Inventory.AllItems);
            }
#endif
            availableIngredients.Clear();
            foreach (Item item in itemList)
            {
                var itemIdentifier = item.prefab.Identifier;
                if (!availableIngredients.ContainsKey(itemIdentifier))
                {
                    availableIngredients[itemIdentifier] = new List<Item>(itemList.Count);
                }
                availableIngredients[itemIdentifier].Add(item);
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

            bool isClient = GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

            targetItem.RequiredItems.ForEach(requiredItem => {
                for (int i = 0; i < requiredItem.Amount; i++)
                {
                    foreach (ItemPrefab requiredPrefab in requiredItem.ItemPrefabs)
                    {
                        if (!availableIngredients.ContainsKey(requiredPrefab.Identifier)) { continue; }

                        var availablePrefabs = availableIngredients[requiredPrefab.Identifier];
                        var availablePrefab = availablePrefabs.FirstOrDefault(potentialPrefab =>
                        {
                            return !usedItems.Contains(potentialPrefab) &&
                                   potentialPrefab.ConditionPercentage >= requiredItem.MinCondition * 100.0f &&
                                   potentialPrefab.ConditionPercentage <= requiredItem.MaxCondition * 100.0f;
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
                                unneededItem?.Drop(null, createNetworkEvent: !isClient);
                            }
                            inputContainer.Inventory.TryPutItem(availablePrefab, user: null, createNetworkEvent: !isClient);
                        }
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
