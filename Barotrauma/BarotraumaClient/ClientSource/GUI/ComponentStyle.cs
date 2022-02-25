using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public enum SpriteFallBackState
    {
        None, 
        Hover, 
        Pressed, 
        Selected, 
        HoverSelected,
        Toggle
    }

    public class GUIComponentStyle : GUIPrefab
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

        public readonly Identifier Font;
        public readonly bool ForceUpperCase;

        public readonly Color OutlineColor;
        
        public readonly ContentXElement Element;

        public readonly Dictionary<GUIComponent.ComponentState, List<UISprite>> Sprites;

        public SpriteFallBackState FallBackState;

        public readonly GUIComponentStyle ParentStyle;
        public readonly Dictionary<Identifier, GUIComponentStyle> ChildStyles;

        public static GUIComponentStyle FromHierarchy(IReadOnlyList<Identifier> hierarchy)
        {
            if (hierarchy is null || hierarchy.None()) { return null; }
            GUIStyle.ComponentStyles.TryGet(hierarchy[0], out GUIComponentStyle style);
            for (int i = 1; i < hierarchy.Count; i++)
            {
                if (style is null) { return null; }
                style.ChildStyles.TryGetValue(hierarchy[i], out style);
            }
            return style;
        }

        public static Identifier[] ToHierarchy(GUIComponentStyle style)
        {
            List<Identifier> ids = new List<Identifier>();
            while (style != null)
            {
                ids.Insert(0, style.Identifier);
                style = style.ParentStyle;
            }

            return ids.ToArray();
        }

        public readonly string Name;

        public int? Width { get; private set; }
        public int? Height { get; private set; }

        public GUIComponentStyle(ContentXElement element, UIStyleFile file, GUIComponentStyle parent = null) : base(element, file)
        {
            Name = element.Name.LocalName;

            Element = element;

            Sprites = new Dictionary<GUIComponent.ComponentState, List<UISprite>>();
            foreach (GUIComponent.ComponentState state in Enum.GetValues(typeof(GUIComponent.ComponentState)))
            {
                Sprites[state] = new List<UISprite>();
            }

            ParentStyle = parent;
            ChildStyles = new Dictionary<Identifier, GUIComponentStyle>();

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

            Font = element.GetAttributeIdentifier("font", ""); 
            ForceUpperCase = element.GetAttributeBool("forceuppercase", false);

            foreach (var subElement in element.Elements())
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
                        Identifier styleName = subElement.NameAsIdentifier();
                        if (ChildStyles.ContainsKey(styleName))
                        {
                            DebugConsole.ThrowError("UI style \"" + element.Name.ToString() + "\" contains multiple child styles with the same name (\"" + styleName + "\")!");
                            ChildStyles[styleName] = new GUIComponentStyle(subElement, file, this);
                        }
                        else
                        {
                            ChildStyles.Add(styleName, new GUIComponentStyle(subElement, file, this));
                        }
                        break;
                }
            }

            GetSize(element);
        }

        public Sprite GetDefaultSprite()
        {
            return GetSprite(GUIComponent.ComponentState.None);
        }
        public Sprite GetSprite(GUIComponent.ComponentState state)
        {
            return Sprites.ContainsKey(state) ? Sprites[state]?.First()?.Sprite : null;
        }

        public void GetSize(XElement element)
        {
            Point size = new Point(0, 0);
            foreach (var subElement in element.Elements())
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

        public override void Dispose() { }
    }
}
