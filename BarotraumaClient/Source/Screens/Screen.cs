using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Screen
    {
        public virtual void AddToGUIUpdateList()
        {
        }

        public virtual void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
        }

        public void ColorFade(Color from, Color to, float duration)
        {
            if (duration <= 0.0f) return;
            
            CoroutineManager.StartCoroutine(UpdateColorFade(from, to, duration));
        }

        private IEnumerable<object> UpdateColorFade(Color from, Color to, float duration)
        {
            while (Selected != this)
            {
                yield return CoroutineStatus.Running;
            }

            float timer = 0.0f;

            while (timer < duration)
            {
                GUI.ScreenOverlayColor = Color.Lerp(from, to, Math.Min(timer / duration, 1.0f));

                timer += CoroutineManager.UnscaledDeltaTime;

                yield return CoroutineStatus.Running;
            }

            GUI.ScreenOverlayColor = to;

            yield return CoroutineStatus.Success;
        }

        protected void DrawSubmarineIndicator(SpriteBatch spriteBatch, Submarine submarine, Color color)
        {
            Vector2 subDiff = submarine.WorldPosition - Cam.WorldViewCenter;

            if (Math.Abs(subDiff.X) > Cam.WorldView.Width || Math.Abs(subDiff.Y) > Cam.WorldView.Height)
            {
                Vector2 normalizedSubDiff = Vector2.Normalize(subDiff);

                Vector2 iconPos =
                    Cam.WorldToScreen(Cam.WorldViewCenter) +
                    new Vector2(normalizedSubDiff.X * GameMain.GraphicsWidth * 0.4f, -normalizedSubDiff.Y * GameMain.GraphicsHeight * 0.4f);

                GUI.SubmarineIcon.Draw(spriteBatch, iconPos, color);

                Vector2 arrowOffset = normalizedSubDiff * GUI.SubmarineIcon.size.X * 0.7f;
                arrowOffset.Y = -arrowOffset.Y;
                GUI.Arrow.Draw(spriteBatch, iconPos + arrowOffset, color, MathUtils.VectorToAngle(arrowOffset) + MathHelper.PiOver2);
            }
        }
    }
}
