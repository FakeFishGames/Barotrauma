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
            progressBar = new GUIProgressBar(new Rectangle(0,0,200,20), Color.Green, GUI.Style, 0.0f, Alignment.BottomCenter, GuiFrame);

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
                        DebugConsole.ThrowError("Tried to deconstruct item \""+targetItem.Name+"\" but couldn't find item prefab \""+deconstructProduct+"\"!");
                        continue;
                    }
                    Item.Spawner.AddToSpawnQueue(itemPrefab, containers[1].Inventory);
                }

                container.Inventory.RemoveItem(targetItem);
                Item.Spawner.AddToRemoveQueue(targetItem);

                activateButton.Text = "Deconstruct";
                progressBar.BarSize = 0.0f;
                progressTimer = 0.0f;
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
        
    }
}
