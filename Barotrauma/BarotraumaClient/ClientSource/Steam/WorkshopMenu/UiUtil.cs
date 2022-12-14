#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma.Steam
{
    abstract partial class WorkshopMenu
    {
        protected static RectTransform NewItemRectT(GUILayoutGroup parent, float heightScale = 1.0f)
            => new RectTransform((1.0f, 0.06f * heightScale), parent.RectTransform, Anchor.CenterLeft);

        protected static void Spacer(GUILayoutGroup parent, float height = 0.03f)
        {
            new GUIFrame(new RectTransform((1.0f, height), parent.RectTransform, Anchor.CenterLeft), style: null);
        }

        protected static GUITextBlock Label(GUILayoutGroup parent, LocalizedString str, GUIFont font, float heightScale = 1.0f)
        {
            return new GUITextBlock(NewItemRectT(parent, heightScale), str, font: font);
        }

        protected static GUITextBox ScrollableTextBox(GUILayoutGroup parent, float heightScale, string text)
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

        protected static GUIDropDown DropdownEnum<T>(
            GUILayoutGroup parent, Func<T, LocalizedString> textFunc, T currentValue,
            Action<T> setter) where T : Enum
            => Dropdown(parent, textFunc, (T[])Enum.GetValues(typeof(T)), currentValue, setter);

        protected static GUIDropDown Dropdown<T>(
            GUILayoutGroup parent, Func<T, LocalizedString> textFunc, IReadOnlyList<T> values, T currentValue,
            Action<T> setter, float heightScale = 1.0f)
        {
            var dropdown = new GUIDropDown(NewItemRectT(parent, heightScale));
            SwapDropdownValues(dropdown, textFunc, values, currentValue, setter);
            return dropdown;
        }

        protected static void SwapDropdownValues<T>(
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
        
        protected static int Round(float v) => (int)MathF.Round(v);
        protected static string Percentage(float v) => $"{Round(v * 100)}";

        protected struct ActionCarrier
        {
            public readonly Identifier Id;
            public readonly Action Action;
            public ActionCarrier(Identifier id, Action action)
            {
                Id = id;
                Action = action;
            }
        }

        protected GUIComponent CreateActionCarrier(GUIComponent parent, Identifier id, Action action)
            => new GUIFrame(new RectTransform(Vector2.Zero, parent.RectTransform), style: null)
                { UserData = new ActionCarrier(id, action) };

        protected GUITextBox CreateSearchBox(RectTransform searchRectT)
        {
            var searchHolder = new GUIFrame(searchRectT, style: null);
            var searchBox = new GUITextBox(new RectTransform(Vector2.One, searchHolder.RectTransform), "", createClearButton: true);
            var searchTitle = new GUITextBlock(new RectTransform(Vector2.One, searchHolder.RectTransform) {Anchor = Anchor.TopLeft},
                textColor: Color.DarkGray * 0.6f,
                text: TextManager.Get("Search") + "...",
                textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };
            new GUICustomComponent(new RectTransform(Vector2.Zero, searchHolder.RectTransform), onUpdate:
                (f, component) =>
                {
                    searchTitle.RectTransform.NonScaledSize = searchBox.Frame.RectTransform.NonScaledSize;
                });
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = searchBox.Text.IsNullOrWhiteSpace(); };

            searchBox.OnTextChanged += (sender, str) =>
            {
                UpdateModListItemVisibility();
                return true;
            };
            return searchBox;
        }

        protected static void CreateModErrorInfo(ContentPackage mod, GUIComponent uiElement, GUITextBlock nameText)
        {
            uiElement.ToolTip = "";
            if (mod.FatalLoadErrors.Any())
            {
                const int maxErrorsToShow = 5;
                nameText.TextColor = GUIStyle.Red;
                uiElement.ToolTip =
                    TextManager.GetWithVariable("ContentPackageHasFatalErrors", "[packagename]", mod.Name)
                    + '\n' + string.Join('\n', mod.FatalLoadErrors.Take(maxErrorsToShow).Select(e => e.Message));
                if (mod.FatalLoadErrors.Length > maxErrorsToShow)
                {
                    uiElement.ToolTip += '\n' + TextManager.GetWithVariable("workshopitemdownloadprompttruncated", "[number]", (mod.FatalLoadErrors.Count() - maxErrorsToShow).ToString());
                }
            }

            if (mod.EnableError.IsSome())
            {
                nameText.TextColor = GUIStyle.Red;
                if (!uiElement.ToolTip.IsNullOrWhiteSpace()) { uiElement.ToolTip += "\n"; }
                uiElement.ToolTip += TextManager.GetWithVariable(
                    "ContentPackageEnableError", "[packagename]", mod.Name);
            }
        }
    }
}
