using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemLabel : ItemComponent, IDrawableComponent
    {
        [HasDefaultValue("", true), Editable(100)]
        public string Text
        {
            get;
            set;
        }

        [Editable, HasDefaultValue("0.0,0.0,0.0,1.0", true)]
        public string TextColor
        {
            get;
            set;
        }
        
        [Editable, HasDefaultValue(1.0f, true)]
        public float TextScale
        {
            get;
            set;
        }
        
        public override void Move(Vector2 amount)
        {
            //do nothing
        }

        public ItemLabel(Item item, XElement element)
            : base(item, element)
        {
        }
    }
}
