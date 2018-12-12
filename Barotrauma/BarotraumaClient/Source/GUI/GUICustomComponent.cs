using System;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    /// <summary>
    /// GUIComponent that can be used to render custom content on the UI
    /// </summary>
    class GUICustomComponent : GUIComponent
    {
        public Action<SpriteBatch, GUICustomComponent> OnDraw;
        public Action<float, GUICustomComponent> OnUpdate;

        public GUICustomComponent(RectTransform rectT, Action<SpriteBatch, GUICustomComponent> onDraw = null, Action<float, GUICustomComponent> onUpdate = null) : base(null, rectT)
        {
            OnDraw = onDraw;
            OnUpdate = onUpdate;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (Visible) OnDraw?.Invoke(spriteBatch, this);
        }

        protected override void Update(float deltaTime)
        {
            if (Visible) OnUpdate?.Invoke(deltaTime, this);
        }
    }
}
