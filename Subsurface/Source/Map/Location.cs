using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{

    class Location
    {
        private string name;

        private Vector2 mapPosition;

        private LocationType type;

        private HireManager hireManager;

        public List<LocationConnection> Connections;

        public string Name
        {
            get { return name; }
        }

        public Vector2 MapPosition
        {
            get { return mapPosition; }
        }

        public bool Discovered;
        
        public LocationType Type
        {
            get { return type; }
        }

        public HireManager HireManager
        {
            get { return hireManager; }
        }

        public Location(Vector2 mapPosition)
        {
            this.type = LocationType.Random();

            this.name = RandomName(type);

            this.mapPosition = mapPosition;

            if (type.HasHireableCharacters)
            {
                hireManager = new HireManager();
                hireManager.GenerateCharacters(Character.HumanConfigFile, 10);
            }

            Connections = new List<LocationConnection>();
        }

        public static Location CreateRandom(Vector2 position)
        {
            return new Location(position);        
        }

        private string RandomName(LocationType type)
        {
            string name = ToolBox.GetRandomLine("Content/Map/locationNames.txt");
            int nameFormatIndex = Rand.Int(type.NameFormats.Count, false);
            return type.NameFormats[nameFormatIndex].Replace("[name]", name);
        }
    }
}
