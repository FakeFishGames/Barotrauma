using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class GUICanvas : RectTransform
    {
        protected GUICanvas() : base(Vector2.One, parent: null) { }

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
                        GameMain.Instance.OnResolutionChanged += RecalculateSize;
                    }
                }
                return _instance;
            }
        }

        // Turn public, if there is a need to call this manually.
        private static void RecalculateSize()
        {
            Instance.Resize(Vector2.One, resizeChildren: true);
            Instance.GetAllChildren().Select(c => c.GUIComponent as GUITextBlock).ForEach(t => t?.SetTextPos());
        }
    }
}
