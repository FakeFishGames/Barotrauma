using Microsoft.Xna.Framework;
using System.Collections.Generic;

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

        public int MissionsCompleted;

        private Mission mission;
        public Mission Mission
        {
            get
            {
                if (mission == null || mission.Completed)
                {
                    if (mission != null && mission.Completed) MissionsCompleted++;

                    long seed = (long)locations[0].MapPosition.X + (long)locations[0].MapPosition.Y * 100;
                    seed += (long)locations[1].MapPosition.X * 10000 + (long)locations[1].MapPosition.Y * 1000000;

                    MTRandom rand = new MTRandom((int)((seed + MissionsCompleted) % int.MaxValue));

                    mission = Mission.LoadRandom(locations, rand, true, MissionType.Random, true);
                    if (GameSettings.VerboseLogging && mission != null)
                    {
                        DebugConsole.NewMessage("Generated a new mission for a location connection (seed: " + seed + ", type: " + mission.Name + ")", Color.White);
                    }
                }

                return mission;
            }
        }

        public Location[] Locations
        {
            get { return locations; }
        }

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

        public float Length
        {
            get;
            private set;
        }

        public LocationConnection(Location location1, Location location2)
        {
            locations = new Location[] { location1, location2 };

            MissionsCompleted = 0;

            Length = Vector2.Distance(location1.MapPosition, location2.MapPosition);
        }

        public void CheckMissionCompleted()
        {
            if (mission != null && mission.Completed)
            {
                MissionsCompleted++;
                mission = null;
            }
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
