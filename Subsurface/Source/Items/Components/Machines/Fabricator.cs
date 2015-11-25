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
        //public static List<FabricableItem> list = new List<FabricableItem>();

        //public readonly string[] FabricatorTags;

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

            int width = 500, height = 300;
            itemList = new GUIListBox(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);
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

            int width = 200, height = 150;
            selectedItemFrame = new GUIFrame(new Rectangle(itemList.Rect.Right - width - 20, GameMain.GraphicsHeight/2-height/2, width, height), Color.Black*0.8f);
            //selectedItemFrame.Padding = GUI.style.smallPadding;

            if (targetItem.TargetItem.sprite != null)
            {
                GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), targetItem.TargetItem.sprite, Alignment.CenterX, selectedItemFrame);
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);

                string text = targetItem.TargetItem.Name + "\n";
                text += "Required items:\n";
                foreach (ItemPrefab ip in targetItem.RequiredItems)
                {
                    text += "   - " + ip.Name + "\n";
                }
                text += "Required time: "+targetItem.RequiredTime+" s";

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(0, 0, 0, 25),
                    text,
                    Color.Transparent, Color.White,
                    Alignment.CenterX | Alignment.CenterY,
                    Alignment.Left, null,
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

            ItemContainer container = item.GetComponent<ItemContainer>();
            foreach (ItemPrefab ip in fabricatedItem.RequiredItems)
            {
                var requiredItem = container.inventory.Items.FirstOrDefault(it => it != null && it.Prefab == ip);
                container.inventory.RemoveItem(requiredItem);
            }
            
            Item.Spawner.QueueItem(fabricatedItem.TargetItem, item.Position);

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
                    if (Array.Find(container.inventory.Items, it => it != null && it.Prefab == ip) != null) continue;
                    selectedItemFrame.GetChild<GUIButton>().Enabled = false;
                    break;
                }
            }

            itemList.Update(0.016f);
            itemList.Draw(spriteBatch);

            if (selectedItemFrame != null)
            {
                selectedItemFrame.Update(0.016f);
                selectedItemFrame.Draw(spriteBatch);
            }
        }
    }
}
