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

        public enum ComponentState { None, Hover, Selected};

        protected Alignment alignment;

        protected static KeyboardDispatcher keyboardDispatcher;

        protected object userData;

        protected Rectangle rect;

        protected Vector4 padding;

        protected Color color;
        protected Color hoverColor;
        protected Color selectedColor;

        protected GUIComponent parent;
        public List<GUIComponent> children;

        protected ComponentState state;

        protected float alpha;
                
        public GUIComponent Parent
        {
            get { return parent; }
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


        public float Alpha
        {
            get
            {
                return alpha;
            }
            set
            {
                alpha = MathHelper.Clamp(value, 0.0f, 1.0f);
                foreach (GUIComponent child in children)
                {
                    child.Alpha = value;
                }
            }
        }

        protected GUIComponent()
        {
            alpha = 1.0f;

            children = new List<GUIComponent>();
        }

        public static void Init(GameWindow window)
        {
            keyboardDispatcher = new KeyboardDispatcher(window);
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


            //Color newColor = color;
            //if (state == ComponentState.Selected)   newColor = selectedColor;
            //if (state == ComponentState.Hover)      newColor = hoverColor;

            //GUI.DrawRectangle(spriteBatch, rect, newColor*alpha, true);
            //DrawChildren(spriteBatch);
        }

        public virtual void Update(float deltaTime)
        {
            if (rect.Contains(PlayerInput.MousePosition))
            {
                MouseOn = this;
            }
            else
            {
                if (MouseOn == this) MouseOn = null;
            }

            foreach (GUIComponent child in children)
            {
                child.Update(deltaTime);
            }
        }

        protected virtual void UpdateDimensions(GUIComponent parent)
        {
            if (parent!=null)
            {
                if (rect.Width == 0) rect.Width = parent.Rect.Width - rect.X 
                    - (int)parent.Padding.X - (int)parent.Padding.Z;

                if (rect.Height == 0) rect.Height = parent.Rect.Height - rect.Y
                    - (int)parent.Padding.Y - (int)parent.Padding.W;

                if (alignment.HasFlag(Alignment.CenterX))
                {
                    rect.X += parent.Rect.X + (int)parent.Rect.Width/2 - (int)rect.Width/2;
                }
                else if (alignment.HasFlag(Alignment.Right))
                {
                    rect.X += parent.Rect.X + (int)parent.Rect.Width - (int)parent.Padding.Z - (int)rect.Width;
                }
                else
                {
                    rect.X += parent.Rect.X + (int)parent.Padding.X;
                }

                if (alignment.HasFlag(Alignment.CenterY))
                {
                    rect.Y += parent.Rect.Y + (int)parent.Rect.Height / 2 - (int)rect.Height / 2;
                }
                else if (alignment.HasFlag(Alignment.Bottom))
                {
                    rect.Y += parent.Rect.Y + (int)parent.Rect.Height - (int)parent.Padding.W - (int)rect.Height;
                }
                else
                {
                    rect.Y += parent.Rect.Y + (int)parent.Padding.Y;
                }

            }
        }

        public virtual void DrawChildren(SpriteBatch spriteBatch)
        {
            foreach (GUIComponent child in children)
            {
                child.Draw(spriteBatch);
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

        public virtual void ClearChildren()
        {
            children.Clear();
        }
    }
}
