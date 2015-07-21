using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{

    class Location
    {
        string name;

        Vector2 mapPosition;

        LocationType type;

        public List<LocationConnection> connections;

        public string Name
        {
            get { return name; }
        }

        public Vector2 MapPosition
        {
            get { return mapPosition; }
        }

        public Location(Vector2 mapPosition)
        {
            this.name = RandomName(LocationType.Random());

            this.mapPosition = mapPosition;

            connections = new List<LocationConnection>();
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
