using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    abstract partial class Screen
    {
        public readonly GUIFrame Frame;

        protected Screen()
        {
            Frame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                CanBeFocused = false
            };
        }

        /// <summary>
        /// By default, submits the screen's main GUIFrame and,
        /// if requested upon construction, the social drawer,
        /// to the GUI update list.
        /// </summary>
        public virtual void AddToGUIUpdateList()
        {
            Frame.AddToGUIUpdateList();
        }

        public virtual void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
        }

        public void ColorFade(Color from, Color to, float duration)
        {
            if (duration <= 0.0f) return;
            
            CoroutineManager.StartCoroutine(UpdateColorFade(from, to, duration));
        }

        private IEnumerable<CoroutineStatus> UpdateColorFade(Color from, Color to, float duration)
        {
            while (Selected != this)
            {
                yield return CoroutineStatus.Running;
            }

            float timer = 0.0f;

            while (timer < duration)
            {
                GUI.ScreenOverlayColor = Color.Lerp(from, to, Math.Min(timer / duration, 1.0f));
                timer += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            GUI.ScreenOverlayColor = to;

            yield return CoroutineStatus.Success;
        }

        public virtual void OnFileDropped(string filePath, string extension) { }

        public virtual void Release()
        {
            Frame.RectTransform.Parent = null;
        }
    }
}
