using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationType
    {
        private static List<LocationType> list = new List<LocationType>();
        //sum of the commonness-values of each location type
        private static int totalWeight;

        private string name;

        private int commonness;

        private List<string> nameFormats;

        private Sprite symbolSprite;

        private Sprite backGround;

        public bool HasHireableCharacters
        {
            get;
            private set;
        }
        
        public string Name
        {
            get { return name; }
        }
        
        public List<string> NameFormats
        {
            get { return nameFormats; }
        }

        public Sprite Sprite
        {
            get { return symbolSprite; }
        }

        public Sprite Background
        {
            get { return backGround; }
        }

        private LocationType(XElement element)
        {
            name = element.Name.ToString();

            commonness = ToolBox.GetAttributeInt(element, "commonness", 1);
            totalWeight += commonness;

            HasHireableCharacters = ToolBox.GetAttributeBool(element, "hireablecharacters", false);

            nameFormats = new List<string>();
            foreach (XAttribute nameFormat in element.Element("nameformats").Attributes())
            {
                nameFormats.Add(nameFormat.Value);
            }

            string spritePath = ToolBox.GetAttributeString(element, "symbol", "Content/Map/beaconSymbol.png");
            symbolSprite = new Sprite(spritePath, new Vector2(0.5f, 0.5f));

            string backgroundPath = ToolBox.GetAttributeString(element, "background", "");
            backGround = new Sprite(backgroundPath, Vector2.Zero);
            //sprite.Origin = ;

        }

        public static LocationType Random()
        {
            Debug.Assert(list.Count > 0, "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            int randInt = Rand.Int(totalWeight, false);

            foreach (LocationType type in list)
            {
                if (randInt < type.commonness) return type;
                randInt -= type.commonness;
            }

            return null;
        }

        public static void Init(string file)
        {
            XDocument doc = ToolBox.TryLoadXml(file);

            if (doc==null)
            {
                return;
            }

            foreach (XElement element in doc.Root.Elements())
            {
                LocationType locationType = new LocationType(element);
                list.Add(locationType);
            }
        }
    }
}
