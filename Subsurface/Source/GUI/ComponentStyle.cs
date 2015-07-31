using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class GUIComponentStyle
    {
        public readonly Vector4 Padding;

        public readonly Color Color;

        public readonly Color textColor;

        public readonly Color HoverColor;
        public readonly Color SelectedColor;

        public readonly Color OutlineColor;

        public readonly List<Sprite> Sprites;


        public GUIComponentStyle(XElement element)
        {
            Sprites = new List<Sprite>();

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
                        Sprites.Add(new Sprite(subElement));
                        break;
                }
            }
        }
    }
}
