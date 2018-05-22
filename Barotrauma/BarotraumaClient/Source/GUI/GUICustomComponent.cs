using System;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    /// <summary>
    /// GUIComponent that can be used to render custom content on the UI
    /// </summary>
    class GUICustomComponent : GUIComponent
    {
        private Action<SpriteBatch, GUICustomComponent> onDraw;
        private Action<float, GUICustomComponent> onUpdate;

        public GUICustomComponent(RectTransform rectT, Action<SpriteBatch, GUICustomComponent> onDraw, Action<float, GUICustomComponent> onUpdate) : base(null, rectT)
        {
            this.onDraw = onDraw;
            this.onUpdate = onUpdate;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (Visible) onDraw?.Invoke(spriteBatch, this);
        }

        protected override void Update(float deltaTime)
        {
            if (Visible) onUpdate?.Invoke(deltaTime, this);
        }
    }
}
