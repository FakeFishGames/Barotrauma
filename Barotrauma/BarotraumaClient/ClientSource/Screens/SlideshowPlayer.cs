using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    class SlideshowPlayer : GUIComponent
    {
        private readonly SlideshowPrefab slideshowPrefab;
        private readonly LocalizedString pressAnyKeyText;

        private int state;

        private Color overlayColor, textColor;

        private float timer;

        private LocalizedString currentText;

        public bool LastTextShown => state >= slideshowPrefab.Slides.Length;
        public bool Finished => state > slideshowPrefab.Slides.Length;
        
        public SlideshowPlayer(RectTransform rectT, SlideshowPrefab prefab) : base(null, rectT)
        {
            slideshowPrefab = prefab;
            overlayColor = Color.Black;
            textColor = Color.Transparent;
            pressAnyKeyText = TextManager.Get("pressanykey");
            RefreshText();
        }

        public void Restart()
        {
            state = 0;
        }

        public void Finish()
        {
            state = slideshowPrefab.Slides.Length + 1;
        }

        protected override void Update(float deltaTime)
        {
            var slide = slideshowPrefab.Slides[Math.Min(state, slideshowPrefab.Slides.Length - 1)];
            if (!Visible || (Finished && timer > slide.FadeOutDuration)) { return; }

            timer += deltaTime;

            if (state == 0)
            {
                overlayColor = Color.Lerp(Color.Black, Color.White, Math.Min((timer - slide.FadeInDelay) / slide.FadeInDuration, 1.0f));
            }
            else
            {
                overlayColor = Color.Lerp(Color.Transparent, Color.White, Math.Min((timer - slide.FadeInDelay) / slide.FadeInDuration, 1.0f));
            }

            if (timer > slide.TextFadeInDelay)
            {
                textColor = Color.Lerp(Color.Transparent, Color.White, Math.Min((timer - slide.TextFadeInDelay) / slide.TextFadeInDuration, 1.0f));
                if (AnyKeyHit())
                {
                    if (timer > slide.TextFadeInDelay + slide.FadeInDuration)
                    {
                        overlayColor = textColor = Color.Transparent;
                        timer = 0.0f;
                        state++;
                        RefreshText();
                    }
                    else
                    {
                        timer = slide.TextFadeInDelay + slide.TextFadeInDuration;
                    }
                }
            }
            else
            {
                textColor = Color.Transparent;
                if (AnyKeyHit())
                {
                    timer = slide.TextFadeInDelay + slide.TextFadeInDuration;
                }
            }

            if (state >= slideshowPrefab.Slides.Length)
            {
                overlayColor = Color.Lerp(Color.White, Color.Transparent, Math.Min(timer / slide.FadeOutDuration, 1.0f));
                textColor = Color.Lerp(Color.White, Color.Transparent, Math.Min(timer / slide.FadeOutDuration, 1.0f));
                if (timer >= slide.FadeOutDuration)
                {
                    state++;
                    RefreshText();
                }
            }

            static bool AnyKeyHit()
            {
                return 
                    PlayerInput.GetKeyboardState.GetPressedKeys().Any(k => PlayerInput.KeyHit(k)) ||
                    PlayerInput.PrimaryMouseButtonClicked();
            }
        }

        private void RefreshText()
        {
            var slide = slideshowPrefab.Slides[Math.Min(state, slideshowPrefab.Slides.Length - 1)];
            currentText = slide.Text
                .Replace("[submarine]", Submarine.MainSub?.Info.Name ?? "Unknown")
                .Replace("[location]", Level.Loaded?.StartOutpost?.Info.Name ?? "Unknown");
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (slideshowPrefab.Slides.IsEmpty) { return; }

            var slide = slideshowPrefab.Slides[Math.Min(state, slideshowPrefab.Slides.Length - 1)];
            if ((Finished && timer > slide.FadeOutDuration)) { return; }

            var overlaySprite = slide.Portrait;

            if (overlaySprite != null)
            {
                Sprite prevPortrait = null;
                if (state > 0 && state < slideshowPrefab.Slides.Length)
                {
                    prevPortrait = slideshowPrefab.Slides[state - 1].Portrait;
                    DrawOverlay(prevPortrait, Color.White);
                }
                if (prevPortrait?.Texture != overlaySprite.Texture)
                {
                    DrawOverlay(overlaySprite, overlayColor);
                }
            }
            else
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), overlayColor, isFilled: true);
            }

            if (!currentText.IsNullOrEmpty() && textColor.A > 0)
            {
                var backgroundSprite = GUIStyle.GetComponentStyle("CommandBackground").GetDefaultSprite();
                Vector2 centerPos = new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) / 2;
                LocalizedString wrappedText = ToolBox.WrapText(currentText, GameMain.GraphicsWidth / 3, GUIStyle.Font);
                Vector2 textSize = GUIStyle.Font.MeasureString(wrappedText);
                Vector2 textPos = centerPos - textSize / 2;
                backgroundSprite.Draw(spriteBatch,
                    centerPos,
                    Color.White * (textColor.A / 255.0f),
                    origin: backgroundSprite.size / 2,
                    rotate: 0.0f,
                    scale: new Vector2(GameMain.GraphicsWidth / 2 / backgroundSprite.size.X, textSize.Y / backgroundSprite.size.Y * 2.0f));

                GUI.DrawString(spriteBatch, textPos + Vector2.One, wrappedText, Color.Black * (textColor.A / 255.0f));
                GUI.DrawString(spriteBatch, textPos, wrappedText, textColor);

                if (timer > slide.TextFadeInDelay * 2)
                {
                    float alpha = Math.Min(timer - slide.TextFadeInDelay * 2, 1.0f);
                    Vector2 bottomTextPos = centerPos + new Vector2(0.0f, textSize.Y / 2 + 40 * GUI.Scale) - GUIStyle.Font.MeasureString(pressAnyKeyText) / 2;
                    GUI.DrawString(spriteBatch, bottomTextPos + Vector2.One, pressAnyKeyText, Color.Black * (textColor.A / 255.0f) * alpha);
                    GUI.DrawString(spriteBatch, bottomTextPos, pressAnyKeyText, textColor * alpha);
                }
            }

            void DrawOverlay(Sprite sprite, Color color)
            {
                GUI.DrawBackgroundSprite(spriteBatch, sprite, color);
            }
        }
    }
}
