using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class LocationConnection
    {
        private Location[] locations;
        private Level level;

        public Biome Biome;

        public float Difficulty;

        public List<Vector2[]> CrackSegments;

        public bool Passed;

        public Level Level
        {
            get { return level; }
            set { level = value; }
        }

        public Vector2 CenterPos
        {
            get
            {
                return (locations[0].MapPosition + locations[1].MapPosition) / 2.0f;
            }
        }

        public Location[] Locations
        {
            get { return locations; }
        }

        public float Length
        {
            get;
            private set;
        }

        public LocationConnection(Location location1, Location location2)
        {
            locations = new Location[] { location1, location2 };
            
            Length = Vector2.Distance(location1.MapPosition, location2.MapPosition);
        }

        public Location OtherLocation(Location location)
        {
            if (locations[0] == location)
            {
                return locations[1];
            }
            else if (locations[1] == location)
            {
                return locations[0];
            }
            else
            {
                return null;
            }
        }
    }
}
