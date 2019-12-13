using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ParamsEditor
    {
        private static ParamsEditor _instance;
        public static ParamsEditor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ParamsEditor();
                }
                return _instance;
            }
        }

        public GUIComponent Parent { get; private set; }
        public GUIListBox EditorBox { get; private set; }
        /// <summary>
        /// Uses Linq queries. Don't use too frequently or reimplement.
        /// </summary>
        public IEnumerable<SerializableEntityEditor> FindEntityEditors() => EditorBox.Content.RectTransform.Children
            .Select(c => c.GUIComponent as SerializableEntityEditor)
            .Where(c => c != null);

        public GUIListBox CreateEditorBox(RectTransform rectT = null)
        {
            rectT = rectT ?? new RectTransform(new Vector2(0.25f, 1f), GUI.Canvas) { MinSize = new Point(340, GameMain.GraphicsHeight) };
            rectT.SetPosition(Anchor.TopRight);
            Parent = new GUIFrame(rectT, null, Color);
            EditorBox = new GUIListBox(new RectTransform(Vector2.One * 0.98f, rectT, Anchor.Center), color: Color.Black, style: null)
            {
                Spacing = 10,
                AutoHideScrollBar = true,
                KeepSpaceForScrollBar = true
            };
            return EditorBox;
        }

        public void Clear()
        {
            EditorBox.ClearChildren();
        }

        public ParamsEditor(RectTransform rectT = null)
        {
            EditorBox = CreateEditorBox();
        }

        public static Color Color = new Color(20, 20, 20, 255);
    }
}
