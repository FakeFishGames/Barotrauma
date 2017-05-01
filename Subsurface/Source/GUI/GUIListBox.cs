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

        public override Color Color
        {
            get
            {
                return base.Color;
            }
            set
            {
                base.Color = value;

                frame.Color = value;
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

        public GUIListBox(Rectangle rect, string style, GUIComponent parent = null)
            : this(rect, style, Alignment.TopLeft, parent)
        {
        }

        public GUIListBox(Rectangle rect, string style, Alignment alignment, GUIComponent parent = null)
            : this(rect, null, alignment, style, parent, false)
        {
        }

        public GUIListBox(Rectangle rect, Color? color, string style = null, GUIComponent parent = null)
            : this(rect, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

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
                scrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.X, this.rect.Bottom - 20, this.rect.Width, 20), null, 1.0f, "");
            }
            else
            {
                scrollBar = new GUIScrollBar(
                    new Rectangle(this.rect.Right - 20, this.rect.Y, 20, this.rect.Height), null, 1.0f, "");
            }

            scrollBar.IsHorizontal = isHorizontal;            

            frame = new GUIFrame(new Rectangle(0, 0, this.rect.Width, this.rect.Height), style, this);
            if (style != null) GUI.Style.Apply(frame, style, this);

            UpdateScrollBarSize();

            children.Clear();

            enabled = true;

            scrollBarEnabled = true;

            scrollBar.BarScroll = 0.0f;
        }
        
        public void Select(object userData, bool force = false)
        {
            for (int i = 0; i < children.Count; i++)
            {
                if (!children[i].UserData.Equals(userData)) continue;

                Select(i, force);

                //if (OnSelected != null) OnSelected(Selected, Selected.UserData);
                if (!SelectMultiple) return;
            }
        }

        private void UpdateChildrenRect(float deltaTime)
        {
            int x = rect.X, y = rect.Y;

            if (!scrollBarHidden)
            {
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
                if (child == frame || !child.Visible) continue;

                child.Rect = new Rectangle(x, y, child.Rect.Width, child.Rect.Height);
                if (scrollBar.IsHorizontal)
                {
                    x += child.Rect.Width + spacing;
                }
                else
                {
                    y += child.Rect.Height + spacing;
                }
                
                if (deltaTime>0.0f) child.Update(deltaTime);
                if (enabled && child.CanBeFocused &&
                    (MouseOn == this || (MouseOn != null && this.IsParentOf(MouseOn))) && child.Rect.Contains(PlayerInput.MousePosition))
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

        public override void AddToGUIUpdateList()
        {
            if (!Visible) return;
            if (ComponentsToUpdate.Contains(this)) return;
            ComponentsToUpdate.Add(this);

            try
            {
                List<GUIComponent> fixedChildren = new List<GUIComponent>(children);
                int lastVisible = 0;
                for (int i = 0; i < fixedChildren.Count; i++)
                {
                    if (fixedChildren[i] == frame) continue;

                    if (!IsChildVisible(fixedChildren[i]))
                    {
                        if (lastVisible > 0) break;
                        continue;
                    }

                    lastVisible = i;
                    fixedChildren[i].AddToGUIUpdateList();
                }
            }
            catch (Exception e)
            {
                DebugConsole.NewMessage("Error in AddToGUIUpdateList! GUIComponent runtime type: " + this.GetType().ToString() + "; children count: " + children.Count.ToString(), Color.Red);
                throw e;
            }

            if (scrollBarEnabled && !scrollBarHidden) scrollBar.AddToGUIUpdateList();
        }

        public override Rectangle MouseRect
        {
            get
            {
                return Rectangle.Empty;
            }
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            UpdateChildrenRect(deltaTime);
            
            //base.Update(deltaTime);

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

            scrollBarHidden = scrollBar.BarSize >= 1.0f;
        }

        public override void AddChild(GUIComponent child)
        {
            //temporarily reduce the size of the rect to prevent the child from expanding over the scrollbar
            if (scrollBar.IsHorizontal)            
                rect.Height -= scrollBar.Rect.Height;
            else
                rect.Width -= scrollBar.Rect.Width;

            base.AddChild(child);

            if (scrollBar.IsHorizontal)
                rect.Height += scrollBar.Rect.Height;
            else
                rect.Width += scrollBar.Rect.Width;
            
            UpdateScrollBarSize();
            UpdateChildrenRect(0.0f);
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
        
        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
            
            frame.Draw(spriteBatch);

            if (!scrollBarHidden) scrollBar.Draw(spriteBatch);

            GameMain.CurrGraphicsDevice.ScissorRectangle = frame.Rect;

            int lastVisible = 0;
            for (int i = 0; i < children.Count; i++)
            {
                GUIComponent child = children[i];
                if (child == frame) continue;

                if (!IsChildVisible(child))
                {
                    if (lastVisible > 0) break;
                    continue;
                }

                lastVisible = i;         
                child.Draw(spriteBatch);
            }
            
            GameMain.CurrGraphicsDevice.ScissorRectangle = new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private bool IsChildVisible(GUIComponent child)
        {
            if (child == null || !child.Visible) return false;

            if (scrollBar.IsHorizontal)
            {
                if (child.Rect.Right < rect.X) return false;
                if (child.Rect.X > rect.Right) return false;
            }
            else
            {
                if (child.Rect.Bottom < rect.Y) return false;
                if (child.Rect.Y > rect.Bottom) return false;
            }

            return true;
        }
    }
}
