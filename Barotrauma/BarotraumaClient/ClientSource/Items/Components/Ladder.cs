using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Ladder : ItemComponent, IDrawableComponent
    {
        public float BackgroundSpriteDepth
        {
            get { return item.GetDrawDepth() + 0.1f; }
        }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        private Sprite backgroundSprite;

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (backgroundSprite == null) { return; }

            backgroundSprite.DrawTiled(spriteBatch,
                new Vector2(item.DrawPosition.X - item.Rect.Width / 2, -(item.DrawPosition.Y + item.Rect.Height / 2)) - backgroundSprite.Origin,
                new Vector2(backgroundSprite.size.X, item.Rect.Height), color: item.Color,
                depth: BackgroundSpriteDepth);
        }

        partial void InitProjSpecific(XElement element)
        {
            var backgroundSpriteElement = element.GetChildElement("backgroundsprite");
            if (backgroundSpriteElement != null)
            {
                backgroundSprite = new Sprite(backgroundSpriteElement);
            }
        }

        partial void RemoveProjSpecific()
        {
            backgroundSprite?.Remove();
            backgroundSprite = null;
        }
    }
}
