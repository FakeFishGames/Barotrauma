using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class GUIListBox : GUIComponent
    {
        protected List<GUIComponent> selected;

        public delegate bool OnSelectedHandler(GUIComponent component, object obj);
        public OnSelectedHandler OnSelected;

        public delegate object CheckSelectedHandler();
        public CheckSelectedHandler CheckSelected;

        public GUIScrollBar ScrollBar { get; private set; }
        public GUIFrame Content { get; private set; }

        private int totalSize;

        private int spacing;

        private bool scrollBarEnabled;
        private bool scrollBarHidden;

        private bool enabled;

        public bool SelectMultiple;

        public bool HideChildrenOutsideFrame = true;

        public GUIComponent Selected
        {
            get
            {
                return selected.FirstOrDefault();
            }
        }

        public List<GUIComponent> AllSelected
        {
            get { return selected; }
        }

        public object SelectedData
        {
            get
            {
                return Selected?.UserData;
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (Selected == null) return -1;
                return Content.Children.FindIndex(x => x == Selected);
            }
        }

        public float BarScroll
        {
            get { return ScrollBar.BarScroll; }
            set { ScrollBar.BarScroll = value; }
        }

        public float BarSize
        {
            get { return ScrollBar.BarSize; }
        }

        public int Spacing
        {
            get { return spacing; }
            set { spacing = value; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                //scrollBar.Enabled = value;
            }
        }
        
        [Obsolete("Use RectTransform instead of Rect")]
        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                base.Rect = value;
                Content.Rect = value;
                ScrollBar.Rect = ScrollBar.IsHorizontal ?
                    new Rectangle(rect.X, rect.Bottom - 20, rect.Width, 20) :
                    new Rectangle(rect.Right - 20, rect.Y, 20, rect.Height);            
            }
        }

        public override Color Color
        {
            get
            {
                return base.Color;
            }
            set
            {
                base.Color = value;

                Content.Color = value;
            }
        }

        public bool ScrollBarEnabled
        {
            get { return scrollBarEnabled; }
            set
            {
                scrollBarEnabled = value;
            }
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, string style, GUIComponent parent = null)
            : this(rect, style, Alignment.TopLeft, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, string style, Alignment alignment, GUIComponent parent = null)
            : this(rect, null, alignment, style, parent, false)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, Color? color, string style = null, GUIComponent parent = null)
            : this(rect, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIListBox(Rectangle rect, Color? color, Alignment alignment, string style = null, GUIComponent parent = null, bool isHorizontal = false)
            : base(style)
        {
            this.rect = rect;
            this.alignment = alignment;

            selected = new List<GUIComponent>();

            if (color != null) this.color = (Color)color;

            if (parent != null)
                parent.AddChild(this);

            scrollBarHidden = true;

            if (isHorizontal)
            {
                ScrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.X, this.rect.Bottom - 20, this.rect.Width, 20), null, 1.0f, "");
            }
            else
            {
                ScrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.Right - 20, this.rect.Y, 20, this.rect.Height), null, 1.0f, "");
            }

            ScrollBar.IsHorizontal = isHorizontal;

            Content = new GUIFrame(new Rectangle(0, 0, this.rect.Width, this.rect.Height), style, this);
            if (style != null) GUI.Style.Apply(Content, "", this);

            UpdateScrollBarSize();

            //Children.Clear();

            enabled = true;

            scrollBarEnabled = true;

            ScrollBar.BarScroll = 0.0f;
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIListBox(RectTransform rectT, bool isHorizontal = false, Color? color = null, string style = "") : base(style, rectT)
        {
            selected = new List<GUIComponent>();

            Content = new GUIFrame(new RectTransform(Vector2.One, rectT), style);
            Content.CanBeFocused = false;
            if (style != null) GUI.Style.Apply(Content, "", this);

            if (color.HasValue)
            {
                this.color = color.Value;
            }
            scrollBarHidden = true;
            if (isHorizontal)
            {
                ScrollBar = new GUIScrollBar(new RectTransform(new Point(Rect.Width, 20), rectT, Anchor.BottomLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, 20) });
            }
            else
            {
                ScrollBar = new GUIScrollBar(new RectTransform(new Point(20, Rect.Height), rectT, Anchor.TopRight, Pivot.TopLeft) { AbsoluteOffset = new Point(20, 0) });
            }
            ScrollBar.IsHorizontal = isHorizontal;
            UpdateScrollBarSize();
            enabled = true;
            scrollBarEnabled = true;
            ScrollBar.BarScroll = 0.0f;
            padding = Vector4.Zero;
        }

        public void Select(object userData, bool force = false)
        {
            var children = Content.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if ((children[i].UserData != null && children[i].UserData.Equals(userData)) ||
                    (children[i].UserData == null && userData == null))
                {
                    Select(i, force);
                    if (!SelectMultiple) return;
                }
            }
        }

        public override void SetDimensions(Point size, bool expandChildren = false)
        {
            base.SetDimensions(size, expandChildren);

            // TODO: does not work with RectTransform
            if (ScrollBar.IsHorizontal)
            {
                ScrollBar.Rect = new Rectangle(Rect.X, Rect.Bottom - 20, Rect.Width, 20);
            }
            else
            {
                ScrollBar.Rect = new Rectangle(Rect.Right - 20, Rect.Y, 20, Rect.Height);
            }

            UpdateScrollBarSize();
        }

        private void UpdateChildrenRect()
        {
            var children = Content.Children;
            int x = Content.Rect.X, y = Content.Rect.Y;
            if (RectTransform != null)
            {
                x = 0;
                y = 0;
            }
            if (!scrollBarHidden)
            {
                if (ScrollBar.IsHorizontal)
                {
                    x -= (int)((totalSize - Content.Rect.Width) * ScrollBar.BarScroll);
                }
                else
                {
                    y -= (int)((totalSize - Content.Rect.Height) * ScrollBar.BarScroll);
                }
            }

            for (int i = 0; i < children.Count; i++)
            {
                GUIComponent child = children[i];
                if (!child.Visible) { continue; }
                if (RectTransform != null)
                {
                    child.RectTransform.AbsoluteOffset = new Point(x, y);
                }
                else
                {
                    child.Rect = new Rectangle(x, y, child.Rect.Width, child.Rect.Height);
                }
                if (ScrollBar.IsHorizontal)
                {
                    x += child.Rect.Width + spacing;
                }
                else
                {
                    y += child.Rect.Height + spacing;
                }

                // selecting
                if (enabled && child.CanBeFocused && (GUI.IsMouseOn(child)) && child.Rect.Contains(PlayerInput.MousePosition))
                {
                    child.State = ComponentState.Hover;
                    if (PlayerInput.LeftButtonClicked())
                    {
                        Select(i);
                    }
                }
                else if (selected.Contains(child))
                {
                    child.State = ComponentState.Selected;

                    if (CheckSelected != null)
                    {
                        if (CheckSelected() != child.UserData) selected.Remove(child);
                    }
                }
                else
                {
                    child.State = ComponentState.None;
                }
            }
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) { return; }
            base.AddToGUIUpdateList(true, order);
            if (ignoreChildren) { return; }
            Content.AddToGUIUpdateList(true, order);
            var children = Content.Children;
            int lastVisible = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (!child.Visible) continue;
                if (!IsChildInsideFrame(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }
                lastVisible = i;
                child.AddToGUIUpdateList(false, order);
            }
            if (scrollBarEnabled && !scrollBarHidden)
            {
                ScrollBar.AddToGUIUpdateList(false, order);
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            UpdateChildrenRect();

            if ((GUI.IsMouseOn(this) || IsParentOf(GUI.MouseOn) || GUI.IsMouseOn(ScrollBar)) && PlayerInput.ScrollWheelSpeed != 0)
            {
                ScrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * BarSize;
            }
        }

        public void Select(int childIndex, bool force = false)
        {
            var children = Content.Children;
            if (childIndex >= children.Count || childIndex < 0) return;

            bool wasSelected = true;
            if (OnSelected != null) wasSelected = OnSelected(children[childIndex], children[childIndex].UserData) || force;
            
            if (!wasSelected) return;

            if (SelectMultiple)
            {
                if (selected.Contains(children[childIndex]))
                {
                    selected.Remove(children[childIndex]);
                }
                else
                {
                    selected.Add(children[childIndex]);
                }
            }
            else
            {
                selected.Clear();
                selected.Add(children[childIndex]);
            }
        }

        public void Deselect()
        {
            selected.Clear();
        }

        public void UpdateScrollBarSize()
        {
            if (Content == null)
            {
                totalSize = 0;
                return;
            }
            totalSize = (int)(padding.Y + padding.W);
            totalSize += (int)(Content.Padding.Y + Content.Padding.W);

            var children = Content.Children;
            foreach (GUIComponent child in children)
            {
                if (!child.Visible) { continue; }
                totalSize += (ScrollBar.IsHorizontal) ? child.Rect.Width : child.Rect.Height;
            }

            totalSize += (children.Count - 1) * spacing;

            ScrollBar.BarSize = ScrollBar.IsHorizontal ?
                Math.Max(Math.Min(Content.Rect.Width / (float)totalSize, 1.0f), 5.0f / Content.Rect.Width) :
                Math.Max(Math.Min(Content.Rect.Height / (float)totalSize, 1.0f), 5.0f / Content.Rect.Height);

            scrollBarHidden = ScrollBar.BarSize >= 1.0f;
        }

        public override void AddChild(GUIComponent child)
        {
            // The old system calls this method in the constructor. Therefore this check. TODO: remove
            if (child is GUIScrollBar || Content == null)
            {
                base.AddChild(child);
                return;
            }
            if (child.RectTransform != null)
            {
                child.RectTransform.Parent = Content.RectTransform;
            }
            else
            {
                Content.AddChild(child);
            }
            UpdateScrollBarSize();
            // Handle resizing, if the scroll bar size visibility has changed
            if (!scrollBarHidden)
            {
                int x = ScrollBar.IsHorizontal ? 0 : ScrollBar.Rect.Width;
                int y = ScrollBar.IsHorizontal ? ScrollBar.Rect.Height : 0;
                if (Content.RectTransform != null)
                {
                    Content.RectTransform.Resize(new Point(Rect.Width - x, Rect.Height - y), resizeChildren: true);
                }
                else
                {
                    Content.Rect = new Rectangle(Content.Rect.X, Content.Rect.Y, Rect.Width - x, Rect.Height - y);
                }
            }
            else
            {
                if (Content.RectTransform != null)
                {
                    Content.RectTransform.Resize(new Point(Rect.Width, Rect.Height), resizeChildren: true);
                }
                else
                {
                    Content.Rect = Rect;
                }
            }
            UpdateChildrenRect();
        }

        public override void ClearChildren()
        {
            Content.ClearChildren();
            selected.Clear();
        }

        public override void RemoveChild(GUIComponent child)
        {
            if (child == null) return;
            if (RectTransform != null)
            {
                child.RectTransform.Parent = null;
            }
            else
            {
                Content.RemoveChild(child);
            }
            if (selected.Contains(child)) selected.Remove(child);
            UpdateScrollBarSize();
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Content.DrawManually(spriteBatch, alsoChildren: false);

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (HideChildrenOutsideFrame) spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, Content.Rect);

            var children = Content.Children;
            int lastVisible = 0;
            for (int i = 0; i < children.Count; i++)
            {
                GUIComponent child = children[i];
                if (!child.Visible) continue;
                if (!IsChildInsideFrame(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }
                lastVisible = i;
                child.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
            }

            if (HideChildrenOutsideFrame) spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;

            if (ScrollBarEnabled && !scrollBarHidden) ScrollBar.DrawManually(spriteBatch, alsoChildren: true, recursive: true);

            //// Debug
            //GUI.DrawString(spriteBatch, new Vector2(800, 0), "scroll bar total size: " + totalSize.ToString(), Color.White, Color.Black * 0.5f);
            //if (Frame != null && Frame.RectTransform != null)
            //{
            //    GUI.DrawString(spriteBatch, new Vector2(800, 40), $"Frame location: {Frame.Rect.Location}, Size: {Frame.Rect.Size}, Offset: {Frame.RectTransform.AbsoluteOffset}", Color.White, Color.Black * 0.5f);
            //    GUI.DrawString(spriteBatch, new Vector2(800, 80), "child count: " + Frame.Children.Count().ToString(), Color.White, Color.Black * 0.5f);

            //    int y = 80;
            //    foreach (var child in Frame.Children)
            //    {
            //        if (child.RectTransform == null) { continue; }
            //        y += 30;
            //        GUI.DrawString(spriteBatch, new Vector2(800, y), $"Child location: {child.Rect.Location}, Size: {child.Rect.Size}, Offset: {child.RectTransform.AbsoluteOffset}", Color.White, Color.Black * 0.5f);
            //    }
            //}
        }

        private bool IsChildInsideFrame(GUIComponent child)
        {
            if (child == null) return false;

            if (ScrollBar.IsHorizontal)
            {
                if (child.Rect.Right < Content.Rect.X) return false;
                if (child.Rect.X > Content.Rect.Right) return false;
            }
            else
            {
                if (child.Rect.Bottom < Content.Rect.Y) return false;
                if (child.Rect.Y > Content.Rect.Bottom) return false;
            }

            return true;
        }
    }
}
