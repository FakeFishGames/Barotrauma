using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        GUIProgressBar progressBar;
        GUIButton activateButton;

        float progressTimer;

        ItemContainer container;
        
        public Deconstructor(Item item, XElement element) 
            : base(item, element)
        {
            progressBar = new GUIProgressBar(new Rectangle(0,0,200,20), Color.Green, "", 0.0f, Alignment.BottomCenter, GuiFrame);

            activateButton = new GUIButton(new Rectangle(0, 0, 200, 20), "Deconstruct", Alignment.TopCenter, "", GuiFrame);
            activateButton.OnClicked = ToggleActive;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (container == null || container.Inventory.Items.All(i => i == null))
            {
                SetActive(false);
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
                if (containers.Count < 2)
                {
                    DebugConsole.ThrowError("Error in Deconstructor.Update: Deconstructors must have two ItemContainer components!");

                    return;
                }

                foreach (DeconstructItem deconstructProduct in targetItem.Prefab.DeconstructItems)
                {
                    if (deconstructProduct.RequireFullCondition && targetItem.Condition < 100.0f) continue;

                    var itemPrefab = MapEntityPrefab.list.FirstOrDefault(ip => ip.Name.ToLowerInvariant() == deconstructProduct.ItemPrefabName.ToLowerInvariant()) as ItemPrefab;
                    if (itemPrefab == null)
                    {
                        DebugConsole.ThrowError("Tried to deconstruct item \"" + targetItem.Name + "\" but couldn't find item prefab \"" + deconstructProduct + "\"!");
                        continue;
                    }

                    //container full, drop the items outside the deconstructor
                    if (containers[1].Inventory.Items.All(i => i != null))
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, item.Position, item.Submarine);
                    }
                    else
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, containers[1].Inventory);
                    }
                }

                container.Inventory.RemoveItem(targetItem);
                Entity.Spawner.AddToRemoveQueue(targetItem);

                if (container.Inventory.Items.Any(i => i != null))
                {
                    progressTimer = 0.0f;
                    progressBar.BarSize = 0.0f;
                }
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update((float)Timing.Step);
        }

        private bool ToggleActive(GUIButton button, object obj)
        {
            SetActive(!IsActive);

            currPowerConsumption = IsActive ? powerConsumption : 0.0f;

            if (GameMain.Server != null)
            {
                item.CreateServerEvent<Deconstructor>(this);
            }
            else if (GameMain.Client != null)
            {
                item.CreateClientEvent<Deconstructor>(this);
            }
            
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

            if (container.Inventory.Items.All(i => i == null)) active = false;

            IsActive = active;

            if (!IsActive)
            {
                progressBar.BarSize = 0.0f;
                progressTimer = 0.0f;

                activateButton.Text = "Deconstruct";
            }
            else
            {

                activateButton.Text = "Cancel";
            }

            container.Inventory.Locked = IsActive;            
        }


        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            msg.Write(IsActive);
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            bool active = msg.ReadBoolean();

            item.CreateServerEvent<Deconstructor>(this);

            if (item.CanClientAccess(c))
            {
                SetActive(active);
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsActive);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            SetActive(msg.ReadBoolean());
        }
        
    }
}
