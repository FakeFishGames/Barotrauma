using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class GUIMessageBox : GUIFrame
    {
        public static List<GUIComponent> MessageBoxes = new List<GUIComponent>();

        public const int DefaultWidth = 400, DefaultHeight = 250;
        
        public List<GUIButton> Buttons { get; private set; } = new List<GUIButton>();
        public GUIFrame BackgroundFrame { get; private set; }
        public GUIFrame InnerFrame { get; private set; }
        public GUITextBlock Header { get; private set; }
        public GUITextBlock Text { get; private set; }

        public static GUIComponent VisibleBox
        {
            get { return MessageBoxes.Count == 0 ? null : MessageBoxes[0]; }
        }

        public GUIMessageBox(string headerText, string text)
            : this(headerText, text, new string[] {"OK"}, DefaultWidth, 0)
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(string headerText, string text, int width, int height)
            : this(headerText, text, new string[] { "OK" }, width, height)
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(string headerText, string text, string[] buttons, int width = DefaultWidth, int height = 0, Alignment textAlignment = Alignment.TopLeft, GUIComponent parent = null)
            : base(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                Color.Black * 0.5f, Alignment.TopLeft, null, parent)
        {
            int headerHeight = 30;

            var frame = new GUIFrame(new Rectangle(0, 0, width, height), null, Alignment.Center, "", this);
            GUI.Style.Apply(frame, "", this);
            
            if (height == 0)
            {
                string wrappedText = ToolBox.WrapText(text, frame.Rect.Width - frame.Padding.X - frame.Padding.Z, GUI.Font);
                string[] lines = wrappedText.Split('\n');
                foreach (string line in lines)
                {
                    height += (int)GUI.Font.MeasureString(line).Y;
                }
                height += string.IsNullOrWhiteSpace(headerText) ? 220 : 220 - headerHeight;
            }
            frame.Rect = new Rectangle(frame.Rect.X, GameMain.GraphicsHeight / 2 - height/2, frame.Rect.Width, height);

            var header = new GUITextBlock(new Rectangle(0, 0, 0, headerHeight), headerText, null, null, textAlignment, "", frame, true);
            GUI.Style.Apply(header, "", this);            

            if (!string.IsNullOrWhiteSpace(text))
            {
                var textBlock = new GUITextBlock(new Rectangle(0, string.IsNullOrWhiteSpace(headerText) ? 0 : headerHeight, 0, height - 70), text,
                    null, null, textAlignment, "", frame, true);
                GUI.Style.Apply(textBlock, "", this);
            }

            int x = 0;
            Buttons = new List<GUIButton>(buttons.Length);
            for (int i = 0; i < buttons.Length; i++)
            {
                this.Buttons[i] = new GUIButton(new Rectangle(x, 0, 150, 30), buttons[i], Alignment.Left | Alignment.Bottom, "", frame);

                x += this.Buttons[i].Rect.Width + 20;
            }

            MessageBoxes.Add(this);
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIMessageBox(RectTransform rectT, string headerText, string text, Alignment textAlignment = Alignment.TopCenter)
            : base(rectT, "")
        {
            BackgroundFrame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), rectT, Anchor.Center), null, Color.Black * 0.5f);
            float headerHeight = 0.2f;
            float margin = 0.05f;
            InnerFrame = new GUIFrame(rectT);
            GUI.Style.Apply(InnerFrame, "", this);
            Header = null;
            if (!string.IsNullOrWhiteSpace(headerText))
            {
                Header = new GUITextBlock(new RectTransform(new Vector2(1, headerHeight), InnerFrame.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0, margin)
                }, headerText, textAlignment: Alignment.Center);
                GUI.Style.Apply(Header, "", this);
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                float offset = headerHeight + margin;
                var size = Header == null ? Vector2.One : new Vector2(1 - margin * 2, 1 - offset + margin);
                Text = new GUITextBlock(new RectTransform(size, InnerFrame.RectTransform, Anchor.TopCenter)
                {
                    RelativeOffset = new Vector2(0, offset)
                }, text, textAlignment: textAlignment, wrap: true);
                GUI.Style.Apply(Text, "", this);
            }
            MessageBoxes.Add(this);
        }

        // Custom draw order so that the background is rendered behind the parent.
        public override void Draw(SpriteBatch spriteBatch, bool drawChildren = true)
        {
            if (drawChildren)
            {
                BackgroundFrame.Draw(spriteBatch);
            }
            base.Draw(spriteBatch, false);
            if (drawChildren)
            {
                InnerFrame.Draw(spriteBatch);
                Header.Draw(spriteBatch);
                Text.Draw(spriteBatch);
                Buttons.ForEach(b => b.Draw(spriteBatch));
            }
        }

        public void Close()
        {
            if (Parent != null) Parent.RemoveChild(this);
            if (MessageBoxes.Contains(this)) MessageBoxes.Remove(this);
        }

        public bool Close(GUIButton button, object obj)
        {
            Close();

            return true;
        }

        public static void CloseAll()
        {
            MessageBoxes.Clear();
        }

        /// <summary>
        /// Parent does not matter. It's overridden.
        /// </summary>
        public void AddButton(RectTransform rectT, string text, GUIButton.OnClickedHandler onClick)
        {
            rectT.Parent = RectTransform;
            Buttons.Add(new GUIButton(rectT, text) { OnClicked = onClick });
        }
    }
}
