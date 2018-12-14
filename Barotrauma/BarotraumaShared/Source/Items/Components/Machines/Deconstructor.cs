using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        private float progressTimer;
        private float progressState;

        private ItemContainer inputContainer, outputContainer;
        
        public Deconstructor(Item item, XElement element) 
            : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void OnItemLoaded()
        {
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
            if (inputContainer == null || inputContainer.Inventory.Items.All(i => i == null))
            {
                SetActive(false);
                return;
            }

            if (voltage < minVoltage) return;

            //move items towards the last slot in the inventory if there's free slots
            for (int i = inputContainer.Inventory.Capacity - 2; i >= 0; i--)
            {
                if (inputContainer.Inventory.Items[i] != null && inputContainer.Inventory.Items[i + 1] == null)
                {
                    inputContainer.Inventory.TryPutItem(inputContainer.Inventory.Items[i], i + 1, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: true);
                }
            }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (powerConsumption == 0.0f) voltage = 1.0f;

            progressTimer += deltaTime * voltage;
            Voltage -= deltaTime * 10.0f;

            var targetItem = inputContainer.Inventory.Items.LastOrDefault(i => i != null);
            if (targetItem == null) { return; }

            progressState = Math.Min(progressTimer / targetItem.Prefab.DeconstructTime, 1.0f);
            if (progressTimer > targetItem.Prefab.DeconstructTime)
            {
                foreach (DeconstructItem deconstructProduct in targetItem.Prefab.DeconstructItems)
                {
                    float percentageHealth = targetItem.Condition / targetItem.Prefab.Health;
                    if (percentageHealth <= deconstructProduct.MinCondition || percentageHealth > deconstructProduct.MaxCondition) continue;

                    var itemPrefab = MapEntityPrefab.Find(null, deconstructProduct.ItemIdentifier) as ItemPrefab;
                    if (itemPrefab == null)
                    {
                        DebugConsole.ThrowError("Tried to deconstruct item \"" + targetItem.Name + "\" but couldn't find item prefab \"" + deconstructProduct.ItemIdentifier + "\"!");
                        continue;
                    }

                    float condition = deconstructProduct.CopyCondition ?
                        percentageHealth * itemPrefab.Health :
                        itemPrefab.Health * deconstructProduct.OutCondition;
                    
                    //container full, drop the items outside the deconstructor
                    if (outputContainer.Inventory.Items.All(i => i != null))
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, item.Position, item.Submarine, condition);
                    }
                    else
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, outputContainer.Inventory, condition);
                    }
                }

                inputContainer.Inventory.RemoveItem(targetItem);
                Entity.Spawner.AddToRemoveQueue(targetItem);

                if (inputContainer.Inventory.Items.Any(i => i != null))
                {
                    progressTimer = 0.0f;
                }
            }
        }

        private void SetActive(bool active, Character user = null)
        {
            if (inputContainer.Inventory.Items.All(i => i == null)) active = false;

            IsActive = active;

            if (user != null)
            {
                GameServer.Log(user.LogName + (IsActive ? " activated " : " deactivated ") + item.Name, ServerLog.MessageType.ItemInteraction);
            }

            if (!IsActive) { progressState = 0.0f; }

#if CLIENT
            if (!IsActive)
            {
                progressTimer = 0.0f;
                activateButton.Text = TextManager.Get("DeconstructorDeconstruct");
            }
            else
            {
                activateButton.Text = TextManager.Get("DeconstructorCancel");
            }
#endif

            inputContainer.Inventory.Locked = IsActive;            
        }
        
        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            bool active = msg.ReadBoolean();

            item.CreateServerEvent(this);

            if (item.CanClientAccess(c))
            {
                SetActive(active, c.Character);
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsActive);
        }
    }
}
