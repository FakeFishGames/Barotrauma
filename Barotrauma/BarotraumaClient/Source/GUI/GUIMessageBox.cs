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

        public enum Type
        {
            Default,
            InGame
        }

        public List<GUIButton> Buttons { get; private set; } = new List<GUIButton>();
        //public GUIFrame BackgroundFrame { get; private set; }
        public GUILayoutGroup Content { get; private set; }
        public GUIFrame InnerFrame { get; private set; }
        public GUITextBlock Header { get; private set; }
        public GUITextBlock Text { get; private set; }
        public string Tag { get; private set; }

        public GUIImage Icon
        {
            get;
            private set;
        }

        public Color IconColor
        {
            get { return Icon == null ? Color.White : Icon.Color; }
            set
            {
                if (Icon == null) { return; }
                Icon.Color = value;
            }
        }

        private bool alwaysVisible;

        private float openState;
        private bool closing;

        private Type type;

        public static GUIComponent VisibleBox => MessageBoxes.LastOrDefault();

        public GUIMessageBox(string headerText, string text, Vector2? relativeSize = null, Point? minSize = null)
            : this(headerText, text, new string[] { "OK" }, relativeSize, minSize)
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(string headerText, string text, string[] buttons, Vector2? relativeSize = null, Point? minSize = null, Alignment textAlignment = Alignment.TopLeft, Type type = Type.Default, string tag = "", Sprite icon = null)
            : base(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "GUIMessageBox." + type)
        {
            int width = (int)(DefaultWidth * (type == Type.Default ? 1.0f : 1.5f)), height = 0;
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

            InnerFrame = new GUIFrame(new RectTransform(new Point(width, height), RectTransform, type == Type.InGame ? Anchor.TopCenter : Anchor.Center) { IsFixedSize = false }, style: null);
            GUI.Style.Apply(InnerFrame, "", this);
            this.type = type;
            Tag = tag;

            if (type == Type.Default)
            {
                Content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), InnerFrame.RectTransform, Anchor.Center)) { AbsoluteSpacing = 5 };
                            
                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), 
                    headerText, textAlignment: Alignment.Center, wrap: true);
                GUI.Style.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
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
                    if (minSize.HasValue) { height = Math.Max(height, minSize.Value.Y); }

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
            }
            else if (type == Type.InGame)
            {
                InnerFrame.RectTransform.AbsoluteOffset = new Point(0, GameMain.GraphicsHeight);
                alwaysVisible = true;
                CanBeFocused = false;
                GUI.Style.Apply(InnerFrame, "", this);

                var horizontalLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), InnerFrame.RectTransform, Anchor.Center), 
                    isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };
                if (icon != null)
                {
                    Icon = new GUIImage(new RectTransform(new Vector2(0.2f, 0.95f), horizontalLayoutGroup.RectTransform), icon, scaleToFit: true);
                }

                Content = new GUILayoutGroup(new RectTransform(new Vector2(icon != null ? 0.65f : 0.85f, 1.0f), horizontalLayoutGroup.RectTransform));

                var buttonContainer = new GUIFrame(new RectTransform(new Vector2(0.15f, 1.0f), horizontalLayoutGroup.RectTransform), style: null);
                Buttons = new List<GUIButton>(1)
                {
                    new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), buttonContainer.RectTransform, Anchor.Center), style: "GUIButtonSolidHorizontalArrow")
                    {
                        OnClicked = Close
                    }
                };

                Header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), headerText, wrap: true);
                GUI.Style.Apply(Header, "", this);
                Header.RectTransform.MinSize = new Point(0, Header.Rect.Height);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), Content.RectTransform), text, textAlignment: textAlignment, wrap: true);
                    GUI.Style.Apply(Text, "", this);
                    /*Content.Recalculate();
                    Text.RectTransform.NonScaledSize = Text.RectTransform.MinSize = Text.RectTransform.MaxSize =
                        new Point(Text.Rect.Width, Text.Rect.Height);
                    Text.RectTransform.IsFixedSize = true;*/
                }

                if (height == 0)
                {
                    height += Header.Rect.Height + Content.AbsoluteSpacing;
                    height += (Text == null ? 0 : Text.Rect.Height) + Content.AbsoluteSpacing;
                    if (minSize.HasValue) { height = Math.Max(height, minSize.Value.Y); }

                    InnerFrame.RectTransform.NonScaledSize =
                        new Point(InnerFrame.Rect.Width, (int)Math.Max(height / Content.RectTransform.RelativeSize.Y, height + (int)(50 * GUI.yScale)));
                    Content.RectTransform.NonScaledSize =
                        new Point(Content.Rect.Width, height);
                }
                Buttons[0].RectTransform.MaxSize = new Point(Math.Min(Buttons[0].Rect.Width, Buttons[0].Rect.Height));
            }
            
            MessageBoxes.Add(this);
        }

        public static void AddActiveToGUIUpdateList()
        {
            for (int i = 0; i < MessageBoxes.Count; i++)
            {
                if (MessageBoxes[i] is GUIMessageBox alwaysVisibleMsgBox && alwaysVisibleMsgBox.alwaysVisible)
                {
                    alwaysVisibleMsgBox.AddToGUIUpdateList();
                    break;
                }
            }
            for (int i = MessageBoxes.Count - 1; i >= 0; i--)
            {
                if (MessageBoxes[i].UserData as string == "verificationprompt" ||
                    MessageBoxes[i].UserData as string == "bugreporter")
                {
                    continue;
                }
                if (!(MessageBoxes[i] is GUIMessageBox msgBox) || !msgBox.alwaysVisible)
                {
                    MessageBoxes[i].AddToGUIUpdateList();
                    break;
                }
            }
        }

        protected override void Update(float deltaTime)
        {
            if (type == Type.InGame)
            {
                Vector2 initialPos = new Vector2(0.0f, GameMain.GraphicsHeight);
                Vector2 defaultPos = new Vector2(0.0f, HUDLayoutSettings.InventoryAreaLower.Y - InnerFrame.Rect.Height - 20 * GUI.Scale);
                Vector2 endPos = new Vector2(GameMain.GraphicsWidth, defaultPos.Y);

                /*for (int i = MessageBoxes.IndexOf(this); i >= 0; i--)
                {
                    if (MessageBoxes[i] is GUIMessageBox otherMsgBox && otherMsgBox != this && otherMsgBox.type == type && !otherMsgBox.closing)
                    {
                        defaultPos = new Vector2(
                            Math.Max(otherMsgBox.InnerFrame.RectTransform.AbsoluteOffset.X + 10 * GUI.Scale, defaultPos.X),
                            Math.Max(otherMsgBox.InnerFrame.RectTransform.AbsoluteOffset.Y + 10 * GUI.Scale, defaultPos.Y));
                    }
                }*/

                if (!closing)
                {
                    InnerFrame.RectTransform.AbsoluteOffset = Vector2.SmoothStep(initialPos, defaultPos, openState).ToPoint();
                    openState = Math.Min(openState + deltaTime * 2.0f, 1.0f);
                }
                else
                {
                    openState += deltaTime * 2.0f;
                    InnerFrame.RectTransform.AbsoluteOffset = Vector2.SmoothStep(defaultPos, endPos, openState - 1.0f).ToPoint();
                    if (openState >= 2.0f)
                    {
                        if (Parent != null) { Parent.RemoveChild(this); }
                        if (MessageBoxes.Contains(this)) { MessageBoxes.Remove(this); }
                    }
                }
            }
        }


        public void Close()
        {
            if (type == Type.InGame)
            {
                closing = true;
            }
            else
            {
                if (Parent != null) { Parent.RemoveChild(this); }
                if (MessageBoxes.Contains(this)) { MessageBoxes.Remove(this); }
            }
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
