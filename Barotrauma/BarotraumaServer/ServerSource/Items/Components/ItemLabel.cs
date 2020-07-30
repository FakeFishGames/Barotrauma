using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemLabel : ItemComponent, IDrawableComponent
    {
        [Serialize("", true, description: "The text to display on the label.", alwaysUseInstanceValues: true), Editable(100)]
        public string Text
        {
            get;
            set;
        }

        [Editable, Serialize("0,0,0,255", true, description: "The color of the text displayed on the label.", alwaysUseInstanceValues: true)]
        public Color TextColor
        {
            get;
            set;
        }
        
        [Editable, Serialize(1.0f, true, description: "The scale of the text displayed on the label.", alwaysUseInstanceValues: true)]
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
