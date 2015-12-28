using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Deconstructor : ItemComponent
    {
        GUIProgressBar progressBar;
        GUIButton activateButton;

        float progressTimer;

        ItemContainer container;

        public Deconstructor(Item item, XElement element) 
            : base(item, element)
        {
            progressBar = new GUIProgressBar(new Rectangle(0,0,200,20), Color.Green, 0.0f, Alignment.BottomCenter, GuiFrame);

            activateButton = new GUIButton(new Rectangle(0, 0, 200, 20), "Deconstruct", Alignment.TopCenter, GUI.Style, GuiFrame);
            activateButton.OnClicked = Activate;
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

            progressTimer += deltaTime;

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

                foreach (string deconstructProduct in targetItem.Prefab.DeconstructItems)
                {
                    var itemPrefab = ItemPrefab.list.FirstOrDefault(ip => ip.Name.ToLower() == deconstructProduct.ToLower()) as ItemPrefab;
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
            GuiFrame.Draw(spriteBatch);
        }

        private bool Activate(GUIButton button, object obj)
        {
            container = item.GetComponent<ItemContainer>();
            if (container==null)
            {
                DebugConsole.ThrowError("Error in Deconstructor.Activate: Deconstructors must have two ItemContainer components");
                return false;
            }

            if (IsActive)
            {
                progressBar.BarSize = 0.0f;
                progressTimer = 0.0f;

                activateButton.Text = "Deconstruct";
            }
            else
            {
                if (!container.Inventory.Items.Any(i => i != null)) return false;

                activateButton.Text = "Cancel";
            }

            IsActive = !IsActive;

            container.Inventory.Locked = IsActive;
            
            return true;
        }
    }
}
