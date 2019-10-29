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

        private GUIFrame campaignUIContainer;

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
            campaignUIContainer = new GUIFrame(new RectTransform(Vector2.One, Frame.RectTransform, Anchor.Center), style: null);
        }

        public override void Select()
        {
            base.Select();

            CampaignMode campaign = GameMain.GameSession.GameMode as CampaignMode;
            if (campaign == null) { return; }

            campaign.Map.SelectLocation(-1);

            campaignUIContainer.ClearChildren();
            campaignUI = new CampaignUI(campaign, campaignUIContainer)
            {
                StartRound = StartRound,
                OnLocationSelected = SelectLocation
            };
            campaignUI.UpdateCharacterLists();

            GameAnalyticsManager.SetCustomDimension01("singleplayer");
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.Black);
            
            GUI.DrawBackgroundSprite(spriteBatch, 
                GameMain.GameSession.Map.CurrentLocation.Type.GetPortrait(GameMain.GameSession.Map.CurrentLocation.PortraitId));

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
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
