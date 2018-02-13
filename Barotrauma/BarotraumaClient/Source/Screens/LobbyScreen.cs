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
            Rectangle panelRect = new Rectangle(
                40, 40,
                GameMain.GraphicsWidth - 80,
                100);

            topPanel = new GUIFrame(panelRect, "");
            topPanel.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            
            locationTitle = new GUITextBlock(new Rectangle(0, 0, 200, 25),
                "", Color.Transparent, Color.White, Alignment.TopLeft, "", topPanel);
            locationTitle.Font = GUI.LargeFont;


            GUITextBlock moneyText = new GUITextBlock(new Rectangle(0, 0, 0, 25), "", "", 
                Alignment.BottomLeft, Alignment.BottomLeft, topPanel);
            moneyText.TextGetter = GetMoney;
            
            GUIButton button = new GUIButton(new Rectangle(-240, 0, 100, 30), TextManager.Get("Map"), null, Alignment.BottomRight, "", topPanel);
            button.UserData = CampaignUI.Tab.Map;
            button.OnClicked = SelectTab;
            SelectTab(button, button.UserData);

            button = new GUIButton(new Rectangle(-120, 0, 100, 30), TextManager.Get("Crew"), null, Alignment.BottomRight, "", topPanel);
            button.UserData = CampaignUI.Tab.Crew;
            button.OnClicked = SelectTab;
            
            button = new GUIButton(new Rectangle(0, 0, 100, 30), TextManager.Get("Store"), null, Alignment.BottomRight, "", topPanel);
            button.UserData = CampaignUI.Tab.Store;
            button.OnClicked = SelectTab;
   
            //---------------------------------------------------------------
            //---------------------------------------------------------------

            panelRect = new Rectangle(
                40,
                panelRect.Bottom + 40,
                panelRect.Width,
                GameMain.GraphicsHeight - 120 - panelRect.Height);

            bottomPanel = new GUIFrame(panelRect);      
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

            bottomPanel.ClearChildren();
            campaignUI = new CampaignUI(campaign, bottomPanel);
            campaignUI.StartRound = StartRound;
            campaignUI.OnLocationSelected = SelectLocation;            
            campaignUI.UpdateCharacterLists();
        }
        
        public override void AddToGUIUpdateList()
        {
            base.AddToGUIUpdateList();

            topPanel.AddToGUIUpdateList();
            bottomPanel.AddToGUIUpdateList();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            topPanel.Update((float)deltaTime);
            bottomPanel.Update((float)deltaTime);

            campaignUI.Update((float)deltaTime);

           /* mapZoom += PlayerInput.ScrollWheelSpeed / 1000.0f;
            mapZoom = MathHelper.Clamp(mapZoom, 1.0f, 4.0f);

            GameMain.GameSession.Map.Update((float)deltaTime, new Rectangle(
                bottomPanel[selectedRightPanel].Rect.X + 20,
                bottomPanel[selectedRightPanel].Rect.Y + 20,
                bottomPanel[selectedRightPanel].Rect.Width - 310,
                bottomPanel[selectedRightPanel].Rect.Height - 40), mapZoom);*/
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            /*if (characterList.CountChildren != CrewManager.CharacterInfos.Count)
            {
                UpdateCharacterLists();
            }*/

            graphics.Clear(Color.Black);
            
            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            Sprite backGround = GameMain.GameSession.Map.CurrentLocation.Type.Background;
            spriteBatch.Draw(backGround.Texture, Vector2.Zero, null, Color.White, 0.0f, Vector2.Zero,
                Math.Max((float)GameMain.GraphicsWidth / backGround.SourceRect.Width, (float)GameMain.GraphicsHeight / backGround.SourceRect.Height), SpriteEffects.None, 0.0f);
            
            topPanel.Draw(spriteBatch);
            bottomPanel.Draw(spriteBatch);

            campaignUI.Draw(spriteBatch);

           /* if (selectedRightPanel == (int)PanelTab.Map)
            {
                GameMain.GameSession.Map.Draw(spriteBatch, new Rectangle(
                    bottomPanel[selectedRightPanel].Rect.X + 20, 
                    bottomPanel[selectedRightPanel].Rect.Y + 20,
                    bottomPanel[selectedRightPanel].Rect.Width - 310, 
                    bottomPanel[selectedRightPanel].Rect.Height - 40), mapZoom);
            }

            if (topPanel.UserData as Location != GameMain.GameSession.Map.CurrentLocation)
            {
                UpdateLocationTab(GameMain.GameSession.Map.CurrentLocation);
            }*/

            GUI.Draw((float)deltaTime, spriteBatch, null);

            spriteBatch.End();

        }

        public bool SelectTab(GUIButton button, object selection)
        {
            if (campaignUI == null) return false;

             if (button != null)
             {
                 button.Selected = true;
                 foreach (GUIComponent child in topPanel.children)
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
            LoadingScreen.loadType = LoadType.Singleplayer;
            GameMain.Instance.ShowLoading(LoadRound());
        }

        private IEnumerable<object> LoadRound()
        {
            GameMain.GameSession.StartRound(campaignUI.SelectedLevel, true);
            //Single player initialization logic
            if(GameMain.Server == null && GameMain.Client == null)
            {
                GameMain.NilMod.GameInitialize(false);
            }
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
