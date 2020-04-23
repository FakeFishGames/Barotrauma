using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum Anchor
    {
        TopLeft, TopCenter, TopRight,
        CenterLeft, Center, CenterRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public enum Pivot
    {
        TopLeft, TopCenter, TopRight,
        CenterLeft, Center, CenterRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public enum ScaleBasis
    {
        Normal,
        BothWidth, BothHeight,
        Smallest, Largest
    }

    public class RectTransform
    {
        #region Fields and Properties
        /// <summary>
        /// Should be assigned only by GUIComponent.
        /// Note that RectTransform is created first and the GUIComponent after that.
        /// This means the GUIComponent is not set before the GUIComponent is initialized.
        /// </summary>
        public GUIComponent GUIComponent { get; set; }

        private RectTransform parent;
        public RectTransform Parent
        {
            get { return parent; }
            set
            {
                if (parent == value || value == this) { return; }
                // Remove the child from the old parent
                RemoveFromHierarchy(displayErrors: false);
                parent = value;
                if (parent != null && !parent.children.Contains(this))
                {
                    parent.children.Add(this);
                    RecalculateAll(false, true, true);
                    Parent.ChildrenChanged?.Invoke(this);
                }
                ParentChanged?.Invoke(parent);
            }
        }

        private readonly List<RectTransform> children = new List<RectTransform>();
        public IEnumerable<RectTransform> Children => children;

        public int CountChildren => children.Count;

        private Vector2 relativeSize = Vector2.One;
        /// <summary>
        /// Relative to the parent rect.
        /// </summary>
        public Vector2 RelativeSize
        {
            get { return relativeSize; }
            set
            {
                if (relativeSize.NearlyEquals(value)) { return; }
                relativeSize = value;
                RecalculateAll(resize: true, scale: false, withChildren: true);
            }
        }

        private Point? minSize;
        /// <summary>
        /// Min size in pixels.
        /// Does not affect scaling.
        /// </summary>
        public Point MinSize
        {
            get { return minSize ?? Point.Zero; }
            set
            {
                if (minSize == value) { return; }
                minSize = value;
                RecalculateAll(true, false, true);
            }
        }

        private static Point maxPoint = new Point(int.MaxValue, int.MaxValue);
        private Point? maxSize;

        /// <summary>
        /// Max size in pixels.
        /// Does not affect scaling.
        /// </summary>
        public Point MaxSize
        {
            get { return maxSize ?? maxPoint; }
            set
            {
                if (maxSize == value) { return; }
                maxSize = value;
                RecalculateAll(true, false, true);
            }
        }

        private Point nonScaledSize;
        /// <summary>
        /// Size before scale multiplications.
        /// </summary>
        public Point NonScaledSize
        {
            get { return nonScaledSize; }
            set
            {
                if (nonScaledSize == value) { return; }
                nonScaledSize = value.Clamp(MinSize, MaxSize);
                RecalculateRelativeSize();
                RecalculateAnchorPoint();
                RecalculatePivotOffset();
                RecalculateChildren(resize: true, scale: false);
            }
        }
        /// <summary>
        /// Size after scale multiplications.
        /// </summary>
        public Point ScaledSize => NonScaledSize.Multiply(Scale);

        /// <summary>
        /// Applied to all RectTransforms.
        /// The elements are not automatically resized, if the global scale changes.
        /// You have to manually call RecalculateScale() for all elements after changing the global scale.
        /// This is because there is currently no easy way to inform all the elements without having a reference to them.
        /// Having a reference (static list, or event) is problematic, because deconstructing the elements is not handled manually.
        /// This means that the uncleared references would bloat the memory.
        /// We could recalculate the scale each time it's needed, 
        /// but in that case the calculation would need to be very lightweight and garbage free, which it currently is not.
        /// </summary>
        public static Vector2 globalScale = Vector2.One;

        private Vector2 localScale = Vector2.One;
        public Vector2 LocalScale
        {
            get { return localScale; }
            set
            {
                if (localScale.NearlyEquals(value)) { return; }
                localScale = value;
                RecalculateAll(resize: false, scale: true, withChildren: true);
                ScaleChanged?.Invoke();
            }
        }

        public Vector2 Scale { get; private set; }

        private Vector2 relativeOffset = Vector2.Zero;
        private Point absoluteOffset = Point.Zero;
        private Point screenSpaceOffset = Point.Zero;
        /// <summary>
        /// Defined as portions of the parent size.
        /// Also the direction of the offset is relative, calculated away from the anchor point.
        /// </summary>
        public Vector2 RelativeOffset
        {
            get { return relativeOffset; }
            set
            {
                if (relativeOffset.NearlyEquals(value)) { return; }
                relativeOffset = value;
                RecalculateChildren(false, false);
            }
        }
        /// <summary>
        /// Absolute in pixels but relative to the anchor point.
        /// Calculated away from the anchor point, like a padding.
        /// Use RelativeOffset to set an amount relative to the parent size.
        /// </summary>
        public Point AbsoluteOffset
        {
            get { return absoluteOffset; }
            set
            {
                if (absoluteOffset == value) { return; }
                absoluteOffset = value;
                recalculateRect = true;
                RecalculateChildren(false, false);
            }
        }
        /// <summary>
        /// Screen space offset. From top left corner. In pixels.
        /// </summary>
        public Point ScreenSpaceOffset
        {
            get { return screenSpaceOffset; }
            set
            {
                if (screenSpaceOffset == value) { return; }
                screenSpaceOffset = value;
                recalculateRect = true;
                RecalculateChildren(false, false);
            }
        }
        /// <summary>
        /// Calculated from the selected pivot. In pixels.
        /// </summary>
        public Point PivotOffset { get; private set; }
        /// <summary>
        /// Screen space point in pixels.
        /// </summary>
        public Point AnchorPoint { get; private set; }

        public Point TopLeft
        {
            get
            {
                Point absoluteOffset = ConvertOffsetRelativeToAnchor(AbsoluteOffset, Anchor);
                Point relativeOffset = ParentRect.MultiplySize(RelativeOffset);
                relativeOffset = ConvertOffsetRelativeToAnchor(relativeOffset, Anchor);
                return AnchorPoint + PivotOffset + absoluteOffset + relativeOffset + ScreenSpaceOffset;
            }
        }

        protected Point NonScaledTopLeft
        {
            get
            {
                Point absoluteOffset = ConvertOffsetRelativeToAnchor(AbsoluteOffset, Anchor);
                Point relativeOffset = NonScaledParentRect.MultiplySize(RelativeOffset);
                relativeOffset = ConvertOffsetRelativeToAnchor(relativeOffset, Anchor);
                return AnchorPoint + PivotOffset + absoluteOffset + relativeOffset + ScreenSpaceOffset;
            }
        }

        private bool recalculateRect = true;
        private Rectangle _rect;
        public Rectangle Rect
        {
            get
            {
                if (recalculateRect)
                {
                    _rect = new Rectangle(TopLeft, ScaledSize);
                    recalculateRect = false;
                }
                return _rect;
            }
        }
        public Rectangle ParentRect => Parent != null ? Parent.Rect : ScreenRect;

        protected Rectangle NonScaledRect => new Rectangle(NonScaledTopLeft, NonScaledSize);
        protected Rectangle NonScaledParentRect => parent != null ? Parent.NonScaledRect : ScreenRect;
        protected Rectangle ScreenRect => new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

        private Pivot pivot;
        /// <summary>
        /// Does not automatically calculate children.
        /// Note also that if you change the pivot point with this property, the pivot does not automatically match the anchor.
        /// You can use SetPosition to change everything automatcally or MatchPivotToAnchor to match the pivot to anchor.
        /// </summary>
        public Pivot Pivot
        {
            get { return pivot; }
            set
            {
                if (pivot == value) { return; }
                pivot = value;
                RecalculatePivotOffset();
            }
        }

        private Anchor anchor;
        /// <summary>
        /// Does not automatically calculate children.
        /// Note also that if you change the anchor point with this property, the pivot does not automatically match the anchor.
        /// You can use SetPosition to change everything automatically or MatchPivotToAnchor to match the pivot to anchor.
        /// </summary>
        public Anchor Anchor
        {
            get { return anchor; }
            set
            {
                if (anchor == value) { return; }
                anchor = value;
                RecalculateAnchorPoint();
            }
        }

        private ScaleBasis _scaleBasis;
        public ScaleBasis ScaleBasis 
        { 
            get { return _scaleBasis; }
            set
            {
                _scaleBasis = value;
                RecalculateAbsoluteSize();
            }
        }

        public bool IsLastChild
        {
            get
            {
                if (Parent == null) { return false; }
                var last = Parent.Children.LastOrDefault();
                if (last == null) { return false; }
                return last == this;
            }
        }

        public bool IsFirstChild
        {
            get
            {
                if (Parent == null) { return false; }
                var first = Parent.Children.FirstOrDefault();
                if (first == null) { return false; }
                return first == this;
            }
        }
        #endregion

        #region Events
        public event Action<RectTransform> ParentChanged;
        /// <summary>
        /// The element provided as the argument is the changed child. It may be new in the hierarchy or just repositioned.
        /// </summary>
        public event Action<RectTransform> ChildrenChanged;
        public event Action ScaleChanged;
        public event Action SizeChanged;
        #endregion

        #region Initialization
        public RectTransform(Vector2 relativeSize, RectTransform parent, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, Point? minSize = null, Point? maxSize = null, ScaleBasis scaleBasis = ScaleBasis.Normal)
        {
            Init(parent, anchor, pivot);
            _scaleBasis = scaleBasis;
            this.relativeSize = relativeSize;
            this.minSize = minSize;
            this.maxSize = maxSize;
            RecalculateScale();
            RecalculateAbsoluteSize();
            RecalculateAnchorPoint();
            RecalculatePivotOffset();
            parent?.ChildrenChanged?.Invoke(this);
        }

        /// <summary>
        /// By default, elements defined with an absolute size (in pixels) will scale with the parent.
        /// This can be changed by setting IsFixedSize to true.
        /// </summary>
        public RectTransform(Point absoluteSize, RectTransform parent = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, ScaleBasis scaleBasis = ScaleBasis.Normal, bool isFixedSize = false)
        {
            Init(parent, anchor, pivot);
            _scaleBasis = scaleBasis;
            this.nonScaledSize = absoluteSize;
            RecalculateScale();
            RecalculateRelativeSize();
            if (scaleBasis != ScaleBasis.Normal)
            {
                RecalculateAbsoluteSize();
            }
            RecalculateAnchorPoint();
            RecalculatePivotOffset();
            IsFixedSize = isFixedSize;
            parent?.ChildrenChanged?.Invoke(this);
        }

        public static RectTransform Load(XElement element, RectTransform parent, Anchor defaultAnchor = Anchor.TopLeft)
        {
            Enum.TryParse(element.GetAttributeString("anchor", defaultAnchor.ToString()), out Anchor anchor);
            Enum.TryParse(element.GetAttributeString("pivot", anchor.ToString()), out Pivot pivot);

            Point? minSize = null, maxSize = null;
            ScaleBasis scaleBasis = ScaleBasis.Normal;
            if (element.Attribute("minsize") != null)
            {
                minSize = element.GetAttributePoint("minsize", Point.Zero);
            }
            if (element.Attribute("maxsize") != null)
            {
                maxSize = element.GetAttributePoint("maxsize", new Point(1000, 1000));
            }
            string sb = element.GetAttributeString("scalebasis", null);
            if (sb != null)
            {
                Enum.TryParse(sb, ignoreCase: true, out scaleBasis);
            }
            RectTransform rectTransform;
            if (element.Attribute("absolutesize") != null)
            {
                rectTransform = new RectTransform(element.GetAttributePoint("absolutesize", new Point(1000, 1000)), parent, anchor, pivot, scaleBasis)
                {
                    minSize = minSize,
                    maxSize = maxSize
                };
            }
            else
            {
                rectTransform = new RectTransform(element.GetAttributeVector2("relativesize", Vector2.One), parent, anchor, pivot, minSize, maxSize, scaleBasis);
            }
            rectTransform.RelativeOffset = element.GetAttributeVector2("relativeoffset", Vector2.Zero);
            rectTransform.AbsoluteOffset = element.GetAttributePoint("absoluteoffset", Point.Zero);
            return rectTransform;
        }

        private void Init(RectTransform parent = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null)
        {
            this.parent = parent;
            parent?.children.Add(this);
            Anchor = anchor;
            Pivot = pivot ?? MatchPivotToAnchor(Anchor);
        }
        #endregion

        #region Protected methods
        protected void RecalculateScale()
        {
            var scale = LocalScale * globalScale;
            var parents = GetParents();
            Scale = parents.Any() ? parents.Select(rt => rt.LocalScale).Aggregate((parent, child) => parent * child) * scale : scale;
            recalculateRect = true;
            ScaleChanged?.Invoke();
        }

        protected void RecalculatePivotOffset()
        {
            PivotOffset = CalculatePivotOffset(Pivot, ScaledSize);
            recalculateRect = true;
        }

        protected void RecalculateAnchorPoint()
        {
            AnchorPoint = CalculateAnchorPoint(Anchor, ParentRect);
            recalculateRect = true;
        }

        protected void RecalculateRelativeSize()
        {
            relativeSize = new Vector2(NonScaledSize.X, NonScaledSize.Y) / new Vector2(NonScaledParentRect.Width, NonScaledParentRect.Height);
            recalculateRect = true;
            SizeChanged?.Invoke();
        }

        protected void RecalculateAbsoluteSize()
        {
            Point size = NonScaledParentRect.Size;
            switch (ScaleBasis)
            {
                case ScaleBasis.BothWidth:
                    size.Y = size.X;
                    break;
                case ScaleBasis.BothHeight:
                    size.X = size.Y;
                    break;
                case ScaleBasis.Smallest:
                    if (size.X < size.Y)
                    {
                        size.Y = size.X;
                    }
                    else
                    {
                        size.X = size.Y;
                    }
                    break;
                case ScaleBasis.Largest:
                    if (size.X > size.Y)
                    {
                        size.Y = size.X;
                    }
                    else
                    {
                        size.X = size.Y;
                    }
                    break;
            }
            size = size.Multiply(RelativeSize);
            nonScaledSize = size.Clamp(MinSize, MaxSize);
            recalculateRect = true;
            SizeChanged?.Invoke();
        }

        /// <summary>
        /// If false, the element will resize if the parent is resized (with the children).
        /// If true, the element will resize only when explicitly resized.
        /// Note that scaling always affects the elements.
        /// </summary>
        public bool IsFixedSize { get; set; }

        protected void RecalculateAll(bool resize, bool scale = true, bool withChildren = true)
        {
            if (scale)
            {
                RecalculateScale();
            }
            if (resize && !IsFixedSize)
            {
                RecalculateAbsoluteSize();
            }
            RecalculateAnchorPoint();
            RecalculatePivotOffset();
            if (withChildren)
            {
                RecalculateChildren(resize, scale);
            }
        }

        private bool RemoveFromHierarchy(bool displayErrors = true)
        {
            if (Parent == null)
            {
                if (displayErrors)
                {
                    DebugConsole.ThrowError("Parent null" + Environment.StackTrace);
                }
                return false;
            }
            if (!Parent.children.Contains(this))
            {
                if (displayErrors)
                {
                    DebugConsole.ThrowError("The children of the parent does not contain this child. This should not be possible! " + Environment.StackTrace);
                }
                return false;
            }
            if (!Parent.children.Remove(this))
            {
                if (displayErrors)
                {
                    DebugConsole.ThrowError("Unable to remove the child from the parent. " + Environment.StackTrace);
                }
                return false;
            }
            return true;
        }
        #endregion

        #region Public instance methods
        public void SetPosition(Anchor anchor, Pivot? pivot = null)
        {
            Anchor = anchor;
            Pivot = pivot ?? MatchPivotToAnchor(anchor);
            ScreenSpaceOffset = Point.Zero;
            recalculateRect = true;
            RecalculateChildren(false, false);
        }

        public void Resize(Point absoluteSize, bool resizeChildren = true)
        {
            nonScaledSize = absoluteSize.Clamp(MinSize, MaxSize);
            RecalculateRelativeSize();
            RecalculateAll(resize: false, scale: false, withChildren: false);
            RecalculateChildren(resizeChildren, false);
        }

        public void Resize(Vector2 relativeSize, bool resizeChildren = true)
        {
            this.relativeSize = relativeSize;
            RecalculateAll(resize: true, scale: false, withChildren: false);
            RecalculateChildren(resizeChildren, false);
        }

        public void ChangeScale(Vector2 newScale)
        {
            LocalScale = newScale;
        }

        public void ResetScale()
        {
            ChangeScale(Vector2.One);
        }

        /// <summary>
        /// Currently this needs to be manually called only when the global scale changes.
        /// If the local scale changes, the scale is automatically recalculated.
        /// </summary>
        public void RecalculateScale(bool withChildren)
        {
            RecalculateScale();
            if (withChildren)
            {
                RecalculateChildren(resize: false, scale: true);
            }
        }

        /// <summary>
        /// Manipulates ScreenSpaceOffset. 
        /// If you want to manipulate some other offset, access the property setters directly.
        /// </summary>
        public void Translate(Point translation)
        {
            ScreenSpaceOffset += translation;
        }

        /// <summary>
        /// Returns all parent elements in the hierarchy.
        /// </summary>
        public IEnumerable<RectTransform> GetParents()
        {
            var parents = new List<RectTransform>();
            if (Parent != null)
            {
                parents.Add(Parent);
                return parents.Concat(Parent.GetParents());
            }
            else
            {
                return parents;
            }
        }

        /// <summary>
        /// Returns all child elements in the hierarchy.
        /// </summary>
        public IEnumerable<RectTransform> GetAllChildren()
        {
            return children.Concat(children.SelectManyRecursive(c => c.children)); 
        }

        public int GetChildIndex(RectTransform rectT)
        {
            return children.IndexOf(rectT);
        }

        public RectTransform GetChild(int index)
        {
            return children[index];
        }

        public bool IsParentOf(RectTransform rectT, bool recursive = true)
        {
            return children.Contains(rectT) || (recursive && children.Any(c => c.IsParentOf(rectT)));
        }

        public void ClearChildren()
        {
            children.ForEachMod(c => c.Parent = null);
        }

        public void SortChildren(Comparison<RectTransform> comparison)
        {
            children.Sort(comparison);
            RecalculateAll(false, false, true);
            Parent.ChildrenChanged?.Invoke(this);
        }

        public void ReverseChildren()
        {
            children.Reverse();
            RecalculateAll(false, false, true);
            Parent.ChildrenChanged?.Invoke(this);
        }

        public void SetAsLastChild()
        {
            if (IsLastChild) { return; }
            if (!RemoveFromHierarchy(displayErrors: true)) { return; }
            parent.children.Add(this);
            RecalculateAll(false, true, true);
            parent.ChildrenChanged?.Invoke(this);
        }

        public void SetAsFirstChild()
        {
            if (IsFirstChild) { return; }
            RepositionChildInHierarchy(0);
        }

        public bool RepositionChildInHierarchy(int index)
        {
            if (!RemoveFromHierarchy(displayErrors: true)) { return false; }
            try
            {
                Parent.children.Insert(index, this);
            }
            catch (ArgumentOutOfRangeException e)
            {
                DebugConsole.ThrowError(e.ToString());
                return false;
            }
            RecalculateAll(false, true, true);
            Parent.ChildrenChanged?.Invoke(this);
            return true;
        }

        public void RecalculateChildren(bool resize, bool scale = true)
        {
            for (int i = 0; i < children.Count; i++)
            {
                children[i].RecalculateAll(resize, scale, withChildren: true);
            }
        }

        public void AddChildrenToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            for (int i = 0; i < children.Count; i++)
            {
                children[i].GUIComponent.AddToGUIUpdateList(ignoreChildren, order);
            }
        }

        public void MatchPivotToAnchor() => MatchPivotToAnchor(Anchor);


        private Point? animTargetPos;
        public Point AnimTargetPos
        {
            get { return animTargetPos ?? AbsoluteOffset; }
        }

        public void MoveOverTime(Point targetPos, float duration)
        {
            animTargetPos = targetPos;
            CoroutineManager.StartCoroutine(DoMoveAnimation(targetPos, duration));
        }
        public void ScaleOverTime(Point targetSize, float duration)
        {
            CoroutineManager.StartCoroutine(DoScaleAnimation(targetSize, duration));
        }

        private IEnumerable<object> DoMoveAnimation(Point targetPos, float duration)
        {
            Vector2 startPos = AbsoluteOffset.ToVector2();
            float t = 0.0f;
            while (t < duration && duration > 0.0f)
            {
                t += CoroutineManager.DeltaTime;
                AbsoluteOffset = Vector2.SmoothStep(startPos, targetPos.ToVector2(), t / duration).ToPoint();
                yield return CoroutineStatus.Running;
            }
            AbsoluteOffset = targetPos;
            animTargetPos = null;
            yield return CoroutineStatus.Success;
        }
        private IEnumerable<object> DoScaleAnimation(Point targetSize, float duration)
        {
            Vector2 startSize = NonScaledSize.ToVector2();
            float t = 0.0f;
            while (t < duration && duration > 0.0f)
            {
                t += CoroutineManager.DeltaTime;
                NonScaledSize = Vector2.SmoothStep(startSize, targetSize.ToVector2(), t / duration).ToPoint();
                yield return CoroutineStatus.Running;
            }
            NonScaledSize = targetSize;
            yield return CoroutineStatus.Success;
        }
        #endregion

        #region Static methods
        public static Pivot MatchPivotToAnchor(Anchor anchor)
        {
            if (!Enum.TryParse(anchor.ToString(), out Pivot pivot))
            {
                throw new Exception($"[RectTransform] Cannot match pivot to anchor {anchor}");
            }
            return pivot;
        }

        /// <summary>
        /// Converts the offset so that the direction is always away from the anchor point.
        /// </summary>
        public static Point ConvertOffsetRelativeToAnchor(Point offset, Anchor anchor)
        {
            switch (anchor)
            {
                case Anchor.BottomRight:
                    return offset.Inverse();
                case Anchor.BottomLeft:
                case Anchor.BottomCenter:
                    return new Point(offset.X, -offset.Y);
                case Anchor.TopRight:
                case Anchor.CenterRight:
                    return new Point(-offset.X, offset.Y);
                default:
                    return offset;
            }
        }

        public static Point CalculatePivotOffset(Pivot pivot, Point size)
        {
            int width = size.X;
            int height = size.Y;
            switch (pivot)
            {
                case Pivot.TopLeft:
                    return Point.Zero;
                case Pivot.TopCenter:
                    return new Point(-width / 2, 0);
                case Pivot.TopRight:
                    return new Point(-width, 0);
                case Pivot.CenterLeft:
                    return new Point(0, -height / 2);
                case Pivot.Center:
                    return size.Divide(2).Inverse();
                case Pivot.CenterRight:
                    return new Point(-width, -height / 2);
                case Pivot.BottomLeft:
                    return new Point(0, -height);
                case Pivot.BottomCenter:
                    return new Point(-width / 2, -height);
                case Pivot.BottomRight:
                    return new Point(-width, -height);
                default:
                    throw new NotImplementedException(pivot.ToString());
            }
        }

        public static Point CalculateAnchorPoint(Anchor anchor, Rectangle parent)
        {
            switch (anchor)
            {
                case Anchor.TopLeft:
                    return parent.Location;
                case Anchor.TopCenter:
                    return new Point(parent.Center.X, parent.Top);
                case Anchor.TopRight:
                    return new Point(parent.Right, parent.Top);
                case Anchor.CenterLeft:
                    return new Point(parent.Left, parent.Center.Y);
                case Anchor.Center:
                    return parent.Center;
                case Anchor.CenterRight:
                    return new Point(parent.Right, parent.Center.Y);
                case Anchor.BottomLeft:
                    return new Point(parent.Left, parent.Bottom);
                case Anchor.BottomCenter:
                    return new Point(parent.Center.X, parent.Bottom);
                case Anchor.BottomRight:
                    return new Point(parent.Right, parent.Bottom);
                default:
                    throw new NotImplementedException(anchor.ToString());
            }
        }

        /// <summary>
        /// The elements are not automatically resized, if the global scale changes.
        /// You have to manually call RecalculateScale() for all elements after changing the global scale.
        /// This is because there is currently no easy way to inform all the elements without having a reference to them.
        /// Having a reference (static list, or event) is problematic, because deconstructing the elements is not handled manually.
        /// This means that the uncleared references would bloat the memory.
        /// We could recalculate the scale each time it's needed, 
        /// but in that case the calculation would need to be very lightweight and garbage free, which it currently is not.
        /// </summary>
        public static void ResetGlobalScale()
        {
            globalScale = Vector2.One;
        }
        #endregion
    }
}
