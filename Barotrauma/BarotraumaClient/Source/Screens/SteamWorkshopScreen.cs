using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class SteamWorkshopScreen : Screen
    {
        private GUIFrame menu;
        private GUIListBox installedItemList;
        private GUIListBox availableItemList;

        private GUIFrame itemPreviewFrame;

        public SteamWorkshopScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.9f), GUI.Canvas, Anchor.Center));
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), menu.RectTransform, Anchor.Center), style: null);

            var listContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.4f, 1.0f), paddedFrame.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), listContainer.RectTransform),
                TextManager.Get("Back"));
            button.OnClicked = GameMain.MainMenuScreen.SelectTab;
            button.SelectedColor = button.Color;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), "Installed items");
            installedItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), listContainer.RectTransform), "Available items");
            availableItemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), listContainer.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    ShowItemPreview(userdata as Facepunch.Steamworks.Workshop.Item);
                    return true;
                }
            };

            //--------------------------------------------------------

            itemPreviewFrame = new GUIFrame(new RectTransform(new Vector2(0.58f, 1.0f), paddedFrame.RectTransform, Anchor.TopRight), style: "InnerFrame");
        }

        public override void Select()
        {
            base.Select();
            RefreshItemList();
        }

        private void RefreshItemList()
        {
            SteamManager.GetWorkshopItems(OnItemsReceived);
        }
        
        private void OnItemsReceived(IList<Facepunch.Steamworks.Workshop.Item> itemDetails)
        {
            installedItemList.ClearChildren();
            availableItemList.ClearChildren();
            foreach (var item in itemDetails)
            {
                GUIListBox listBox = item.Installed ? installedItemList : availableItemList;
                var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), listBox.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
                {
                    UserData = item
                };
                new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.25f), itemFrame.RectTransform), item.Title);
                new GUITextBlock(new RectTransform(new Vector2(0.75f, 0.75f), itemFrame.RectTransform, Anchor.BottomLeft), item.Description,
                    wrap: true, font: GUI.SmallFont);
                
                if (item.Installed)
                {
                    new GUITickBox(new RectTransform(new Vector2(0.25f, 0.5f), itemFrame.RectTransform, Anchor.CenterRight), "Enabled")
                    {
                        Selected = SteamManager.CheckWorkshopItemEnabled(item),
                        UserData = item,
                        OnSelected = ToggleItemEnabled
                    };
                }
                else
                {
                    var downloadBtn = new GUIButton(new RectTransform(new Vector2(0.2f, 0.5f), itemFrame.RectTransform, Anchor.CenterRight),
                        TextManager.Get("DownloadButton"))
                    {
                        UserData = item,
                        OnClicked = DownloadItem
                    };
                }
            }
        }

        private bool DownloadItem(GUIButton btn, object userdata)
        {
            var item = (Facepunch.Steamworks.Workshop.Item)userdata;
            item.Download();
            return true;
        }

        private bool ToggleItemEnabled(GUITickBox tickBox)
        {
            Facepunch.Steamworks.Workshop.Item item = tickBox.UserData as Facepunch.Steamworks.Workshop.Item;
            if (tickBox.Selected)
            {
                tickBox.Selected = SteamManager.EnableWorkShopItem(item, false);
            }
            else
            {
                tickBox.Selected = !SteamManager.DisableWorkShopItem(item);
            }
            return true;
        }

        private void ShowItemPreview(Facepunch.Steamworks.Workshop.Item item)
        {
            itemPreviewFrame.ClearChildren();

            if (item == null) return;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), itemPreviewFrame.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.02f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), item.Title, font: GUI.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), item.Description, wrap: true);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Score: " + item.Score);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Upvotes: " + item.VotesUp);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Downvotes: "+item.VotesDown);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Creator: " + item.OwnerName);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Created: " + item.Created);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Modified: " + item.Modified);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), "Url: " + item.Url);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;
            GameMain.TitleScreen.Draw(spriteBatch, graphics, (float)deltaTime);

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);
            
            GUI.Draw((float)deltaTime, spriteBatch);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
        }
    }
}
