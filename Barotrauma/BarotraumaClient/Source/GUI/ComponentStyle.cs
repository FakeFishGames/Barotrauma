using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class GUIComponentStyle
    {
        public readonly Vector4 Padding;

        public readonly Color Color;

        public readonly Color textColor;

        public readonly Color HoverColor;
        public readonly Color SelectedColor;
        public readonly Color PressedColor;

        public readonly Color OutlineColor;

        public readonly XElement Element;

        public readonly Dictionary<GUIComponent.ComponentState, List<UISprite>> Sprites;
        
        public Dictionary<string, GUIComponentStyle> ChildStyles;

        public GUIComponentStyle(XElement element)
        {
            Element = element;

            Sprites = new Dictionary<GUIComponent.ComponentState, List<UISprite>>();
            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
            {
                Sprites[state] = new List<UISprite>();
            }

            ChildStyles = new Dictionary<string, GUIComponentStyle>();

            Padding = element.GetAttributeVector4("padding", Vector4.Zero);

            Color = element.GetAttributeColor("color", Color.Transparent);
            textColor = element.GetAttributeColor("textcolor", Color.Black);
            HoverColor = element.GetAttributeColor("hovercolor", Color);
            SelectedColor = element.GetAttributeColor("selectedcolor", Color);
            PressedColor = element.GetAttributeColor("pressedcolor", Color);
            OutlineColor = element.GetAttributeColor("outlinecolor", Color.Transparent);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        UISprite newSprite = new UISprite(subElement);

                        GUIComponent.ComponentState spriteState = GUIComponent.ComponentState.None;
                        if (subElement.Attribute("state") != null)
                        {
                            string stateStr = subElement.GetAttributeString("state", "None");
                            Enum.TryParse(stateStr, out spriteState);
                            Sprites[spriteState].Add(newSprite);
                        }
                        else
                        {
                            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
                            {
                                Sprites[state].Add(newSprite);
                            }
                        }
                        break;
                    default:
                        ChildStyles.Add(subElement.Name.ToString().ToLowerInvariant(), new GUIComponentStyle(subElement));
                        break;
                }
            }
        }
    }
}
