#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal sealed partial class CircuitBoxLabelNode
    {
        private CircuitBoxLabel headerLabel;
        private readonly GUITextBlock bodyLabel;
        private const string PromptUserData = "LabelEditPrompt";

        public override void DrawHeader(SpriteBatch spriteBatch, RectangleF rect, Color color)
        {
            GUI.DrawString(spriteBatch, new Vector2(rect.X + CircuitBoxSizes.NodeHeaderTextPadding, rect.Center.Y - headerLabel.Size.Y / 2f), headerLabel.Value, GUIStyle.TextColorNormal, font: GUIStyle.LargeFont);
        }

        public override void DrawBody(SpriteBatch spriteBatch, RectangleF rect, Color color)
        {
            bodyLabel.TextOffset = rect.Location - bodyLabel.Rect.Location.ToVector2() + new Vector2(CircuitBoxSizes.NodeBodyTextPadding);
            bodyLabel.DrawManually(spriteBatch);
        }

        public override void OnResized(RectangleF rect)
            => UpdateTextSizes(rect);

        private void UpdateTextSizes(RectangleF rect)
        {
            var size = new Point((int)rect.Width - CircuitBoxSizes.NodeBodyTextPadding * 2, (int)rect.Height - CircuitBoxSizes.NodeBodyTextPadding * 2);
            bodyLabel.RectTransform.NonScaledSize = size;
            bodyLabel.Text = GetLocalizedText(BodyText);
            if (bodyLabel.Font != null)
            {
                bodyLabel.Text = ToolBox.LimitStringHeight(bodyLabel.WrappedText.Value, bodyLabel.Font!, size.Y);
            }
            headerLabel = new CircuitBoxLabel(ToolBox.LimitString(GetLocalizedText(HeaderText), GUIStyle.LargeFont, size.X), GUIStyle.LargeFont);

            static LocalizedString GetLocalizedText(NetLimitedString text) => TextManager.Get(text.Value).Fallback(text.Value);
        }

        public void PromptEditText(GUIComponent parent)
        {
            Color newColor = Color;
            CircuitBox.UI?.SetMenuVisibility(false);
            GUIFrame backgroundBlocker = new(new RectTransform(Vector2.One, parent.RectTransform), style: "GUIBackgroundBlocker")
            {
                UserData = PromptUserData
            };

            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 0.8f), backgroundBlocker.RectTransform, Anchor.Center), isHorizontal: false, childAnchor: Anchor.TopCenter);

            GUILayoutGroup colorLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), mainLayout.RectTransform));
            new GUIFrame(new RectTransform(new Vector2(1f, 0.9f), colorLayout.RectTransform)) { IgnoreLayoutGroups = true };
            GUILayoutGroup colorArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), colorLayout.RectTransform), isHorizontal: true);

            GUIFrame labelArea = new(new RectTransform(new Vector2(1f, 0.65f), mainLayout.RectTransform, Anchor.Center));

            GUIFrame header = new GUIFrame(new RectTransform(new Vector2(1f, 0.15f), labelArea.RectTransform, Anchor.TopLeft), style: "CircuitBoxTop");
            GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1f, 0.86f), labelArea.RectTransform, Anchor.BottomLeft), style: "CircuitBoxFrame");
            header.Color = frame.Color = Color;

            GUITextBox headerTextBox = new GUITextBox(new RectTransform(Vector2.One, header.RectTransform, Anchor.Center), text: HeaderText.Value, font: headerLabel.Font, style: "GUITextBoxNoStyle")
            {
                MaxTextLength = NetLimitedString.MaxLength,
                Text = HeaderText.Value
            };

            GUITextBox bodyTextBox = new GUITextBox(new RectTransform(ToolBox.PaddingSizeParentRelative(frame.RectTransform, 0.95f), frame.RectTransform, Anchor.Center), text: BodyText.Value, font: GUIStyle.Font, style: "GUITextBoxNoStyle", textAlignment: Alignment.TopLeft, wrap: true)
            {
                MaxTextLength = NetLimitedString.MaxLength
            };

            bodyTextBox.OnEnterPressed += (textBox, text) =>
            {
                int caretIndex = textBox.CaretIndex;
                textBox.Text = $"{text[..caretIndex]}\n{text[caretIndex..]}";
                textBox.CaretIndex = caretIndex + 1;

                return true;
            };

            var characterLimit = new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), frame.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.03f, 0.02f) }, text: $"{bodyTextBox.Text.Length}/{NetLimitedString.MaxLength}", font: GUIStyle.SmallFont, textAlignment: Alignment.Right);

            bodyTextBox.OnTextChanged += (textBox, _) =>
            {
                textBox.TextColor = textBox.TextBlock.SelectedTextColor = textBox.Text.Length > NetLimitedString.MaxLength
                    ? GUIStyle.Red
                    : GUIStyle.TextColorNormal;

                characterLimit.Text = $"{textBox.Text.Length}/{NetLimitedString.MaxLength}";
                return true;
            };

            static void UpdateLabelColor(GUITextBox box)
            {
                bool found = TextManager.ContainsTag(box.Text);
                box.TextColor = found
                    ? GUIStyle.Orange
                    : GUIStyle.TextColorNormal;

                if (found)
                {
                    box.ToolTip = TextManager.GetWithVariable("StringPropertyTranslate", "[translation]", TextManager.Get(box.Text));
                }
                else
                {
                    box.ToolTip = string.Empty;
                }
            }

            bodyTextBox.OnDeselected += static (textBox, _) => UpdateLabelColor(textBox);
            headerTextBox.OnDeselected += static (textBox, _) => UpdateLabelColor(textBox);
            UpdateLabelColor(bodyTextBox);
            UpdateLabelColor(headerTextBox);

            mainLayout.Recalculate();
            headerTextBox.ForceUpdate();

            new GUIButton(new RectTransform(new Vector2(0.5f, 0.1f), mainLayout.RectTransform), text: TextManager.Get("confirm"))
            {
                OnClicked = (_, _) =>
                {
                    CircuitBox.RenameLabel(this, newColor, new NetLimitedString(headerTextBox.Text), new NetLimitedString(bodyTextBox.Text));
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

            LocalizedString[] colorComponentLabels =
            {
                TextManager.Get("spriteeditor.colorcomponentr"),
                TextManager.Get("spriteeditor.colorcomponentg"),
                TextManager.Get("spriteeditor.colorcomponentb")
            };
            for (int i = 0; i <= 2; i++)
            {
                var element = new GUIFrame(new RectTransform(new Vector2(0.33f, 1), colorArea.RectTransform), style: null);

                var colorLabel = new GUITextBlock(new RectTransform(new Vector2(0.3f, 1), element.RectTransform, Anchor.CenterLeft), colorComponentLabels[i],
                    font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);

                var numberInput = new GUINumberInput(new RectTransform(new Vector2(0.7f, 1), element.RectTransform, Anchor.CenterRight), NumberType.Int)
                {
                    Font = GUIStyle.SubHeadingFont,
                    MinValueInt = 0,
                    MaxValueInt = 255
                };
                switch (i)
                {
                    case 0:
                        colorLabel.TextColor = GUIStyle.Red;
                        numberInput.IntValue = Color.R;
                        numberInput.OnValueChanged += numInput =>
                        {
                            newColor.R = (byte)numInput.IntValue;
                            header.Color = frame.Color = newColor;
                        };
                        break;
                    case 1:
                        colorLabel.TextColor = GUIStyle.Green;
                        numberInput.IntValue = Color.G;
                        numberInput.OnValueChanged += numInput =>
                        {
                            newColor.G = (byte)numInput.IntValue;
                            header.Color = frame.Color = newColor;
                        };
                        break;
                    case 2:
                        colorLabel.TextColor = GUIStyle.Blue;
                        numberInput.IntValue = Color.B;
                        numberInput.OnValueChanged += numInput =>
                        {
                            newColor.B = (byte)numInput.IntValue;
                            header.Color = frame.Color = newColor;
                        };
                        break;
                }
            }
        }

        public void RemoveEditPrompt(GUIComponent parent)
        {
            if (parent.FindChild(PromptUserData) is not { } promptParent) { return; }
            parent.RemoveChild(promptParent);
        }
    }
}