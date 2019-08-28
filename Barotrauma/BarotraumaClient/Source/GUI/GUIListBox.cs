using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    public class GUIListBox : GUIComponent, IKeyboardSubscriber
    {
        protected List<GUIComponent> selected;

        public delegate bool OnSelectedHandler(GUIComponent component, object obj);
        public OnSelectedHandler OnSelected;

        public delegate object CheckSelectedHandler();
        public CheckSelectedHandler CheckSelected;

        public GUIScrollBar ScrollBar { get; private set; }
        public GUIFrame Content { get; private set; }

        private Dictionary<GUIComponent, bool> childVisible = new Dictionary<GUIComponent, bool>();

        private int totalSize;
        private bool childrenNeedsRecalculation;
        private bool scrollBarNeedsRecalculation;

        public bool SelectMultiple;

        public bool HideChildrenOutsideFrame = true;

        private bool useGridLayout;

        public bool UseGridLayout
        {
            get { return useGridLayout; }
            set
            {
                if (useGridLayout == value) return;
                useGridLayout = value;
                childrenNeedsRecalculation = true;
                scrollBarNeedsRecalculation = true;
            }
        }

        public GUIComponent SelectedComponent
        {
            get
            {
                return selected.FirstOrDefault();
            }
        }

        public bool Selected { get; set; }

        public List<GUIComponent> AllSelected
        {
            get { return selected; }
        }

        public object SelectedData
        {
            get
            {
                return SelectedComponent?.UserData;
            }
        }

        public int SelectedIndex
        {
            get
            {
                if (SelectedComponent == null) return -1;
                return Content.RectTransform.GetChildIndex(SelectedComponent.RectTransform);
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

        public float TotalSize
        {
            get { return totalSize; }
        }

        public int Spacing { get; set; }

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
        
        /// <summary>
        /// Disables the scroll bar without hiding it.
        /// </summary>
        public bool ScrollBarEnabled { get; set; } = true;

        public bool ScrollBarVisible
        {
            get
            {
                return ScrollBar.Visible;
            }
            set
            {
                ScrollBar.Visible = value;
                AutoHideScrollBar = false;
            }
        }

        /// <summary>
        /// Automatically hides the scroll bar when the content fits in.
        /// </summary>
        public bool AutoHideScrollBar { get; set; } = true;

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
            Content.RectTransform.ChildrenChanged += (_) => 
            {
                scrollBarNeedsRecalculation = true;
                childrenNeedsRecalculation = true;
            };

            if (style != null) GUI.Style.Apply(Content, "", this);

            if (color.HasValue)
            {
                this.color = color.Value;
            }

            if (isHorizontal)
            {
                ScrollBar = new GUIScrollBar(new RectTransform(new Point(Rect.Width, 20), rectT, Anchor.BottomLeft, Pivot.TopLeft) { AbsoluteOffset = new Point(0, 20) }, isHorizontal: isHorizontal);
            }
            else
            {
                ScrollBar = new GUIScrollBar(new RectTransform(new Point(20, Rect.Height), rectT, Anchor.TopRight, Pivot.TopLeft) { AbsoluteOffset = new Point(20, 0) }, isHorizontal: isHorizontal);
            }
            UpdateScrollBarSize();
            Enabled = true;
            ScrollBar.BarScroll = 0.0f;
            
            RectTransform.ScaleChanged += UpdateDimensions;
            RectTransform.SizeChanged += UpdateDimensions;
        }

        private void UpdateDimensions()
        {
            if (!ScrollBarEnabled)
            {
                Content.RectTransform.NonScaledSize = Rect.Size;
            }
            else
            {
                Point frameSize = ScrollBar.IsHorizontal ?
                    new Point(Rect.Width, Rect.Height - 20) :
                    new Point(Rect.Width - 20, Rect.Height);
                Content.RectTransform.NonScaledSize = frameSize;
            }
            ScrollBar.RectTransform.NonScaledSize = ScrollBar.IsHorizontal ? new Point(Rect.Width, 20) : new Point(20, Rect.Height);
        }
        
        public void Select(object userData, bool force = false, bool autoScroll = true)
        {
            var children = Content.Children;

            int i = 0;
            foreach (GUIComponent child in children)
            {
                if ((child.UserData != null && child.UserData.Equals(userData)) ||
                    (child.UserData == null && userData == null))
                {
                    Select(i, force, autoScroll);
                    if (!SelectMultiple) return;
                }
                i++;
            }
        }

        private void RepositionChildren()
        {
            var children = Content.Children;
            int x = 0, y = 0;
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
                    if (child.RectTransform.AbsoluteOffset.X != x || child.RectTransform.AbsoluteOffset.Y != y)
                    {
                        child.RectTransform.AbsoluteOffset = new Point(x, y);
                    }
                }

                if (useGridLayout)
                {
                    if (ScrollBar.IsHorizontal)
                    {
                        if (y + child.Rect.Height + Spacing > Content.Rect.Height)
                        {
                            y = 0;
                            x += child.Rect.Width + Spacing;
                            if (child.RectTransform.AbsoluteOffset.X != x || child.RectTransform.AbsoluteOffset.Y != y)
                            {
                                child.RectTransform.AbsoluteOffset = new Point(x, y);
                            }
                            y += child.Rect.Height + Spacing;
                        }
                        else
                        {
                            y += child.Rect.Height + Spacing;
                        }

                    }
                    else
                    {
                        if (x + child.Rect.Width + Spacing > Content.Rect.Width)
                        {
                            x = 0;
                            y += child.Rect.Height + Spacing;
                            if (child.RectTransform.AbsoluteOffset.X != x || child.RectTransform.AbsoluteOffset.Y != y)
                            {
                                child.RectTransform.AbsoluteOffset = new Point(x, y);
                            }
                            x += child.Rect.Width + Spacing;
                        }
                        else
                        {
                            x += child.Rect.Width + Spacing;
                        }
                    }
                }
                else
                {
                    if (ScrollBar.IsHorizontal)
                    {
                        x += child.Rect.Width + Spacing;
                    }
                    else
                    {
                        y += child.Rect.Height + Spacing;
                    }
                }
            }
        }
        
        private void UpdateChildrenRect()
        {
            for (int i = 0; i < Content.CountChildren; i++)
            {
                var child = Content.RectTransform.GetChild(i)?.GUIComponent;
                if (child == null) continue;

                // selecting
                if (Enabled && child.CanBeFocused && (GUI.IsMouseOn(child)) && child.Rect.Contains(PlayerInput.MousePosition))
                {
                    child.State = ComponentState.Hover;
                    if (PlayerInput.LeftButtonClicked())
                    {
                        Select(i, autoScroll: false);
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
            
            foreach (GUIComponent child in Content.Children)
            {
                if (!childVisible.ContainsKey(child)) { childVisible[child] = child.Visible; }
                if (childVisible[child] != child.Visible)
                {
                    childVisible[child] = child.Visible;
                    childrenNeedsRecalculation = true;
                    scrollBarNeedsRecalculation = true;
                    break;
                }
            }            

            if (childrenNeedsRecalculation)
            {
                RecalculateChildren();
                childVisible.Clear();
            }

            UpdateOrder = order;
            GUI.AddToUpdateList(this);
            
            if (ignoreChildren)
            {
                OnAddedToGUIUpdateList?.Invoke(this);
                return;
            }
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
            if (ScrollBar.Enabled)
            {
                ScrollBar.AddToGUIUpdateList(false, order);
            }
            OnAddedToGUIUpdateList?.Invoke(this);
        }

        public void RecalculateChildren()
        {
            foreach (GUIComponent child in Content.Children)
            {
                ClampChildMouseRects(child);
            }
            RepositionChildren();
            childrenNeedsRecalculation = false;
        }

        private void ClampChildMouseRects(GUIComponent child)
        {
            child.ClampMouseRectToParent = true;

            //no need to go through grandchildren if the child is a GUIListBox, it handles this by itself
            if (child is GUIListBox) return;

            foreach (GUIComponent grandChild in child.Children)
            {
                ClampChildMouseRects(grandChild);
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            UpdateChildrenRect();
            RepositionChildren();

            if (scrollBarNeedsRecalculation)
            {
                UpdateScrollBarSize();
                scrollBarNeedsRecalculation = false;
            }

            ScrollBar.Enabled = ScrollBarEnabled && ScrollBar.BarSize < 1.0f;
            if (AutoHideScrollBar)
            {
                ScrollBar.Visible = ScrollBar.BarSize < 1.0f;
            }

            if ((GUI.IsMouseOn(this) || GUI.IsMouseOn(ScrollBar)) && PlayerInput.ScrollWheelSpeed != 0)
            {
                ScrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * BarSize;
            }
        }

        public void SelectNext(bool force = false, bool autoScroll = true)
        {
            Select(Math.Min(Content.CountChildren - 1, SelectedIndex + 1), force, autoScroll);
        }

        public void SelectPrevious(bool force = false, bool autoScroll = true)
        {
            Select(Math.Max(0, SelectedIndex - 1), force, autoScroll);
        }

        public void Select(int childIndex, bool force = false, bool autoScroll = true)
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

            // Ensure that the selected element is visible. This may not be the case, if the selection is run from code. (e.g. if we have two list boxes that are synced)
            // TODO: This method only works when moving one item up/down (e.g. when using the up and down arrows)
            if (autoScroll)
            {
                if (ScrollBar.IsHorizontal)
                {
                    if (child.Rect.X < MouseRect.X)
                    {
                        //child outside the left edge of the frame -> move left
                        ScrollBar.BarScroll -= (float)(MouseRect.X - child.Rect.X) / (totalSize - Content.Rect.Width);
                    }
                    else if (child.Rect.Right > MouseRect.Right)
                    {
                        //child outside the right edge of the frame -> move right
                        ScrollBar.BarScroll += (float)(child.Rect.Right - MouseRect.Right) / (totalSize - Content.Rect.Width);
                    }
                }
                else
                {
                    if (child.Rect.Y < MouseRect.Y)
                    {
                        //child above the top of the frame -> move up
                        ScrollBar.BarScroll -= (float)(MouseRect.Y - child.Rect.Y) / (totalSize - Content.Rect.Height);
                    }
                    else if (child.Rect.Bottom > MouseRect.Bottom)
                    {
                        //child below the bottom of the frame -> move down
                        ScrollBar.BarScroll += (float)(child.Rect.Bottom - MouseRect.Bottom) / (totalSize - Content.Rect.Height);
                    }
                }
            }

            // If one of the children is the subscriber, we don't want to register, because it will unregister the child.
            if (RectTransform.GetAllChildren().None(rt => rt.GUIComponent == GUI.KeyboardDispatcher.Subscriber))
            {
                Selected = true;
                GUI.KeyboardDispatcher.Subscriber = this;
            }
        }

        public void Deselect()
        {
            Selected = false;
            if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                GUI.KeyboardDispatcher.Subscriber = null;
            }
            selected.Clear();
        }

        public void UpdateScrollBarSize()
        {
            if (Content == null) return;

            totalSize = 0;
            var children = Content.Children.Where(c => c.Visible);
            if (useGridLayout)
            {
                int pos = 0;
                foreach (GUIComponent child in children)
                {
                    if (ScrollBar.IsHorizontal)
                    {
                        if (pos + child.Rect.Height + Spacing > Content.Rect.Height)
                        {
                            pos = 0;
                            totalSize += child.Rect.Width + Spacing;
                        }
                        pos += child.Rect.Height + Spacing;

                        if (child == children.Last())
                        {
                            totalSize += child.Rect.Width + Spacing;
                        }
                    }
                    else
                    {
                        if (pos + child.Rect.Width + Spacing > Content.Rect.Width)
                        {
                            pos = 0;
                            totalSize += child.Rect.Height + Spacing;
                        }
                        pos += child.Rect.Width + Spacing;

                        if (child == children.Last())
                        {
                            totalSize += child.Rect.Height + Spacing;
                        }
                    }
                }
            }
            else
            {
                foreach (GUIComponent child in children)
                {
                    totalSize += (ScrollBar.IsHorizontal) ? child.Rect.Width : child.Rect.Height;
                }
                totalSize += Content.CountChildren * Spacing;
            }


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

        public override void DrawChildren(SpriteBatch spriteBatch, bool recursive)
        {
            //do nothing (the children have to be drawn in the Draw method after the ScissorRectangle has been set)
            return;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Content.DrawManually(spriteBatch, alsoChildren: false);
            
            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            RasterizerState prevRasterizerState = spriteBatch.GraphicsDevice.RasterizerState;
            if (HideChildrenOutsideFrame)
            {                    
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, Content.Rect);
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
            }

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

            if (HideChildrenOutsideFrame)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: prevRasterizerState);
            }

            if (ScrollBar.Visible) ScrollBar.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
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

        public void ReceiveTextInput(char inputChar)
        {
            GUI.KeyboardDispatcher.Subscriber = null;
        }
        public void ReceiveTextInput(string text) { }
        public void ReceiveCommandInput(char command) { }

        public void ReceiveSpecialInput(Keys key)
        {
            switch (key)
            {
                case Keys.Down:
                    SelectNext();
                    break;
                case Keys.Up:
                    SelectPrevious();
                    break;
                case Keys.Enter:
                case Keys.Space:
                case Keys.Escape:
                    GUI.KeyboardDispatcher.Subscriber = null;
                    break;
            }
        }
    }
}
