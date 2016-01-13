using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class FabricableItem
    {
        public readonly ItemPrefab TargetItem;

        public readonly List<Tuple<ItemPrefab, int>> RequiredItems;

        public readonly float RequiredTime;

        //ListOrSomething requiredLevels

        public FabricableItem(XElement element)
        {
            string name = ToolBox.GetAttributeString(element, "name", "").ToLower();

            TargetItem = ItemPrefab.list.Find(ip => ip.Name.ToLower() == name) as ItemPrefab;
            if (TargetItem == null)
            {
                DebugConsole.ThrowError("Error in Fabricable Item! Item ''" + element.Name + "'' not found.");
                return;
            }

            RequiredItems = new List<Tuple<ItemPrefab, int>>();

            string[] requiredItemNames = ToolBox.GetAttributeString(element, "requireditems", "").Split(',');
            foreach (string requiredItemName in requiredItemNames)
            {
                ItemPrefab requiredItem = ItemPrefab.list.Find(ip => ip.Name.ToLower() == requiredItemName.Trim().ToLower()) as ItemPrefab;
                if (requiredItem == null) continue;

                var existing = RequiredItems.Find(r => r.Item1 == requiredItem);

                if (existing == null)
                {

                    RequiredItems.Add(new Tuple<ItemPrefab, int>(requiredItem, 1));
                }
                else
                {
                    RequiredItems.Remove(existing);
                    RequiredItems.Add(new Tuple<ItemPrefab, int>(requiredItem, existing.Item2+1));
                }

            }

            RequiredTime = ToolBox.GetAttributeFloat(element, "requiredtime", 1.0f);
        }
    }

    class Fabricator : ItemComponent
    {
        private List<FabricableItem> fabricableItems;

        private GUIListBox itemList;

        private GUIFrame selectedItemFrame;

        GUIProgressBar progressBar;
        GUIButton activateButton;

        private FabricableItem fabricatedItem;
        private float timeUntilReady;

        private float lastNetworkUpdate;

        public Fabricator(Item item, XElement element) 
            : base(item, element)
        {
            fabricableItems = new List<FabricableItem>();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString() != "fabricableitem") continue;

                FabricableItem fabricableItem = new FabricableItem(subElement);
                if (fabricableItem.TargetItem != null) fabricableItems.Add(fabricableItem);                
            }

            GuiFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            itemList = new GUIListBox(new Rectangle(0,0,GuiFrame.Rect.Width/2-20,0), GUI.Style, GuiFrame);
            itemList.OnSelected = SelectItem;
            //structureList.CheckSelected = MapEntityPrefab.GetSelected;

            foreach (FabricableItem fi in fabricableItems)
            {
                Color color = ((itemList.CountChildren % 2) == 0) ? Color.Transparent : Color.Black*0.3f;
                
                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),  fi.TargetItem.Name,
                    color, Color.White,
                    Alignment.Left,  Alignment.Left, null, itemList);
                textBlock.UserData = fi;
                textBlock.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                textBlock.HoverColor = Color.Gold * 0.2f;
                textBlock.SelectedColor = Color.Gold * 0.5f;
            }
        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            FabricableItem targetItem = obj as FabricableItem;
            if (targetItem == null) return false;

            if (selectedItemFrame != null) GuiFrame.RemoveChild(selectedItemFrame);

            //int width = 200, height = 150;
            selectedItemFrame = new GUIFrame(new Rectangle(0,0,(int)(GuiFrame.Rect.Width*0.4f),250), Color.Black*0.8f, Alignment.CenterY | Alignment.Right, null, GuiFrame);

            selectedItemFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            progressBar = new GUIProgressBar(new Rectangle(0, 0, 0, 20), Color.Green, GUI.Style, 0.0f, Alignment.BottomCenter, selectedItemFrame);
            progressBar.IsHorizontal = true;

            if (targetItem.TargetItem.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(10, 0, 40, 40), targetItem.TargetItem.sprite, Alignment.TopLeft, selectedItemFrame);
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
                img.Color = targetItem.TargetItem.SpriteColor;

                new GUITextBlock(
                    new Rectangle(60, 0, 0, 25),
                    targetItem.TargetItem.Name,
                    Color.Transparent, Color.White,
                    Alignment.TopLeft,
                    Alignment.TopLeft, null,
                    selectedItemFrame, true);

                string text = "Required items:\n";
                foreach (Tuple<ItemPrefab,int> ip in targetItem.RequiredItems)
                {
                    text += "   - " + ip.Item1.Name + " x"+ip.Item2+"\n";
                }
                text += "Required time: " + targetItem.RequiredTime + " s";

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 50, 0, 25),
                    text,
                    Color.Transparent, Color.White,
                    Alignment.TopLeft,
                    Alignment.TopLeft, null,
                    selectedItemFrame);

                activateButton = new GUIButton(new Rectangle(0, -30, 100, 20), "Create", Color.White, Alignment.CenterX | Alignment.Bottom, GUI.Style, selectedItemFrame);
                activateButton.OnClicked = StartButtonClicked;
                activateButton.UserData = targetItem;
                activateButton.Enabled = false;
            }

            return true;
        }

        public override bool Pick(Character picker)
        {
            return (picker != null);
        }

        private bool StartButtonClicked(GUIButton button, object obj)
        {
            if (fabricatedItem == null)
            {
                StartFabricating(obj as FabricableItem);

                item.NewComponentEvent(this, true, true);
            }
            else
            {
                CancelFabricating();

                item.NewComponentEvent(this, true, true);
            }
            
            //listElement.Color = Color.Green;
            //itemList.Enabled = false;

            //activateButton.Text = "Cancel";

            //fabricatedItem = obj as FabricableItem;
            //IsActive = true;

            //timeUntilReady = fabricatedItem.RequiredTime;

            return true;
        }

        private void StartFabricating(FabricableItem selectedItem)
        {
            if (selectedItem == null) return;

            itemList.Enabled = false;

            activateButton.Text = "Cancel";

            fabricatedItem = selectedItem;
            IsActive = true;

            timeUntilReady = fabricatedItem.RequiredTime;

            var containers = item.GetComponents<ItemContainer>();
            containers[0].Inventory.Locked = true;
            containers[1].Inventory.Locked = true;
        }

        private void CancelFabricating()
        {
            itemList.Enabled = true;
            IsActive = false;
            fabricatedItem = null;

            if (activateButton != null)
            {
                activateButton.Text = "Create";
            }
            if (progressBar != null) progressBar.BarSize = 0.0f;

            timeUntilReady = 0.0f;

            var containers = item.GetComponents<ItemContainer>();
            containers[0].Inventory.Locked = false;
            containers[1].Inventory.Locked = false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            timeUntilReady -= deltaTime;

            if (progressBar!=null)
            {
                progressBar.BarSize = fabricatedItem == null ? 0.0f : (fabricatedItem.RequiredTime - timeUntilReady) / fabricatedItem.RequiredTime;
            }

            if (timeUntilReady > 0.0f) return;

            var containers = item.GetComponents<ItemContainer>();
            if (containers.Count<2)
            {
                DebugConsole.ThrowError("Error while fabricating a new item: fabricators must have two ItemContainer components");
                return;
            }

            foreach (Tuple<ItemPrefab,int> ip in fabricatedItem.RequiredItems)
            {
                var requiredItem = containers[0].Inventory.Items.FirstOrDefault(it => it != null && it.Prefab == ip.Item1);
                containers[0].Inventory.RemoveItem(requiredItem);
            }
                        
            Item.Spawner.QueueItem(fabricatedItem.TargetItem, containers[1].Inventory);

            CancelFabricating();
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            FabricableItem targetItem = itemList.SelectedData as FabricableItem;
            if (targetItem != null)
            {
                activateButton.Enabled = true;

                ItemContainer container = item.GetComponent<ItemContainer>();
                foreach (Tuple<ItemPrefab,int> ip in targetItem.RequiredItems)
                {
                    if (Array.FindAll(container.Inventory.Items, it => it != null && it.Prefab == ip.Item1).Count() < ip.Item2) continue;
                    activateButton.Enabled = false;
                    break;
                }
            }

            GuiFrame.Update((float)Physics.step);
            GuiFrame.Draw(spriteBatch);

            //itemList.Update(0.016f);
            //itemList.Draw(spriteBatch);

            //if (selectedItemFrame != null)
            //{
            //    selectedItemFrame.Update(0.016f);
            //    selectedItemFrame.Draw(spriteBatch);
            //}
        }

        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            int itemIndex = fabricatedItem == null ? -1 : fabricableItems.IndexOf(fabricatedItem);

            message.WriteRangedInteger(-1, fabricableItems.Count-1, itemIndex);
            
            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message, float sendingTime)
        {
            if (sendingTime < lastNetworkUpdate) return;

            int itemIndex = message.ReadRangedInteger(-1, fabricableItems.Count-1);

            if (itemIndex == -1)
            {
                CancelFabricating();
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricableItems.IndexOf(fabricatedItem) != itemIndex) return;

                if (itemIndex < 0 || itemIndex >= fabricableItems.Count) return;

                SelectItem(null, fabricableItems[itemIndex]);
                StartFabricating(fabricableItems[itemIndex]);
                timeUntilReady -= sendingTime - (float)Lidgren.Network.NetTime.Now;
            }

            lastNetworkUpdate = sendingTime;
        }
    }
}
