using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class Screen
    {
        private static Screen selected;
        
        public static Screen Selected
        {
            get { return selected; }
        }

        public virtual void Deselect()
        {
        }

        public virtual void Select()
        {
            if (selected != null && selected != this)
            {
                selected.Deselect();
                GUIComponent.KeyboardDispatcher.Subscriber = null;
            }
            selected = this;
        }

        public virtual Camera Cam
        {
            get { return null; }
        }

        public virtual void AddToGUIUpdateList()
        {
        }

        public virtual void Update(double deltaTime)
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

    }
}
