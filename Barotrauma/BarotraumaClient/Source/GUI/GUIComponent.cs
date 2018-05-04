using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public abstract class GUIComponent
    {
        #region Hierarchy
        // TODO: remove the backup field when the old system is not needed.
        private GUIComponent parent;
        public GUIComponent Parent => RectTransform != null ? RectTransform.Parent?.Element : parent;

        // TODO: remove the backup field when the old system is not needed.
        private List<GUIComponent> children = new List<GUIComponent>();
        /// <summary>
        /// TODO: return IEnumerable.
        /// Maps RectTransform children's elements to a new collection. For efficiency, access RectTransform.Children directly.
        /// </summary>
        public List<GUIComponent> Children => RectTransform != null ? RectTransform.Children.Select(c => c.Element).ToList() : children;

        public T GetChild<T>() where T : GUIComponent
        {
            foreach (GUIComponent child in Children)
            {
                if (child is T) return child as T;
            }
            return default(T);
        }

        public GUIComponent GetChild(object obj)
        {
            foreach (GUIComponent child in Children)
            {
                if (child.UserData == obj) return child;
            }
            return null;
        }

        public bool IsParentOf(GUIComponent component)
        {
            if (component == null) { return false; }
            if (RectTransform != null && component.RectTransform != null)
            {
                return RectTransform.IsParentOf(component.RectTransform);
            }
            else
            {
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    if (children[i] == component) return true;
                    if (children[i].IsParentOf(component)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// TODO: remove this method
        /// </summary>
        public virtual void AddChild(GUIComponent child)
        {
            if (RectTransform != null)
            {
                DebugConsole.ThrowError("Tried to add child on a component using RectTransform.\n" + Environment.StackTrace);
                return;
            }
            if (child == null) return;
            if (child.IsParentOf(this))
            {
                DebugConsole.ThrowError("Tried to add the parent of a GUIComponent as a child.\n" + Environment.StackTrace);
                return;
            }
            if (child == this)
            {
                DebugConsole.ThrowError("Tried to add a GUIComponent as its own child\n" + Environment.StackTrace);
                return;
            }
            if (children.Contains(child))
            {
                DebugConsole.ThrowError("Tried to add a the same child twice to a GUIComponent" + Environment.StackTrace);
                return;
            }

            child.parent = this;
            child.UpdateDimensions(this);

            children.Add(child);
        }

        /// <summary>
        /// TODO: remove this method
        /// </summary>
        public virtual void RemoveChild(GUIComponent child)
        {
            if (RectTransform != null)
            {
                DebugConsole.ThrowError("Tried to remove a child from a component using RectTransform.\n" + Environment.StackTrace);
                return;
            }
            if (child == null) return;
            if (children.Contains(child)) children.Remove(child);
        }

        // TODO: refactor
        public GUIComponent FindChild(object userData, bool recursive = false)
        {
            var matchingChild = Children.FirstOrDefault(c => c.userData == userData);
            if (recursive && matchingChild == null)
            {
                foreach (GUIComponent child in Children)
                {
                    matchingChild = child.FindChild(userData, recursive);
                    if (matchingChild != null) return matchingChild;
                }
            }

            return matchingChild;
        }

        public IEnumerable<GUIComponent> FindChildren(object userData)
        {
            return Children.Where(c => c.userData == userData);
        }

        /// <summary>
        /// TODO: remove this method
        /// </summary>
        public virtual void ClearChildren()
        {
            if (RectTransform != null)
            {
                DebugConsole.ThrowError("Tried to clear childs from a component using RectTransform.\n" + Environment.StackTrace);
                return;
            }
            children.Clear();
        }
        #endregion

        const float FlashDuration = 1.5f;

        public static GUIComponent MouseOn
        {
            get;
            private set;
        }
        public static void ForceMouseOn(GUIComponent c)
        {
            MouseOn = c;
        }

        protected static List<GUIComponent> ComponentsToUpdate = new List<GUIComponent>();

        public virtual void AddToGUIUpdateList()
        {
            if (!Visible) return;
            if (ComponentsToUpdate.Contains(this)) return;
            ComponentsToUpdate.Add(this);

            List<GUIComponent> fixedChildren = new List<GUIComponent>(Children);
            foreach (GUIComponent c in fixedChildren)
            {
                c.AddToGUIUpdateList();
            }
        }

        public static void ClearUpdateList()
        {
            if (keyboardDispatcher != null &&
                KeyboardDispatcher.Subscriber is GUIComponent &&
                !ComponentsToUpdate.Contains((GUIComponent)KeyboardDispatcher.Subscriber))
            {
                KeyboardDispatcher.Subscriber = null;
            }

            ComponentsToUpdate.Clear();
        }

        public static GUIComponent UpdateMouseOn()
        {
            MouseOn = null;
            for (int i = ComponentsToUpdate.Count - 1; i >= 0; i--)
            {
                GUIComponent c = ComponentsToUpdate[i];
                if (c.MouseRect.Contains(PlayerInput.MousePosition))
                {
                    MouseOn = c;
                    break;
                }
            }
            return MouseOn;
        }

        protected static KeyboardDispatcher keyboardDispatcher;

        public enum ComponentState { None, Hover, Pressed, Selected };

        protected Alignment alignment;

        protected GUIComponentStyle style;

        protected object userData;

        // TODO: remove when the old system is not needed.
        protected Rectangle rect;

        public bool CanBeFocused;

        protected Vector4 padding;

        protected Color color;
        protected Color hoverColor;
        protected Color selectedColor;

        protected ComponentState state;

        protected Color flashColor;
        protected float flashTimer;

        public virtual ScalableFont Font
        {
            get;
            set;
        }

        public virtual string ToolTip
        {
            get;
            set;
        }

        public GUIComponentStyle Style
        {
            get { return style; }
        }

        public bool Visible
        {
            get;
            set;
        }

        public bool TileSprites;

        private static GUITextBlock toolTipBlock;

        public Vector2 Center
        {
            get { return new Vector2(Rect.Center.X, Rect.Center.Y); }
        }

        protected Rectangle ClampRect(Rectangle r)
        {
            if (Parent == null || !ClampMouseRectToParent) return r;
            Rectangle parentRect = Parent.ClampRect(Parent.Rect);
            if (parentRect.Width <= 0 || parentRect.Height <= 0) return Rectangle.Empty;
            if (parentRect.X > r.X)
            {
                int diff = parentRect.X - r.X;
                r.X = parentRect.X;
                r.Width -= diff;
            }
            if (parentRect.Y > r.Y)
            {
                int diff = parentRect.Y - r.Y;
                r.Y = parentRect.Y;
                r.Height -= diff;
            }
            if (parentRect.X + parentRect.Width < r.X + r.Width)
            {
                int diff = (r.X + r.Width) - (parentRect.X + parentRect.Width);
                r.Width -= diff;
            }
            if (parentRect.Y + parentRect.Height < r.Y + r.Height)
            {
                int diff = (r.Y + r.Height) - (parentRect.Y + parentRect.Height);
                r.Height -= diff;
            }
            if (r.Width <= 0 || r.Height <= 0) return Rectangle.Empty;
            return r;
        }

        /// <summary>
        /// Does not set the rect values if the component uses RectTransform. TODO: remove setter.
        /// </summary>
        public virtual Rectangle Rect
        {
            get { return RectTransform != null ? RectTransform.Rect : rect; }
            set
            {
                if (RectTransform == null)
                {
                    int prevX = rect.X, prevY = rect.Y;
                    int prevWidth = rect.Width, prevHeight = rect.Height;

                    rect = value;

                    if (prevX == rect.X && prevY == rect.Y && rect.Width == prevWidth && rect.Height == prevHeight) return;

                    //TODO: fix this (or replace with something better in the new GUI system)
                    //simply expanding the rects by the same amount as their parent only works correctly in some special cases
                    foreach (GUIComponent child in Children)
                    {
                        child.Rect = new Rectangle(
                            child.rect.X + (rect.X - prevX),
                            child.rect.Y + (rect.Y - prevY),
                            Math.Max(child.rect.Width + (rect.Width - prevWidth), 0),
                            Math.Max(child.rect.Height + (rect.Height - prevHeight), 0));
                    }
                }
                if (Parent != null && Parent is GUIListBox)
                {
                    ((GUIListBox)Parent).UpdateScrollBarSize();
                }
            }
        }

        public bool ClampMouseRectToParent = true;
        public virtual Rectangle MouseRect
        {
            get
            {
                if (!CanBeFocused) return Rectangle.Empty;
                return ClampMouseRectToParent ? ClampRect(Rect) : Rect;
            }
        }

        public Dictionary<ComponentState, List<UISprite>> sprites;

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
            get { return Children.Count(); }
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

        protected GUIComponent(string style)
        {
            Visible = true;

            TileSprites = true;

            OutlineColor = Color.Transparent;

            Font = GUI.Font;

            CanBeFocused = true;

            if (style != null)
                GUI.Style.Apply(this, style);
        }

        private RectTransform rectTransform;
        public RectTransform RectTransform
        {
            get { return rectTransform; }
            set
            {
                rectTransform = value;
                // This is the only place where the element should be assigned!
                if (rectTransform != null)
                {
                    rectTransform.Element = this;
                }
            }
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        protected GUIComponent(string style, RectTransform rectT) : this(style)
        {
            RectTransform = rectT;
            rect = RectTransform.Rect;
        }

        public static void Init(GameWindow window)
        {
            keyboardDispatcher = new KeyboardDispatcher(window);
        }

        protected virtual void SetAlpha(float a)
        {
            color = new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, a);
        }

        public virtual void Flash(Color? color = null)
        {
            flashTimer = FlashDuration;
            flashColor = (color == null) ? Color.Red * 0.8f : (Color)color;
        }

        public void FadeOut(float duration, bool removeAfter)
        {
            CoroutineManager.StartCoroutine(LerpAlpha(0.0f, duration, removeAfter));
        }

        private IEnumerable<object> LerpAlpha(float to, float duration, bool removeAfter)
        {
            float t = 0.0f;
            float startA = color.A;

            while (t < duration)
            {
                t += CoroutineManager.DeltaTime;

                SetAlpha(MathHelper.Lerp(startA, to, t / duration));

                yield return CoroutineStatus.Running;
            }

            SetAlpha(to);

            if (removeAfter && Parent != null)
            {
                Parent.RemoveChild(this);
            }

            yield return CoroutineStatus.Success;
        }

        public virtual void Draw(SpriteBatch spriteBatch) 
        {
            if (!Visible) return;
            var rect = Rect;

            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            if (flashTimer > 0.0f)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(rect.X - 5, rect.Y - 5, rect.Width + 10, rect.Height + 10),
                    flashColor * (flashTimer / FlashDuration), true);
            }

            if (currColor.A > 0.0f && (sprites == null || !sprites.Any())) GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A / 255.0f), true);

            if (sprites != null && sprites[state] != null && currColor.A > 0.0f)
            {
                foreach (UISprite uiSprite in sprites[state])
                {
                    if (uiSprite.Slice)
                    {
                        Vector2 pos = new Vector2(rect.X, rect.Y);

                        int centerWidth = Math.Max(rect.Width - uiSprite.Slices[0].Width - uiSprite.Slices[2].Width, 0);
                        int centerHeight = Math.Max(rect.Height - uiSprite.Slices[0].Height - uiSprite.Slices[8].Height, 0);

                        Vector2 scale = new Vector2(
                            MathHelper.Clamp((float)rect.Width / (uiSprite.Slices[0].Width + uiSprite.Slices[2].Width),0, 1),
                            MathHelper.Clamp((float)rect.Height / (uiSprite.Slices[0].Height + uiSprite.Slices[6].Height), 0, 1));

                        for (int x = 0; x < 3; x++)
                        {
                            float width = (x == 1 ? centerWidth : uiSprite.Slices[x].Width) * scale.X;
                            for (int y = 0; y < 3; y++)
                            {
                                float height = (y == 1 ? centerHeight : uiSprite.Slices[x + y * 3].Height) * scale.Y;

                                spriteBatch.Draw(uiSprite.Sprite.Texture,
                                    new Rectangle((int)pos.X, (int)pos.Y, (int)width, (int)height),
                                    uiSprite.Slices[x + y * 3],
                                    currColor * (currColor.A / 255.0f));

                                pos.Y += height;
                            }
                            pos.X += width;
                            pos.Y = rect.Y;
                        }
                    }
                    else if (uiSprite.Tile)
                    {
                        Vector2 startPos = new Vector2(rect.X, rect.Y);
                        Vector2 size = new Vector2(Math.Min(uiSprite.Sprite.SourceRect.Width, rect.Width), Math.Min(uiSprite.Sprite.SourceRect.Height, rect.Height));

                        if (uiSprite.Sprite.size.X == 0.0f) size.X = rect.Width;
                        if (uiSprite.Sprite.size.Y == 0.0f) size.Y = rect.Height;

                        uiSprite.Sprite.DrawTiled(spriteBatch, startPos, size, color: currColor * (currColor.A / 255.0f));
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
        }

        // TODO: use the RectTransform rect? Needs refactoring, because the setter is not accessible.
        public void DrawToolTip(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            int width = 400;
            if (toolTipBlock == null || (string)toolTipBlock.userData != ToolTip)
            {
                toolTipBlock = new GUITextBlock(new Rectangle(0, 0, width, 18), ToolTip, "GUIToolTip", Alignment.TopLeft, Alignment.TopLeft, null, true, GUI.SmallFont);
                toolTipBlock.padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                toolTipBlock.rect.Width = (int)(GUI.SmallFont.MeasureString(toolTipBlock.WrappedText).X + 20);
                toolTipBlock.rect.Height = toolTipBlock.WrappedText.Split('\n').Length * 18 + 7;
                toolTipBlock.userData = ToolTip;

            }

            toolTipBlock.rect = new Rectangle(MouseOn.Rect.Center.X, MouseOn.rect.Bottom, toolTipBlock.rect.Width, toolTipBlock.rect.Height);
            if (toolTipBlock.rect.Right > GameMain.GraphicsWidth - 10)
            {
                toolTipBlock.rect.Location -= new Point(toolTipBlock.rect.Right - (GameMain.GraphicsWidth - 10), 0);
            }

            toolTipBlock.Draw(spriteBatch);
        }

        public virtual void Update(float deltaTime)
        {
            if (!Visible) return;

            if (flashTimer>0.0f) flashTimer -= deltaTime;

            /*if (CanBeFocused)
            {
                if (rect.Contains(PlayerInput.MousePosition))
                {
                    MouseOn = this;
                }
                else
                {
                    if (MouseOn == this) MouseOn = null;
                }

            }*/
            
            //use a fixed list since children can change their order in the main children list
            //TODO: maybe find a more efficient way of handling changes in list order
            // Note: this will create a new list per each frame.
            List<GUIComponent> fixedChildren = new List<GUIComponent>(Children);
            foreach (GUIComponent c in fixedChildren)
            {
                if (!c.Visible) continue;
                c.Update(deltaTime);
            }
        }

        public virtual void SetDimensions(Point size, bool expandChildren = false)
        {
            if (RectTransform != null) { return; }
            rect = new Rectangle(rect.X, rect.Y, size.X, size.Y);

            if (expandChildren)
            {
                //TODO: fix this(or replace with something better in the new GUI system)
                //simply expanding the rects by the same amount as their parent only works correctly in some special cases
                Point expandAmount = size - rect.Size;
                foreach (GUIComponent child in Children)
                {
                    child.Rect = new Rectangle(
                        child.rect.X,
                        child.rect.Y,
                        child.rect.Width + expandAmount.X,
                        child.rect.Height + expandAmount.Y);
                }
            }
        }

        protected virtual void UpdateDimensions(GUIComponent parent = null)
        {
            // Don't manipulate the rect, if the component is using RectTransform component.
            if (RectTransform != null) { return; }

            Rectangle parentRect = (parent == null) ? new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight) : parent.rect;

            Vector4 padding = (parent == null) ? Vector4.Zero : parent.padding;

            if (rect.Width == 0) rect.Width = parentRect.Width - rect.X
                - (int)padding.X - (int)padding.Z;

            if (rect.Height == 0) rect.Height = parentRect.Height - rect.Y
                - (int)padding.Y - (int)padding.W;

            if (alignment.HasFlag(Alignment.CenterX))
            {
                rect.X += parentRect.X + (int)parentRect.Width / 2 - (int)rect.Width / 2;
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
            if (style == null) return;

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
            if (RectTransform != null)
            {
                RectTransform.Children.ForEach(c => c.Element.Draw(spriteBatch));
            }
            else
            {
                for (int i = 0; i < children.Count; i++)
                {
                    children[i].Draw(spriteBatch);
                }
            }
        }
    }
}
