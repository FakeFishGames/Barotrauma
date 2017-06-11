using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Xml.Linq;
namespace Barotrauma.Items.Components
{
    class ItemLabel : ItemComponent, IDrawableComponent
    {
        private GUITextBlock textBlock;

        [HasDefaultValue("", true), Editable(100)]
        public string Text
        {
            get { return textBlock.Text.Replace("\n", ""); }
            set 
            {
                if (value == TextBlock.Text || item.Rect.Width < 5) return;

                if (textBlock.Rect.Width != item.Rect.Width  || textBlock.Rect.Height != item.Rect.Height)
                {
                    textBlock = null;
                }

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
                if (textBlock != null) textBlock.TextColor = textColor;
            }
        }
        
        [Editable, HasDefaultValue(1.0f, true)]
        public float TextScale
        {
            get { return textBlock == null ? 1.0f : textBlock.TextScale; }
            set
            {
                if (textBlock != null) textBlock.TextScale = MathHelper.Clamp(value, 0.1f, 10.0f);
            }
        }

        private GUITextBlock TextBlock
        {
            get 
            { 
                if (textBlock == null)
                {
                    textBlock = new GUITextBlock(new Rectangle(item.Rect.X,-item.Rect.Y,item.Rect.Width, item.Rect.Height), "", 
                        Color.Transparent, textColor, 
                        Alignment.TopLeft, Alignment.Center, 
                        null, null, true);
                    textBlock.Font = GUI.SmallFont;
                    textBlock.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    textBlock.TextDepth = item.Sprite.Depth - 0.0001f;
                    textBlock.TextScale = TextScale;
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
        
        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            var drawPos = new Vector2(
                item.DrawPosition.X - item.Rect.Width/2.0f,
                -(item.DrawPosition.Y + item.Rect.Height/2.0f));

            textBlock.Draw(spriteBatch, drawPos - textBlock.Rect.Location.ToVector2());
        }
    }
}