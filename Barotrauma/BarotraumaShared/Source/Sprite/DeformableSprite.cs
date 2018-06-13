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

        public DeformableSprite(XElement element, int? subdivisionsX, int? subdivisionsY)
        {
            sprite = new Sprite(element);
            InitProjSpecific(element, subdivisionsX, subdivisionsY);
        }

        partial void InitProjSpecific(XElement element, int? subdivisionsX, int? subdivisionsY);
    }
}
