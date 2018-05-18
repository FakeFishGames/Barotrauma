using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class GUILayoutGroup : GUIComponent
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
                relativeSpacing = MathHelper.Clamp(value, 0, float.MaxValue);
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

        public GUILayoutGroup(RectTransform rectT, bool isHorizontal = false, Anchor childAnchor = Anchor.TopLeft, int absoluteSpacing = 0, float relativeSpacing = 0) : base(null, rectT)
        {
            this.isHorizontal = isHorizontal;
            this.childAnchor = childAnchor;
            this.absoluteSpacing = MathHelper.Clamp(absoluteSpacing, 0, int.MaxValue);
            this.relativeSpacing = MathHelper.Clamp(relativeSpacing, 0, float.MaxValue);
            rectT.ChildrenChanged += (child) => needsToRecalculate = true;
            rectT.ScaleChanged += () => needsToRecalculate = true;
            rectT.SizeChanged += () => needsToRecalculate = true;
        }

        private bool needsToRecalculate;
        protected void Recalculate()
        {
            //TODO: option to stretch the children to fit the layout group?
            int absPos = 0;
            float relPos = 0;
            foreach (var child in RectTransform.Children)
            {
                if (child.GUIComponent.IgnoreLayoutGroups) { continue; }
                child.SetPosition(childAnchor);
                if (isHorizontal)
                {
                    child.RelativeOffset = new Vector2(relPos, 0);
                    child.AbsoluteOffset = new Point(absPos, 0);
                    absPos += child.Rect.Width + absoluteSpacing;
                }
                else
                {
                    child.RelativeOffset = new Vector2(0, relPos);
                    child.AbsoluteOffset = new Point(0, absPos);
                    absPos += child.Rect.Height + absoluteSpacing;
                }
                relPos += relativeSpacing;
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
