﻿using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        private float progressTimer;
        private float progressState;

        private Character user;

        private float userDeconstructorSpeedMultiplier = 1.0f;

        private const float TinkeringSpeedIncrease = 2.5f;

        private ItemContainer inputContainer, outputContainer;

        public ItemContainer InputContainer
        {
            get { return inputContainer; }
        }

        public ItemContainer OutputContainer
        {
            get { return outputContainer; }
        }

        /// <summary>
        /// Should the output items left in the deconstructor be automatically moved to the main sub at the end of the round 
        /// if the deconstructor is not in the main sub?
        /// </summary>
        public bool RelocateOutputToMainSub;

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool DeconstructItemsSimultaneously { get; set; }

        [Editable(MinValueFloat = 0.1f, MaxValueFloat = 1000), Serialize(1.0f, IsPropertySaveable.Yes)]
        public float DeconstructionSpeed { get; set; }

        public Deconstructor(Item item, ContentXElement element)
            : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            var containers = item.GetComponents<ItemContainer>().ToList();
            if (containers.Count < 2)
            {
                DebugConsole.ThrowError("Error in item \"" + item.Name + "\": Deconstructors must have two ItemContainer components!");
                return;
            }

            inputContainer = containers[0];
            outputContainer = containers[1];

#if CLIENT
            Identifier eventIdentifier = new Identifier(nameof(Deconstructor));
            inputContainer.OnContainedItemsChanged.RegisterOverwriteExisting(eventIdentifier, OnItemSlotsChanged);
#endif

            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific();

        partial void OnItemSlotsChanged(ItemContainer container);

        public override void Update(float deltaTime, Camera cam)
        {
            MoveInputQueue();

            if (inputContainer == null || inputContainer.Inventory.IsEmpty())
            {
                SetActive(false);
                return;
            }
            
            if (!HasPower) { return; }

            var repairable = item.GetComponent<Repairable>();
            if (repairable != null)
            {
                repairable.LastActiveTime = (float)Timing.TotalTime + 10.0f;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime);

            progressTimer += deltaTime * Math.Min(powerConsumption <= 0.0f ? 1 : Voltage, MaxOverVoltageFactor);

            float tinkeringStrength = 0f;
            if (repairable.IsTinkering)
            {
                tinkeringStrength = repairable.TinkeringStrength;
            }
            // doesn't quite work properly, remaining time changes if tinkering stops
            float deconstructionSpeedModifier = userDeconstructorSpeedMultiplier * (1f + tinkeringStrength * TinkeringSpeedIncrease);

            float deconstructionSpeed = item.StatManager.GetAdjustedValueMultiplicative(ItemTalentStats.DeconstructorSpeed, DeconstructionSpeed);

            if (DeconstructItemsSimultaneously)
            {
                float deconstructTime = 0.0f;
                foreach (Item targetItem in inputContainer.Inventory.AllItems)
                {
                    deconstructTime += targetItem.Prefab.DeconstructTime / (deconstructionSpeed * deconstructionSpeedModifier);
                }

                progressState = Math.Min(progressTimer / deconstructTime, 1.0f);
                if (progressTimer > deconstructTime)
                {
                    List<Item> items = inputContainer.Inventory.AllItems.ToList();
                    foreach (Item targetItem in items)
                    {
                        if ((Entity.Spawner?.IsInRemoveQueue(targetItem) ?? false) || !inputContainer.Inventory.AllItems.Contains(targetItem)) { continue; }
                        var validDeconstructItems = targetItem.Prefab.DeconstructItems.Where(it =>
                            it.IsValidDeconstructor(item) &&
                            (it.RequiredOtherItem.Length == 0 || it.RequiredOtherItem.Any(r => items.Any(it => it != targetItem && (it.HasTag(r) || it.Prefab.Identifier == r))))).ToList();

                        ProcessItem(targetItem, items, validDeconstructItems, allowRemove: validDeconstructItems.Any() || !targetItem.Prefab.DeconstructItems.Any());                        
                    }
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    progressTimer = 0.0f;
                    progressState = 0.0f;

                }
            }
            else
            {
                var targetItem = inputContainer.Inventory.LastOrDefault();
                if (targetItem == null) { return; }

                var validDeconstructItems = targetItem.Prefab.DeconstructItems.Where(it => it.IsValidDeconstructor(item)).ToList();
                float deconstructTime = validDeconstructItems.Any() ? targetItem.Prefab.DeconstructTime / (deconstructionSpeed * deconstructionSpeedModifier) : 1.0f;

                progressState = Math.Min(progressTimer / deconstructTime, 1.0f);
                if (progressTimer > deconstructTime)
                {
                    ProcessItem(targetItem, inputContainer.Inventory.AllItemsMod, validDeconstructItems, allowRemove: validDeconstructItems.Any() || !targetItem.Prefab.DeconstructItems.Any());

#if SERVER
                    item.CreateServerEvent(this);
#endif
                    progressTimer = 0.0f;
                    progressState = 0.0f;

                }
            }
        }

        private void ProcessItem(Item targetItem, IEnumerable<Item> inputItems, List<DeconstructItem> validDeconstructItems, bool allowRemove = true)
        {
            // In multiplayer, the server handles the deconstruction into new items
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            float amountMultiplier = 1f;

            if (user != null && !user.Removed)
            {
                var abilityTargetItem = new AbilityDeconstructedItem(targetItem, user);
                user.CheckTalents(AbilityEffectType.OnItemDeconstructed, abilityTargetItem);

                foreach (Character character in Character.GetFriendlyCrew(user))
                {
                    character.CheckTalents(AbilityEffectType.OnItemDeconstructedByAlly, abilityTargetItem);
                }

                var itemCreationMultiplier = new AbilityItemCreationMultiplier(targetItem.Prefab, amountMultiplier);
                user.CheckTalents(AbilityEffectType.OnItemDeconstructedMaterial, itemCreationMultiplier);
                amountMultiplier = (int)itemCreationMultiplier.Value;
            }

            if (targetItem.Prefab.RandomDeconstructionOutput)
            {
                int amount = targetItem.Prefab.RandomDeconstructionOutputAmount;
                List<int> deconstructItemIndexes = new List<int>();
                for (int i = 0; i < validDeconstructItems.Count; i++)
                {
                    deconstructItemIndexes.Add(i);
                }
                List<float> commonness = validDeconstructItems.Select(i => i.Commonness).ToList();
                List<DeconstructItem> products = new List<DeconstructItem>();

                for (int i = 0; i < amount; i++)
                {
                    if (deconstructItemIndexes.Count < 1) { break; }
                    var itemIndex = ToolBox.SelectWeightedRandom(deconstructItemIndexes, commonness, Rand.RandSync.Unsynced);
                    products.Add(validDeconstructItems[itemIndex]);
                    var removeIndex = deconstructItemIndexes.IndexOf(itemIndex);
                    deconstructItemIndexes.RemoveAt(removeIndex);
                    commonness.RemoveAt(removeIndex);
                }

                foreach (DeconstructItem deconstructProduct in products)
                {
                    CreateDeconstructProduct(deconstructProduct, inputItems, (int)(amountMultiplier * deconstructProduct.Amount));
                }
            }
            else
            {
                foreach (DeconstructItem deconstructProduct in validDeconstructItems)
                {
                    CreateDeconstructProduct(deconstructProduct, inputItems, (int)(amountMultiplier * deconstructProduct.Amount));
                }
            }

            void CreateDeconstructProduct(DeconstructItem deconstructProduct, IEnumerable<Item> inputItems, int amount)
            {
                float percentageHealth = targetItem.Condition / targetItem.MaxCondition;

                if (percentageHealth < deconstructProduct.MinCondition || percentageHealth > deconstructProduct.MaxCondition) { return; }

                if (MapEntityPrefab.FindByIdentifier(deconstructProduct.ItemIdentifier) is not ItemPrefab itemPrefab)
                {
                    DebugConsole.ThrowError("Tried to deconstruct item \"" + targetItem.Name + "\" but couldn't find item prefab \"" + deconstructProduct.ItemIdentifier + "\"!");
                    return;
                }

                float condition = deconstructProduct.CopyCondition ?
                    percentageHealth * itemPrefab.Health * deconstructProduct.OutConditionMax :
                    itemPrefab.Health * Rand.Range(deconstructProduct.OutConditionMin, deconstructProduct.OutConditionMax);

                if (DeconstructItemsSimultaneously && deconstructProduct.RequiredOtherItem.Length > 0)
                {
                    foreach (Item otherItem in inputItems)
                    {
                        if (targetItem == otherItem) { continue; }
                        if (deconstructProduct.RequiredOtherItem.Any(r => otherItem.HasTag(r) || r == otherItem.Prefab.Identifier))
                        {
                            var targetGeneticMaterial = targetItem.GetComponent<GeneticMaterial>();
                            var otherGeneticMaterial = otherItem.GetComponent<GeneticMaterial>();
                            if (targetGeneticMaterial != null && otherGeneticMaterial != null)
                            {
                                var result = targetGeneticMaterial.Combine(otherGeneticMaterial, user, out Item itemToDestroy);
                                if (result == GeneticMaterial.CombineResult.Refined)
                                {
                                    inputContainer.Inventory.RemoveItem(otherItem);
                                    OutputContainer.Inventory.RemoveItem(otherItem);
                                    Entity.Spawner.AddItemToRemoveQueue(itemToDestroy);
                                }
                                if (result != GeneticMaterial.CombineResult.None)
                                {
                                    OnCombinedOrRefined();
                                }
                                allowRemove = false;
                                return;
                            }
                            else
                            {
                                inputContainer.Inventory.RemoveItem(otherItem);
                                OutputContainer.Inventory.RemoveItem(otherItem);
                                Entity.Spawner.AddItemToRemoveQueue(otherItem);
                                OnCombinedOrRefined();
                            }
                        }
                    }

                    void OnCombinedOrRefined()
                    {
                        user?.CheckTalents(AbilityEffectType.OnGeneticMaterialCombinedOrRefined);
                        foreach (Character character in Character.GetFriendlyCrew(user))
                        {
                            character.CheckTalents(AbilityEffectType.OnCrewGeneticMaterialCombinedOrRefined);
                        }
                    }
                }

                if (user != null && !user.Removed)
                {
                    // used to spawn items directly into the deconstructor
                    var itemDeconstructedInventory = new AbilityItemDeconstructedInventory(targetItem.Prefab, item);
                    user.CheckTalents(AbilityEffectType.OnItemDeconstructedInventory, itemDeconstructedInventory);
                }

                for (int i = 0; i < amount; i++)
                {
                    Entity.Spawner.AddItemToSpawnQueue(itemPrefab, outputContainer.Inventory, condition, onSpawned: (Item spawnedItem) =>
                    {
                        spawnedItem.StolenDuringRound = targetItem.StolenDuringRound;
                        spawnedItem.AllowStealing = targetItem.AllowStealing;
                        spawnedItem.OriginalOutpost = targetItem.OriginalOutpost;
                        spawnedItem.SpawnedInCurrentOutpost = targetItem.SpawnedInCurrentOutpost;
                        if (RelocateOutputToMainSub && user is { AIController: HumanAIController humanAi })
                        {
                            humanAi.HandleRelocation(spawnedItem);
                        }
                        for (int i = 0; i < outputContainer.Capacity; i++)
                        {
                            var containedItem = outputContainer.Inventory.GetItemAt(i);
                            bool combined = false;
                            if (containedItem?.OwnInventory != null)
                            {
                                foreach (Item subItem in containedItem.ContainedItems.ToList())
                                {
                                    if (subItem.Combine(spawnedItem, null)) 
                                    {
                                        combined = true;
                                        break; 
                                    }
                                }
                            }
                            if (!combined)
                            {
                                if (containedItem?.Combine(spawnedItem, null) ?? false)
                                {
                                    break;
                                }
                            }
                        }
                        PutItemsToLinkedContainer();
                    });
                }
            }

            if (targetItem.Prefab.ContentPackage == ContentPackageManager.VanillaCorePackage &&
                /* we don't need info of every item, we can get a good sample size just by logging 5% */
                Rand.Range(0.0f, 1.0f) < 0.05f)
            {
                GameAnalyticsManager.AddDesignEvent("ItemDeconstructed:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "none") + ":" + targetItem.Prefab.Identifier);
            }

            if (targetItem.AllowDeconstruct && allowRemove)
            {
                //drop all items that are inside the deconstructed item
                foreach (ItemContainer ic in targetItem.GetComponents<ItemContainer>())
                {
                    if (ic?.Inventory == null || ic.RemoveContainedItemsOnDeconstruct) { continue; }
                    foreach (Item outputItem in ic.Inventory.AllItemsMod)
                    {
                        tryPutInOutputSlots(outputItem);
                        if (RelocateOutputToMainSub && user != null && user.AIController is HumanAIController humanAi)
                        {
                            humanAi.HandleRelocation(outputItem);
                        }
                    }
                }
                inputContainer.Inventory.RemoveItem(targetItem);
                Entity.Spawner.AddItemToRemoveQueue(targetItem);
                MoveInputQueue();
                PutItemsToLinkedContainer();
            }
            else
            {
                if (Entity.Spawner?.IsInRemoveQueue(targetItem) ?? false)
                {
                    targetItem.Drop(dropper: null);
                }
                else
                {
                    tryPutInOutputSlots(targetItem);
                }
            }

            void tryPutInOutputSlots(Item item)
            {
                for (int i = 0; i < outputContainer.Capacity; i++)
                {
                    var containedItem = outputContainer.Inventory.GetItemAt(i);
                    if (containedItem?.OwnInventory != null && containedItem.GetComponent<GeneticMaterial>() == null && containedItem.OwnInventory.TryPutItem(item, user: null))
                    {
                        return;
                    }
                }
                if (!outputContainer.Inventory.TryPutItem(item, user: null))
                {
                    item.Drop(dropper: null);
                }
            }
        }

        private void PutItemsToLinkedContainer()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (outputContainer.Inventory.IsEmpty()) { return; }

            foreach (MapEntity linkedTo in item.linkedTo)
            {
                if (linkedTo is Item linkedItem)
                {
                    var fabricator = linkedItem.GetComponent<Fabricator>();
                    if (fabricator != null) { continue; }
                    var itemContainer = linkedItem.GetComponent<ItemContainer>();
                    if (itemContainer == null) { continue; }
                    outputContainer.Inventory.AllItemsMod.ForEach(containedItem => itemContainer.Inventory.TryPutItem(containedItem, user: null, createNetworkEvent: true));
                }
            }
        }

        /// <summary>
        /// Move items towards the last slot in the inventory if there's free slots
        /// </summary>
        private void MoveInputQueue()
        {
            for (int i = inputContainer.Inventory.Capacity - 2; i >= 0; i--)
            {
                while (inputContainer.Inventory.GetItemAt(i) is Item item1 && inputContainer.Inventory.CanBePutInSlot(item1, i + 1))
                {
                    if (!inputContainer.Inventory.TryPutItem(item1, i + 1, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: true))
                    {
                        break;
                    }
                }
            }
        }

        private IEnumerable<(Item item, DeconstructItem output)> GetAvailableOutputs(bool checkRequiredOtherItems = true)
        {
            var items = inputContainer.Inventory.AllItems;
            foreach (Item inputItem in items)
            {
                if (!inputItem.AllowDeconstruct) { continue; }
                
                foreach (var deconstructItem in inputItem.Prefab.DeconstructItems)
                {
                    // check for deconstructor compatibility (for example, 'geneticresearchstation' tag)
                    if (deconstructItem.RequiredDeconstructor.Length > 0)
                    {
                        if (deconstructItem.RequiredDeconstructor.None(requiredId => item.HasTag(requiredId) || item.Prefab.Identifier == requiredId)) { continue; }
                    }
                    
                    // check for other required items in the same deconstructor (for example, 'geneticmaterial')
                    if (deconstructItem.RequiredOtherItem.Length > 0 && checkRequiredOtherItems)
                    {
                        // no matching item with the required id, skip
                        if (deconstructItem.RequiredOtherItem.None(requiredId => items.Any(it => it.HasTag(requiredId) || it.Prefab.Identifier == requiredId))) { continue; }
                        
                        bool validOtherItemFound = false;
                        foreach (Item otherInputItem in items)
                        {
                            if (otherInputItem == inputItem) { continue; }
                            if (deconstructItem.RequiredOtherItem.None(requiredId => otherInputItem.HasTag(requiredId) || otherInputItem.Prefab.Identifier == requiredId)) { continue; }

                            // skip if genetic materials cannot be combined (or refined)
                            var geneticMaterial = inputItem.GetComponent<GeneticMaterial>();
                            var otherGeneticMaterial = otherInputItem.GetComponent<GeneticMaterial>();
                            if (geneticMaterial != null && otherGeneticMaterial != null)
                            {
                                if (!geneticMaterial.CanBeCombinedWith(otherGeneticMaterial)) { continue; }
                            }
                            validOtherItemFound = true;
                        }
                        if (!validOtherItemFound) { continue; }
                    }
                    
                    yield return (inputItem, deconstructItem);
                }
            }
        }

        public void SetActive(bool active, Character user = null, bool createNetworkEvent = false)
        {
            PutItemsToLinkedContainer();

            this.user = user;
            RelocateOutputToMainSub = false;

            if (inputContainer.Inventory.IsEmpty()) { active = false; }

            IsActive = active;
            //currPowerConsumption = IsActive ? powerConsumption : 0.0f;
            userDeconstructorSpeedMultiplier = user != null ? 1f + user.GetStatValue(StatTypes.DeconstructorSpeedMultiplier) : 1f;

#if SERVER
            if (user != null)
            {
                GameServer.Log(GameServer.CharacterLogName(user) + (IsActive ? " activated " : " deactivated ") + item.Name, ServerLog.MessageType.ItemInteraction);
            }
            if (createNetworkEvent)
            {
                item.CreateServerEvent(this);
            }
#endif
            if (!IsActive)
            {
                progressTimer = 0.0f;
                progressState = 0.0f;
            }
#if CLIENT
            else
            {
                HintManager.OnStartDeconstructing(user, this); 
                if (Item.Submarine is { Info.IsOutpost: true } && user is { IsBot: true })
                {
                    HintManager.OnItemMarkedForRelocation();
                }
            }
#endif

            inputContainer.Inventory.Locked = IsActive;
        }
    }
    class AbilityDeconstructedItem : AbilityObject, IAbilityItem, IAbilityCharacter
    {
        public AbilityDeconstructedItem(Item item, Character character)
        {
            Item = item;
            Character = character;
        }
        public Item Item { get; set; }
        public Character Character { get; set; }
    }

    class AbilityItemCreationMultiplier : AbilityObject, IAbilityValue, IAbilityItemPrefab
    {
        public AbilityItemCreationMultiplier(ItemPrefab itemPrefab, float itemAmountMultiplier)
        {
            ItemPrefab = itemPrefab;
            Value = itemAmountMultiplier;
        }
        public ItemPrefab ItemPrefab { get; set; }
        public float Value { get; set; }
    }

    class AbilityItemDeconstructedInventory : AbilityObject, IAbilityItem, IAbilityItemPrefab
    {
        public AbilityItemDeconstructedInventory(ItemPrefab itemPrefab, Item item)
        {
            ItemPrefab = itemPrefab;
            Item = item;
        }
        public ItemPrefab ItemPrefab { get; set; }
        public Item Item { get; set; }
    }

}
