using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class GUICanvas : RectTransform
    {
        private static readonly object mutex = new object();

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
                    _instance.ChildrenChanged += OnChildrenChanged;
                }
                return _instance;
            }
        }

        //GUICanvas stores the children as weak references, to allow elements that we no longer need to get garbage collected
        private readonly List<WeakReference<RectTransform>> childrenWeakRef = new List<WeakReference<RectTransform>>();

        private static Vector2 size => new Vector2(GameMain.GraphicsWidth / (float)GUI.UIWidth, 1f);

        protected override Rectangle NonScaledUIRect => UIRect;

        private enum ResizeAxis { Both = 0, X = 1, Y = 2 }

        private static void OnChildrenChanged(RectTransform _)
        {
            lock (mutex)
            {
                //add weak reference if we don't have one yet
                foreach (var child in _instance.Children)
                {
                    if (!_instance.childrenWeakRef.Any(c => c.TryGetTarget(out var existingChild) && existingChild == child))
                    {
                        _instance.childrenWeakRef.Add(new WeakReference<RectTransform>(child));
                    }
                }
                //get rid of strong references
                _instance.children.Clear();
                //remove dead children
                for (int i = _instance.childrenWeakRef.Count - 2; i >= 0; i--)
                {
                    if (!_instance.childrenWeakRef[i].TryGetTarget(out var child) || child.Parent != _instance)
                    {
                        _instance.childrenWeakRef.RemoveAt(i);
                    }
                }
            }
        }

        // Turn public, if there is a need to call this manually.
        private static void RecalculateSize()
        {
            Vector2 recalculatedSize = size;

            // Scale children that are supposed to encompass the whole screen so that they are properly scaled on ultrawide as well
            for (int i = 0; i < Instance.childrenWeakRef.Count; i++)
            {
                if (!_instance.childrenWeakRef[i].TryGetTarget(out RectTransform target) || target == null) { continue; };

                _instance.children.Add(target);

                if (target.RelativeSize.X < 1 && target.RelativeSize.Y < 1) { continue; }

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
            _instance.children.Clear();
        }
    }
}
