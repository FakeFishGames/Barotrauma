using System;
using System.Collections.Generic;
using System.Linq;
using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public abstract class GUIComponent
    {
        const float FlashDuration = 1.5f;

        public static GUIComponent MouseOn;
        
        protected static KeyboardDispatcher keyboardDispatcher;

        public enum ComponentState { None, Hover, Selected};

        protected Alignment alignment;

        protected GUIComponentStyle style;
        
        protected object userData;

        protected Rectangle rect;

        public bool CanBeFocused;

        protected Vector4 padding;

        protected Color color;
        protected Color hoverColor;
        protected Color selectedColor;

        protected GUIComponent parent;
        public List<GUIComponent> children;

        protected ComponentState state;

        protected Color flashColor;
        protected float flashTimer;

        public virtual SpriteFont Font
        {
            get;
            set;
        }

        public string ToolTip
        {
            get;
            set;
        }

        public bool Visible
        {
            get;
            set;
        }

        public bool TileSprites;

        private GUITextBlock toolTipBlock;

        //protected float alpha;
                
        public GUIComponent Parent
        {
            get { return parent; }
        }

        public Vector2 Center
        {
            get { return new Vector2(rect.Center.X, rect.Center.Y); }
        }
                
        public Rectangle Rect
        {
            get { return rect; }
            set 
            {
                int prevX = rect.X, prevY = rect.Y;
                int prevWidth = rect.Width, prevHeight = rect.Height;

                rect = value;

                if (prevX == rect.X && prevY == rect.Y && rect.Width == prevWidth && rect.Height == prevHeight) return;
                
                foreach (GUIComponent child in children)
                {
                    child.Rect = new Rectangle(
                        child.rect.X + (rect.X - prevX), 
                        child.rect.Y + (rect.Y - prevY),
                        Math.Max(child.rect.Width + (rect.Width - prevWidth),0),
                        Math.Max(child.rect.Height + (rect.Height - prevHeight),0));
                }                
            }
        }

        public List<UISprite> sprites;
        //public Alignment SpriteAlignment { get; set; }
        //public bool RepeatSpriteX, RepeatSpriteY;

        public virtual Color OutlineColor { get; set; }

        public ComponentState State
        {
            get { return state; }
            set { state = value; }
        }

        public object UserData
        {
            get { return userData; }
            set { userData = value; }
        }

        public virtual Vector4 Padding
        {
            get { return padding; }
            set { padding = value; }
        }

        public int CountChildren
        {
            get { return children.Count(); }
        }

        public virtual Color Color
        {
            get { return color; }
            set { color = value; }
        }

        public virtual Color HoverColor
        {
            get { return hoverColor; }
            set { hoverColor = value; }
        }

        public virtual Color SelectedColor
        {
            get { return selectedColor; }
            set { selectedColor = value; }
        }

        public static KeyboardDispatcher KeyboardDispatcher
        {
            get { return keyboardDispatcher; }
        }

        protected GUIComponent(GUIStyle style)
        {
            Visible = true;

            TileSprites = true;

            OutlineColor = Color.Transparent;

            Font = GUI.Font;

            sprites = new List<UISprite>();
            children = new List<GUIComponent>();

            CanBeFocused = true;

            if (style!=null) style.Apply(this);
        }

        public static void Init(GameWindow window)
        {
            keyboardDispatcher = new KeyboardDispatcher(window);
        }

        public T GetChild<T>()
        {
            foreach (GUIComponent child in children)
            {
                if (child is T) return (T)(object)child;
            }

            return default(T);
        }

        public GUIComponent GetChild(object obj)
        {
            foreach (GUIComponent child in children)
            {
                if (child.UserData == obj) return child;
            }
            return null;
        }

        public bool IsParentOf(GUIComponent component)
        {
            for(int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] == component) return true;
                if (children[i].IsParentOf(component)) return true;
            }

            return false;
        }

        public void Flash(Color? color = null)
        {
            flashTimer = FlashDuration;
            flashColor = (color == null) ? Color.Red * 0.8f : (Color)color;

            foreach (GUIComponent child in children)
            {
                child.Flash();
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch) 
        {
            if (!Visible) return;

            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            if (flashTimer>0.0f)
            {
                GUI.DrawRectangle(spriteBatch, 
                    new Rectangle(rect.X-5,rect.Y-5,rect.Width+10,rect.Height+10), 
                    flashColor * (flashTimer / FlashDuration), true);
            }

            if (currColor.A>0.0f && !sprites.Any()) GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A / 255.0f), true);

            if (sprites != null)
            {
                foreach (UISprite uiSprite in sprites)
                {
                    if (uiSprite.Tile)
                    {
                        Vector2 startPos = new Vector2(rect.X, rect.Y);
                        Vector2 size = new Vector2(Math.Min(uiSprite.Sprite.SourceRect.Width, rect.Width), Math.Min(uiSprite.Sprite.SourceRect.Height, rect.Height));

                        if (uiSprite.Sprite.size.X == 0.0f) size.X = rect.Width;
                        if (uiSprite.Sprite.size.Y == 0.0f) size.Y = rect.Height;

                        uiSprite.Sprite.DrawTiled(spriteBatch, startPos, size, currColor * (currColor.A / 255.0f));
                    }
                    else
                    {
                        if (uiSprite.MaintainAspectRatio)
                        {
                            float scale = (float)(rect.Width) / uiSprite.Sprite.SourceRect.Width;

                            spriteBatch.Draw(uiSprite.Sprite.Texture, rect,
                                new Rectangle(uiSprite.Sprite.SourceRect.X, uiSprite.Sprite.SourceRect.Y, (int)(uiSprite.Sprite.SourceRect.Width), (int)(rect.Height / scale)), 
                                currColor * (currColor.A / 255.0f), 0.0f, Vector2.Zero, SpriteEffects.None, 0.0f);
                        }
                        else
                        {
                            spriteBatch.Draw(uiSprite.Sprite.Texture, rect, uiSprite.Sprite.SourceRect, currColor * (currColor.A / 255.0f));
                        }


                    }
                }
            }

            //Color newColor = color;
            //if (state == ComponentState.Selected)   newColor = selectedColor;
            //if (state == ComponentState.Hover)      newColor = hoverColor;

            //GUI.DrawRectangle(spriteBatch, rect, newColor*alpha, true);
            //DrawChildren(spriteBatch);
        }

        public void DrawToolTip(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            int width = 400;
            if (toolTipBlock==null || (string)toolTipBlock.userData != ToolTip)
            {
                //string wrappedText = ToolBox.WrapText(ToolTip, width, GUI.SmallFont);
                toolTipBlock = new GUITextBlock(new Rectangle(0,0,width, 18), ToolTip, GUI.Style, Alignment.TopLeft, Alignment.TopLeft, null, true, GUI.SmallFont);
                toolTipBlock.padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                toolTipBlock.rect.Width = (int)(GUI.SmallFont.MeasureString(toolTipBlock.Text).X + 20);
                toolTipBlock.rect.Height = toolTipBlock.Text.Split('\n').Length * 18;
                toolTipBlock.Color = Color.Black * 0.7f;
                //toolTipBlock.OutlineColor = Color.White;
                toolTipBlock.userData = ToolTip;
            }

            toolTipBlock.rect = new Rectangle(MouseOn.Rect.Center.X, MouseOn.rect.Bottom, toolTipBlock.rect.Width, toolTipBlock.rect.Height);
            toolTipBlock.Draw(spriteBatch);
        }

        public virtual void Update(float deltaTime)
        {
            if (!Visible) return;

            if (flashTimer>0.0f) flashTimer -= deltaTime;

            if (CanBeFocused)
            {
                if (rect.Contains(PlayerInput.MousePosition))
                {
                    MouseOn = this;
                }
                else
                {
                    if (MouseOn == this) MouseOn = null;
                }

            }

            for (int i = 0; i < children.Count; i++)
            {
                children[i].Update(deltaTime);
            }
        }

        protected virtual void UpdateDimensions(GUIComponent parent = null)
        {
            Rectangle parentRect = (parent==null) ? new Rectangle(0,0,GameMain.GraphicsWidth, GameMain.GraphicsHeight) : parent.rect;

            Vector4 padding = (parent == null) ? Vector4.Zero : parent.padding;

            if (rect.Width == 0) rect.Width = parentRect.Width - rect.X 
                - (int)padding.X - (int)padding.Z;

            if (rect.Height == 0) rect.Height = parentRect.Height - rect.Y
                - (int)padding.Y - (int)padding.W;

            if (alignment.HasFlag(Alignment.CenterX))
            {
                rect.X += parentRect.X + (int)parentRect.Width/2 - (int)rect.Width/2;
            }
            else if (alignment.HasFlag(Alignment.Right))
            {
                rect.X += parentRect.X + (int)parentRect.Width - (int)padding.Z - (int)rect.Width;
            }
            else
            {
                rect.X += parentRect.X + (int)padding.X;
            }

            if (alignment.HasFlag(Alignment.CenterY))
            {
                rect.Y += parentRect.Y + (int)parentRect.Height / 2 - (int)rect.Height / 2;
            }
            else if (alignment.HasFlag(Alignment.Bottom))
            {
                rect.Y += parentRect.Y + (int)parentRect.Height - (int)padding.W - (int)rect.Height;
            }
            else
            {
                rect.Y += parentRect.Y + (int)padding.Y;
            }            
        }

        public virtual void ApplyStyle(GUIComponentStyle style)
        {            
            color = style.Color;
            hoverColor = style.HoverColor;
            selectedColor = style.SelectedColor;
            
            padding = style.Padding;
            sprites = new List<UISprite>(style.Sprites);

            OutlineColor = style.OutlineColor;

            this.style = style;
        }

        public virtual void DrawChildren(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < children.Count; i++ )
            {
                children[i].Draw(spriteBatch);
            }
        }

        public virtual void AddChild(GUIComponent child)
        {
            child.parent = this;
            child.UpdateDimensions(this);

            children.Add(child);
        }

        public virtual void RemoveChild(GUIComponent child)
        {
            if (child == null) return;
            if (children.Contains(child)) children.Remove(child);            
        }

        public GUIComponent FindChild(object userData)
        {
            return children.FirstOrDefault(c => c.userData == userData);
        }

        public List<GUIComponent> FindChildren(object userData)
        {
            return children.FindAll(c => c.userData == userData);
        }

        public virtual void ClearChildren()
        {
            children.Clear();
        }
    }
}
