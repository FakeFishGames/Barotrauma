#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma.Steam
{
    public partial class WorkshopMenu
    {
        private static RectTransform NewItemRectT(GUILayoutGroup parent, float heightScale = 1.0f)
            => new RectTransform((1.0f, 0.06f * heightScale), parent.RectTransform, Anchor.CenterLeft);

        private static void Spacer(GUILayoutGroup parent, float height = 0.03f)
        {
            new GUIFrame(new RectTransform((1.0f, height), parent.RectTransform, Anchor.CenterLeft), style: null);
        }

        private static GUITextBlock Label(GUILayoutGroup parent, LocalizedString str, GUIFont font, float heightScale = 1.0f)
        {
            return new GUITextBlock(NewItemRectT(parent, heightScale), str, font: font);
        }

        private static GUITextBox ScrollableTextBox(GUILayoutGroup parent, float heightScale, string text)
        {
            var containingListBox = new GUIListBox(NewItemRectT(parent, heightScale));
            var textBox = new GUITextBox(
                new RectTransform(Vector2.One, containingListBox.Content.RectTransform),
                "", style: "GUITextBoxNoBorder", wrap: true,
                textAlignment: Alignment.TopLeft);
            textBox.OnTextChanged += (textBox, text) =>
            {
                string wrappedText = textBox.TextBlock.WrappedText.Value;
                int measuredHeight = (int)textBox.Font.MeasureString(wrappedText).Y;
                textBox.RectTransform.NonScaledSize =
                    (containingListBox.Content.Rect.Width,
                        Math.Max(measuredHeight, containingListBox.Content.Rect.Height));
                containingListBox.UpdateScrollBarSize();

                return true;
            };
            textBox.OnEnterPressed += (textBox, text) =>
            {
                string str = textBox.Text;
                int cursorPos = textBox.CaretIndex;
                textBox.Text = $"{str[..cursorPos]}\n{str[cursorPos..]}";
                textBox.CaretIndex = cursorPos + 1;

                return true;
            };
            textBox.Text = text;
            return textBox;
        }

        private static GUIDropDown DropdownEnum<T>(
            GUILayoutGroup parent, Func<T, LocalizedString> textFunc, T currentValue,
            Action<T> setter) where T : Enum
            => Dropdown(parent, textFunc, (T[])Enum.GetValues(typeof(T)), currentValue, setter);

        private static GUIDropDown Dropdown<T>(
            GUILayoutGroup parent, Func<T, LocalizedString> textFunc, IReadOnlyList<T> values, T currentValue,
            Action<T> setter, float heightScale = 1.0f)
        {
            var dropdown = new GUIDropDown(NewItemRectT(parent, heightScale));
            SwapDropdownValues(dropdown, textFunc, values, currentValue, setter);
            return dropdown;
        }

        private static void SwapDropdownValues<T>(
            GUIDropDown dropdown, Func<T, LocalizedString> textFunc, IReadOnlyList<T> values, T currentValue,
            Action<T> setter)
        {
            if (dropdown.ListBox.Content.Children.Any(c => !(c.UserData is T)))
            {
                throw new Exception("SwapValues must preserve the type of the dropdown's userdata");
            }

            dropdown.OnSelected = null;
            dropdown.ClearChildren();

            values.ForEach(v => dropdown.AddItem(text: textFunc(v), userData: v));
            dropdown.Select(values.IndexOf(currentValue));
            dropdown.OnSelected = (dd, obj) =>
            {
                setter((T)obj);
                return true;
            };
        }
        
        private static int Round(float v) => (int)MathF.Round(v);
        private static string Percentage(float v) => $"{Round(v * 100)}%";

        private struct ActionCarrier
        {
            public readonly Identifier Id;
            public readonly Action Action;
            public ActionCarrier(Identifier id, Action action)
            {
                Id = id;
                Action = action;
            }
        }

        private GUIComponent CreateActionCarrier(GUIComponent parent, Identifier id, Action action)
            => new GUIFrame(new RectTransform(Vector2.Zero, parent.RectTransform), style: null)
                { UserData = new ActionCarrier(id, action) };
    }
}
