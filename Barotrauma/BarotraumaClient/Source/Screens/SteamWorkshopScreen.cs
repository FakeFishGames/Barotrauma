using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Steamworks;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class SteamWorkshopScreen : Screen
    {
        private GUIFrame menu;
        private GUIListBox subscriptionList;
        
        public SteamWorkshopScreen()
        {
            int width = Math.Min(GameMain.GraphicsWidth - 160, 1000);
            int height = Math.Min(GameMain.GraphicsHeight - 160, 700);

            Rectangle panelRect = new Rectangle(0, 0, width, height);

            menu = new GUIFrame(panelRect, null, Alignment.Center, "");
            menu.Padding = new Vector4(40.0f, 40.0f, 40.0f, 20.0f);
            
            subscriptionList = new GUIListBox(new Rectangle((int)(menu.Rect.Width * 0.3f), 30, 0, 0), "", menu);
            
            //--------------------------------------------------------

            GUIButton button = new GUIButton(new Rectangle(-20, -20, 100, 30), TextManager.Get("Back"), Alignment.TopLeft, "", menu);
            button.OnClicked = GameMain.MainMenuScreen.SelectTab;
            button.SelectedColor = button.Color;
        }

        public override void Select()
        {
            base.Select();
            RefreshSubscriptionList();
        }

        private void RefreshSubscriptionList()
        {
            subscriptionList.ClearChildren();
            SteamManager.SteamWorkshop.GetSubscribedItems(OnSubscribedItemCountReceived);
            //SteamManager.SteamWorkshop.GetSubscribedItemDetails(OnSubscribedItemCountReceived, OnSubscribedItemDetailsReceived);
        }

        private void OnSubscribedItemCountReceived(int itemCount)
        {
            if (itemCount == 0)
            {
                new GUITextBlock(new Rectangle(0, 0, 0, 30), "Could not find any Steam Workshop item subscriptions.", "", subscriptionList);
            }
            else
            {
                for (int i = 0; i < itemCount; i++)
                {
                    SteamManager.SteamWorkshop.GetItemDetails(i, OnSubscribedItemDetailsReceived);
                }
            }
        }

        private void OnSubscribedItemDetailsReceived(RemoteStorageGetPublishedFileDetailsResult_t itemDetails)
        {
            AddSubscribedItem(itemDetails);            
        }

        private void AddSubscribedItem(RemoteStorageGetPublishedFileDetailsResult_t itemDetails)
        {
            //PLACEHOLDER
            var itemFrame = new GUIFrame(new Rectangle(0, 0, 0, 80), Color.Transparent, "ListBoxElement", subscriptionList);
            itemFrame.UserData = itemDetails;

            new GUITextBlock(new Rectangle(0, 0, 0, 20), itemDetails.m_rgchTitle, "", itemFrame);
            new GUITextBlock(new Rectangle(0, 20, itemFrame.Rect.Width - 150, 0), itemDetails.m_rgchDescription,
                null, null, Alignment.TopLeft, Alignment.TopLeft, "", itemFrame, true, GUI.SmallFont);

            var downloadBtn = new GUIButton(new Rectangle(0, 0, 120, 20), TextManager.Get("DownloadButton"), Alignment.CenterRight, "", itemFrame);
            downloadBtn.UserData = itemDetails;
            downloadBtn.OnClicked = DownloadSubscribedItem;
        }

        private bool DownloadSubscribedItem(GUIButton button, object userdata)
        {
            RemoteStorageGetPublishedFileDetailsResult_t itemDetails = (RemoteStorageGetPublishedFileDetailsResult_t)userdata;
            SteamManager.SteamWorkshop.DownloadSubscribedItem(itemDetails, OnItemReceived);
            return true;
        }

        private void OnItemReceived(RemoteStorageDownloadUGCResult_t result, byte[] data)
        {
            DebugConsole.Log("Received file " + result.m_pchFileName + " from Steam Workshop");
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
