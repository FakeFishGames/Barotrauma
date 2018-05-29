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
        
        private bool needsRecalculation;

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
                return Content.RectTransform.GetChildIndex(Selected.RectTransform);
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
                
        /*[Obsolete("Use RectTransform instead of Rect")]
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
        }*/

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

        public GUIListBox(RectTransform rectT, bool isHorizontal = false, Color? color = null, string style = "") : base(style, rectT)
        {
            selected = new List<GUIComponent>();

            Point frameSize = isHorizontal ? 
                new Point(rectT.NonScaledSize.X, rectT.NonScaledSize.Y - 20) :
                new Point(rectT.NonScaledSize.X - 20, rectT.NonScaledSize.Y);

            Content = new GUIFrame(new RectTransform(frameSize, rectT), style)
            {
                CanBeFocused = false                
            };
            Content.RectTransform.ChildrenChanged += (_) => { needsRecalculation = true; };

            if (style != null) GUI.Style.Apply(Content, "", this);

            if (color.HasValue)
            {
                this.color = color.Value;
            }

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
            Enabled = true;
            scrollBarEnabled = true;
            ScrollBar.BarScroll = 0.0f;

            RectTransform.ScaleChanged += UpdateDimensions;
            RectTransform.SizeChanged += UpdateDimensions;
        }

        private void UpdateDimensions()
        {
            Point frameSize = ScrollBar.IsHorizontal ?
                new Point(Rect.Width, Rect.Height - 20) :
                new Point(Rect.Width - 20, Rect.Height);

            Content.RectTransform.NonScaledSize = frameSize;
            ScrollBar.RectTransform.NonScaledSize = ScrollBar.IsHorizontal ? new Point(Rect.Width, 20) : new Point(20, Rect.Height);
        }
        
        public void Select(object userData, bool force = false)
        {
            var children = Content.Children;

            int i = 0;
            foreach (GUIComponent child in children)
            {
                if ((child.UserData != null && child.UserData.Equals(userData)) ||
                    (child.UserData == null && userData == null))
                {
                    Select(i, force);
                    if (!SelectMultiple) return;
                }
                i++;
            }
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
            if (ScrollBar.BarSize < 1.0f)
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

            for (int i = 0; i < Content.CountChildren; i++)
            {
                GUIComponent child = Content.GetChild(i);
                if (!child.Visible) { continue; }
                if (RectTransform != null)
                {
                    child.RectTransform.AbsoluteOffset = new Point(x, y);
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
                if (Enabled && child.CanBeFocused && (GUI.IsMouseOn(child)) && child.Rect.Contains(PlayerInput.MousePosition))
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
            int lastVisible = 0;
            for (int i = 0; i < Content.CountChildren; i++)
            {
                var child = Content.GetChild(i);
                if (!child.Visible) continue;
                if (!IsChildInsideFrame(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }
                lastVisible = i;
                child.AddToGUIUpdateList(false, order);
            }
            if (scrollBarEnabled)
            {
                ScrollBar.AddToGUIUpdateList(false, order);
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            UpdateChildrenRect();

            if (needsRecalculation)
            {
                UpdateScrollBarSize();
                needsRecalculation = false;
            }

            ScrollBar.Enabled = scrollBarEnabled && ScrollBar.BarSize < 1.0f;

            if ((GUI.IsMouseOn(this) || IsParentOf(GUI.MouseOn) || GUI.IsMouseOn(ScrollBar)) && PlayerInput.ScrollWheelSpeed != 0)
            {
                ScrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * BarSize;
            }
        }

        public void Select(int childIndex, bool force = false)
        {
            if (childIndex >= Content.CountChildren || childIndex < 0) return;

            GUIComponent child = Content.GetChild(childIndex);

            bool wasSelected = true;
            if (OnSelected != null) wasSelected = force || OnSelected(child, child.UserData);
            
            if (!wasSelected) return;

            if (SelectMultiple)
            {
                if (selected.Contains(child))
                {
                    selected.Remove(child);
                }
                else
                {
                    selected.Add(child);
                }
            }
            else
            {
                selected.Clear();
                selected.Add(child);
            }
        }

        public void Deselect()
        {
            selected.Clear();
        }

        public void UpdateScrollBarSize()
        {
            if (Content == null) return;
            
            totalSize = 0;
            var children = Content.Children;
            foreach (GUIComponent child in children)
            {
                if (!child.Visible) { continue; }
                totalSize += (ScrollBar.IsHorizontal) ? child.Rect.Width : child.Rect.Height;
            }

            totalSize += Content.CountChildren * spacing;

            ScrollBar.BarSize = ScrollBar.IsHorizontal ?
                Math.Max(Math.Min(Content.Rect.Width / (float)totalSize, 1.0f), 5.0f / Content.Rect.Width) :
                Math.Max(Math.Min(Content.Rect.Height / (float)totalSize, 1.0f), 5.0f / Content.Rect.Height);
        }
        
        public override void ClearChildren()
        {
            Content.ClearChildren();
            selected.Clear();
        }

        public override void RemoveChild(GUIComponent child)
        {
            if (child == null) return;
            child.RectTransform.Parent = null;
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

            int i = 0;
            foreach (GUIComponent child in Content.Children)
            {
                if (!child.Visible) continue;
                if (!IsChildInsideFrame(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }
                lastVisible = i;
                child.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
                i++;
            }
            
            if (HideChildrenOutsideFrame) spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;

            if (ScrollBarEnabled) ScrollBar.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
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
