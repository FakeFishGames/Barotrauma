using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    internal abstract class DebugWindow
    {
        private static readonly List<DebugWindow> AllWindows = new();
        private static readonly Queue<DebugWindow> ToClose = new();

        protected readonly GUIFrame Frame;
        protected readonly GUILayoutGroup Content;

        protected DebugWindow(bool createRefreshButton = false)
        {
            Frame = new(new(new Vector2(0.25f, 0.5f), GUI.Canvas, Anchor.Center), "ItemUI")
            {
                CanBeFocused = false
            };

            int dragIconHeight = GUIStyle.ItemFrameMargin.Y / 4;
            GUIDragHandle handle = new(new(Vector2.One, Frame.RectTransform, Anchor.Center), Frame.RectTransform, null);
            new GUIImage(new(new Point(Frame.Rect.Width, dragIconHeight), handle.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, dragIconHeight / 2), MinSize = new Point(0, dragIconHeight) }, "GUIDragIndicatorHorizontal")
            {
                CanBeFocused = false
            };

            Rectangle margins = new(0, 0, 2, 2);
            GUILayoutGroup buttonArea = new GUILayoutGroup(new RectTransform(new Point(Frame.Rect.Width, GUIStyle.ItemFrameMargin.Y / 2) - margins.Size, Frame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = margins.Location }, true, Anchor.CenterRight);

            new GUIButton(new(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.Smallest), style: "GUICancelButton", color: GUIStyle.Red)
            {
                ToolTip = TextManager.Get("Close"),
                OnClicked = (_, _) =>
                {
                    Close();
                    return true;
                }
            };

            if (createRefreshButton)
            {
                new GUIButton(new(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.Smallest), style: "GUIButtonRefresh")
                {
                    ToolTip = TextManager.Get("ReloadLinkedSub"),
                    OnClicked = (_, _) =>
                    {
                        Refresh();
                        return true;
                    }
                };
            }

            Content = new(new(Frame.Rect.Size - GUIStyle.ItemFrameMargin, Frame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset })
            {
                CanBeFocused = false,
                Stretch = true
            };

            AllWindows.Add(this);
        }

        protected static GUITextBlock CreateListEntry(GUIListBox list, object data, out GUILayoutGroup right)
        {
            GUITextBlock entry = new(new(Vector2.UnitX, list.Content.RectTransform, minSize: new(0, (int)GUIStyle.Font.LineHeight)), "", GUIStyle.TextColorNormal)
            {
                Padding = Vector4.Zero,
                UserData = data
            };

            right = new(new(Vector2.One, entry.RectTransform), true, Anchor.CenterRight)
            {
                CanBeFocused = false
            };

            return entry;
        }

        protected static GUITextBox CreateFilterBox(GUIComponent parent, GUITextBox.OnTextChangedHandler onTextChanged)
        {
            GUITextBox filterBox = new(new(Vector2.UnitX, parent.RectTransform), createClearButton: true);
            GUITextBlock filterLabel = new(new(Vector2.UnitY, filterBox.RectTransform, Anchor.CenterLeft), TextManager.Get("serverlog.filter"), GUIStyle.TextColorNormal * 0.5f);
            filterBox.OnSelected += (_, _) => filterLabel.Visible = false;
            filterBox.OnDeselected += (sender, _) => filterLabel.Visible = !sender.Text.Any();
            filterBox.OnTextChanged += (textBox, text) => filterLabel.Visible = !text.Any() && !textBox.Selected;
            filterBox.OnTextChanged += onTextChanged;
            return filterBox;
        }

        protected static void FilterEntries(GUIListBox list, string filter) => list.Content.Children.OfType<GUITextBlock>().ForEach(i => i.Visible = i.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));

        protected virtual void Refresh() { }

        protected virtual void Update() => Frame.AddToGUIUpdateList();
        public static void UpdateAll()
        {
            while (ToClose.Count > 0)
            {
                DebugWindow window = ToClose.Dequeue();
                window.Frame.RectTransform.Parent = null;
                AllWindows.Remove(window);
            }

            AllWindows.ForEach(window => window.Update());
        }

        protected virtual void Draw(SpriteBatch spriteBatch) { }
        public static void DrawAll(SpriteBatch spriteBatch) => AllWindows.ForEach(window => window.Draw(spriteBatch));

        protected virtual void Close() => ToClose.Enqueue(this);
        public static void CloseAll() => AllWindows.ForEach(window => window.Close());
    }
}