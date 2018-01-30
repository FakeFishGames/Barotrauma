using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Location
    {
        private string name;

        private Vector2 mapPosition;

        private LocationType type;

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

        public Location(Vector2 mapPosition, int? zone)
        {
            this.type = LocationType.Random("", zone);
            this.name = RandomName(type);
            this.mapPosition = mapPosition;

#if CLIENT
            if (type.HasHireableCharacters)
            {
                hireManager = new HireManager();
                hireManager.GenerateCharacters(this, HireManager.MaxAvailableCharacters);
            }
#endif

            Connections = new List<LocationConnection>();
        }

        public static Location CreateRandom(Vector2 position, int? zone)
        {
            return new Location(position, zone);        
        }

        private string RandomName(LocationType type)
        {
            string randomName = ToolBox.GetRandomLine("Content/Map/locationNames.txt");
            int nameFormatIndex = Rand.Int(type.NameFormats.Count, Rand.RandSync.Server);
            return type.NameFormats[nameFormatIndex].Replace("[name]", randomName);
        }
    }
}
