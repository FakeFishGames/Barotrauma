using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class TraitorMissionPrefab
    {
        public static readonly List<TraitorMissionPrefab> List = new List<TraitorMissionPrefab>();

        public readonly string Identifier;

        public readonly Sprite Icon;
        public readonly Color IconColor;

        public static void Init()
        {
            var files = GameMain.Instance.GetFilesOfType(ContentType.TraitorMissions);
            foreach (string file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc?.Root == null) { continue; }

                foreach (XElement element in doc.Root.Elements())
                {
                    List.Add(new TraitorMissionPrefab(element));
                }
            }
        }

        private TraitorMissionPrefab(XElement element)
        {
            Identifier = element.GetAttributeString("identifier", "");
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() == "icon")
                {
                    Icon = new Sprite(subElement);
                    IconColor = subElement.GetAttributeColor("color", Color.White);
                }
            }
        }
    }
}
