#nullable enable

using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Allows accessing the element selected in the carousel in contexts where the type of T isn't known. 
    /// Pretty hacky, but I could not think of a better way to do this (<see cref="Networking.ServerSettings.NetPropertyData"/> in which this is used).
    /// </summary>
    public interface IGUISelectionCarouselAccessor
    {
        object? GetSelectedElement();
        void SelectElement(object? value);
    }

    /// <summary>
    /// An UI element that allows toggling through a set of options with buttons to the left and right
    /// </summary>
    public class GUISelectionCarousel<T> : GUIComponent, IGUISelectionCarouselAccessor
    {
        public record class Element(T value, LocalizedString text, LocalizedString toolTip);

        public delegate void OnValueChangedHandler(GUISelectionCarousel<T> carousel);
        public OnValueChangedHandler? OnValueChanged;

        public GUITextBlock TextBlock { get; private set; }

        public GUIButton RightButton { get; private set; }
        public GUIButton LeftButton { get; private set; }

        private readonly List<Element> elements = new List<Element>();

        private readonly GUILayoutGroup layoutGroup;

        public Element? SelectedElement { get; private set; }
        public T? SelectedValue => SelectedElement == null ? default : SelectedElement.value;
        public LocalizedString SelectedText => SelectedElement?.text ?? string.Empty;

        public override bool Enabled 
        { 
            get => base.Enabled; 
            set
            {
                base.Enabled = RightButton.Enabled = LeftButton.Enabled = TextBlock.Enabled = value;
            }
        }


        public override Color Color
        {
            get { return color; }
            set
            {
                color = value;
                TextBlock.Color = color;
            }
        }

        public Color TextColor
        {
            get { return TextBlock.TextColor; }
            set { TextBlock.TextColor = value; }
        }

        public override Color HoverColor
        {
            get => base.HoverColor;
            set
            {
                base.HoverColor = value;
                TextBlock.HoverColor = value;
            }
        }

        public GUISelectionCarousel(RectTransform rectT, string style = "", params (T value, LocalizedString text)[] newElements) : base(style, rectT)
        {
            layoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, rectT), isHorizontal: true, childAnchor: Anchor.CenterLeft) 
            { 
                RelativeSpacing = 0.05f, 
                Stretch = true 
            };

            LeftButton = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), layoutGroup.RectTransform), style: "GUIButtonToggleLeft");
            GUIStyle.Apply(LeftButton, "LeftButton", this);
            TextBlock = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), layoutGroup.RectTransform), "", textAlignment: Alignment.Center, style: "GUITextBox");
            GUIStyle.Apply(TextBlock, "TextBlock", this);
            RightButton = new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), layoutGroup.RectTransform), style: "GUIButtonToggleRight");
            GUIStyle.Apply(RightButton, "RightButton", this);

            RightButton.OnClicked += (btn, userData) =>
            {
                if (elements.Count < 2) { return false; }
                if (SelectedElement == null)
                {
                    SelectElement(elements.First());
                }
                else
                {
                    int newIndex = (elements.IndexOf(SelectedElement) + 1) % elements.Count;
                    SelectElement(elements[newIndex]);
                }
                return true;
            };
            LeftButton.OnClicked += (btn, userData) =>
            {
                if (elements.Count < 2) { return false; }
                if (SelectedElement == null)
                {
                    SelectElement(elements.First());
                }
                else
                {
                    int newIndex = MathUtils.PositiveModulo((elements.IndexOf(SelectedElement) - 1), elements.Count);
                    SelectElement(elements[newIndex]);
                }
                return true;
            };

            if (newElements != null && newElements.Any()) 
            { 
                SetElements(newElements);
            }
        }

        public object? GetSelectedElement()
        {
            return SelectedValue;
        }

        /// <summary>
        /// Select the element whose value matches the specified value. If null, deselects the currently selected element.
        /// </summary>
        public void SelectElement(object? value)
        {
            if (value == null)
            {
                SelectElement(null);
                return;
            }
            if (elements.FirstOrDefault(e => value.Equals(e.value)) is { } element)
            {
                SelectElement(element);
            }
        }

        public void SelectElement(Element? element)
        {
            SelectedElement = element;
            TextBlock.Text = element?.text ?? string.Empty;
            TextBlock.ToolTip = element?.toolTip ?? string.Empty;
            OnValueChanged?.Invoke(this);
        }

        /// <summary>
        /// Clears all existing elements from the carousels and adds the specified new elements to it
        /// </summary>
        public void SetElements(params (T value, LocalizedString text)[] elements)
        {
            this.elements.Clear();
            foreach ((T value, LocalizedString text) in elements)
            {
                AddElement(value, text);
            }
        }

        /// <summary>
        /// Clears all existing elements from the carousels and adds the specified new elements to it
        /// </summary>
        public void SetElements(params (T value, LocalizedString text, LocalizedString toolTip)[] elements)
        {
            this.elements.Clear();
            foreach ((T value, LocalizedString text, LocalizedString toolTip) in elements)
            {
                AddElement(value, text, toolTip);
            }
        }

        public void AddElement(T value, LocalizedString text, LocalizedString? tooltip = null)
        {
            var newElement = new Element(value, text, tooltip ?? string.Empty);
            elements.Add(newElement);
            if (SelectedElement == null)
            {
                SelectElement(newElement);
            }
        }
    }
}
