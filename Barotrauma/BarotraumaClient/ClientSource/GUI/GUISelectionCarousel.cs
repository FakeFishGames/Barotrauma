#nullable enable

using System;
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
        
        /// <summary>
        /// Are there some conditions for selecting a particular element?
        /// </summary>
        public Func<T, bool>? ElementSelectionCondition { get; set; }

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
            
            RightButton.OnClicked += (_, _) => SelectNextValidElement();
            LeftButton.OnClicked += (_, _) => SelectNextValidElement(directionLeft: true);

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
            var matchingElement = elements.Where(e => value.Equals(e.value)) // selection is in the set of possible values
                .FirstOrDefault(e => ElementSelectionCondition == null || ElementSelectionCondition(e.value)); // selection matches extra conditions, if any
            if (matchingElement != null)
            {
                SelectElement(matchingElement);
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
        /// <summary>
        /// Refresh the current selection, for example if there are conditions for which elements are valid, and those might have changed
        /// </summary>
        public void Refresh()
        {
            if (SelectedElement != null)
            {
                if (ElementSelectionCondition == null || ElementSelectionCondition(SelectedElement.value))
                {
                    return;
                }
            }
            
            SelectElement(elements.FirstOrDefault(e => ElementSelectionCondition == null || ElementSelectionCondition(e.value)));
        }

        private bool SelectNextValidElement(bool directionLeft = false)
        {
            if (elements.Count < 2) { return false; }
            
            // Try to find a valid next/previous element
            int currentIndex = SelectedElement == null ? -1 : elements.IndexOf(SelectedElement);
            int newIndex = currentIndex;
            for (int i = 0; i < elements.Count; i++)
            {
                newIndex = directionLeft ? MathUtils.PositiveModulo((newIndex - 1), elements.Count) : (newIndex + 1) % elements.Count;
                if (ElementSelectionCondition == null || ElementSelectionCondition(elements[newIndex].value))
                {
                    SelectElement(elements[newIndex]);
                    return true;
                }
            }
            
            // No valid elements found
            SelectElement(null);
            return true;
        }
    }
}
