using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;
namespace Barotrauma.Items.Components
{
    class ItemLabel : ItemComponent
    {
        private GUITextBlock textBlock;

        [HasDefaultValue("", true), Editable(100)]
        public string Text
        {
            get { return textBlock.Text; }
            set 
            {
                if (value == TextBlock.Text || item.Rect.Width < 5) return;
                TextBlock.Text = value;
            }
        }

        private Color textColor;
        [Editable, HasDefaultValue("0.0,0.0,0.0,1.0", true)]
        public string TextColor
        {
            get { return ToolBox.Vector4ToString(textColor.ToVector4()); }
            set
            {
                textColor = new Color(ToolBox.ParseToVector4(value));
            }
        }

        private GUITextBlock TextBlock
        {
            get 
            { 
                if (textBlock==null)
                {
                    textBlock = new GUITextBlock(new Rectangle(item.Rect.X,-item.Rect.Y,item.Rect.Width, item.Rect.Height), "", 
                        Color.Transparent, Color.Black, 
                        Alignment.TopLeft, Alignment.Center, 
                        null, null, true);
                    textBlock.Font = GUI.SmallFont;
                    textBlock.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                }
                return textBlock; 
            }
        }

        public override void Move(Vector2 amount)
        {
            textBlock.Rect = new Rectangle(item.Rect.X, -item.Rect.Y, item.Rect.Width, item.Rect.Height);
        }

        public ItemLabel(Item item, XElement element)
            : base(item, element)
        {
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            base.Draw(spriteBatch, editing);

            textBlock.Draw(spriteBatch);
        }
    }
}