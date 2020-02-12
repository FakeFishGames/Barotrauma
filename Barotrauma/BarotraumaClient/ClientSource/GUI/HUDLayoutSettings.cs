using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

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
                if (value == inventoryTopY) return;
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

        /*public static Rectangle HealthBarAreaRight
        {
            get; private set;
        }*/
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

        public static int Padding
        {
            get; private set;
        }

        static HUDLayoutSettings()
        {
            if (GameMain.Instance != null)
            {
                GameMain.Instance.OnResolutionChanged += CreateAreas;
                GameMain.Config.OnHUDScaleChanged += CreateAreas;
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
            
            int infoAreaWidth = (int)(142 * GUI.Scale * CharacterInfo.BgScale);
            int infoAreaHeight = (int)(98 * GUI.Scale * CharacterInfo.BgScale);
            int portraitSize = (int)(125 * GUI.Scale);
            BottomRightInfoArea = new Rectangle(GameMain.GraphicsWidth - Padding * 2 - infoAreaWidth, GameMain.GraphicsHeight - Padding * 2 - infoAreaHeight, infoAreaWidth, infoAreaHeight);
            PortraitArea = new Rectangle(GameMain.GraphicsWidth - Padding - portraitSize, GameMain.GraphicsHeight - Padding - portraitSize, portraitSize, portraitSize);

            //horizontal slices at the corners of the screen for health bar and affliction icons
            int healthBarHeight = (int)Math.Max(25f * GUI.Scale, 12.5f);
            int afflictionAreaHeight = (int)(50 * GUI.Scale);
            int healthBarWidth = BottomRightInfoArea.Width;
            //int healthBarWidth = (int)((BottomRightInfoArea.Width + CharacterInventory.SlotSize.X + CharacterInventory.Spacing) * 1.1f);
            HealthBarArea = new Rectangle(BottomRightInfoArea.X, BottomRightInfoArea.Y - healthBarHeight - (int)(8 * GUI.Scale), healthBarWidth, healthBarHeight);
            AfflictionAreaLeft = new Rectangle(HealthBarArea.X, HealthBarArea.Y - Padding - afflictionAreaHeight, HealthBarArea.Width, afflictionAreaHeight);
            
            //HealthBarAreaRight = new Rectangle(Padding, GameMain.GraphicsHeight - healthBarHeight - Padding, healthBarWidth, healthBarHeight);
            /*if (HealthBarAreaRight.Y + healthBarHeight * 0.75f < PortraitArea.Y)
            {
                HealthBarAreaRight = new Rectangle(GameMain.GraphicsWidth - Padding - healthBarWidth, HealthBarAreaRight.Y, HealthBarAreaRight.Width, HealthBarAreaRight.Height);
            }*/
            //AfflictionAreaRight = new Rectangle(HealthBarAreaRight.X, HealthBarAreaRight.Y + healthBarHeight + Padding, healthBarWidth, afflictionAreaHeight);

            int messageAreaWidth = GameMain.GraphicsWidth / 3;
            MessageAreaTop = new Rectangle((GameMain.GraphicsWidth - messageAreaWidth) / 2, ButtonAreaTop.Bottom, messageAreaWidth, ButtonAreaTop.Height);

            bool isFourByThree = GUI.IsFourByThree();
            int chatBoxWidth = !isFourByThree ? (int)(475 * GUI.Scale) : (int)(375 * GUI.Scale);
            int chatBoxHeight = (int)Math.Max(GameMain.GraphicsHeight * 0.25f, 150);
            ChatBoxArea = new Rectangle(Padding, GameMain.GraphicsHeight - Padding - chatBoxHeight, chatBoxWidth, chatBoxHeight);

            int objectiveAnchorWidth = (int)(250 * GUI.Scale);
            int objectiveAnchorOffsetY = (int)(150 * GUI.Scale);
            ObjectiveAnchor = new Rectangle(Padding, ChatBoxArea.Y - objectiveAnchorOffsetY, objectiveAnchorWidth, 0);

            CrewArea = new Rectangle(Padding, Padding, (int)Math.Max(400 * GUI.Scale, 220), ObjectiveAnchor.Top - Padding * 2);

            InventoryAreaLower = new Rectangle(Padding, inventoryTopY, GameMain.GraphicsWidth - Padding * 2, GameMain.GraphicsHeight - inventoryTopY);

            int healthWindowWidth = (int)(GameMain.GraphicsWidth * 0.5f);
            int healthWindowHeight = (int)(GameMain.GraphicsWidth * 0.5f * 0.65f);
            int healthWindowX = GameMain.GraphicsWidth / 2 - healthWindowWidth / 2;
            int healthWindowY = GameMain.GraphicsHeight / 2 - healthWindowHeight / 2;

            HealthWindowAreaLeft = new Rectangle(healthWindowX, healthWindowY, healthWindowWidth, healthWindowHeight);
        }

        public static void Draw(SpriteBatch spriteBatch)
        {
            GUI.DrawRectangle(spriteBatch, ButtonAreaTop, Color.White * 0.5f);
            GUI.DrawRectangle(spriteBatch, MessageAreaTop, GUI.Style.Orange * 0.5f);
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
            if (Inventory.IsMouseOnInventory()) { return false; }
            
            bool input = PlayerInput.PrimaryMouseButtonDown() || PlayerInput.SecondaryMouseButtonClicked();
            return input && !rect.Contains(PlayerInput.MousePosition);
        }
    }
}
