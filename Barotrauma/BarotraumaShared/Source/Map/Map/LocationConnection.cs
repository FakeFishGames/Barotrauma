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

        public int MissionsCompleted;

        private List<Mission> availableMissions = new List<Mission>();
        public IEnumerable<Mission> AvailableMissions
        {
            get
            {
                CheckMissionCompleted();

                for (int i = availableMissions.Count; i < 3; i++)
                {
                    string seed = locations[0].Name + locations[1].Name;
                    MTRandom rand = new MTRandom((ToolBox.StringToInt(seed) + MissionsCompleted * 10 + i) % int.MaxValue);

                    var mission = Mission.LoadRandom(locations, rand, true, MissionType.Random, true);
                    if (mission == null) { break; }
                    if (availableMissions.Any(m => m.Prefab == mission.Prefab)) { continue; }
                    if (GameSettings.VerboseLogging && mission != null)
                    {
                        DebugConsole.NewMessage("Generated a new mission for a location connection (seed: " + seed + ", type: " + mission.Name + ")", Color.White);
                    }
                    availableMissions.Add(mission);
                }

                return availableMissions;
            }
        }

        public Mission SelectedMission
        {
            get;
            set;
        }

        public int SelectedMissionIndex
        {
            get { return availableMissions.IndexOf(SelectedMission); }
            set
            {
                if (value < 0 || value >= AvailableMissions.Count() )
                {
                    SelectedMission = null;
                    return;
                }
                SelectedMission = availableMissions[value];
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
            foreach (Mission mission in availableMissions)
            {
                if (mission.Completed)
                {
                    MissionsCompleted++;
                }
            }

            availableMissions.RemoveAll(m => m.Completed);
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
