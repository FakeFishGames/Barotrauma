using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class LocationConnection
    {
        public Biome Biome;

        public float Difficulty;

        public readonly List<Vector2[]> CrackSegments = new List<Vector2[]>();

        public bool Passed;

        public bool Locked;

        public LevelData LevelData { get; set; }

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

        private readonly List<Mission> availableMissions = new List<Mission>();
        public IEnumerable<Mission> AvailableMissions
        {
            get
            {
                availableMissions.RemoveAll(m => m.Completed || (m.Failed && !m.Prefab.AllowRetry));
                return availableMissions;
            }
        }

        public LocationConnection(Location location1, Location location2)
        {
            if (location1 == null)
            {
                throw new ArgumentException("Invalid location connection: location1 was null");
            }
            if (location2 == null)
            {
                throw new ArgumentException("Invalid location connection: location2 was null");
            }
            if (location1 == location2)
            {
                throw new ArgumentException("Invalid location connection: location1 was the same as location2");
            }

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
