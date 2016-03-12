using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class BackgroundSpritePrefab
    {
        public readonly Sprite Sprite;

        public readonly Alignment Alignment;

        public readonly int Commonness;

        public BackgroundSpritePrefab(XElement element)
        {
            string alignmentStr = ToolBox.GetAttributeString(element, "alignment", "BottomCenter");

            if (!Enum.TryParse(alignmentStr, out Alignment)) Alignment = Alignment.BottomCenter;

            Commonness = ToolBox.GetAttributeInt(element, "commonness", 1);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "sprite") continue;

                Sprite = new Sprite(subElement);
                break;
            }
        }

    }
}
