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
        private GUIListBox itemList;
        
        public SteamWorkshopScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(new RectTransform(new Vector2(0.8f, 0.9f), GUI.Canvas, Anchor.Center));
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), menu.RectTransform, Anchor.Center), style: null);

            itemList = new GUIListBox(new RectTransform(new Vector2(0.4f, 0.8f), paddedFrame.RectTransform, Anchor.BottomLeft));
            
            //--------------------------------------------------------

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.2f, 0.1f), paddedFrame.RectTransform),
                TextManager.Get("Back"));
            button.OnClicked = GameMain.MainMenuScreen.SelectTab;
            button.SelectedColor = button.Color;
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
            itemList.ClearChildren();
            foreach (var item in itemDetails)
            {
                var itemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), itemList.Content.RectTransform, minSize: new Point(0, 80)),
                    style: "ListBoxElement")
                {
                    UserData = item
                };
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.25f), itemFrame.RectTransform), item.Title);
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.75f), itemFrame.RectTransform, Anchor.BottomLeft), item.Description,
                    wrap: true, font: GUI.SmallFont);

                if (item.Installed)
                {
                    new GUITextBlock(new RectTransform(new Vector2(0.3f, 0.25f), itemFrame.RectTransform, Anchor.CenterRight), "Installed", textAlignment: Alignment.CenterRight);
                }
                else
                {
                    var downloadBtn = new GUIButton(new RectTransform(new Vector2(0.2f, 0.2f), itemFrame.RectTransform, Anchor.CenterRight),
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
