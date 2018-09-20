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

        public readonly Dictionary<GUIComponent.ComponentState, List<UISprite>> Sprites;
        
        public Dictionary<string, GUIComponentStyle> ChildStyles;

        public GUIComponentStyle(XElement element)
        {
            Sprites = new Dictionary<GUIComponent.ComponentState, List<UISprite>>();
            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
            {
                Sprites[state] = new List<UISprite>();
            }

            ChildStyles = new Dictionary<string, GUIComponentStyle>();

            Padding = element.GetAttributeVector4("padding", Vector4.Zero);

            Vector4 colorVector = element.GetAttributeVector4("color", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            Color = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("textcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            textColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("hovercolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            HoverColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("selectedcolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            SelectedColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("pressedcolor", new Vector4(1, 1, 1, 1));
            PressedColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = element.GetAttributeVector4("outlinecolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            OutlineColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);
            
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
