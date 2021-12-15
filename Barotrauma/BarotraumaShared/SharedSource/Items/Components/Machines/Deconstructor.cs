using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        private float progressTimer;
        private float progressState;

        private bool hasPower;

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

        [Serialize(false, true)]
        public bool DeconstructItemsSimultaneously { get; set; }

        [Editable, Serialize(1.0f, true)]
        public float DeconstructionSpeed { get; set; }

        public override bool RecreateGUIOnResolutionChange => true;

        public Deconstructor(Item item, XElement element)
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

            OnItemLoadedProjSpecific();
        }

        partial void OnItemLoadedProjSpecific();

        public override void Update(float deltaTime, Camera cam)
        {
            MoveInputQueue();

            if (inputContainer == null || inputContainer.Inventory.IsEmpty())
            {
                SetActive(false);
                return;
            }

            hasPower = Voltage >= MinVoltage;
            if (!hasPower) { return; }

            var repairable = item.GetComponent<Repairable>();
            if (repairable != null)
            {
                repairable.LastActiveTime = (float)Timing.TotalTime + 10.0f;
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerConsumption <= 0.0f) { Voltage = 1.0f; }
            progressTimer += deltaTime * Math.Min(Voltage, 1.0f);

            float tinkeringStrength = 0f;
            if (repairable.IsTinkering)
            {
                tinkeringStrength = repairable.TinkeringStrength;
            }
            // doesn't quite work properly, remaining time changes if tinkering stops
            float deconstructionSpeedModifier = userDeconstructorSpeedMultiplier * (1f + tinkeringStrength * TinkeringSpeedIncrease);

            if (DeconstructItemsSimultaneously)
            {
                float deconstructTime = 0.0f;
                foreach (Item targetItem in inputContainer.Inventory.AllItems)
                {
                    deconstructTime += targetItem.Prefab.DeconstructTime / (DeconstructionSpeed * deconstructionSpeedModifier);
                }

                progressState = Math.Min(progressTimer / deconstructTime, 1.0f);
                if (progressTimer > deconstructTime)
                {
                    List<Item> items = inputContainer.Inventory.AllItems.ToList();
                    foreach (Item targetItem in items)
                    {
                        if ((Entity.Spawner?.IsInRemoveQueue(targetItem) ?? false) || !inputContainer.Inventory.AllItems.Contains(targetItem)) { continue; }
                        var validDeconstructItems = targetItem.Prefab.DeconstructItems.FindAll(it =>
                            (it.RequiredDeconstructor.Length == 0 || it.RequiredDeconstructor.Any(r => item.HasTag(r) || item.Prefab.Identifier.Equals(r, StringComparison.OrdinalIgnoreCase))) &&
                            (it.RequiredOtherItem.Length == 0 || it.RequiredOtherItem.Any(r => items.Any(it => it != targetItem && (it.HasTag(r) || it.Prefab.Identifier.Equals(r, StringComparison.OrdinalIgnoreCase))))));

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

                var validDeconstructItems = targetItem.Prefab.DeconstructItems.FindAll(it =>
                    it.RequiredDeconstructor.Length == 0 || it.RequiredDeconstructor.Any(r => item.HasTag(r) || item.Prefab.Identifier.Equals(r, StringComparison.OrdinalIgnoreCase)));

                float deconstructTime = validDeconstructItems.Any() ? targetItem.Prefab.DeconstructTime / (DeconstructionSpeed * deconstructionSpeedModifier) : 1.0f;

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

                var itemCreationMultiplier = new AbilityValueItem(amountMultiplier, targetItem.Prefab);
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
                    CreateDeconstructProduct(deconstructProduct, inputItems, amountMultiplier);
                }
            }
            else
            {
                foreach (DeconstructItem deconstructProduct in validDeconstructItems)
                {
                    CreateDeconstructProduct(deconstructProduct, inputItems, amountMultiplier);
                }
            }

            void CreateDeconstructProduct(DeconstructItem deconstructProduct, IEnumerable<Item> inputItems, float amountMultiplier)
            {
                float percentageHealth = targetItem.Condition / targetItem.MaxCondition;

                if (percentageHealth < deconstructProduct.MinCondition || percentageHealth > deconstructProduct.MaxCondition) { return; }

                if (!(MapEntityPrefab.Find(null, deconstructProduct.ItemIdentifier) is ItemPrefab itemPrefab))
                {
                    DebugConsole.ThrowError("Tried to deconstruct item \"" + targetItem.Name + "\" but couldn't find item prefab \"" + deconstructProduct.ItemIdentifier + "\"!");
                    return;
                }

                float condition = deconstructProduct.CopyCondition ?
                    percentageHealth * itemPrefab.Health :
                    itemPrefab.Health * Rand.Range(deconstructProduct.OutConditionMin, deconstructProduct.OutConditionMax);

                if (DeconstructItemsSimultaneously && deconstructProduct.RequiredOtherItem.Length > 0)
                {
                    foreach (Item otherItem in inputItems)
                    {
                        if (targetItem == otherItem) { continue; }
                        if (deconstructProduct.RequiredOtherItem.Any(r => otherItem.HasTag(r) || r.Equals(otherItem.Prefab.Identifier, StringComparison.OrdinalIgnoreCase)))
                        {
                            user?.CheckTalents(AbilityEffectType.OnGeneticMaterialCombinedOrRefined);
                            foreach (Character character in Character.GetFriendlyCrew(user))
                            {
                                character.CheckTalents(AbilityEffectType.OnCrewGeneticMaterialCombinedOrRefined);
                            }

                            var geneticMaterial1 = targetItem.GetComponent<GeneticMaterial>();
                            var geneticMaterial2 = otherItem.GetComponent<GeneticMaterial>();
                            if (geneticMaterial1 != null && geneticMaterial2 != null)
                            {
                                if (geneticMaterial1.Combine(geneticMaterial2, user))
                                {
                                    inputContainer.Inventory.RemoveItem(otherItem);
                                    OutputContainer.Inventory.RemoveItem(otherItem);
                                    Entity.Spawner.AddToRemoveQueue(otherItem);
                                }
                                allowRemove = false;
                                return;
                            }
                            inputContainer.Inventory.RemoveItem(otherItem);
                            OutputContainer.Inventory.RemoveItem(otherItem);
                            Entity.Spawner.AddToRemoveQueue(otherItem);
                        }
                    }
                }

                if (user != null && !user.Removed)
                {
                    // used to spawn items directly into the deconstructor
                    var itemContainer = new AbilityItemPrefabItem(item, targetItem.Prefab);
                    user.CheckTalents(AbilityEffectType.OnItemDeconstructedInventory, itemContainer);
                }

                int amount = (int)amountMultiplier;

                for (int i = 0; i < amount; i++)
                {
                    Entity.Spawner.AddToSpawnQueue(itemPrefab, outputContainer.Inventory, condition, onSpawned: (Item spawnedItem) =>
                    {
                        spawnedItem.StolenDuringRound = targetItem.StolenDuringRound;
                        spawnedItem.AllowStealing = targetItem.AllowStealing;
                        for (int i = 0; i < outputContainer.Capacity; i++)
                        {
                            var containedItem = outputContainer.Inventory.GetItemAt(i);
                            if (containedItem?.OwnInventory != null)
                            {
                                foreach (Item subItem in containedItem.ContainedItems.ToList())
                                {
                                    if (subItem.Combine(spawnedItem, null))
                                    {
                                        break;
                                    }
                                }
                            }
                            else if (containedItem?.Combine(spawnedItem, null) ?? false)
                            {
                                break;
                            }
                        }
                        PutItemsToLinkedContainer();
                    });
                }
            }

            if (targetItem.AllowDeconstruct && allowRemove)
            {
                //drop all items that are inside the deconstructed item
                foreach (ItemContainer ic in targetItem.GetComponents<ItemContainer>())
                {
                    if (ic?.Inventory == null || ic.RemoveContainedItemsOnDeconstruct) { continue; }
                    foreach (Item containedItem in ic.Inventory.AllItemsMod)
                    {
                        if (!outputContainer.Inventory.TryPutItem(containedItem, user: null))
                        {
                            containedItem.Drop(dropper: null);
                        }
                    }
                }
                inputContainer.Inventory.RemoveItem(targetItem);
                Entity.Spawner.AddToRemoveQueue(targetItem);
                MoveInputQueue();
                PutItemsToLinkedContainer();
            }
            else
            {
                if (!outputContainer.Inventory.CanBePut(targetItem) || (Entity.Spawner?.IsInRemoveQueue(targetItem) ?? false))
                {
                    targetItem.Drop(dropper: null);
                }
                else
                {
                    outputContainer.Inventory.TryPutItem(targetItem, user: null, createNetworkEvent: true);
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
                    if (deconstructItem.RequiredDeconstructor.Length > 0)
                    {
                        if (!deconstructItem.RequiredDeconstructor.Any(r => item.HasTag(r) || item.Prefab.Identifier.Equals(r, StringComparison.OrdinalIgnoreCase))) { continue; }
                    }
                    if (deconstructItem.RequiredOtherItem.Length > 0 && checkRequiredOtherItems)
                    {
                        if (!deconstructItem.RequiredOtherItem.Any(r => items.Any(it => it.HasTag(r) || it.Prefab.Identifier.Equals(r, StringComparison.OrdinalIgnoreCase)))) { continue; }
                        bool validOtherItemFound = false;
                        foreach (Item otherInputItem in items)
                        {
                            if (otherInputItem == inputItem) { continue; }
                            if (!deconstructItem.RequiredOtherItem.Any(r => otherInputItem.HasTag(r) || otherInputItem.Prefab.Identifier.Equals(r, StringComparison.OrdinalIgnoreCase))) { continue; }

                            var geneticMaterial1 = inputItem.GetComponent<GeneticMaterial>();
                            var geneticMaterial2 = otherInputItem.GetComponent<GeneticMaterial>();
                            if (geneticMaterial1 != null && geneticMaterial2 != null)
                            {
                                if (!geneticMaterial1.CanBeCombinedWith(geneticMaterial2)) { continue; }
                            }
                            validOtherItemFound = true;
                        }
                        if (!validOtherItemFound) { continue; }
                    }
                    yield return (inputItem, deconstructItem);
                }
            }
        }

        private void SetActive(bool active, Character user = null)
        {
            PutItemsToLinkedContainer();

            this.user = user;

            if (inputContainer.Inventory.IsEmpty()) { active = false; }

            IsActive = active;
            currPowerConsumption = IsActive ? powerConsumption : 0.0f;
            userDeconstructorSpeedMultiplier = user != null ? 1f + user.GetStatValue(StatTypes.DeconstructorSpeedMultiplier) : 1f;

#if SERVER
            if (user != null)
            {
                GameServer.Log(GameServer.CharacterLogName(user) + (IsActive ? " activated " : " deactivated ") + item.Name, ServerLog.MessageType.ItemInteraction);
            }
#endif
            if (!IsActive)
            {
                progressTimer = 0.0f;
                progressState = 0.0f;
            }

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

}
