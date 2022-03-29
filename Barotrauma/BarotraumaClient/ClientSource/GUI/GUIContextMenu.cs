#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    struct ContextMenuOption
    {
        public LocalizedString Label;
        public Action OnSelected;
        public ContextMenuOption[]? SubOptions;
        public bool IsEnabled;
        public LocalizedString Tooltip;


        public ContextMenuOption(string label, bool isEnabled, Action onSelected)
            : this(TextManager.Get(label).Fallback(label), isEnabled, onSelected) { }
        
        public ContextMenuOption(Identifier labelTag, bool isEnabled, Action onSelected)
            : this(TextManager.Get(labelTag), isEnabled, onSelected) { }
        
        // Creates a regular context menu
        public ContextMenuOption(LocalizedString label, bool isEnabled, Action onSelected)
        {
            Label = label;
            OnSelected = onSelected;
            IsEnabled = isEnabled;
            SubOptions = null;
            Tooltip = string.Empty;
        }

        // Creates a option with a sub context menu
        public ContextMenuOption(string label, bool isEnabled, params ContextMenuOption[] options): this(label, isEnabled, () => { })
        {
            SubOptions = options;
        }
    }

    internal class GUIContextMenu : GUIComponent
    {
        public static GUIContextMenu? CurrentContextMenu;

        private readonly Dictionary<ContextMenuOption, GUITextBlock> Options = new Dictionary<ContextMenuOption, GUITextBlock>();
        private GUIContextMenu? SubMenu;
        public readonly GUITextBlock? HeaderLabel;
        public GUITextBlock? ParentOption;

        /// <summary>
        /// Creates a context menu. This constructor does not make the context menu active.
        /// Use <see cref="CreateContextMenu(Barotrauma.ContextMenuOption[])"/> to make right click context menus. 
        /// </summary>
        /// <param name="position">Position at which to create the context menu</param>
        /// <param name="header">Header text</param>
        /// <param name="style">Background style</param>
        /// <param name="options">list of context menu options</param>
        public GUIContextMenu(Vector2? position, LocalizedString header, string style, params ContextMenuOption[] options) : base(style, new RectTransform(Point.Zero, GUI.Canvas))
        {
            Vector2 pos = position ?? PlayerInput.MousePosition;
            GUIFont headerFont = GUIStyle.SubHeadingFont;
            GUIFont font = GUIStyle.SmallFont; // font the context menu options use
            Vector4 padding = new Vector4(4), headerPadding = new Vector4(8);
            int horizontalPadding = (int) (padding.X + padding.Z), verticalPadding = (int) (padding.Y + padding.W);
            bool hasHeader = !header.IsNullOrWhiteSpace();

            //----------------------------------------------------------------------------------
            // Estimate the size of the context menu
            //----------------------------------------------------------------------------------

            Dictionary<ContextMenuOption, Vector2> optionsAndSizes = new Dictionary<ContextMenuOption, Vector2>();

            // estimate how big the context menu needs to be
            Point estimatedSize = new Point(horizontalPadding, verticalPadding);

            if (hasHeader)
            {
                InflateSize(ref estimatedSize, header, headerFont);
            }

            foreach (ContextMenuOption option in options)
            {
                Vector2 optionSize = InflateSize(ref estimatedSize, option.Label, font);
                optionsAndSizes.Add(option, optionSize);
            }

            // it's better to overestimate the size since it's going to be cropped anyways
            estimatedSize = estimatedSize.Multiply(1.2f);

            RectTransform.NonScaledSize = estimatedSize;
            RectTransform.AbsoluteOffset = pos.ToPoint();

            //----------------------------------------------------------------------------------
            // Construct the GUI elements
            //----------------------------------------------------------------------------------

            GUILayoutGroup background = new GUILayoutGroup(new RectTransform(Vector2.One, RectTransform, Anchor.Center));

            if (hasHeader)
            {
                HeaderLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), background.RectTransform), header, font: headerFont) { Padding = headerPadding };
            }

            GUIListBox optionList = new GUIListBox(new RectTransform(new Vector2(1f, hasHeader ? 0.8f : 1f), background.RectTransform), style: null)
            {
                AutoHideScrollBar = false,
                ScrollBarVisible = false,
                Padding = hasHeader ? new Vector4(4, 0, 4, 4) : padding
            };

            foreach (var (option, size) in optionsAndSizes)
            {
                GUITextBlock optionElement = new GUITextBlock(new RectTransform(size.ToPoint(), optionList.Content.RectTransform), option.Label, font: font)
                {
                    UserData = option,
                    Enabled = option.IsEnabled
                };
                Options.Add(option, optionElement);

                if (!option.Tooltip.IsNullOrWhiteSpace() && optionElement.Enabled)
                {
                    optionElement.ToolTip = option.Tooltip;
                }

                if (!option.IsEnabled)
                {
                    optionElement.TextColor *= 0.5f;
                }
            }

            //----------------------------------------------------------------------------------
            // Positioning and cropping the context menu
            //----------------------------------------------------------------------------------

            List<GUIComponent> children = optionList.Content.Children.ToList();

            // Resize all children to the size of their text
            foreach (GUITextBlock block in children.Where(c => c is GUITextBlock).Cast<GUITextBlock>())
            {
                block.RectTransform.NonScaledSize = new Point((int) (block.TextSize.X + (block.Padding.X + block.Padding.Z)), (int) (18 * GUI.Scale));
            }

            int largestWidth = children.Max(c => c.Rect.Width + horizontalPadding);

            // if the header is bigger than any of the options then overwrite
            if (HeaderLabel != null)
            {
                RectTransform headerTransform = HeaderLabel.RectTransform;
                headerTransform.MinSize = new Point((int) (HeaderLabel.TextSize.X + (headerPadding.X + headerPadding.Z)), headerTransform.NonScaledSize.Y);
                if (largestWidth < headerTransform.MinSize.X)
                {
                    largestWidth = headerTransform.MinSize.X;
                }
            }

            // resize all children to the size of the longest element
            foreach (GUIComponent c in children)
            {
                c.RectTransform.MinSize = new Point(largestWidth, c.Rect.Height);
            }

            // the cropped size of the option list
            Point newSize = new Point(largestWidth, children.Sum(c => c.Rect.Height) + verticalPadding);
            // resize the menu itself taking into account the option menus relative Y size
            RectTransform.NonScaledSize = new Point(newSize.X, (int) (newSize.Y / optionList.RectTransform.RelativeSize.Y));
            optionList.RectTransform.NonScaledSize = newSize;

            // move the context menu if it would go outside of screen
            if (RectTransform.Rect.Bottom > GameMain.GraphicsHeight)
            {
                Rectangle rect = RectTransform.Rect;
                RectTransform.AbsoluteOffset = new Point(rect.X, rect.Y - rect.Height);
            }

            if (RectTransform.Rect.Right > GameMain.GraphicsWidth)
            {
                Rectangle rect = RectTransform.Rect;
                RectTransform.AbsoluteOffset = new Point(rect.X - rect.Width, rect.Y);
            }

            background.Recalculate();

            optionList.OnSelected = OnSelected;
        }

        public static GUIContextMenu CreateContextMenu(params ContextMenuOption[] options) => CreateContextMenu(PlayerInput.MousePosition, string.Empty, null, options);

        public static GUIContextMenu CreateContextMenu(Vector2? pos, LocalizedString header, Color? headerColor, params ContextMenuOption[] options)
        {
            GUIContextMenu menu = new GUIContextMenu(pos,header, "GUIToolTip", options);
            if (headerColor != null)
            {
                menu.HeaderLabel?.OverrideTextColor(headerColor.Value);
            }
            CurrentContextMenu = menu;
            return menu;
        }
        
        private bool OnSelected(GUIComponent _, object data)
        {
            if (data is ContextMenuOption option && option.IsEnabled)
            {
                CurrentContextMenu = null;
                option.OnSelected();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Inflates a point by the size of the text
        /// </summary>
        /// <param name="size">Pint to resize</param>
        /// <param name="label">String whose size to inflate by</param>
        /// <param name="font">What font to use</param>
        /// <returns>The size of the text</returns>
        private Vector2 InflateSize(ref Point size, LocalizedString label, ScalableFont font)
        {
            Vector2 textSize = font.MeasureString(label);
            size.X = Math.Max((int) Math.Ceiling(textSize.X), size.X);
            size.Y += (int) Math.Ceiling(textSize.Y);
            return textSize;
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // keep the parent highlighted
            if (ParentOption != null)
            {
                ParentOption.State = ComponentState.Hover;
            }

            if (SubMenu != null && !SubMenu.IsMouseOver())
            {
                SubMenu = null;
                return;
            }

            foreach (var (option, textBlock) in Options)
            {
                // Create a new sub context menu if hovering over an option with sub options
                if (GUI.MouseOn != textBlock) { continue; }
                if (option.IsEnabled && option.SubOptions is { } subOptions && subOptions.Any())
                {
                    Vector2 subMenuPos = new Vector2(textBlock.MouseRect.Right + 4, textBlock.MouseRect.Y);
                    SubMenu = new GUIContextMenu(subMenuPos, "", "GUIToolTip", subOptions)
                    {
                        ParentOption = textBlock
                    };
                }
            }
        }

        /// <summary>
        /// Checks if the mouse cursor is over this context menu or any of its sub menus
        /// </summary>
        /// <returns></returns>
        private bool IsMouseOver()
        {
            Rectangle expandedRect = Rect;
            expandedRect.Inflate(20, 20);

            bool isMouseOn = expandedRect.Contains(PlayerInput.MousePosition);

            if (ParentOption != null)
            {
                isMouseOn |= GUI.MouseOn == ParentOption;
            }

            // Recursively check sub context menus
            if (!isMouseOn && SubMenu != null)
            {
                isMouseOn = SubMenu.IsMouseOver();
            }

            return isMouseOn;
        }

        public override void AddToGUIUpdateList(bool ignoreChildren = false, int order = 0)
        {
            base.AddToGUIUpdateList(ignoreChildren, order);
            SubMenu?.AddToGUIUpdateList();
        }

        public static void AddActiveToGUIUpdateList()
        {
            if (CurrentContextMenu != null && !CurrentContextMenu.IsMouseOver())
            {
                CurrentContextMenu = null;
            }

            CurrentContextMenu?.AddToGUIUpdateList();
        }
    }
}