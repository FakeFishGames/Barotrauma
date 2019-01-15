using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Location
    {
        public List<LocationConnection> Connections;

        private string baseName;
        private int nameFormatIndex;

        public bool Discovered;

        public int TypeChangeTimer;

        public string Name { get; private set; }

        public Vector2 MapPosition { get; private set; }

        public LocationType Type { get; private set; }

        public Location(Vector2 mapPosition, int? zone)
        {
            this.Type = LocationType.Random("", zone);
            this.Name = RandomName(Type);
            this.MapPosition = mapPosition;

            Connections = new List<LocationConnection>();
        }

        public static Location CreateRandom(Vector2 position, int? zone)
        {
            return new Location(position, zone);        
        }

        public void ChangeType(LocationType newType)
        {
            if (newType == Type) return;

            Type = newType;
            Name = Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", baseName);
        }

        private string RandomName(LocationType type)
        {
            baseName = type.GetRandomName();
            nameFormatIndex = Rand.Int(type.NameFormats.Count, Rand.RandSync.Server);
            return type.NameFormats[nameFormatIndex].Replace("[name]", baseName);
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();
    }
}
