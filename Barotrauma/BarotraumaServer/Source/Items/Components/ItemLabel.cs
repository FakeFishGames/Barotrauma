using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemLabel : ItemComponent, IDrawableComponent
    {
        [SerializableProperty("", true), Editable(100)]
        public string Text
        {
            get;
            set;
        }

        [Editable, SerializableProperty("0.0,0.0,0.0,1.0", true)]
        public Color TextColor
        {
            get;
            set;
        }
        
        [Editable, SerializableProperty(1.0f, true)]
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
