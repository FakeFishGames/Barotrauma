using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class GUICanvas : RectTransform
    {
        protected GUICanvas() : base(size, parent: null) { }

        private static GUICanvas _instance;
        public static GUICanvas Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GUICanvas();
                    if (GameMain.Instance != null)
                    {
                        GameMain.Instance.ResolutionChanged += RecalculateSize;
                    }
                    _instance.ItemComponentHolder = new GUIFrame(new RectTransform(Vector2.One, _instance, Anchor.Center)).RectTransform;
                }
                return _instance;
            }
        }

        public RectTransform ItemComponentHolder;

        private static Vector2 size => new Vector2(GameMain.GraphicsWidth / (float)GUI.UIWidth, 1f);

        protected override Rectangle NonScaledUIRect => UIRect;

        private enum ResizeAxis { Both = 0, X = 1, Y = 2 }

        // Turn public, if there is a need to call this manually.
        private static void RecalculateSize()
        {
            Vector2 recalculatedSize = size;

            // Scale children that are supposed to encompass the whole screen so that they are properly scaled on ultrawide as well
            for (int i = 0; i < Instance.Children.Count(); i++)
            {
                RectTransform target = Instance.GetChild(i);
                if (target == null || target.RelativeSize.X < 1 && target.RelativeSize.Y < 1) continue;

                ResizeAxis axis;

                if (target.RelativeSize.X >= 1 && target.RelativeSize.Y >= 1)
                {
                    axis = ResizeAxis.Both;
                }
                else if (target.RelativeSize.X >= 1)
                {
                    axis = ResizeAxis.X;
                }
                else
                {
                    axis = ResizeAxis.Y;
                }

                switch (axis)
                {
                    case ResizeAxis.Both:
                        target.RelativeSize = recalculatedSize;
                        break;

                    case ResizeAxis.X:
                        target.RelativeSize = new Vector2(recalculatedSize.X, target.RelativeSize.Y);
                        break;

                    case ResizeAxis.Y:
                        target.RelativeSize = new Vector2(target.RelativeSize.X, recalculatedSize.Y);
                        break;
                }
            }

            Instance.Resize(size, resizeChildren: true);
            Instance.GetAllChildren().Select(c => c.GUIComponent as GUITextBlock).ForEach(t => t?.SetTextPos());
        }
    }
}
