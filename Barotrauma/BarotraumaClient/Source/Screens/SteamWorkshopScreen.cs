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

            menu = new GUIFrame(panelRect, null, Alignment.Center, "");
            menu.Padding = new Vector4(40.0f, 40.0f, 40.0f, 20.0f);
            
            itemList = new GUIListBox(new Rectangle((int)(menu.Rect.Width * 0.3f), 30, 0, 0), "", menu);
            
            //--------------------------------------------------------

            GUIButton button = new GUIButton(new Rectangle(-20, -20, 100, 30), TextManager.Get("Back"), Alignment.TopLeft, "", menu);
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
                var itemFrame = new GUIFrame(new Rectangle(0, 0, 0, 80), Color.Transparent, "ListBoxElement", itemList);
                itemFrame.UserData = item;
                new GUITextBlock(new Rectangle(0, 0, 0, 20), item.Title, "", itemFrame);
                new GUITextBlock(new Rectangle(0, 20, itemFrame.Rect.Width - 150, 0), item.Description,
                    null, null, Alignment.TopLeft, Alignment.TopLeft, "", itemFrame, true, GUI.SmallFont);

                var downloadBtn = new GUIButton(new Rectangle(0, 0, 120, 20), TextManager.Get("DownloadButton"), Alignment.CenterRight, "", itemFrame);
                downloadBtn.UserData = item;
                downloadBtn.OnClicked = DownloadItem;
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

            menu.Draw(spriteBatch);

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            menu.Update((float)deltaTime);
        }
    }
}
