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

        public readonly List<ItemPrefab> RequiredItems;

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

            RequiredItems = new List<ItemPrefab>();

            string[] requiredItemNames = ToolBox.GetAttributeString(element, "requireditems", "").Split(',');
            foreach (string requiredItemName in requiredItemNames)
            {
                ItemPrefab requiredItem = ItemPrefab.list.Find(ip => ip.Name.ToLower() == requiredItemName.Trim().ToLower()) as ItemPrefab;
                if (requiredItem == null) continue;
                RequiredItems.Add(requiredItem);
            }

            RequiredTime = ToolBox.GetAttributeFloat(element, "requiredtime", 1.0f);
        }
    }

    class Fabricator : ItemComponent
    {
        List<FabricableItem> fabricableItems;

        GUIListBox itemList;

        GUIFrame selectedItemFrame;

        FabricableItem fabricatedItem;
        float timeUntilReady;

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
            selectedItemFrame = new GUIFrame(new Rectangle(0,0,(int)(GuiFrame.Rect.Width*0.4f),200), Color.Black*0.8f, Alignment.CenterY | Alignment.Right, null, GuiFrame);

            selectedItemFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

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
                foreach (ItemPrefab ip in targetItem.RequiredItems)
                {
                    text += "   - " + ip.Name + "\n";
                }
                text += "Required time: "+targetItem.RequiredTime+" s";

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 50, 0, 25),
                    text,
                    Color.Transparent, Color.White,
                    Alignment.TopLeft,
                    Alignment.TopLeft, null,
                    selectedItemFrame);

                GUIButton button = new GUIButton(new Rectangle(0,0,100,20), "Create", Color.White, Alignment.CenterX | Alignment.Bottom, GUI.Style, selectedItemFrame);
                button.OnClicked = StartFabricating;
                button.UserData = targetItem;


            }

            return true;
        }

        public override bool Pick(Character picker)
        {
            return (picker != null);
        }

        private bool StartFabricating(GUIButton button, object obj)
        {
            GUIComponent listElement = itemList.GetChild(obj);
            
            listElement.Color = Color.Green;
            itemList.Enabled = false;

            fabricatedItem = obj as FabricableItem;
            IsActive = true;

            timeUntilReady = fabricatedItem.RequiredTime;

            return true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            timeUntilReady -= deltaTime;

            if (timeUntilReady > 0.0f) return;

            var containers = item.GetComponents<ItemContainer>();
            if (containers.Count<2)
            {
                DebugConsole.ThrowError("Error while fabricating a new item: fabricators must have two ItemContainer components");
                return;
            }

            foreach (ItemPrefab ip in fabricatedItem.RequiredItems)
            {
                var requiredItem = containers[0].Inventory.Items.FirstOrDefault(it => it != null && it.Prefab == ip);
                containers[0].Inventory.RemoveItem(requiredItem);
            }
                        
            Item.Spawner.QueueItem(fabricatedItem.TargetItem, containers[1].Inventory);

            itemList.Enabled = true;
            IsActive = false;
            fabricatedItem = null;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            FabricableItem targetItem = itemList.SelectedData as FabricableItem;
            if (targetItem != null)
            {
                selectedItemFrame.GetChild<GUIButton>().Enabled = true;

                ItemContainer container = item.GetComponent<ItemContainer>();
                foreach (ItemPrefab ip in targetItem.RequiredItems)
                {
                    if (Array.Find(container.Inventory.Items, it => it != null && it.Prefab == ip) != null) continue;
                    selectedItemFrame.GetChild<GUIButton>().Enabled = false;
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
    }
}
