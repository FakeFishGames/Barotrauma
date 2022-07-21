using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    static class HUDLayoutSettings
    {
        public static bool DebugDraw;

        private static int inventoryTopY;
        public static int InventoryTopY
        {
            get { return inventoryTopY; }
            set
            {
                if (value == inventoryTopY) { return; }
                inventoryTopY = value;
                CreateAreas();
            }
        }

        public static Rectangle ButtonAreaTop
        {
            get; private set;
        }

        public static Rectangle MessageAreaTop
        {
            get; private set;
        }

        public static Rectangle CrewArea
        {
            get; private set;
        }

        public static Rectangle ChatBoxArea
        {
            get; private set;
        }

        public static Rectangle ObjectiveAnchor
        {
            get; private set;
        }

        public static Rectangle InventoryAreaLower
        {
            get; private set;
        }

        public static Rectangle HealthBarArea
        {
            get; private set;
        }

        public static Rectangle BottomRightInfoArea
        {
            get; private set;
        }

        public static Rectangle AfflictionAreaLeft
        {
            get; private set;
        }

        public static Rectangle HealthWindowAreaLeft
        {
            get; private set;
        }

        public static Rectangle PortraitArea
        {
            get; private set;
        }

        public static Rectangle VotingArea
        {
            get; private set;
        }

        public static int Padding
        {
            get; private set;
        }

        static HUDLayoutSettings()
        {
            if (GameMain.Instance != null)
            {
                GameMain.Instance.ResolutionChanged += CreateAreas;
                CreateAreas();
                CharacterInfo.Init();
            }
        }
        
        public static RectTransform ToRectTransform(Rectangle rect, RectTransform parent)
        {
            return new RectTransform(new Vector2(rect.Width / (float)GameMain.GraphicsWidth, rect.Height / (float)GameMain.GraphicsHeight), parent)
            {
                RelativeOffset = new Vector2(rect.X / (float)GameMain.GraphicsWidth, rect.Y / (float)GameMain.GraphicsHeight)
            };
        }

        public static void CreateAreas()
        {
            Padding = (int)(11 * GUI.Scale);

            if (inventoryTopY == 0) { inventoryTopY = GameMain.GraphicsHeight - 30; }

            //slice from the top of the screen for misc buttons (info, end round, server controls)
            ButtonAreaTop = new Rectangle(Padding, Padding, GameMain.GraphicsWidth - Padding * 2, (int)(50 * GUI.Scale));
            
            int infoAreaWidth = (int)(142 * GUI.Scale);
            int infoAreaHeight = (int)(98 * GUI.Scale);
            int portraitSize = (int)(infoAreaHeight * 0.95f);
            BottomRightInfoArea = new Rectangle(GameMain.GraphicsWidth - Padding * 2 - infoAreaWidth, GameMain.GraphicsHeight - Padding * 2 - infoAreaHeight, infoAreaWidth, infoAreaHeight);
            PortraitArea = new Rectangle(GameMain.GraphicsWidth - portraitSize, BottomRightInfoArea.Bottom - portraitSize + Padding / 2, portraitSize, portraitSize);

            //horizontal slices at the corners of the screen for health bar and affliction icons
            int afflictionAreaHeight = (int)(50 * GUI.Scale);
            int healthBarWidth = BottomRightInfoArea.Width;

            var healthBarChildStyles = GUIStyle.GetComponentStyle("CharacterHealthBar")?.ChildStyles;
            if (healthBarChildStyles!= null && healthBarChildStyles.TryGetValue("GUIFrame".ToIdentifier(), out var style))
            {
                if (style.Sprites.TryGetValue(GUIComponent.ComponentState.None, out var uiSprites) && uiSprites.FirstOrDefault() is { } uiSprite)
                {
                    // The default health bar uses a sliced sprite so let's make sure the health bar area is calculated accordingly
                    healthBarWidth += (int)(uiSprite.NonSliceSize.X * Math.Min(GUI.Scale, 1f));
                }
            }
            int healthBarHeight = (int)(50f * GUI.Scale);
            HealthBarArea = new Rectangle(BottomRightInfoArea.Right - healthBarWidth + (int)Math.Floor(1 / GUI.Scale), BottomRightInfoArea.Y - healthBarHeight + GUI.IntScale(10), healthBarWidth, healthBarHeight);
            AfflictionAreaLeft = new Rectangle(HealthBarArea.X, HealthBarArea.Y - Padding - afflictionAreaHeight, HealthBarArea.Width, afflictionAreaHeight);            


            int messageAreaWidth = GameMain.GraphicsWidth / 3;
            MessageAreaTop = new Rectangle((GameMain.GraphicsWidth - messageAreaWidth) / 2, ButtonAreaTop.Bottom + ButtonAreaTop.Height, messageAreaWidth, ButtonAreaTop.Height);

            bool isFourByThree = GUI.IsFourByThree();
            int chatBoxWidth = !isFourByThree ? (int)(475 * GUI.Scale) : (int)(375 * GUI.Scale);
            int chatBoxHeight = (int)Math.Max(GameMain.GraphicsHeight * 0.25f, 150);
            ChatBoxArea = new Rectangle(Padding, GameMain.GraphicsHeight - Padding - chatBoxHeight, chatBoxWidth, chatBoxHeight);

            int objectiveAnchorWidth = (int)(250 * GUI.Scale);
            int objectiveAnchorOffsetY = (int)(150 * GUI.Scale);
            ObjectiveAnchor = new Rectangle(Padding, ChatBoxArea.Y - objectiveAnchorOffsetY, objectiveAnchorWidth, 0);

            int crewAreaY = ButtonAreaTop.Bottom + Padding;
            int crewAreaHeight = ObjectiveAnchor.Top - Padding - crewAreaY;
            CrewArea = new Rectangle(Padding, crewAreaY, (int)Math.Max(400 * GUI.Scale, 220), crewAreaHeight);

            InventoryAreaLower = new Rectangle(ChatBoxArea.Right + Padding * 7, inventoryTopY, GameMain.GraphicsWidth - Padding * 9 - ChatBoxArea.Width, GameMain.GraphicsHeight - inventoryTopY);

            int healthWindowWidth = (int)(GameMain.GraphicsWidth * 0.5f);
            int healthWindowHeight = (int)(GameMain.GraphicsWidth * 0.5f * 0.65f);
            int healthWindowX = GameMain.GraphicsWidth / 2 - healthWindowWidth / 2;
            int healthWindowY = GameMain.GraphicsHeight / 2 - healthWindowHeight / 2;

            HealthWindowAreaLeft = new Rectangle(healthWindowX, healthWindowY, healthWindowWidth, healthWindowHeight);

            int votingAreaWidth = (int)(400 * GUI.Scale);
            int votingAreaX = GameMain.GraphicsWidth - Padding - votingAreaWidth;
            int votingAreaY = Padding + ButtonAreaTop.Height;

            // Height is based on text content
            VotingArea = new Rectangle(votingAreaX, votingAreaY, votingAreaWidth, 0);
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            GUI.DrawRectangle(spriteBatch, ButtonAreaTop, Color.White * 0.5f);
            GUI.DrawRectangle(spriteBatch, MessageAreaTop, GUIStyle.Orange * 0.5f);
            GUI.DrawRectangle(spriteBatch, CrewArea, Color.Blue * 0.5f);
            GUI.DrawRectangle(spriteBatch, ChatBoxArea, Color.Cyan * 0.5f);
            GUI.DrawRectangle(spriteBatch, HealthBarArea, Color.Red * 0.5f);
            GUI.DrawRectangle(spriteBatch, AfflictionAreaLeft, Color.Red * 0.5f);
            GUI.DrawRectangle(spriteBatch, InventoryAreaLower, Color.Yellow * 0.5f);
            GUI.DrawRectangle(spriteBatch, HealthWindowAreaLeft, Color.Red * 0.5f);
            GUI.DrawRectangle(spriteBatch, BottomRightInfoArea, Color.Green * 0.5f);
        }
    }

    public static class HUD
    {
        public static bool CloseHUD(Rectangle rect)
        {
            // Always close when hitting escape
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape)) { return true; }

            //don't close when the cursor is on a UI element
            if (GUI.MouseOn != null) { return false; }

            //don't close when hovering over an inventory element
            if (Inventory.IsMouseOnInventory) { return false; }
            
            bool input = PlayerInput.PrimaryMouseButtonDown() || PlayerInput.SecondaryMouseButtonClicked();
            return input && !rect.Contains(PlayerInput.MousePosition);
        }
    }
}
