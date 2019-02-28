using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    class LocationConnection
    {
        public Biome Biome;

        public float Difficulty;

        public List<Vector2[]> CrackSegments;

        public bool Passed;

        public Level Level { get; set; }

        public Vector2 CenterPos
        {
            get
            {
                return (Locations[0].MapPosition + Locations[1].MapPosition) / 2.0f;
            }
        }

        public Location[] Locations { get; private set; }

        public float Length
        {
            get;
            private set;
        }

        public LocationConnection(Location location1, Location location2)
        {
            Locations = new Location[] { location1, location2 };
            
            Length = Vector2.Distance(location1.MapPosition, location2.MapPosition);
        }

        public Location OtherLocation(Location location)
        {
            if (Locations[0] == location)
            {
                return Locations[1];
            }
            else if (Locations[1] == location)
            {
                return Locations[0];
            }
            else
            {
                return null;
            }
        }
    }
}
