using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
{
    public class GUIListBox : GUIComponent
    {
        protected GUIComponent selected;

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

        public GUIComponent Selected
        {
            get
            {
                return selected;
            }
        }
        
        public object SelectedData
        {
            get 
            {
                return (selected == null) ? null : selected.UserData; 
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (selected == null) return -1;
                return children.FindIndex(x => x == selected);
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

        public GUIListBox(Rectangle rect, Color? color, Alignment alignment, GUIStyle style = null, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            this.alignment = alignment;

            if (color!=null) this.color = (Color)color;

            if (parent != null)
                parent.AddChild(this);

            scrollBarHidden = true;

            scrollBar = new GUIScrollBar(
                new Rectangle(this.rect.X + this.rect.Width-20, this.rect.Y, 20, this.rect.Height), color, 1.0f, style);

            frame = new GUIFrame(Rectangle.Empty, style, this);
            if (style != null) style.Apply(frame, this);

            UpdateScrollBarSize();

            children.Clear();

            enabled = true;

            scrollBarEnabled = true;

            scrollBar.BarScroll = 0.0f;
        }

        public void Select(object selection)
        {
            foreach (GUIComponent child in children)
            {
                if (child.UserData != selection) continue;
                
                selected = child;
                if (OnSelected != null) OnSelected(selected, selected.UserData);
                return;                
            }
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            base.Update(deltaTime);
            
            scrollBar.Update(deltaTime);
            
            if ((MouseOn==this || MouseOn==scrollBar || IsParentOf(MouseOn) )&& PlayerInput.ScrollWheelSpeed!=0)
            {
                scrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed/500.0f) * BarSize;
            }
        }

        public void Select(int childIndex)
        {
            //children[0] is the GUIFrame, ignore it
            childIndex += 1;

            if (childIndex >= children.Count || childIndex<0) return;

            selected = children[childIndex];
            if (OnSelected != null) OnSelected(selected, selected.UserData);
        }

        public void Deselect()
        {
            selected = null;
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
                Math.Min((float)rect.Width / (float)totalSize, 1.0f) : 
                Math.Min((float)rect.Height / (float)totalSize, 1.0f);

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
            selected = null;
        }

        public override void RemoveChild(GUIComponent child)
        {
            base.RemoveChild(child);

            if (selected == child) selected = null;

            UpdateScrollBarSize();            
        }

        private void ShowScrollBar()
        {
            if (scrollBarHidden) Rect = new Rectangle(rect.X, rect.Y, rect.Width - scrollBar.Rect.Width, rect.Height);
            scrollBarHidden = false;
            
        }

        private void HideScrollBar()
        {
            if (!scrollBarHidden) Rect = new Rectangle(rect.X, rect.Y, rect.Width + scrollBar.Rect.Width, rect.Height);
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
                    x -= (int)((totalSize - rect.Height) * scrollBar.BarScroll);
                }
                else
                {
                    y -= (int)((totalSize - rect.Height) * scrollBar.BarScroll);
                }
            }

            for (int i = 0; i < children.Count; i++ )
            {
                GUIComponent child = children[i];
                if (child == frame) continue;

                child.Rect = new Rectangle(child.Rect.X, y, child.Rect.Width, child.Rect.Height);
                y += child.Rect.Height + spacing;

                if (child.Rect.Y + child.Rect.Height < rect.Y) continue;
                if (child.Rect.Y + child.Rect.Height > rect.Y + rect.Height) break;

                if (child.Rect.Y < rect.Y && child.Rect.Y + child.Rect.Height >= rect.Y)
                {
                    y = rect.Y;
                    continue;
                }

                if (selected == child)
                {
                    child.State = ComponentState.Selected;

                    if (CheckSelected != null)
                    {
                        if (CheckSelected() != selected.UserData) selected = null;
                    }
                }
                else if (enabled && child.CanBeFocused && 
                    (MouseOn == this || (MouseOn != null && this.IsParentOf(MouseOn))) && child.Rect.Contains(PlayerInput.MousePosition))
                {
                    child.State = ComponentState.Hover;
                    if (PlayerInput.LeftButtonClicked())
                    {
                        Debug.WriteLine("clicked");
                        selected = child;
                        if (OnSelected != null)
                        {
                            if (!OnSelected(selected, child.UserData)) selected = null;
                        }

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
