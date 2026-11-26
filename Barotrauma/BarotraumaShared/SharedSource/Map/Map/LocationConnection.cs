using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class LocationConnection
    {
        public Biome Biome;

        public float Difficulty;

        public readonly List<Vector2[]> CrackSegments = new List<Vector2[]>();

#if CLIENT
        /// <summary>
        /// Mission icons to display on the campaign map. 
        /// Note that some of the missions may have already been failed/completed, but the icon is still present (e.g. until it moves to the end of the connection)
        /// </summary>
        public readonly List<Map.MissionIcon> MissionIcons = new List<Map.MissionIcon>();

        /// <summary>
        /// Number of icons (e.g. beacon station, hunting grounds, depth warning) on the connection.
        /// Calculated by and set by the Map draw methods.
        /// </summary>
        public int IconCount;
#endif

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
            set;
        }

        /// <summary>
        /// Missions that take place in this level, originating from the location at either side.
        /// </summary>
        public IEnumerable<Mission> AvailableMissions
        {
            get
            {
                return Locations[0].GetMissionsInConnection(this).Union(Locations[1].GetMissionsInConnection(this)).Distinct();
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
