using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Ladder : ItemComponent, IDrawableComponent
    {
        public float BackgroundSpriteDepth
        {
            get { return item.GetDrawDepth() + 0.05f; }
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
                new Vector2(item.DrawPosition.X - item.Rect.Width / 2 * item.Scale, -(item.DrawPosition.Y + item.Rect.Height / 2)) - backgroundSprite.Origin * item.Scale,
                new Vector2(backgroundSprite.size.X * item.Scale, item.Rect.Height), color: item.Color,
                textureScale: Vector2.One * item.Scale,
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
