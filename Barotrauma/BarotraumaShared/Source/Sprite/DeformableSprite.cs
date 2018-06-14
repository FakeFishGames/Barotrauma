using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class DeformableSprite
    {
        private Sprite sprite;

        public Vector2 Size
        {
            get { return sprite.size; }
        }

        public Vector2 Origin
        {
            get { return sprite.Origin; }
            set { sprite.Origin = value; }
        }

        public Sprite Sprite
        {
            get { return sprite; }
        }

        public DeformableSprite(XElement element, int? subdivisionsX = null, int? subdivisionsY = null)
        {
            sprite = new Sprite(element);
            InitProjSpecific(element, subdivisionsX, subdivisionsY);
        }

        partial void InitProjSpecific(XElement element, int? subdivisionsX, int? subdivisionsY);
    }
}
