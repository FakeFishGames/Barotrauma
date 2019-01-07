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

        private HireManager hireManager;
        public HireManager HireManager
        {
            get { return hireManager; }
        }

        private string baseName;
        private int nameFormatIndex;

        public bool Discovered;

        public int TypeChangeTimer;

        public string Name
        {
            get { return name; }
        }

        public Vector2 MapPosition
        {
            get { return mapPosition; }
        }
        
        public LocationType Type
        {
            get { return type; }
        }

        public Location(Vector2 mapPosition, int? zone)
        {
            this.type = LocationType.Random("", zone);
            this.name = RandomName(type);
            this.mapPosition = mapPosition;
            
            if (type.HasHireableCharacters)
            {
                hireManager = new HireManager();
                hireManager.GenerateCharacters(this, HireManager.MaxAvailableCharacters);
            }

            Connections = new List<LocationConnection>();
        }

        public static Location CreateRandom(Vector2 position, int? zone)
        {
            return new Location(position, zone);        
        }

        public void ChangeType(LocationType newType)
        {
            if (newType == type) return;

            type = newType;
            name = type.NameFormats[nameFormatIndex % type.NameFormats.Count].Replace("[name]", baseName);
            
            if (type.HasHireableCharacters)
            {
                hireManager = new HireManager();
                hireManager.GenerateCharacters(this, HireManager.MaxAvailableCharacters);
            }
        }

        private string RandomName(LocationType type)
        {
            baseName = type.GetRandomName();
            nameFormatIndex = Rand.Int(type.NameFormats.Count, Rand.RandSync.Server);
            return type.NameFormats[nameFormatIndex].Replace("[name]", baseName);
        }
    }
}
