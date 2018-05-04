using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

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

    public class RectTransform
    {
        #region Fields and Properties
        /// <summary>
        /// Should be assigned only by GUIComponent.
        /// </summary>
        public GUIComponent Element { get; set; }

        private RectTransform parent;
        public RectTransform Parent
        {
            get { return parent; }
            set
            {
                if (parent != value)
                {
                    parent?.children.Remove(this);
                }
                parent = value;
                parent?.children.Add(this);
            }
        }

        private HashSet<RectTransform> children = new HashSet<RectTransform>();
        public IEnumerable<RectTransform> Children => children;

        private Vector2 relativeSize = Vector2.One;
        /// <summary>
        /// Relative to the parent rect.
        /// </summary>
        public Vector2 RelativeSize
        {
            get { return relativeSize; }
            set
            {
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
                localScale = value;
                RecalculateAll(resize: false, scale: true, withChildren: true);
            }
        }

        public Vector2 Scale { get; private set; }

        private Vector2 relativeOffset = Vector2.Zero;
        private Point absoluteOffset = Point.Zero;
        private Point screenSpaceOffset = Point.Zero;
        /// <summary>
        /// Defined as portions of the parent size.
        /// Also the direction of the offset is relative, calculated away from the anchor point, like a padding.
        /// </summary>
        public Vector2 RelativeOffset
        {
            get { return relativeOffset; }
            set
            {
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
                absoluteOffset = value;
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
                screenSpaceOffset = value;
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

        public Rectangle Rect => new Rectangle(TopLeft, ScaledSize);
        public Rectangle ParentRect => Parent != null ? Parent.Rect : ScreenRect;

        protected Rectangle NonScaledRect => new Rectangle(NonScaledTopLeft, NonScaledSize);
        protected Rectangle NonScaledParentRect => parent != null ? Parent.NonScaledRect : ScreenRect;
        protected Rectangle ScreenRect => new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

        private Pivot pivot;
        public Pivot Pivot
        {
            get { return pivot; }
            set
            {
                pivot = value;
                RecalculatePivotOffset();
            }
        }

        private Anchor anchor;
        public Anchor Anchor
        {
            get { return anchor; }
            set
            {
                anchor = value;
                RecalculateAnchorPoint();
            }
        }
        #endregion

        #region Initialization
        public RectTransform(Vector2 relativeSize, RectTransform parent, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null, Point? minSize = null, Point? maxSize = null)
        {
            Init(parent, anchor, pivot);
            this.relativeSize = relativeSize;
            this.minSize = minSize;
            this.maxSize = maxSize;
            RecalculateScale();
            RecalculateAbsoluteSize();
            RecalculateAnchorPoint();
            RecalculatePivotOffset();
        }

        public RectTransform(Point absoluteSize, RectTransform parent = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null)
        {
            Init(parent, anchor, pivot);
            this.nonScaledSize = absoluteSize;
            RecalculateScale();
            RecalculateRelativeSize();            
            RecalculateAnchorPoint();
            RecalculatePivotOffset();
        }

        private void Init(RectTransform parent = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null)
        {
            Parent = parent;
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
        }

        protected void RecalculatePivotOffset()
        {
            PivotOffset = CalculatePivotOffset(Pivot, ScaledSize);
        }

        protected void RecalculateAnchorPoint()
        {
            AnchorPoint = CalculateAnchorPoint(Anchor, ParentRect);
        }

        protected void RecalculateRelativeSize()
        {
            relativeSize = new Vector2(NonScaledSize.X, NonScaledSize.Y) / new Vector2(NonScaledParentRect.Width, NonScaledParentRect.Height);
        }

        protected void RecalculateAbsoluteSize()
        {
            nonScaledSize = NonScaledParentRect.Size.Multiply(RelativeSize).Clamp(MinSize, MaxSize);
        }

        protected void RecalculateAll(bool resize, bool scale = true, bool withChildren = true)
        {
            if (scale)
            {
                RecalculateScale();
            }
            if (resize)
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

        public void RecalculateChildren(bool resize, bool scale = true)
        {
            children.ForEach(c => c.RecalculateAll(resize, scale, withChildren: true));
        }
        #endregion

        #region Public instance methods
        public void SetPosition(Anchor anchor, Pivot? pivot = null)
        {
            Anchor = anchor;
            Pivot = pivot ?? MatchPivotToAnchor(anchor);
            ScreenSpaceOffset = Point.Zero;
            RecalculateChildren(false, false);
        }

        public void Resize(Point absoluteSize, bool resizeChildren = true)
        {
            nonScaledSize = absoluteSize;
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
        /// Returns all elements in the hierarchy tree.
        /// </summary>
        /// <returns></returns>
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
        /// Returns all elements in the hierarchy tree. TODO: test
        /// </summary>
        /// <returns></returns>
        public IEnumerable<RectTransform> GetChildren()
        {
            var childs = children.AsEnumerable();
            children.ForEach(c => childs.Concat(c.GetChildren()));
            return childs;
        }

        /// <summary>
        /// TODO: test
        /// </summary>
        public bool IsParentOf(RectTransform rectT)
        {
            return children.Contains(rectT) || children.Any(c => c.IsParentOf(rectT));
            //foreach (var child in children)
            //{
            //    if (child == rectT) { return true; }
            //    if (child.IsParentOf(rectT)) { return true; }
            //}
            //return false;
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
