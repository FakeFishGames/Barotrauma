using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    class GUILayoutGroup : GUIComponent
    {
        private bool isHorizontal;

        private int spacing;

        private Anchor childAnchor;

        public GUILayoutGroup(RectTransform rectT, bool isHorizontal = false, Anchor childAnchor = Anchor.TopLeft, int spacing = 0) : base(null, rectT)
        {
            this.isHorizontal = isHorizontal;
            this.childAnchor = childAnchor;
            this.spacing = spacing;
        }

        protected override void Update(float deltaTime)
        {
            //TODO: only update layout when children are added/removed
            //TODO: option to stretch the children to fit the layout group?
            int pos = 0;
            foreach (RectTransform child in RectTransform.Children)
            {
                child.Anchor = childAnchor;

                if (isHorizontal)
                {
                    child.AbsoluteOffset = new Point(pos, 0);
                    pos += child.Rect.Width + spacing;
                }
                else
                {
                    child.AbsoluteOffset = new Point(0, pos);
                    pos += child.Rect.Height + spacing;
                }                
            }
        }
    }
}
