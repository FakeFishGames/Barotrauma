using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class MissionPrefab : PrefabWithUintIdentifier
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

        public bool DisplayTargetHudIcons
        {
            get;
            private set;
        }

        public float HudIconMaxDistance
        {
            get;
            private set;
        }

        public Sprite HudIcon
        {
            get
            {
                return hudIcon ?? Icon;
            }
        }

        public Color HudIconColor
        {
            get
            {
                return hudIconColor ?? IconColor;
            }
        } 

        private Sprite hudIcon;
        private Color? hudIconColor;

        partial void InitProjSpecific(ContentXElement element)
        {
            DisplayTargetHudIcons = element.GetAttributeBool("displaytargethudicons", false);
            HudIconMaxDistance = element.GetAttributeFloat("hudiconmaxdistance", 1000.0f);
            foreach (var subElement in element.Elements())
            {
                string name = subElement.Name.ToString();
                if (name.Equals("icon", StringComparison.OrdinalIgnoreCase))
                {
                    Icon = new Sprite(subElement);
                    IconColor = subElement.GetAttributeColor("color", Color.White);
                }
                else if (name.Equals("hudicon", StringComparison.OrdinalIgnoreCase))
                {
                    hudIcon = new Sprite(subElement);
                    hudIconColor = subElement.GetAttributeColor("color");
                }
            }
        }

        partial void DisposeProjectSpecific()
        {
            Icon?.Remove();
        }
    }
}
