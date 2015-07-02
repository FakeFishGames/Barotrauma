using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class LightComponent : ItemComponent
    {
        private Color lightColor;

        private Sprite sprite;

        [InGameEditable, HasDefaultValue("1.0,1.0,1.0,1.0", true)]
        public string LightColor
        {
            get { return ToolBox.Vector4ToString(lightColor.ToVector4()); }
            set
            {
                lightColor = new Color(ToolBox.ParseToVector4(value));
            }
        }

        public LightComponent(Item item, XElement element)
            : base (item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "sprite") continue;
                sprite = new Sprite(subElement);
                break;
            }

            //lightColor = new Color(ToolBox.GetAttributeVector4(element, "color", Vector4.One));
        }
        
        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (!isActive || sprite==null) return;
            sprite.Draw(spriteBatch, new Vector2(item.Position.X, -item.Position.Y), 0.0f, 1.0f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None);
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            switch (connection.Name)
            {
                case "toggle":
                    isActive = !isActive;
                    break;
                case "set_state":           
                    isActive = (signal != "0");                   
                    break;
            }
        }
    }
}
