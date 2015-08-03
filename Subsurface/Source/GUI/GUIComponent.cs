using System;
using System.Collections.Generic;
using System.Linq;
using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
{
    abstract class GUIComponent
    {
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

        protected List<Sprite> sprites;
        //public Alignment SpriteAlignment { get; set; }
        //public bool RepeatSpriteX, RepeatSpriteY;

        public Color OutlineColor { get; set; }

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

        public Color HoverColor
        {
            get { return hoverColor; }
            set { hoverColor = value; }
        }

        public Color SelectedColor
        {
            get { return selectedColor; }
            set { selectedColor = value; }
        }


        //public float Alpha
        //{
        //    get
        //    {
        //        return alpha;
        //    }
        //    set
        //    {
        //        alpha = MathHelper.Clamp(value, 0.0f, 1.0f);
        //        foreach (GUIComponent child in children)
        //        {
        //            child.Alpha = value;
        //        }
        //    }
        //}

        protected GUIComponent(GUIStyle style)
        {
            //alpha = 1.0f;

            OutlineColor = Color.Transparent;

            Font = GUI.Font;

            sprites = new List<Sprite>();
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
            foreach (GUIComponent child in children)
            {
                if (child == component) return true;
                if (child.IsParentOf(component)) return true;
            }

            return false;
        }

        public virtual void Draw(SpriteBatch spriteBatch) 
        {
            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A / 255.0f), true);

            if (sprites != null)
            {
                foreach (Sprite sprite in sprites)
                {
                    Vector2 startPos = new Vector2(rect.X, rect.Y);
                    Vector2 size = new Vector2(sprite.SourceRect.Width, sprite.SourceRect.Height);

                    if (sprite.size.X == 0.0f) size.X = rect.Width;
                    if (sprite.size.Y == 0.0f) size.Y = rect.Height;

                    sprite.DrawTiled(spriteBatch, startPos, size, currColor * (currColor.A / 255.0f));
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
            int width = 200;
            if (toolTipBlock==null || (string)toolTipBlock.userData != ToolTip)
            {
                string wrappedText = ToolBox.WrapText(ToolTip, width, GUI.SmallFont);
                toolTipBlock = new GUITextBlock(new Rectangle(0,0,width, wrappedText.Split('\n').Length*15), ToolTip, GUI.style, null, true);
                toolTipBlock.userData = ToolTip;
            }

            toolTipBlock.rect = new Rectangle((int)PlayerInput.MousePosition.X, (int)PlayerInput.MousePosition.Y, toolTipBlock.rect.Width, toolTipBlock.rect.Height);
            toolTipBlock.Draw(spriteBatch);
        }

        public virtual void Update(float deltaTime)
        {
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

            foreach (GUIComponent child in children)
            {
                child.Update(deltaTime);
            }
        }

        protected virtual void UpdateDimensions(GUIComponent parent = null)
        {
            Rectangle parentRect = (parent==null) ? new Rectangle(0,0,Game1.GraphicsWidth, Game1.GraphicsHeight) : parent.rect;

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
            sprites = style.Sprites;

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
            if (children.Contains(child)) children.Remove(child);            
        }

        public GUIComponent FindChild(object userData)
        {
            foreach (GUIComponent child in children)
            {
                if (child.userData == userData) return child;
            }

            return null;
        }

        public virtual void ClearChildren()
        {
            children.Clear();
        }
    }
}
