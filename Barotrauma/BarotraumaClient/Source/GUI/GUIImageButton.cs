using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    public class GUIImageButton : GUIComponent
    {
        //Image Vars
        public float Rotation;

        private Sprite sprite;

        private Rectangle sourceRect;

        bool crop;

        //Button Vars
        protected GUITextBlock textBlock;

        public delegate bool OnDoubleClickedHandler(GUIImageButton button, object obj);
        public OnDoubleClickedHandler OnDoubleClicked;

        public delegate bool OnClickedHandler(GUIImageButton button, object obj);
        public OnClickedHandler OnClicked;

        public delegate bool OnPressedHandler();
        public OnPressedHandler OnPressed;

        public bool CanBeSelected = true;

        private bool enabled;

        public bool Enabled
        {
            get
            {
                return enabled;
            }

            set
            {
                if (value == enabled) return;

                enabled = value;
                //frame.Color = enabled ? color : Color.Gray * 0.7f;
            }
        }

        public override Color Color
        {
            get { return base.Color; }
            set
            {
                base.Color = value;
                //frame.Color = value;
            }
        }

        public bool Crop
        {
            get
            { 
                return crop;
            }
            set
            {
                crop = value;
                if (crop)
                {                                
                    sourceRect.Width = Math.Min(sprite.SourceRect.Width, Rect.Width);
                    sourceRect.Height = Math.Min(sprite.SourceRect.Height, Rect.Height);
                }
            }
        }

        public float Scale
        {
            get;
            set;
        }

        public Color TextColor
        {
            get { return textBlock.TextColor; }
            set { textBlock.TextColor = value; }
        }

        public override ScalableFont Font
        {
            get
            {
                return (textBlock == null) ? GUI.Font : textBlock.Font;
            }
            set
            {
                base.Font = value;
                if (textBlock != null) textBlock.Font = value;
            }
        }

        public string Text
        {
            get { return textBlock.Text; }
            set { textBlock.Text = value; }
        }

        public override string ToolTip
        {
            get
            {
                return base.ToolTip;
            }
            set
            {
                textBlock.ToolTip = value;
                base.ToolTip = value;
            }
        }

        public Rectangle SourceRect
        {
            get { return sourceRect; }
            set { sourceRect = value; }
        }

        public bool Selected { get; set; }

        public GUIImageButton(Rectangle rect, string spritePath, Alignment alignment, GUIComponent parent = null)
            : this(rect, new Sprite(spritePath, Vector2.Zero), alignment, parent)
        {
        }

        public GUIImageButton(Rectangle rect, Sprite sprite, Alignment alignment, GUIComponent parent = null)
            : this(rect, sprite==null ? Rectangle.Empty : sprite.SourceRect, sprite, alignment, parent)
        {
        }

        public GUIImageButton(Rectangle rect, Rectangle sourceRect, Sprite sprite, Alignment alignment, GUIComponent parent = null)
            : base(null)
        {
            this.rect = rect;

            this.alignment = alignment;

            color = Color.White;

            //alpha = 1.0f;

            Scale = 1.0f;

            this.sprite = sprite;

            if (rect.Width == 0) this.rect.Width = (int)sprite.size.X;
            if (rect.Height == 0) this.rect.Height = (int)Math.Min(sprite.size.Y, sprite.size.Y * (this.rect.Width / sprite.size.X));

            this.sourceRect = sourceRect;

            if (parent != null) parent.AddChild(this);
            this.parent = parent;

            //frame = new GUIFrame(Rectangle.Empty, style, this);
            //GUI.Style.Apply(frame, style == "" ? "GUIButton" : style);

            textBlock = new GUITextBlock(Rectangle.Empty, "",
                Color.Transparent, (this.style == null) ? Color.Black : this.style.textColor,
                Alignment.CenterLeft, null, this);
            //GUI.Style.Apply(textBlock, style, this);

            Enabled = true;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Color currColor = color;
            if (state == ComponentState.Hover) currColor = hoverColor;
            if (state == ComponentState.Selected) currColor = selectedColor;

            if (sprite != null && sprite.Texture != null)
            {
                spriteBatch.Draw(sprite.Texture, new Vector2(rect.X, rect.Y), sourceRect, currColor * (currColor.A / 255.0f), Rotation, Vector2.Zero,
                    Scale, SpriteEffects.None, 0.0f);
            }          
            
            DrawChildren(spriteBatch);
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;
            base.Update(deltaTime);
            if (rect.Contains(PlayerInput.MousePosition) && CanBeSelected && Enabled && (MouseOn == null || MouseOn == this || IsParentOf(MouseOn)))
            {
                state = ComponentState.Hover;
                if (PlayerInput.LeftButtonHeld())
                {
                    if (OnPressed != null)
                    {
                        if (OnPressed()) state = ComponentState.Pressed;
                    }
                }
                else if (PlayerInput.DoubleClicked())
                {
                    GUI.PlayUISound(GUISoundType.Click);
                    if (OnDoubleClicked != null)
                    {
                        if (OnDoubleClicked(this, UserData) && CanBeSelected) state = ComponentState.Selected;
                    }
                    else
                    {
                        Selected = !Selected;
                        // state = state == ComponentState.Selected ? ComponentState.None : ComponentState.Selected;
                    }
                }
                else if (PlayerInput.LeftButtonClicked())
                {
                    GUI.PlayUISound(GUISoundType.Click);
                    if (OnClicked != null)
                    {
                        if (OnClicked(this, UserData) && CanBeSelected) state = ComponentState.Selected;
                    }
                    else
                    {
                        Selected = !Selected;
                        // state = state == ComponentState.Selected ? ComponentState.None : ComponentState.Selected;
                    }
                }
            }
            else
            {
                state = Selected ? ComponentState.Selected : ComponentState.None;
            }
            //frame.State = state;
        }
    }
}
