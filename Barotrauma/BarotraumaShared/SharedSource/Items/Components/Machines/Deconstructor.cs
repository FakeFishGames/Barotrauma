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

        private ItemContainer inputContainer, outputContainer;

        public ItemContainer InputContainer
        {
            get { return inputContainer; }
        }

        public ItemContainer OutputContainer
        {
            get { return outputContainer; }
        }
        
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

            var targetItem = inputContainer.Inventory.LastOrDefault();
            if (targetItem == null) { return; }

            float deconstructTime = targetItem.Prefab.DeconstructItems.Any() ? targetItem.Prefab.DeconstructTime / DeconstructionSpeed : 1.0f;

            progressState = Math.Min(progressTimer / deconstructTime, 1.0f);
            if (progressTimer > deconstructTime)
            {
                // In multiplayer, the server handles the deconstruction into new items
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

                if (targetItem.Prefab.RandomDeconstructionOutput)
                {
                    int amount = targetItem.Prefab.RandomDeconstructionOutputAmount;
                    List<int> deconstructItemIndexes = new List<int>();
                    for (int i = 0; i < targetItem.Prefab.DeconstructItems.Count; i++)
                    {
                        deconstructItemIndexes.Add(i);
                    }
                    List<float> commonness = targetItem.Prefab.DeconstructItems.Select(i => i.Commonness).ToList();
                    List<DeconstructItem> products = new List<DeconstructItem>();

                    for (int i = 0; i < amount; i++)
                    {
                        if (deconstructItemIndexes.Count < 1) { break; }
                        var itemIndex = ToolBox.SelectWeightedRandom(deconstructItemIndexes, commonness, Rand.RandSync.Unsynced);
                        products.Add(targetItem.Prefab.DeconstructItems[itemIndex]);
                        var removeIndex = deconstructItemIndexes.IndexOf(itemIndex);
                        deconstructItemIndexes.RemoveAt(removeIndex);
                        commonness.RemoveAt(removeIndex);
                    }
                    foreach (DeconstructItem deconstructProduct in products)
                    {
                        CreateDeconstructProduct(deconstructProduct);
                    }
                }
                else
                {
                    foreach (DeconstructItem deconstructProduct in targetItem.Prefab.DeconstructItems)
                    {
                        CreateDeconstructProduct(deconstructProduct);
                    }
                }

                void CreateDeconstructProduct(DeconstructItem deconstructProduct)
                {
                    float percentageHealth = targetItem.Condition / targetItem.Prefab.Health;
                    if (percentageHealth <= deconstructProduct.MinCondition || percentageHealth > deconstructProduct.MaxCondition) { return; }

                    if (!(MapEntityPrefab.Find(null, deconstructProduct.ItemIdentifier) is ItemPrefab itemPrefab))
                    {
                        DebugConsole.ThrowError("Tried to deconstruct item \"" + targetItem.Name + "\" but couldn't find item prefab \"" + deconstructProduct.ItemIdentifier + "\"!");
                        return;
                    }

                    float condition = deconstructProduct.CopyCondition ?
                        percentageHealth * itemPrefab.Health :
                        itemPrefab.Health * deconstructProduct.OutCondition;

                    Entity.Spawner.AddToSpawnQueue(itemPrefab, outputContainer.Inventory, condition, onSpawned: (Item spawnedItem) =>
                    {
                        for (int i = 0; i < outputContainer.Capacity; i++)
                        {
                            var containedItem = outputContainer.Inventory.GetItemAt(i);
                            if (containedItem?.Combine(spawnedItem, null) ?? false)
                            {
                                break;
                            }
                        }
                        PutItemsToLinkedContainer();
                    });
                }

                if (targetItem.Prefab.AllowDeconstruct)
                {
                    //drop all items that are inside the deconstructed item
                    foreach (ItemContainer ic in targetItem.GetComponents<ItemContainer>())
                    {
                        if (ic?.Inventory == null || ic.RemoveContainedItemsOnDeconstruct) { continue; }
                        ic.Inventory.AllItemsMod.ForEach(containedItem => outputContainer.Inventory.TryPutItem(containedItem, user: null));
                    }
                    inputContainer.Inventory.RemoveItem(targetItem);
                    Entity.Spawner.AddToRemoveQueue(targetItem);
                    MoveInputQueue();
                    PutItemsToLinkedContainer();
                }
                else
                {
                    if (!outputContainer.Inventory.CanBePut(targetItem))
                    {
                        targetItem.Drop(dropper: null);
                    }
                    else
                    {
                        outputContainer.Inventory.TryPutItem(targetItem, user: null, createNetworkEvent: true);
                    }
                }
#if SERVER
                item.CreateServerEvent(this);
#endif
                progressTimer = 0.0f;
                progressState = 0.0f;
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

        private void SetActive(bool active, Character user = null)
        {
            PutItemsToLinkedContainer();

            if (inputContainer.Inventory.IsEmpty()) { active = false; }

            IsActive = active;
            currPowerConsumption = IsActive ? powerConsumption : 0.0f;
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

#if CLIENT
            activateButton.Text = TextManager.Get(IsActive ? "DeconstructorCancel" : "DeconstructorDeconstruct");
#endif

            inputContainer.Inventory.Locked = IsActive;
        }
    }
}
