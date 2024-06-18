using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class DeformableSprite
    {
        public Vector2 Size
        {
            get { return Sprite.size; }
        }

        public Vector2 Origin
        {
            get { return Sprite.Origin; }
            set { Sprite.Origin = value; }
        }

        public Sprite Sprite { get; private set; }

        public DeformableSprite(ContentXElement element, int? subdivisionsX = null, int? subdivisionsY = null, string filePath = "", bool lazyLoad = false, bool invert = false, float sourceRectScale = 1)
        {
            Sprite = new Sprite(element, file: filePath, lazyLoad: lazyLoad, sourceRectScale: sourceRectScale);
            InitProjSpecific(element, subdivisionsX, subdivisionsY, lazyLoad, invert);
        }

        partial void InitProjSpecific(XElement element, int? subdivisionsX, int? subdivisionsY, bool lazyLoad, bool invert);
    }
}
