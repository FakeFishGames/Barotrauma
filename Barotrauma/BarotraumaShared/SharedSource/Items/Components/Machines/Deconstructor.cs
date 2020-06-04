using Barotrauma.Networking;
using System;
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

            if (inputContainer == null || inputContainer.Inventory.Items.All(i => i == null))
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

            var targetItem = inputContainer.Inventory.Items.LastOrDefault(i => i != null);
            if (targetItem == null) { return; }

            float deconstructTime = targetItem.Prefab.DeconstructItems.Any() ? targetItem.Prefab.DeconstructTime : 1.0f;

            progressState = Math.Min(progressTimer / deconstructTime, 1.0f);
            if (progressTimer > deconstructTime)
            {
                int emptySlots = outputContainer.Inventory.Items.Where(i => i == null).Count();

                foreach (DeconstructItem deconstructProduct in targetItem.Prefab.DeconstructItems)
                {
                    float percentageHealth = targetItem.Condition / targetItem.Prefab.Health;
                    if (percentageHealth <= deconstructProduct.MinCondition || percentageHealth > deconstructProduct.MaxCondition) continue;

                    if (!(MapEntityPrefab.Find(null, deconstructProduct.ItemIdentifier) is ItemPrefab itemPrefab))
                    {
                        DebugConsole.ThrowError("Tried to deconstruct item \"" + targetItem.Name + "\" but couldn't find item prefab \"" + deconstructProduct.ItemIdentifier + "\"!");
                        continue;
                    }

                    float condition = deconstructProduct.CopyCondition ?
                        percentageHealth * itemPrefab.Health :
                        itemPrefab.Health * deconstructProduct.OutCondition;

                    //container full, drop the items outside the deconstructor
                    if (emptySlots <= 0)
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, item.Position, item.Submarine, condition);
                    }
                    else
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, outputContainer.Inventory, condition);
                        emptySlots--;
                    }
                }

                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    if (targetItem.Prefab.AllowDeconstruct)
                    {
                        //drop all items that are inside the deconstructed item
                        foreach (ItemContainer ic in targetItem.GetComponents<ItemContainer>())
                        {
                            if (ic?.Inventory?.Items == null) { continue; }
                            foreach (Item containedItem in ic.Inventory.Items)
                            {
                                containedItem?.Drop(dropper: null, createNetworkEvent: true);
                            }
                        }

                        inputContainer.Inventory.RemoveItem(targetItem);
                        Entity.Spawner.AddToRemoveQueue(targetItem);
                        MoveInputQueue();
                        PutItemsToLinkedContainer();
                    }
                    else
                    {
                        if (outputContainer.Inventory.Items.All(i => i != null))
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
        }

        private void PutItemsToLinkedContainer()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (outputContainer.Inventory.Items.All(it => it == null)) return;
            
            foreach (MapEntity linkedTo in item.linkedTo)
            {
                if (linkedTo is Item linkedItem)
                {
                    var fabricator = linkedItem.GetComponent<Fabricator>();
                    if (fabricator != null) { continue; }
                    var itemContainer = linkedItem.GetComponent<ItemContainer>();
                    if (itemContainer == null) { continue; }

                    foreach (Item containedItem in outputContainer.Inventory.Items)
                    {
                        if (containedItem == null) { continue; }
                        if (itemContainer.Inventory.Items.All(it => it != null)) { break; }
                        itemContainer.Inventory.TryPutItem(containedItem, user: null, createNetworkEvent: true);
                    }
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
                if (inputContainer.Inventory.Items[i] != null && inputContainer.Inventory.Items[i + 1] == null)
                {
                    inputContainer.Inventory.TryPutItem(inputContainer.Inventory.Items[i], i + 1, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: true);
                }
            }
        }

        private void SetActive(bool active, Character user = null)
        {
            PutItemsToLinkedContainer();

            if (inputContainer.Inventory.Items.All(i => i == null)) { active = false; }

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
