using System;
using System.Linq;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma
{
    public class GUIListBox : GUIComponent
    {
        protected List<GUIComponent> selected;

        public delegate bool OnSelectedHandler(GUIComponent component, object obj);
        public OnSelectedHandler OnSelected;

        public delegate object CheckSelectedHandler();
        public CheckSelectedHandler CheckSelected;

        private GUIScrollBar scrollBar;
        private GUIFrame frame;

        private int totalSize;

        private int spacing;

        private bool scrollBarEnabled;
        private bool scrollBarHidden;

        private bool enabled;

        public bool SelectMultiple;

        public GUIComponent Selected
        {
            get
            {
                return selected.Any() ? selected[0] : null;
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
                return (Selected == null) ? null : Selected.UserData;
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (Selected == null) return -1;
                return children.FindIndex(x => x == Selected);
            }
        }

        public float BarScroll
        {
            get { return scrollBar.BarScroll; }
            set { scrollBar.BarScroll = value; }
        }

        public float BarSize
        {
            get { return scrollBar.BarSize; }
        }

        public int Spacing
        {
            get { return spacing; }
            set { spacing = value; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        public bool ScrollBarEnabled
        {
            get { return scrollBarEnabled; }
            set
            {
                if (value)
                {
                    if (!scrollBarEnabled && scrollBarHidden) ShowScrollBar();
                }
                else
                {
                    if (scrollBarEnabled && !scrollBarHidden) HideScrollBar();
                }

                scrollBarEnabled = value;
            }
        }

        public GUIListBox(Rectangle rect, GUIStyle style, GUIComponent parent = null)
            : this(rect, style, Alignment.TopLeft, parent)
        {
        }

        public GUIListBox(Rectangle rect, GUIStyle style, Alignment alignment, GUIComponent parent = null)
            : this(rect, null, style, parent)
        {
        }

        public GUIListBox(Rectangle rect, Color? color, GUIStyle style = null, GUIComponent parent = null)
            : this(rect, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

        public GUIListBox(Rectangle rect, Color? color, Alignment alignment, GUIStyle style = null, GUIComponent parent = null, bool isHorizontal = false)
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
                scrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.X, this.rect.Bottom - 20, this.rect.Width, 20), null, 1.0f, GUI.Style);
            }
            else
            {
                scrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.Right - 20, this.rect.Y, 20, this.rect.Height), null, 1.0f, GUI.Style);
            }
            

            frame = new GUIFrame(Rectangle.Empty, style, this);
            if (style != null) style.Apply(frame, this);

            UpdateScrollBarSize();

            children.Clear();

            enabled = true;

            scrollBarEnabled = true;

            scrollBar.BarScroll = 0.0f;
        }

        public void Select(object selection, bool force = false)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (!children[i].UserData.Equals(selection)) continue;

                Select(i, force);

                //if (OnSelected != null) OnSelected(Selected, Selected.UserData);
                if (!SelectMultiple) return;
            }
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            base.Update(deltaTime);

            if (scrollBarEnabled && !scrollBarHidden) scrollBar.Update(deltaTime);

            if ((MouseOn == this || MouseOn == scrollBar || IsParentOf(MouseOn)) && PlayerInput.ScrollWheelSpeed != 0)
            {
                scrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * BarSize;
            }
        }

        public void Select(int childIndex, bool force = false)
        {
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
            totalSize = 0;
            foreach (GUIComponent child in children)
            {
                if (child == frame) continue;
                totalSize += (scrollBar.IsHorizontal) ? child.Rect.Width : child.Rect.Height;
                totalSize += spacing;
            }

            scrollBar.BarSize = scrollBar.IsHorizontal ?
                Math.Max(Math.Min((float)rect.Width / (float)totalSize, 1.0f), 5.0f / rect.Width) :
                Math.Max(Math.Min((float)rect.Height / (float)totalSize, 1.0f), 5.0f / rect.Height);

            if (scrollBar.BarSize < 1.0f && scrollBarHidden) ShowScrollBar();
            if (scrollBar.BarSize >= 1.0f && !scrollBarHidden) HideScrollBar();
        }

        public override void AddChild(GUIComponent child)
        {
            base.AddChild(child);

            //float oldScroll = scrollBar.BarScroll;
            //float oldSize = scrollBar.BarSize;
            UpdateScrollBarSize();

            //if (oldSize == 1.0f && scrollBar.BarScroll == 0.0f) scrollBar.BarScroll = 1.0f;

            //if (scrollBar.BarSize < 1.0f && oldScroll == 1.0f)
            //{
            //    scrollBar.BarScroll = 1.0f;
            //}

        }

        public override void ClearChildren()
        {
            base.ClearChildren();
            selected.Clear();
        }

        public override void RemoveChild(GUIComponent child)
        {
            base.RemoveChild(child);

            if (selected.Contains(child)) selected.Remove(child);

            UpdateScrollBarSize();
        }

        private void ShowScrollBar()
        {
            if (scrollBarHidden && !scrollBar.IsHorizontal) Rect = new Rectangle(rect.X, rect.Y, rect.Width - scrollBar.Rect.Width, rect.Height);
            scrollBarHidden = false;

        }

        private void HideScrollBar()
        {
            if (!scrollBarHidden && !scrollBar.IsHorizontal) Rect = new Rectangle(rect.X, rect.Y, rect.Width + scrollBar.Rect.Width, rect.Height);
            scrollBarHidden = true;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            base.Draw(spriteBatch);

            frame.Draw(spriteBatch);
            //GUI.DrawRectangle(spriteBatch, rect, color*alpha, true);

            int x = rect.X, y = rect.Y;

            if (!scrollBarHidden)
            {
                scrollBar.Draw(spriteBatch);
                if (scrollBar.IsHorizontal)
                {
                    x -= (int)((totalSize - rect.Width) * scrollBar.BarScroll);
                }
                else
                {
                    y -= (int)((totalSize - rect.Height) * scrollBar.BarScroll);
                }
            }

            for (int i = 0; i < children.Count; i++)
            {
                GUIComponent child = children[i];
                if (child == frame) continue;

                child.Rect = new Rectangle(x, y, child.Rect.Width, child.Rect.Height);
                if (scrollBar.IsHorizontal)
                {
                    x += child.Rect.Width + spacing;
                }
                else
                {
                    y += child.Rect.Height + spacing;
                }

                child.Visible = false;

                if (scrollBar.IsHorizontal)
                {
                    if (child.Rect.Right < rect.X) continue;
                    if (child.Rect.Right > rect.Right) break;

                    if (child.Rect.X < rect.X && child.Rect.Right >= rect.X)
                    {
                        x = rect.X;
                        continue;
                    }
                }
                else
                {
                    if (child.Rect.Y + child.Rect.Height < rect.Y) continue;
                    if (child.Rect.Y + child.Rect.Height > rect.Y + rect.Height) break;

                    if (child.Rect.Y < rect.Y && child.Rect.Y + child.Rect.Height >= rect.Y)
                    {
                        y = rect.Y;
                        continue;
                    }
                }



                child.Visible = true;

                if (enabled && child.CanBeFocused &&
                    (MouseOn == this || (MouseOn != null && this.IsParentOf(MouseOn))) && child.Rect.Contains(PlayerInput.MousePosition))
                {
                    child.State = ComponentState.Hover;
                    if (PlayerInput.LeftButtonClicked())
                    {
                        Debug.WriteLine("clicked");
                        Select(i);
                        //selected = child;
                        //if (OnSelected != null)
                        //{
                        //    if (!OnSelected(selected, child.UserData)) selected = null;
                        //}

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

                child.Draw(spriteBatch);
            }

            //GUI.DrawRectangle(spriteBatch, rect, Color.Black, false);
        }
    }
}
