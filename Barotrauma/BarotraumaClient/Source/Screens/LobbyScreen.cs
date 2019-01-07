using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class LobbyScreen : Screen
    {
        private CampaignUI campaignUI;
        
        private GUIFrame topPanel, bottomPanel;

        private GUITextBlock locationTitle;

        private CrewManager CrewManager
        {
            get { return GameMain.GameSession.CrewManager; }
        }

        public string GetMoney()
        {
            return campaignUI == null ? "" : campaignUI.GetMoney();
        }

        public LobbyScreen()
        {
            topPanel = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.1f), Frame.RectTransform, Anchor.TopCenter, Pivot.TopCenter)
            {
                RelativeOffset = new Vector2(0.0f, 0.05f)
            });

            GUIFrame paddedToPanel = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.6f), topPanel.RectTransform, Anchor.Center), style: null);

            locationTitle = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), paddedToPanel.RectTransform), "", Color.White, GUI.LargeFont);

            GUITextBlock moneyText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.5f), paddedToPanel.RectTransform, Anchor.BottomLeft), "");
            moneyText.TextGetter = GetMoney;

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.07f, 0.3f), topPanel.RectTransform, Anchor.CenterRight, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.05f, 0.0f)
            }, TextManager.Get("Map"));
            button.UserData = CampaignUI.Tab.Map;
            button.OnClicked = SelectTab;
            SelectTab(button, button.UserData);

            button = new GUIButton(new RectTransform(new Vector2(0.07f, 0.3f), topPanel.RectTransform, Anchor.CenterRight, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.13f, 0.0f)
            }, TextManager.Get("Crew"));
            button.UserData = CampaignUI.Tab.Crew;
            button.OnClicked = SelectTab;
            
            button = new GUIButton(new RectTransform(new Vector2(0.07f, 0.3f), topPanel.RectTransform, Anchor.CenterRight, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(0.21f, 0.0f)
            }, TextManager.Get("Store"));
            button.UserData = CampaignUI.Tab.Store;
            button.OnClicked = SelectTab;
   
            //---------------------------------------------------------------
            //---------------------------------------------------------------
            
            bottomPanel = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.78f), Frame.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter)
            {
                RelativeOffset = new Vector2(0.0f, 0.05f)
            });
        }

        public override void Select()
        {
            base.Select();

            CampaignMode campaign = GameMain.GameSession.GameMode as CampaignMode;

            if (campaign == null)
            {
                return;
            }

            locationTitle.Text = TextManager.Get("Location") + ": " + campaign.Map.CurrentLocation.Name;

            campaign.Map.SelectLocation(-1);

            bottomPanel.ClearChildren();
            campaignUI = new CampaignUI(campaign, bottomPanel);
            campaignUI.StartRound = StartRound;
            campaignUI.OnLocationSelected = SelectLocation;
            campaignUI.UpdateCharacterLists();

            GameAnalyticsManager.SetCustomDimension01("singleplayer");
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);
            
            GUI.DrawBackgroundSprite(spriteBatch, GameMain.GameSession.Map.CurrentLocation.Type.Background);

            spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();

        }

        public bool SelectTab(GUIButton button, object selection)
        {
            if (campaignUI == null) return false;

             if (button != null)
             {
                 button.Selected = true;
                 foreach (GUIComponent child in topPanel.Children)
                 {
                     GUIButton otherButton = child as GUIButton;
                     if (otherButton == null || otherButton == button) continue;
                     otherButton.Selected = false;
                 }
             }
            campaignUI.SelectTab((CampaignUI.Tab)selection);

            return true;
        }

        public void SelectLocation(Location location, LocationConnection locationConnection)
        {
        }

        private void StartRound()
        {
            if (GameMain.GameSession.Map.SelectedConnection == null) return;

            GameMain.Instance.ShowLoading(LoadRound());
        }

        private IEnumerable<object> LoadRound()
        {
            GameMain.GameSession.StartRound(campaignUI.SelectedLevel, 
                reloadSub: true, 
                loadSecondSub: false,
                mirrorLevel: GameMain.GameSession.Map.CurrentLocation != GameMain.GameSession.Map.SelectedConnection.Locations[0]);
            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public bool QuitToMainMenu(GUIButton button, object selection)
        {
            GameMain.MainMenuScreen.Select();
            return true;
        }
    }
}
