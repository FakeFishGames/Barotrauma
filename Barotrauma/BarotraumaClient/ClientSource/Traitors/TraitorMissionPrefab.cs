using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class TraitorMissionPrefab : Prefab
    {
        public static readonly PrefabCollection<TraitorMissionPrefab> Prefabs = new PrefabCollection<TraitorMissionPrefab>();

        public readonly Sprite Icon;
        public readonly Color IconColor;

        public TraitorMissionPrefab(ContentXElement element, TraitorMissionsFile file) : base(file, element)
        {
            foreach (var subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("icon", StringComparison.OrdinalIgnoreCase))
                {
                    Icon = new Sprite(subElement);
                    IconColor = subElement.GetAttributeColor("color", Color.White);
                }
            }
        }

        public override void Dispose()
        {
            Icon?.Remove();
        }
    }
}
