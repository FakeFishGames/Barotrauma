using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MissionPrefab
    {
        public Sprite Icon
        {
            get;
            private set;
        }

        public Color IconColor
        {
            get;
            private set;
        }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("icon", StringComparison.OrdinalIgnoreCase)) { continue; }    
                Icon = new Sprite(subElement);
                IconColor = subElement.GetAttributeColor("color", Color.White);
            }
        }
    }
}
