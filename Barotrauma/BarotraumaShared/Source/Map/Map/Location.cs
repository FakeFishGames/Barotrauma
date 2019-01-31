using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

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

        public int PortraitId { get; private set; }

        public int MissionsCompleted;

        private List<Mission> availableMissions = new List<Mission>();
        public IEnumerable<Mission> AvailableMissions
        {
            get
            {
                CheckMissionCompleted();

                for (int i = availableMissions.Count; i < Connections.Count * 2; i++)
                {
                    int seed = (ToolBox.StringToInt(Name) + MissionsCompleted * 10 + i) % int.MaxValue;
                    MTRandom rand = new MTRandom(seed);

                    LocationConnection connection = Connections[(MissionsCompleted + i) % Connections.Count];
                    Location destination = connection.OtherLocation(this);

                    var mission = Mission.LoadRandom(new Location[] { this, destination }, rand, true, MissionType.Random, true);
                    if (mission == null) { continue; }
                    if (availableMissions.Any(m => m.Prefab == mission.Prefab)) { continue; }
                    if (GameSettings.VerboseLogging && mission != null)
                    {
                        DebugConsole.NewMessage("Generated a new mission for a location connection (seed: " + seed.ToString("X") + ", type: " + mission.Name + ")", Color.White);
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
                if (value < 0 || value >= AvailableMissions.Count())
                {
                    SelectedMission = null;
                    return;
                }
                SelectedMission = availableMissions[value];
            }
        }

        public Location(Vector2 mapPosition, int? zone)
        {
            this.Type = LocationType.Random("", zone);
            this.Name = RandomName(Type);
            this.MapPosition = mapPosition;

            PortraitId = ToolBox.StringToInt(Name);

            Connections = new List<LocationConnection>();
        }

        public static Location CreateRandom(Vector2 position, int? zone)
        {
            return new Location(position, zone);        
        }

        public IEnumerable<Mission> GetMissionsInConnection(LocationConnection connection)
        {
            System.Diagnostics.Debug.Assert(Connections.Contains(connection));
            return AvailableMissions.Where(m => m.Locations[1] == connection.OtherLocation(this));
        }

        public void ChangeType(LocationType newType)
        {
            if (newType == Type) return;

            Type = newType;
            Name = Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", baseName);
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
