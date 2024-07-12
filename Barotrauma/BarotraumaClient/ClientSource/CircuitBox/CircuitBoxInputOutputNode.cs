using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal sealed partial class CircuitBoxInputOutputNode
    {
        private const string PromptUserData = "InputOutputEditPrompt";

        public void PromptEdit(GUIComponent parent)
        {
            CircuitBox.UI?.SetMenuVisibility(false);
            GUIFrame backgroundBlocker = new(new RectTransform(Vector2.One, parent.RectTransform), style: "GUIBackgroundBlocker")
            {
                UserData = PromptUserData
            };

            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 0.8f), backgroundBlocker.RectTransform, Anchor.Center), isHorizontal: false, childAnchor: Anchor.TopCenter);
            GUIFrame labelArea = new(new RectTransform(new Vector2(1f, 0.8f), mainLayout.RectTransform, Anchor.Center));

            GUILayoutGroup labelLayout = new GUILayoutGroup(new RectTransform(Vector2.One, labelArea.RectTransform), childAnchor: Anchor.Center);
            GUIListBox labelList = new GUIListBox(new RectTransform(ToolBox.PaddingSizeParentRelative(labelLayout.RectTransform, 0.9f), labelLayout.RectTransform));

            Dictionary<string, GUITextBox> textBoxes = new();

            foreach (var conn in Connectors)
            {
                bool found = ConnectionLabelOverrides.TryGetValue(conn.Name, out string labelOverride);

                GUILayoutGroup connLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.12f), labelList.Content.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(0.4f, 1f), connLayout.RectTransform), text: conn.Connection.DisplayName, font: GUIStyle.SubHeadingFont);
                GUITextBox box = GUI.CreateTextBoxWithPlaceholder(new RectTransform(new Vector2(0.6f, 1f), connLayout.RectTransform), text: found ? labelOverride : string.Empty, conn.Connection.DisplayName.Value);
                box.MaxTextLength = MaxConnectionLabelLength;

                textBoxes.Add(conn.Name, box);
            }

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), mainLayout.RectTransform), text: TextManager.Get("confirm"))
            {
                OnClicked = (_, _) =>
                {
                    var newOverrides = textBoxes.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value.Text);

                    foreach (var (key, value) in newOverrides.ToImmutableDictionary())
                    {
                        if (ConnectionLabelOverrides.TryGetValue(key, out string newValue))
                        {
                            if (newValue == value)
                            {
                                newOverrides.Remove(key);
                            }
                        }
                        else if (string.IsNullOrWhiteSpace(value))
                        {
                            newOverrides.Remove(key);
                        }
                    }

                    CircuitBox.SetConnectionLabelOverrides(this, newOverrides);
                    RemoveEditPrompt(parent);
                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), mainLayout.RectTransform), text: TextManager.Get("cancel"))
            {
                OnClicked = (_, _) =>
                {
                    RemoveEditPrompt(parent);
                    return true;
                }
            };
        }

        public void RemoveEditPrompt(GUIComponent parent)
        {
            if (parent.FindChild(PromptUserData) is not { } promptParent) { return; }
            parent.RemoveChild(promptParent);
        }
    }
}