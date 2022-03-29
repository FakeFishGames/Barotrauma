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

        public delegate void OnRearrangedHandler(GUIListBox listBox, object obj);
        public OnRearrangedHandler OnRearranged;

        /// <summary>
        /// A frame drawn behind the content of the listbox
        /// </summary>
        public GUIFrame ContentBackground { get; private set; }

        /// <summary>
        /// A frame that contains the contents of the listbox. The frame itself is not rendered.
        /// </summary>
        public GUIFrame Content { get; private set; }
        public GUIScrollBar ScrollBar { get; private set; }

        private readonly Dictionary<GUIComponent, bool> childVisible = new Dictionary<GUIComponent, bool>();
          
        private int totalSize;
        private bool childrenNeedsRecalculation;
        private bool scrollBarNeedsRecalculation;
        private bool dimensionsNeedsRecalculation;

        // TODO: Define in styles?
        private int ScrollBarSize
        {
            get
            {
                //use the average of the "desired" size and the scaled size
                //scaling the bar linearly with the resolution tends to make them too large on large resolutions
                float desiredSize = 25.0f;
                float scaledSize = desiredSize * GUI.Scale;
                return (int)((desiredSize + scaledSize) / 2.0f);
            }
        }

        public enum SelectMode
        {
            SelectSingle,
            SelectMultiple,
            RequireShiftToSelectMultiple
        }
        
        public SelectMode CurrentSelectMode = SelectMode.SelectSingle;

        public bool SelectMultiple
        {
            get { return CurrentSelectMode != SelectMode.SelectSingle; }
            set
            {
                CurrentSelectMode = value ? SelectMode.SelectMultiple : SelectMode.SelectSingle;
            }
        }

        public bool HideChildrenOutsideFrame = true;

        private bool useGridLayout;

        private GUIComponent scrollToElement;

        public bool AllowMouseWheelScroll { get; set; } = true;

        public bool AllowArrowKeyScroll { get; set; } = true;

        /// <summary>
        /// Scrolls the list smoothly
        /// </summary>
        public bool SmoothScroll { get; set; }

        /// <summary>
        /// Whether to only allow scrolling from one element to the next when smooth scrolling is enabled
        /// </summary>
        public bool ClampScrollToElements { get; set; }

        /// <summary>
        /// When set to true elements at the bottom of the list are gradually faded
        /// </summary>
        public bool FadeElements { get; set; }
        
        /// <summary>
        /// Adds enough extra padding to the bottom so the end of the scroll will only contain the last element
        /// </summary>
        public bool PadBottom { get; set; }

        /// <summary>
        /// When set to true always selects the topmost item on the list
        /// </summary>
        public bool SelectTop { get; set; }

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
        
        /// <summary>
        /// true if mouse down should select elements instead of mouse up
        /// </summary>
        private readonly bool useMouseDownToSelect = false;

        private Vector4? overridePadding;
        public Vector4 Padding
        {
            get
            {
                if (overridePadding.HasValue) { return overridePadding.Value; }
                if (Style == null) { return Vector4.Zero; }
                return Style.Padding;
            }
            set 
            {
                dimensionsNeedsRecalculation = true;
                overridePadding = value; 
            }
        }

        public GUIComponent SelectedComponent
        {
            get
            {
                return selected.FirstOrDefault();
            }
        }

        // TODO: fix implicit hiding
        public override bool Selected
        {
            get { return isSelected; }
            set { isSelected = value; }
        }

        public IReadOnlyList<GUIComponent> AllSelected => selected;

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
        public bool KeepSpaceForScrollBar { get; set; }

        public bool CanTakeKeyBoardFocus { get; set; } = true;

        public bool ScrollBarVisible
        {
            get
            {
                return ScrollBar.Visible;
            }
            set
            {
                if (ScrollBar.Visible == value) { return; }
                ScrollBar.Visible = value;
                dimensionsNeedsRecalculation = true;
            }
        }

        /// <summary>
        /// Automatically hides the scroll bar when the content fits in.
        /// </summary>
        public bool AutoHideScrollBar { get; set; } = true;
        private bool IsScrollBarOnDefaultSide { get; set; }

        public enum DragMode
        {
            NoDragging,
            DragWithinBox,
            DragOutsideBox
        }
        
        private DragMode currentDragMode = DragMode.NoDragging;
        public DragMode CurrentDragMode
        {
            get
            {
                return currentDragMode;
            }
            set
            {
                if (value == DragMode.NoDragging && currentDragMode != DragMode.NoDragging && isDraggingElement)
                {
                    DraggedElement = null;
                }
                currentDragMode = value;
            }
        }

        private GUIComponent draggedElement;
        private Point dragMousePosRelativeToTopLeftCorner;
        private bool isDraggingElement => draggedElement != null;
        
        public bool HasDraggedElementIndexChanged { get; private set; }

        public GUIComponent DraggedElement
        {
            get
            {
                return draggedElement;
            }
            set
            {
                if (value == draggedElement) { return; }
                draggedElement = value;
                HasDraggedElementIndexChanged = false;

                if (value == null) { return; }

                dragMousePosRelativeToTopLeftCorner = PlayerInput.MousePosition.ToPoint() - value.Rect.Location;

                if (SelectMultiple)
                {
                    if (!AllSelected.Contains(DraggedElement))
                    {
                        Select(DraggedElement.ToEnumerable());
                    }
                }
            }
        }

        //This exists to work around the fact that rendering child
        //elements on top of the listbox's siblings is a clusterfuck.
        public bool HideDraggedElement = false;
        
        private readonly bool isHorizontal;

        /// <summary>
        /// Setting this to true and CanBeFocused to false allows the list background to be unfocusable while the elements can still be interacted with.
        /// </summary>
        public bool CanInteractWhenUnfocusable { get; set; } = false;

        public override Rectangle MouseRect
        {
            get
            {
                if (!CanBeFocused && !CanInteractWhenUnfocusable) { return Rectangle.Empty; }
                return ClampMouseRectToParent ? ClampRect(Rect) : Rect;
            }
        }

        /// <param name="isScrollBarOnDefaultSide">For horizontal listbox, default side is on the bottom. For vertical, it's on the right.</param>
        public GUIListBox(RectTransform rectT, bool isHorizontal = false, Color? color = null, string style = "", bool isScrollBarOnDefaultSide = true, bool useMouseDownToSelect = false) : base(style, rectT)
        {
            this.isHorizontal = isHorizontal;
            HoverCursor = CursorState.Hand;
            CanBeFocused = true;
            selected = new List<GUIComponent>();
            this.useMouseDownToSelect = useMouseDownToSelect;
            ContentBackground = new GUIFrame(new RectTransform(Vector2.One, rectT), style)
            {
                CanBeFocused = false                
            };
            Content = new GUIFrame(new RectTransform(Vector2.One, ContentBackground.RectTransform), style: null)
            {
                CanBeFocused = false
            };
            Content.RectTransform.ChildrenChanged += (_) => 
            {
                scrollBarNeedsRecalculation = true;
                childrenNeedsRecalculation = true;
            };
            if (style != null)
            {
                GUIStyle.Apply(ContentBackground, "", this);
            }
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            IsScrollBarOnDefaultSide = isScrollBarOnDefaultSide;
            Point size;
            Anchor anchor;
            if (isHorizontal)
            {
                size = new Point((int)(Rect.Width - Padding.X - Padding.Z), (int)(ScrollBarSize * GUI.Scale));
                anchor = isScrollBarOnDefaultSide ? Anchor.BottomCenter : Anchor.TopCenter;
            }
            else
            {
                // TODO: Should this be multiplied by the GUI.Scale as well?
                size = new Point(ScrollBarSize, (int)(Rect.Height - Padding.Y - Padding.W));
                anchor = isScrollBarOnDefaultSide ? Anchor.CenterRight : Anchor.CenterLeft;
            }
            ScrollBar = new GUIScrollBar(
                new RectTransform(size, rectT, anchor)
                {
                    AbsoluteOffset = isHorizontal ?
                        new Point(0, IsScrollBarOnDefaultSide ? (int)Padding.W : (int)Padding.Y) :
                        new Point(IsScrollBarOnDefaultSide ? (int)Padding.Z : (int)Padding.X, 0)
                },
                isHorizontal: isHorizontal);
            UpdateScrollBarSize();
            Enabled = true;
            ScrollBar.BarScroll = 0.0f;
            RectTransform.ScaleChanged += () => dimensionsNeedsRecalculation = true;
            RectTransform.SizeChanged += () => dimensionsNeedsRecalculation = true;
            UpdateDimensions();

            rectT.ChildrenChanged += CheckForChildren;
        }

        private void CheckForChildren(RectTransform rectT)
        {
            if (rectT == ScrollBar.RectTransform || rectT == Content.RectTransform || rectT == ContentBackground.RectTransform) { return; }
            throw new InvalidOperationException($"Children were added to {nameof(GUIListBox)}, Add them to {nameof(GUIListBox)}.{nameof(Content)} instead.");
        }

        public void UpdateDimensions()
        {
            dimensionsNeedsRecalculation = false;
            ContentBackground.RectTransform.Resize(Rect.Size);
            bool reduceScrollbarSize = KeepSpaceForScrollBar ? ScrollBarEnabled : ScrollBarVisible;
            Point contentSize = reduceScrollbarSize ? CalculateFrameSize(ScrollBar.IsHorizontal, ScrollBarSize) : Rect.Size;
            Content.RectTransform.Resize(new Point((int)(contentSize.X - Padding.X - Padding.Z), (int)(contentSize.Y - Padding.Y - Padding.W)));
            if (!IsScrollBarOnDefaultSide) { Content.RectTransform.SetPosition(Anchor.BottomRight); }
            Content.RectTransform.AbsoluteOffset = new Point(
                IsScrollBarOnDefaultSide ? (int)Padding.X : (int)Padding.Z,
                IsScrollBarOnDefaultSide ? (int)Padding.Y : (int)Padding.W);
            ScrollBar.RectTransform.Resize(ScrollBar.IsHorizontal ?
                new Point((int)(Rect.Width - Padding.X - Padding.Z), ScrollBarSize) :
                new Point(ScrollBarSize, (int)(Rect.Height - Padding.Y - Padding.W)));
            ScrollBar.RectTransform.AbsoluteOffset = ScrollBar.IsHorizontal ? 
                new Point(0, (int)Padding.W) : 
                new Point((int)Padding.Z, 0);
            UpdateScrollBarSize();
        }

        public void Select(object userData, bool force = false, bool autoScroll = true)
        {
            var children = Content.Children;
            int i = 0;
            foreach (GUIComponent child in children)
            {
                if (Equals(child.UserData, userData))
                {
                    Select(i, force, autoScroll);
                    if (!SelectMultiple) { return; }
                }
                i++;
            }
        }

        private Point CalculateFrameSize(bool isHorizontal, int scrollBarSize)
            => isHorizontal ? new Point(Rect.Width, Rect.Height - scrollBarSize) : new Point(Rect.Width - scrollBarSize, Rect.Height);

        public Vector2 CalculateTopOffset()
        {
            int x = 0;
            int y = 0;
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

            return new Vector2(x, y);
        }

        private void CalculateChildrenOffsets(Action<int, Point> callback)
        {
            Vector2 topOffset = CalculateTopOffset();
            int x = (int)topOffset.X;
            int y = (int)topOffset.Y;

            for (int i = 0; i < Content.CountChildren; i++)
            {
                GUIComponent child = Content.GetChild(i);
                if (child == null || !child.Visible) { continue; }
                if (RectTransform != null)
                {
                    callback(i, new Point(x, y));
                }

                if (useGridLayout)
                {
                    void advanceGridLayout(
                        ref int primaryCoord,
                        ref int secondaryCoord,
                        int primaryChildDimension,
                        int secondaryChildDimension,
                        int primaryParentDimension)
                    {
                        if (primaryCoord + primaryChildDimension + Spacing > primaryParentDimension)
                        {
                            primaryCoord = 0;
                            secondaryCoord += secondaryChildDimension + Spacing;
                            callback(i, new Point(x, y));
                        }
                        primaryCoord += primaryChildDimension + Spacing;
                    }
                    
                    if (ScrollBar.IsHorizontal)
                    {
                        advanceGridLayout(
                            primaryCoord: ref y,
                            secondaryCoord: ref x,
                            primaryChildDimension: child.Rect.Height,
                            secondaryChildDimension: child.Rect.Width,
                            primaryParentDimension: Content.Rect.Height);
                    }
                    else
                    {
                        advanceGridLayout(
                            primaryCoord: ref x,
                            secondaryCoord: ref y,
                            primaryChildDimension: child.Rect.Width,
                            secondaryChildDimension: child.Rect.Height,
                            primaryParentDimension: Content.Rect.Width);
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
        
        private void RepositionChildren()
        {
            CalculateChildrenOffsets((index, offset) =>
            {
                var child = Content.GetChild(index);
                if (child != draggedElement && child.RectTransform.AbsoluteOffset != offset)
                {
                    child.RectTransform.AbsoluteOffset = offset;
                }
            });
        }

        /// <summary>
        /// Scrolls the list to the specific element, currently only works when smooth scrolling and PadBottom are enabled.
        /// </summary>
        /// <param name="component"></param>
        public void ScrollToElement(GUIComponent component)
        {
            SoundPlayer.PlayUISound(GUISoundType.Click);
            List<GUIComponent> children = Content.Children.ToList();
            int index = children.IndexOf(component);
            if (index < 0) { return; }

            if (!Content.Children.Contains(component) || !component.Visible)
            {
                scrollToElement = null;
            }
            else
            {
                scrollToElement = component;
            }
        }

        public void ScrollToEnd(float duration)
        {
            CoroutineManager.StartCoroutine(ScrollCoroutine());

            IEnumerable<CoroutineStatus> ScrollCoroutine()
            {
                if (BarSize >= 1.0f)
                {
                    yield return CoroutineStatus.Success;
                }
                float t = 0.0f;
                float startScroll = BarScroll * BarSize;
                float distanceToTravel = ScrollBar.MaxValue - startScroll;
                float progress = startScroll;
                float speed = distanceToTravel / duration;

                while (t < duration && !MathUtils.NearlyEqual(ScrollBar.MaxValue, progress))
                {
                    t += CoroutineManager.DeltaTime;
                    progress += speed * CoroutineManager.DeltaTime;
                    BarScroll = progress;
                    yield return CoroutineStatus.Running;
                }

                yield return CoroutineStatus.Success;
            }
        }

        private void StartDraggingElement(GUIComponent child)
        {
            DraggedElement = child;
        }

        private bool UpdateDragging()
        {
            if (CurrentDragMode == DragMode.NoDragging || !isDraggingElement) { return false; }
            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                var draggedElem = draggedElement;
                OnRearranged?.Invoke(this, draggedElem.UserData);
                DraggedElement = null;
                RepositionChildren();
                if (AllSelected.Contains(draggedElem)) { return true; }
            }
            else
            {
                Vector2 topOffset = CalculateTopOffset();
                var mousePos = PlayerInput.MousePosition.ToPoint();
                draggedElement.RectTransform.AbsoluteOffset = mousePos - Content.Rect.Location - dragMousePosRelativeToTopLeftCorner;
                if (CurrentDragMode != DragMode.DragOutsideBox)
                {
                    var offset = draggedElement.RectTransform.AbsoluteOffset;
                    draggedElement.RectTransform.AbsoluteOffset =
                        isHorizontal ? new Point(offset.X, 0) : new Point(0, offset.Y);
                }

                int index = Content.RectTransform.GetChildIndex(draggedElement.RectTransform);
                int newIndex = index;

                Point draggedOffsetWhenReleased = Point.Zero;
                CalculateChildrenOffsets((i, offset) =>
                {
                    if (index != i) { return; }
                    draggedOffsetWhenReleased = offset;
                });
                Rectangle draggedRectWhenReleased = new Rectangle(Content.Rect.Location + draggedOffsetWhenReleased, draggedElement.Rect.Size);

                void shiftIndices(
                    float mousePos,
                    ref int draggedRectWhenReleasedLocation,
                    int draggedRectWhenReleasedSize)
                {
                    while (mousePos > (draggedRectWhenReleasedLocation  + draggedRectWhenReleasedSize) && newIndex < Content.CountChildren-1)
                    {
                        newIndex++;
                        draggedRectWhenReleasedLocation += draggedRectWhenReleasedSize;
                    }
                    while (mousePos < draggedRectWhenReleasedLocation && newIndex > 0)
                    {
                        newIndex--;
                        draggedRectWhenReleasedLocation -= draggedRectWhenReleasedSize;
                    }
                    
                    if (newIndex != index && AllSelected.Count > 1)
                    {
                        this.selected.Sort((a, b) => Content.GetChildIndex(a) - Content.GetChildIndex(b));
                        int draggedPos = AllSelected.IndexOf(draggedElement);
                        if (newIndex < draggedPos)
                        {
                            newIndex = draggedPos;
                        }
                        if (newIndex >= Content.CountChildren - (AllSelected.Count - draggedPos))
                        {
                            int max = Content.CountChildren - (AllSelected.Count - draggedPos);
                            newIndex = max;
                        }
                    }
                }
                
                if (isHorizontal)
                {
                    shiftIndices(
                        mousePos.X,
                        ref draggedRectWhenReleased.X,
                        draggedRectWhenReleased.Width);
                }
                else
                {
                    shiftIndices(
                        mousePos.Y,
                        ref draggedRectWhenReleased.Y,
                        draggedRectWhenReleased.Height);
                }

                if (newIndex != index)
                {
                    if (AllSelected.Count > 1)
                    {
                        this.selected.Sort((a, b) => Content.GetChildIndex(a) - Content.GetChildIndex(b));
                        int indexOfDraggedElem = AllSelected.IndexOf(draggedElement);
                        IEnumerable<GUIComponent> allSelected = AllSelected;
                        if (newIndex > index) { allSelected = allSelected.Reverse(); }
                        foreach (var elem in allSelected)
                        {
                            elem.RectTransform.RepositionChildInHierarchy(newIndex + AllSelected.IndexOf(elem) - indexOfDraggedElem);
                        }
                    }
                    else
                    {
                        draggedElement.RectTransform.RepositionChildInHierarchy(newIndex);
                    }
                    HasDraggedElementIndexChanged = true;
                }

                return true;
            }

            return false;
        }
        
        private void UpdateChildrenRect()
        {
            if (UpdateDragging()) { return; }

            if (SelectTop)
            {
                foreach (GUIComponent child in Content.Children)
                {
                    child.CanBeFocused = !selected.Contains(child);
                    if (!child.CanBeFocused)
                    {
                        child.State = ComponentState.None;
                    }
                }
            }
            
            if (SelectTop && Content.Children.Any() && scrollToElement == null)
            {
                GUIComponent component = Content.Children.FirstOrDefault(c => (c.Rect.Y - Content.Rect.Y) / (float)c.Rect.Height > -0.1f);

                if (component != null && !selected.Contains(component))
                {
                    int index = Content.Children.ToList().IndexOf(component);
                    if (index >= 0)
                    {
                        Select(index, false, false, takeKeyBoardFocus: true);
                    }
                }
            }

            for (int i = 0; i < Content.CountChildren; i++)
            {
                var child = Content.RectTransform.GetChild(i)?.GUIComponent;
                if (!(child is { Visible: true })) { continue; }

                // selecting
                if (Enabled && (CanBeFocused || CanInteractWhenUnfocusable) && child.CanBeFocused && child.Rect.Contains(PlayerInput.MousePosition) && GUI.IsMouseOn(child))
                {
                    child.State = ComponentState.Hover;

                    var mouseDown = useMouseDownToSelect ? PlayerInput.PrimaryMouseButtonDown() : PlayerInput.PrimaryMouseButtonClicked();
                    
                    if (mouseDown)
                    {
                        if (SelectTop)
                        {
                            ScrollToElement(child);
                        }
                        Select(i, autoScroll: false, takeKeyBoardFocus: true);
                    }

                    if (CurrentDragMode != DragMode.NoDragging
                        && (CurrentSelectMode != SelectMode.RequireShiftToSelectMultiple || (!PlayerInput.IsShiftDown() && !PlayerInput.IsCtrlDown()))
                        && PlayerInput.PrimaryMouseButtonDown() && GUI.MouseOn == child)
                    {
                        StartDraggingElement(child);
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
                    child.State = !child.ExternalHighlight ? ComponentState.None : ComponentState.Hover;
                }
            }
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            if (!Visible) { return; }

            if (!ignoreChildren)
            {
                foreach (GUIComponent child in Children)
                {
                    if (child == Content || child == ScrollBar || child == ContentBackground) { continue; }
                    child.AddToGUIUpdateList(ignoreChildren, order);
                }
            }
            
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

        public override void ForceLayoutRecalculation()
        {
            base.ForceLayoutRecalculation();
            Content.ForceLayoutRecalculation();
            ScrollBar.ForceLayoutRecalculation();
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
            if (child is GUIListBox) { return; }

            foreach (GUIComponent grandChild in child.Children)
            {
                ClampChildMouseRects(grandChild);
            }
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) { return; }

            UpdateChildrenRect();
            RepositionChildren();

            if (scrollBarNeedsRecalculation)
            {
                UpdateScrollBarSize();
            }

            if (FadeElements)
            {
                foreach (var (component, _) in childVisible)
                {
                    float lerp = 0;
                    float y = component.Rect.Y;
                    float contentY = Content.Rect.Y;
                    float height = component.Rect.Height;
                    if (y < Content.Rect.Y)
                    {
                        float distance = (contentY - y) / height;
                        lerp = distance;
                    }

                    float centerY = Content.Rect.Y + Content.Rect.Height / 2.0f;
                    if (y > centerY)
                    {
                        float distance = (y - centerY) / (centerY - height);
                        lerp = distance;
                    }

                    component.Color = component.HoverColor = ToolBox.GradientLerp(lerp, component.DefaultColor, Color.Transparent);
                    component.DisabledColor = ToolBox.GradientLerp(lerp, component.Style.DisabledColor, Color.Transparent);
                    component.HoverColor = ToolBox.GradientLerp(lerp, component.Style.HoverColor, Color.Transparent);
                    
                    foreach (var child in component.GetAllChildren())
                    {
                        Color gradient = ToolBox.GradientLerp(lerp, child.DefaultColor, Color.Transparent);
                        child.Color = child.HoverColor = gradient;
                        if (child is GUITextBlock block)
                        {
                            block.TextColor = block.HoverTextColor = gradient;
                        }
                    }
                }
            }

            if (scrollToElement != null)
            {
                if (!scrollToElement.Visible || !Content.Children.Contains(scrollToElement))
                {
                    scrollToElement = null;
                }
                else
                {
                    float diff = isHorizontal ? scrollToElement.Rect.X - Content.Rect.X : scrollToElement.Rect.Y - Content.Rect.Y;
                    float speed = MathHelper.Clamp(Math.Abs(diff) * 0.1f, 5.0f, 100.0f);
                    if (Math.Abs(diff) < speed || GUIScrollBar.DraggingBar != null)
                    {
                        speed = Math.Abs(diff);
                        scrollToElement = null;
                    }
                    BarScroll += speed * Math.Sign(diff) / TotalSize;
                } 
            }

            bool IsMouseOn() =>
                FindScrollableParentListBox(GUI.MouseOn) == this ||
                GUI.IsMouseOn(ScrollBar) ||
                (CanInteractWhenUnfocusable && Content.Rect.Contains(PlayerInput.MousePosition));

            if (PlayerInput.ScrollWheelSpeed != 0 && AllowMouseWheelScroll && IsMouseOn())
            {
                if (SmoothScroll)
                {
                    if (ClampScrollToElements)
                    {
                        bool scrollDown = Math.Clamp(PlayerInput.ScrollWheelSpeed, 0, 1) > 0;

                        if (scrollDown)
                        {
                            SelectPrevious(takeKeyBoardFocus: true);
                        }
                        else
                        {
                            SelectNext(takeKeyBoardFocus: true);
                        }
                    }
                }
                else
                {
                    ScrollBar.BarScroll -= (PlayerInput.ScrollWheelSpeed / 500.0f) * ScrollBar.UnclampedBarSize;
                }
            }

            ScrollBar.Enabled = ScrollBarEnabled && BarSize < 1.0f;
            if (AutoHideScrollBar)
            {
                ScrollBarVisible = ScrollBar.Enabled;
            }
            if (dimensionsNeedsRecalculation)
            {
                UpdateDimensions();
            }
        }
        
        private static GUIListBox FindScrollableParentListBox(GUIComponent target)
        {
            if (target is GUIListBox listBox && listBox.ScrollBarEnabled && listBox.BarSize < 1.0f) { return listBox; }
            if (target?.Parent == null) { return null; }
            return FindScrollableParentListBox(target.Parent);
        }

        public void SelectNext(bool force = false, bool autoScroll = true, bool takeKeyBoardFocus = false)
        {
            int index = SelectedIndex + 1;
            while (index < Content.CountChildren)
            {
                GUIComponent child = Content.GetChild(index);
                if (child.Visible)
                {
                    Select(index, force, !SmoothScroll && autoScroll, takeKeyBoardFocus: takeKeyBoardFocus);
                    if (SmoothScroll)
                    {
                        ScrollToElement(child);
                    }
                    break;
                }
                index++;
            }
        }

        public void SelectPrevious(bool force = false, bool autoScroll = true, bool takeKeyBoardFocus = false)
        {
            int index = SelectedIndex - 1;
            while (index >= 0)
            {
                GUIComponent child = Content.GetChild(index);
                if (child.Visible)
                {
                    Select(index, force, !SmoothScroll && autoScroll, takeKeyBoardFocus: takeKeyBoardFocus);
                    if (SmoothScroll)
                    {
                        ScrollToElement(child);
                    }
                    break;
                }
                index--;
            }
        }

        public void Select(int childIndex, bool force = false, bool autoScroll = true, bool takeKeyBoardFocus = false)
        {
            if (childIndex >= Content.CountChildren || childIndex < 0) { return; }

            GUIComponent child = Content.GetChild(childIndex);
            if (child is null) { return; }

            bool wasSelected = true;
            if (OnSelected != null)
            {
                // TODO: The callback is called twice, fix this!
                wasSelected = force || OnSelected(child, child.UserData);
            }

            if (!wasSelected) { return; }

            if (CurrentSelectMode == SelectMode.SelectMultiple ||
                (CurrentSelectMode == SelectMode.RequireShiftToSelectMultiple && PlayerInput.IsCtrlDown()))
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
            else if (CurrentSelectMode == SelectMode.RequireShiftToSelectMultiple && PlayerInput.IsShiftDown())
            {
                var first = SelectedComponent ?? child;
                var last = child;
                int firstIndex = Content.GetChildIndex(first);
                int lastIndex = Content.GetChildIndex(last);
                int sgn = Math.Sign(lastIndex - firstIndex);
                selected.Clear(); selected.Add(first);
                for (int i = firstIndex + sgn; i != lastIndex; i += sgn)
                {
                    if (Content.GetChild(i) is { Visible: true } interChild)
                    {
                        selected.Add(interChild);
                    }
                }
                if (first != last) { selected.Add(last); }
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
            if (takeKeyBoardFocus && CanTakeKeyBoardFocus && RectTransform.GetAllChildren().None(rt => rt.GUIComponent == GUI.KeyboardDispatcher.Subscriber))
            {
                Selected = true;
                GUI.KeyboardDispatcher.Subscriber = this;
            }
        }

        public void Select(IEnumerable<GUIComponent> children)
        {
            Selected = true;
            selected.Clear();
            selected.AddRange(children.Where(c => Content.Children.Contains(c)));
            foreach (var child in selected) { OnSelected?.Invoke(child, child.UserData); }
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
            scrollBarNeedsRecalculation = false;
            if (Content == null) { return; }

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
                if (PadBottom)
                {
                    GUIComponent last = Content.Children.LastOrDefault();
                    if (last != null)
                    {
                        totalSize += Rect.Height - last.Rect.Height;
                    }
                }
            }

            float minScrollBarSize = 20.0f;
            ScrollBar.UnclampedBarSize = ScrollBar.IsHorizontal ?
                Math.Min(Content.Rect.Width / (float)totalSize, 1.0f) :
                Math.Min(Content.Rect.Height / (float)totalSize, 1.0f);
            ScrollBar.BarSize = ScrollBar.IsHorizontal ?
                Math.Max(ScrollBar.UnclampedBarSize, minScrollBarSize / Content.Rect.Width) :
                Math.Max(ScrollBar.UnclampedBarSize, minScrollBarSize / Content.Rect.Height);
        }
        
        public override void ClearChildren()
        {
            Content.ClearChildren();
            selected.Clear();
        }

        public override void RemoveChild(GUIComponent child)
        {
            if (child == null) { return; }
            child.RectTransform.Parent = null;
            if (selected.Contains(child)) { selected.Remove(child); }
            if (draggedElement == child) { DraggedElement = null; }
            UpdateScrollBarSize();
        }

        public override void DrawChildren(SpriteBatch spriteBatch, bool recursive)
        {
            //do nothing (the children have to be drawn in the Draw method after the ScissorRectangle has been set)
            return;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) { return; }

            ContentBackground.DrawManually(spriteBatch, alsoChildren: false);

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (HideChildrenOutsideFrame)
            {                    
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, Content.Rect);
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }

            int lastVisible = 0;

            int i = 0;
            foreach (GUIComponent child in Content.Children)
            {
                if (!child.Visible) { continue; }
                if (child == draggedElement && CurrentDragMode == DragMode.DragOutsideBox) { continue; }
                if (!IsChildInsideFrame(child))
                {
                    if (lastVisible > 0) { break; }
                    continue;
                }
                lastVisible = i;
                child.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
                i++;
            }

            if (isDraggingElement && CurrentDragMode == DragMode.DragOutsideBox && HideDraggedElement)
            {
                Rectangle drawRect = DraggedElement.Rect;
                int draggedElementIndex = Content.GetChildIndex(DraggedElement);
                CalculateChildrenOffsets((index, point) =>
                {
                    if (draggedElementIndex == index)
                    {
                        drawRect.Location = Content.Rect.Location + point;
                    }
                });
                GUI.DrawRectangle(spriteBatch, drawRect, Color.White * 0.5f, thickness: 2f);
            }
            
            if (HideChildrenOutsideFrame)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }

            if (isDraggingElement && CurrentDragMode == DragMode.DragOutsideBox && !HideDraggedElement)
            {
                draggedElement.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
            }

            if (ScrollBarVisible)
            {
                ScrollBar.DrawManually(spriteBatch, alsoChildren: true, recursive: true);
            }
        }

        private bool IsChildInsideFrame(GUIComponent child)
        {
            if (child == null) { return false; }

            if (ScrollBar.IsHorizontal)
            {
                if (child.Rect.Right < Content.Rect.X) { return false; }
                if (child.Rect.X > Content.Rect.Right) { return false; }
            }
            else
            {
                if (child.Rect.Bottom < Content.Rect.Y) { return false; }
                if (child.Rect.Y > Content.Rect.Bottom) { return false; }
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
                    if (!isHorizontal && AllowArrowKeyScroll) { SelectNext(); }
                    break;
                case Keys.Up:
                    if (!isHorizontal && AllowArrowKeyScroll) { SelectPrevious(); }
                    break;
                case Keys.Left:
                    if (isHorizontal && AllowArrowKeyScroll) { SelectPrevious(); }
                    break;
                case Keys.Right:
                    if (isHorizontal && AllowArrowKeyScroll) { SelectNext(); }
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
