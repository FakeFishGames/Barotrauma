using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class RoundSummaryScreen : Screen
    {
        private Sprite backgroundSprite;
        private RoundSummary roundSummary;
        private string loadText;

        private RectTransform prevGuiElementParent;

        public Exception LoadException;

        public static RoundSummaryScreen Select(Sprite backgroundSprite, RoundSummary roundSummary)
        {
            var summaryScreen = new RoundSummaryScreen()
            {
                roundSummary = roundSummary,
                backgroundSprite = backgroundSprite,
                prevGuiElementParent = roundSummary.Frame.RectTransform.Parent,
                loadText = TextManager.Get("campaignstartingpleasewait")
            };
            roundSummary.Frame.RectTransform.Parent = summaryScreen.Frame.RectTransform;
            summaryScreen.Select();
            summaryScreen.AddToGUIUpdateList();
            return summaryScreen;
        }

        public override void Deselect()
        {
            roundSummary.Frame.RectTransform.Parent = prevGuiElementParent;   
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);

            if (backgroundSprite != null)
            {
                float scale = Math.Max(GameMain.GraphicsWidth / backgroundSprite.size.X, GameMain.GraphicsHeight / backgroundSprite.size.Y);
                backgroundSprite.Draw(spriteBatch, new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2, Color.White, backgroundSprite.size / 2, scale: scale);
            }

            GUI.Draw(Cam, spriteBatch);

            string loadingText = loadText + new string('.', (int)Timing.TotalTime % 3 + 1);
            Vector2 textSize = GUI.LargeFont.MeasureString(loadText);
            GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight * 0.95f) - textSize / 2, loadingText, Color.White, font: GUI.LargeFont);

            spriteBatch.End();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            if (LoadException != null)
            {
                var temp = LoadException;
                LoadException = null;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(temp).Throw();
            }
        }
    }
}
