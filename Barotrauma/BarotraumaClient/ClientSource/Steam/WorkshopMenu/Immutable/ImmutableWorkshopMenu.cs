#nullable enable
using System;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma.Steam
{
    sealed class ImmutableWorkshopMenu : WorkshopMenu
    {
        private readonly GUIListBox regularList;
        private readonly GUITextBox filterBox;
        
        public ImmutableWorkshopMenu(GUIFrame parent) : base(parent)
        {
            var mainLayout
                = new GUILayoutGroup(new RectTransform((0.5f, 1.0f), parent.RectTransform, Anchor.Center), isHorizontal: false);
            
            Label(mainLayout, TextManager.Get("enabledcore"), GUIStyle.SubHeadingFont);
            var coreBox = new GUIButton(
                NewItemRectT(mainLayout), style: "GUITextBoxNoIcon", text: ContentPackageManager.EnabledPackages.Core!.Name, textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false,
                CanBeSelected = false
            };
            coreBox.TextBlock.Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f);
            
            Label(mainLayout, TextManager.Get("enabledregular"), GUIStyle.SubHeadingFont);
            regularList = new GUIListBox(
                NewItemRectT(mainLayout, heightScale: 11f))
            {
                OnSelected = (component, o) => false,
                HoverCursor = CursorState.Default
            };
            foreach (var p in ContentPackageManager.EnabledPackages.Regular)
            {
                var regularBox = new GUITextBlock(
                    new RectTransform((1.0f, 0.07f), regularList.Content.RectTransform), text: p.Name)
                {
                    CanBeFocused = false,
                    UserData = p
                };
                if (p.Errors.Any())
                {
                    CreateModErrorInfo(p, regularBox, regularBox);
                    regularBox.CanBeFocused = true;
                }
            }
            filterBox = CreateSearchBox(mainLayout, width: 1.0f);

            Label(mainLayout, TextManager.Get("CannotChangeMods"), GUIStyle.Font);
        }

        protected override void UpdateModListItemVisibility()
        {
            string str = filterBox.Text;
            regularList.Content.Children
                .ForEach(c => c.Visible = str.IsNullOrWhiteSpace()
                                          || (c.UserData is ContentPackage p
                                              && p.Name.Contains(str, StringComparison.OrdinalIgnoreCase)));
        }
    }
}