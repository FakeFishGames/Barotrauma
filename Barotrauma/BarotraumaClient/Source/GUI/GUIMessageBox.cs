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
        private static int DefaultWidth
        {
            get { return Math.Max(400, 400 * (GameMain.GraphicsWidth / 1920)); }
        }

        public List<GUIButton> Buttons { get; private set; } = new List<GUIButton>();
        //public GUIFrame BackgroundFrame { get; private set; }
        public GUILayoutGroup Content { get; private set; }
        public GUIFrame InnerFrame { get; private set; }
        public GUITextBlock Header { get; private set; }
        public GUITextBlock Text { get; private set; }
        public string Tag { get; private set; }

        public static GUIComponent VisibleBox => MessageBoxes.LastOrDefault();

        public GUIMessageBox(string headerText, string text, Vector2? relativeSize = null, Point? minSize = null)
            : this(headerText, text, new string[] { "OK" }, relativeSize, minSize)
        {
            this.Buttons[0].OnClicked = Close;
        }
        
        public GUIMessageBox(string headerText, string text, string[] buttons, Vector2? relativeSize = null, Point? minSize = null, Alignment textAlignment = Alignment.TopLeft, string tag = "")
            : base(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "")
        {
            //int width = (int)(DefaultWidth * GUI.Scale), height = 0;
            int width = DefaultWidth, height = 0;
            if (relativeSize.HasValue)
            {
                width = (int)(GameMain.GraphicsWidth * relativeSize.Value.X);
                height = (int)(GameMain.GraphicsHeight * relativeSize.Value.Y);
            }
            if (minSize.HasValue)
            {
                width = Math.Max(width, minSize.Value.X);
                if (height > 0)
                {
                    height = Math.Max(height, minSize.Value.Y);
                }
            }

            InnerFrame = new GUIFrame(new RectTransform(new Point(width, height), RectTransform, Anchor.Center) { IsFixedSize = false }, style: null);
            GUI.Style.Apply(InnerFrame, "", this);

            Content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), InnerFrame.RectTransform, Anchor.Center)) { AbsoluteSpacing = 5 };
            Tag = tag;
            
            Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), 
                headerText, textAlignment: Alignment.Center, wrap: true);
            GUI.Style.Apply(Header, "", this);
            Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

            if (!string.IsNullOrWhiteSpace(text))
            {
                Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), 
                    text, textAlignment: textAlignment, wrap: true);
                GUI.Style.Apply(Text, "", this);
                Text.RectTransform.NonScaledSize = Text.RectTransform.MinSize = Text.RectTransform.MaxSize = 
                    new Point(Text.Rect.Width, Text.Rect.Height);
                Text.RectTransform.IsFixedSize = true;
            }

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), Content.RectTransform, Anchor.BottomCenter, maxSize: new Point(1000, 50)),
                isHorizontal: true, childAnchor: buttons.Length > 1 ? Anchor.BottomLeft : Anchor.Center)
            {
                AbsoluteSpacing = 5,
                IgnoreLayoutGroups = true
            };
            buttonContainer.RectTransform.NonScaledSize = buttonContainer.RectTransform.MinSize = buttonContainer.RectTransform.MaxSize = 
                new Point(buttonContainer.Rect.Width, (int)(30 * GUI.Scale));
            buttonContainer.RectTransform.IsFixedSize = true;

            if (height == 0)
            {
                height += Header.Rect.Height + Content.AbsoluteSpacing;
                height += (Text == null ? 0 : Text.Rect.Height) + Content.AbsoluteSpacing;
                height += buttonContainer.Rect.Height;
                if (minSize.HasValue)
                {
                    height = Math.Max(height, minSize.Value.Y);
                }

                InnerFrame.RectTransform.NonScaledSize = 
                    new Point(InnerFrame.Rect.Width, (int)Math.Max(height / Content.RectTransform.RelativeSize.Y, height + (int)(50 * GUI.yScale)));
                Content.RectTransform.NonScaledSize =
                    new Point(Content.Rect.Width, height);
            }

            Buttons = new List<GUIButton>(buttons.Length);
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = new GUIButton(new RectTransform(new Vector2(Math.Min(0.9f / buttons.Length, 0.5f), 1.0f), buttonContainer.RectTransform), buttons[i], style: "GUIButtonLarge");
                Buttons.Add(button);
            }

            MessageBoxes.Add(this);
        }

        ///// <summary>
        ///// This is the new constructor.
        ///// TODO: for some reason the background does not prohibit input on the elements that are behind the box
        ///// TODO: allow providing buttons in the constructor
        ///// </summary>
        /*public GUIMessageBox(RectTransform rectT, string headerText, string text, Alignment textAlignment = Alignment.TopCenter)
            : base(rectT, "")
        {
            //BackgroundFrame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight), rectT, Anchor.Center), null, Color.Black * 0.5f);
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
        }*/

        //public override void AddToGUIUpdateList(bool ignoreChildren = false, bool updateLast = false)
        //{
        //    base.AddToGUIUpdateList(ignoreChildren, updateLast);
        //}

        //public override void Draw(SpriteBatch spriteBatch, bool drawChildren = true)
        //{
        //    if (RectTransform == null)
        //    {
        //        base.Draw(spriteBatch, drawChildren);
        //    }
        //    else
        //    {
        //        // Custom draw order so that the background is rendered behind the parent.
        //        if (drawChildren)
        //        {
        //            BackgroundFrame?.Draw(spriteBatch);
        //        }
        //        base.Draw(spriteBatch, false);
        //        if (drawChildren)
        //        {
        //            InnerFrame?.Draw(spriteBatch);
        //            Header?.Draw(spriteBatch);
        //            Text?.Draw(spriteBatch);
        //            Buttons.ForEach(b => b.Draw(spriteBatch));
        //        }
        //    }
        //}
        
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
