using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundSpritePrefab
    {
        public readonly Sprite Sprite;

        public readonly Alignment Alignment;

        public readonly Vector2 Scale;

        public readonly bool AlignWithSurface;

        public readonly Vector2 RandomRotation;

        public readonly int Commonness;

        public BackgroundSpritePrefab(XElement element)
        {
            string alignmentStr = ToolBox.GetAttributeString(element, "alignment", "BottomCenter");

            if (!Enum.TryParse(alignmentStr, out Alignment)) Alignment = Alignment.BottomCenter;

            Commonness = ToolBox.GetAttributeInt(element, "commonness", 1);

            Scale.X = ToolBox.GetAttributeFloat(element, "minsize", 1.0f);
            Scale.Y = ToolBox.GetAttributeFloat(element, "maxsize", 1.0f);

            AlignWithSurface = ToolBox.GetAttributeBool(element, "alignwithsurface", false);

            RandomRotation = ToolBox.GetAttributeVector2(element, "randomrotation", Vector2.Zero);
            RandomRotation.X = MathHelper.ToRadians(RandomRotation.X);
            RandomRotation.Y = MathHelper.ToRadians(RandomRotation.Y);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;

                Sprite = new Sprite(subElement);
                break;
            }
        }

    }
}
