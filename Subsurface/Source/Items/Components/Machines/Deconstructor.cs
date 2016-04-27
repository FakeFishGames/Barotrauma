using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Deconstructor : Powered
    {
        GUIProgressBar progressBar;
        GUIButton activateButton;

        float progressTimer;

        ItemContainer container;

        private float lastNetworkUpdate;

        public Deconstructor(Item item, XElement element) 
            : base(item, element)
        {
            progressBar = new GUIProgressBar(new Rectangle(0,0,200,20), Color.Green, 0.0f, Alignment.BottomCenter, GuiFrame);

            activateButton = new GUIButton(new Rectangle(0, 0, 200, 20), "Deconstruct", Alignment.TopCenter, GUI.Style, GuiFrame);
            activateButton.OnClicked = ToggleActive;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (container == null || !container.Inventory.Items.Any(i=>i!=null))
            {
                progressBar.BarSize = 0.0f;
                //activateButton.Enabled = true;
                if (container != null) container.Inventory.Locked = false;
                IsActive = false;
                return;
            }

            if (voltage < minVoltage) return;

            if (powerConsumption == 0.0f) voltage = 1.0f;

            progressTimer += deltaTime*voltage;
            Voltage -= deltaTime * 10.0f;

            var targetItem = container.Inventory.Items.FirstOrDefault(i => i != null);
            progressBar.BarSize = Math.Min(progressTimer / targetItem.Prefab.DeconstructTime, 1.0f);
            if (progressTimer>targetItem.Prefab.DeconstructTime)
            {
                var containers = item.GetComponents<ItemContainer>();
                if (containers.Count<2)
                {
                    DebugConsole.ThrowError("Error in Deconstructor.Update: Deconstructors must have two ItemContainer components!");
                    
                    return;
                }

                foreach (DeconstructItem deconstructProduct in targetItem.Prefab.DeconstructItems)
                {
                    if (deconstructProduct.RequireFullCondition && targetItem.Condition < 100.0f) continue;

                    var itemPrefab = ItemPrefab.list.FirstOrDefault(ip => ip.Name.ToLowerInvariant() == deconstructProduct.ItemPrefabName.ToLowerInvariant()) as ItemPrefab;
                    if (itemPrefab==null)
                    {
                        DebugConsole.ThrowError("Tried to deconstruct item ''"+targetItem.Name+"'' but couldn't find item prefab ''"+deconstructProduct+"''!");
                        continue;
                    }
                    Item.Spawner.QueueItem(itemPrefab, containers[1].Inventory);
                }

                container.Inventory.RemoveItem(targetItem);
                Item.Remover.QueueItem(targetItem);

                activateButton.Text = "Deconstruct";
                progressBar.BarSize = 0.0f;
                progressTimer = 0.0f;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Update((float)Physics.step);
            GuiFrame.Draw(spriteBatch);
        }

        private bool ToggleActive(GUIButton button, object obj)
        {
            SetActive(!IsActive);

            currPowerConsumption = IsActive ? powerConsumption : 0.0f;

            item.NewComponentEvent(this, true, true);

            return true;
        }

        private void SetActive(bool active)
        {
            container = item.GetComponent<ItemContainer>();
            if (container == null)
            {
                DebugConsole.ThrowError("Error in Deconstructor.Activate: Deconstructors must have two ItemContainer components");
                return;
            }

            IsActive = active;

            if (!IsActive)
            {
                progressBar.BarSize = 0.0f;
                progressTimer = 0.0f;

                activateButton.Text = "Deconstruct";
            }
            else
            {
                if (container.Inventory.Items.All(i => i == null)) return;

                activateButton.Text = "Cancel";
            }

            container.Inventory.Locked = IsActive;
            
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {

            var containers = item.GetComponents<ItemContainer>();
            containers[0].Inventory.FillNetworkData(type, message, null);
            containers[1].Inventory.FillNetworkData(type, message, null);

            message.Write(IsActive);

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            if (sendingTime < lastNetworkUpdate) return;

            var containers = item.GetComponents<ItemContainer>();
            containers[0].Inventory.ReadNetworkData(type, message, sendingTime);
            containers[1].Inventory.ReadNetworkData(type, message, sendingTime);

            SetActive(message.ReadBoolean());

            lastNetworkUpdate = sendingTime;
        }
    }
}
