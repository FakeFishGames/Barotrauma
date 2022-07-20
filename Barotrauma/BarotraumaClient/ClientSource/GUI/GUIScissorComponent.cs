using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUIScissorComponent: GUIComponent
    {
        public GUIComponent Content;

        public GUIScissorComponent(RectTransform rectT) : base(null, rectT)
        {
            Content = new GUIFrame(new RectTransform(Vector2.One, rectT), style: null)
            {
                CanBeFocused = false
            };
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            foreach (GUIComponent child in Children)
            {
                if (child == Content) { continue; }
                throw new InvalidOperationException($"Children were found in {nameof(GUIScissorComponent)}, Add them to {nameof(GUIScissorComponent)}.{nameof(Content)} instead.");
            }

            ClampChildMouseRects(Content);
        }

        public override void DrawChildren(SpriteBatch spriteBatch, bool recursive)
        {
            //do nothing (the children have to be drawn in the Draw method after the ScissorRectangle has been set)
            return;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) { return; }

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            RasterizerState prevRasterizerState = spriteBatch.GraphicsDevice.RasterizerState;

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, Rect);
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);

            foreach (GUIComponent child in Content.Children)
            {
                if (!child.Visible) { continue; }
                child.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
            }

            spriteBatch.End();
            spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: prevRasterizerState);
        }

        private void ClampChildMouseRects(GUIComponent child)
        {
            child.ClampMouseRectToParent = true;

            if (child is GUIListBox) { return; }

            foreach (GUIComponent grandChild in child.Children)
            {
                ClampChildMouseRects(grandChild);
            }
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) { return; }

            UpdateOrder = order;
            GUI.AddToUpdateList(this);

            if (ignoreChildren)
            {
                OnAddedToGUIUpdateList?.Invoke(this);
                return;
            }

            foreach (GUIComponent child in Content.Children)
            {
                if (!child.Visible) { continue; }
                child.AddToGUIUpdateList(false, order);
            }
            OnAddedToGUIUpdateList?.Invoke(this);
        }
    }
}