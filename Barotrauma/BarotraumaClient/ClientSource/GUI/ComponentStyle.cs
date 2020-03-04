using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum TransitionMode
    {
        Linear,
        Smooth,
        Smoother,
        EaseIn,
        EaseOut,
        Exponential
    }

    public enum SpriteFallBackState
    {
        None, 
        Hover, 
        Pressed, 
        Selected, 
        HoverSelected,
        Toggle
    }

    public class GUIComponentStyle
    {
        public readonly Vector4 Padding;

        public readonly Color Color;
        public readonly Color HoverColor;
        public readonly Color SelectedColor;
        public readonly Color PressedColor;
        public readonly Color DisabledColor;

        public readonly Color TextColor;
        public readonly Color HoverTextColor;
        public readonly Color SelectedTextColor;
        public readonly Color DisabledTextColor;

        public readonly float SpriteCrossFadeTime;
        public readonly float ColorCrossFadeTime;
        public readonly TransitionMode TransitionMode;

        public readonly string Font;
        public readonly bool ForceUpperCase;

        public readonly Color OutlineColor;
        
        public readonly XElement Element;

        public readonly Dictionary<GUIComponent.ComponentState, List<UISprite>> Sprites;

        public SpriteFallBackState FallBackState;
        
        public Dictionary<string, GUIComponentStyle> ChildStyles;

        public readonly GUIStyle Style;

        public readonly string Name;

        public int? Width { get; private set; }
        public int? Height { get; private set; }

        public GUIComponentStyle(XElement element, GUIStyle style)
        {
            Name = element.Name.LocalName;

            Style = style;
            Element = element;

            Sprites = new Dictionary<GUIComponent.ComponentState, List<UISprite>>();
            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
            {
                Sprites[state] = new List<UISprite>();
            }

            ChildStyles = new Dictionary<string, GUIComponentStyle>();

            Padding = element.GetAttributeVector4("padding", Vector4.Zero);
            
            Color = element.GetAttributeColor("color", Color.Transparent);
            HoverColor = element.GetAttributeColor("hovercolor", Color);
            SelectedColor = element.GetAttributeColor("selectedcolor", Color);
            DisabledColor = element.GetAttributeColor("disabledcolor", Color);
            PressedColor = element.GetAttributeColor("pressedcolor", Color);
            OutlineColor = element.GetAttributeColor("outlinecolor", Color.Transparent);

            TextColor = element.GetAttributeColor("textcolor", Color.Black);
            HoverTextColor = element.GetAttributeColor("hovertextcolor", TextColor);
            DisabledTextColor = element.GetAttributeColor("disabledtextcolor", TextColor);
            SelectedTextColor = element.GetAttributeColor("selectedtextcolor", TextColor);
            SpriteCrossFadeTime = element.GetAttributeFloat("spritefadetime", SpriteCrossFadeTime);
            ColorCrossFadeTime = element.GetAttributeFloat("colorfadetime", ColorCrossFadeTime);

            if (Enum.TryParse(element.GetAttributeString("colortransition", string.Empty), ignoreCase: true, out TransitionMode transition))
            {
                TransitionMode = transition;
            }
            if (Enum.TryParse(element.GetAttributeString("fallbackstate", GUIComponent.ComponentState.None.ToString()), ignoreCase: true, out SpriteFallBackState s))
            {
                FallBackState = s;
            }

            Font = element.GetAttributeString("font", ""); 
            ForceUpperCase = element.GetAttributeBool("forceuppercase", false);

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
                            //use the same sprite for Hover and HoverSelected if latter is not specified
                            if (spriteState == GUIComponent.ComponentState.HoverSelected && !Sprites.ContainsKey(GUIComponent.ComponentState.HoverSelected))
                            {
                                Sprites[GUIComponent.ComponentState.HoverSelected].Add(newSprite);
                            }
                        }
                        else
                        {
                            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
                            {
                                Sprites[state].Add(newSprite);
                            }
                        }
                        break;
                    case "size":
                        break;
                    default:
                        string styleName = subElement.Name.ToString().ToLowerInvariant();
                        if (ChildStyles.ContainsKey(styleName))
                        {
                            DebugConsole.ThrowError("UI style \"" + element.Name.ToString() + "\" contains multiple child styles with the same name (\"" + styleName + "\")!");
                            ChildStyles[styleName] = new GUIComponentStyle(subElement, style);
                        }
                        else
                        {
                            ChildStyles.Add(styleName, new GUIComponentStyle(subElement, style));
                        }
                        break;
                }
            }

            GetSize(element);
        }

        public void GetSize(XElement element)
        {
            Point size = new Point(0, 0);
            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("size", StringComparison.OrdinalIgnoreCase)) { continue; }
                Point maxResolution = subElement.GetAttributePoint("maxresolution", new Point(int.MaxValue, int.MaxValue));
                if (GameMain.GraphicsWidth <= maxResolution.X && GameMain.GraphicsHeight <= maxResolution.Y)
                {
                    size = new Point(
                        subElement.GetAttributeInt("width", 0), 
                        subElement.GetAttributeInt("height", 0));
                    break;
                }
            }
            if (size.X > 0) { Width = size.X; }
            if (size.Y > 0) { Height = size.Y; }
        }
    }
}
