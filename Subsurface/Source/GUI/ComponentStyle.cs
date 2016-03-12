using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    public class UISprite
    {
        public Sprite Sprite
        {
            get;
            private set;
        }

        public bool Tile
        {
            get;
            private set;
        }

        public bool MaintainAspectRatio
        {
            get;
            private set;
        }

        public UISprite(Sprite sprite, bool tile, bool maintainAspectRatio)
        {
            Sprite = sprite;
            Tile = tile;
            MaintainAspectRatio = maintainAspectRatio;
        }
    }

    public class GUIComponentStyle
    {
        public readonly Vector4 Padding;

        public readonly Color Color;

        public readonly Color textColor;

        public readonly Color HoverColor;
        public readonly Color SelectedColor;

        public readonly Color OutlineColor;

        public readonly List<UISprite> Sprites;

        public readonly bool TileSprites;


        public GUIComponentStyle(XElement element)
        {
            Sprites = new List<UISprite>();

            Padding = ToolBox.GetAttributeVector4(element, "padding", Vector4.Zero);

            Vector4 colorVector = ToolBox.GetAttributeVector4(element, "color", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            Color = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(element, "textcolor", new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            textColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(element, "hovercolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            HoverColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(element, "selectedcolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            SelectedColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);

            colorVector = ToolBox.GetAttributeVector4(element, "outlinecolor", new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            OutlineColor = new Color(colorVector.X, colorVector.Y, colorVector.Z, colorVector.W);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "sprite":
                        Sprite sprite = new Sprite(subElement);
                        bool maintainAspect = ToolBox.GetAttributeBool(subElement, "maintainaspectratio",false);
                        bool tile = ToolBox.GetAttributeBool(subElement, "tile", true);

                        Sprites.Add(new UISprite(sprite, tile, maintainAspect));
                        break;
                }
            }
        }
    }
}
