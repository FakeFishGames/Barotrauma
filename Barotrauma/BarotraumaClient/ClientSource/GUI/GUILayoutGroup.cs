using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    public class GUILayoutGroup : GUIComponent
    {
        private bool isHorizontal;
        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set
            {
                isHorizontal = value;
                needsToRecalculate = true;
            }
        }

        private bool stretch;
        /// <summary>
        /// Note that stretching cannot be undone, because the previous child sizes are not stored.
        /// </summary>
        public bool Stretch
        {
            get { return stretch; }
            set
            {
                stretch = value;
                needsToRecalculate = true;
            }
        }

        private int absoluteSpacing;
        public int AbsoluteSpacing
        {
            get { return absoluteSpacing; }
            set
            {
                absoluteSpacing = MathHelper.Clamp(value, 0, int.MaxValue);
                needsToRecalculate = true;
            }
        }

        private float relativeSpacing;
        public float RelativeSpacing
        {
            get { return relativeSpacing; }
            set
            {
                relativeSpacing = MathHelper.Clamp(value, -1, 1);
                needsToRecalculate = true;
            }
        }

        private Anchor childAnchor;
        public Anchor ChildAnchor
        {
            get { return childAnchor; }
            set
            {
                childAnchor = value;
                needsToRecalculate = true;
            }
        }

        private bool needsToRecalculate;
        public bool NeedsToRecalculate
        {
            get { return needsToRecalculate; }
        }

        public GUILayoutGroup(RectTransform rectT, bool isHorizontal = false, Anchor childAnchor = Anchor.TopLeft) : base(null, rectT)
        {
            CanBeFocused = false;

            this.isHorizontal = isHorizontal;
            this.childAnchor = childAnchor;
            rectT.ChildrenChanged += (child) => needsToRecalculate = true;
            rectT.ScaleChanged += () => needsToRecalculate = true;
            rectT.SizeChanged += () => needsToRecalculate = true;
        }

        public void Recalculate()
        {
            float stretchFactor = 1.0f;
            if (stretch && RectTransform.Children.Count() > 0)
            {
                float minSize = RectTransform.Children
                    .Where(c => !c.GUIComponent.IgnoreLayoutGroups)
                    .Sum(c => isHorizontal ? c.MinSize.X : c.MinSize.Y);

                float totalSize = RectTransform.Children
                    .Where(c => !c.GUIComponent.IgnoreLayoutGroups)
                    .Sum(c => isHorizontal ? 
                        MathHelper.Clamp(c.Rect.Width, c.MinSize.X, c.MaxSize.X) :
                        MathHelper.Clamp(c.Rect.Height, c.MinSize.Y, c.MaxSize.Y));

                float thisSize = (isHorizontal ? Rect.Width : Rect.Height);

                totalSize += 
                    (RectTransform.Children.Count() - 1) * 
                    (absoluteSpacing + relativeSpacing * thisSize);

                stretchFactor = totalSize <= 0.0f || minSize >= thisSize ? 
                    1.0f : 
                    (thisSize - minSize) / (totalSize - minSize);
            }

            int absPos = 0;
            float relPos = 0;
            foreach (var child in RectTransform.Children)
            {
                if (child.GUIComponent.IgnoreLayoutGroups) { continue; }
                child.SetPosition(childAnchor);
                if (isHorizontal)
                {
                    child.RelativeOffset = new Vector2(relPos, child.RelativeOffset.Y);
                    child.AbsoluteOffset = new Point(absPos, child.AbsoluteOffset.Y);
                    absPos += (int)Math.Max((child.Rect.Width + absoluteSpacing) * stretchFactor, child.MinSize.X);
                    if (stretch)
                    {
                        child.RelativeSize = new Vector2(child.RelativeSize.X * stretchFactor, child.RelativeSize.Y);
                    }
                }
                else
                {
                    child.RelativeOffset = new Vector2(child.RelativeOffset.X, relPos);
                    child.AbsoluteOffset = new Point(child.AbsoluteOffset.X, absPos);
                    absPos += (int)Math.Max((child.Rect.Height + absoluteSpacing) * stretchFactor, child.MinSize.Y);
                    if (stretch)
                    {
                        child.RelativeSize = new Vector2(child.RelativeSize.X, child.RelativeSize.Y * stretchFactor);
                    }
                }
                relPos += relativeSpacing * stretchFactor;
            }
            needsToRecalculate = false;
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (needsToRecalculate)
            {
                Recalculate();
            }
        }
    }
}
