using System;
using Microsoft.Xna.Framework;
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

        public bool HideElementsOutsideFrame;

        public GUICustomComponent(RectTransform rectT, Action<SpriteBatch, GUICustomComponent> onDraw = null, Action<float, GUICustomComponent> onUpdate = null) : base(null, rectT)
        {
            OnDraw = onDraw;
            OnUpdate = onUpdate;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (HideElementsOutsideFrame)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, Rect);
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }

            OnDraw?.Invoke(spriteBatch, this);

            if (HideElementsOutsideFrame)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }
        }

        protected override void Update(float deltaTime)
        {
            if (Visible) OnUpdate?.Invoke(deltaTime, this);
        }
    }
}
