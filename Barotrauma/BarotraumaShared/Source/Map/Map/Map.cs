using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class Map
    {
        const int DifficultyZones = 9;
        
        public const int DefaultSize = 2000;

        Vector2 difficultyIncrease = new Vector2(5.0f, 10.0f);
        Vector2 difficultyCutoff = new Vector2(80.0f, 100.0f);

        private List<Level> levels;

        private List<Location> locations;

        private List<LocationConnection> connections;

        private string seed;
        private int size;

        private Location currentLocation;
        private Location selectedLocation;

        private LocationConnection selectedConnection;

        public Action<Location, LocationConnection> OnLocationSelected;
        public Action<Location> OnLocationChanged;

        public Location CurrentLocation
        {
            get { return currentLocation; }
        }

        public int CurrentLocationIndex
        {
            get { return locations.IndexOf(currentLocation); }
        }

        public Location SelectedLocation
        {
            get { return selectedLocation; }
        }

        public int SelectedLocationIndex
        {
            get { return locations.IndexOf(selectedLocation); }
        }

        public LocationConnection SelectedConnection
        {
            get { return selectedConnection; }
        }

        public string Seed
        {
            get { return seed; }
        }

        public List<Location> Locations
        {
            get { return locations; }
        }

        public Map(string seed, int size)
        {
            this.seed = seed;

            this.size = size;

            levels = new List<Level>();

            locations = new List<Location>();

            connections = new List<LocationConnection>();

#if CLIENT
            if (iceTexture == null) iceTexture = new Sprite("Content/Map/iceSurface.png", Vector2.Zero);
            if (iceCraters == null) iceCraters = TextureLoader.FromFile("Content/Map/iceCraters.png");
            if (iceCrack == null)   iceCrack = TextureLoader.FromFile("Content/Map/iceCrack.png");

            if (circleTexture == null) circleTexture = GUI.CreateCircle(512, true);
     
#endif
            Rand.SetSyncedSeed(ToolBox.StringToInt(this.seed));

            GenerateLocations();

            //start from the colony furthest away from the center
            float largestDist = 0.0f;
            Vector2 center = new Vector2(size, size) / 2;
            foreach (Location location in locations)
            {
                if (location.Type.Name != "City") continue;
                float dist = Vector2.DistanceSquared(center, location.MapPosition);
                if (dist > largestDist)
                {
                    largestDist = dist;
                    currentLocation = location;
                }
            }
            
            currentLocation.Discovered = true;

            foreach (LocationConnection connection in connections)
            {
                connection.Level = Level.CreateRandom(connection);
            }
        }

        private void GenerateLocations()
        {
            List<Vector2> sites = new List<Vector2>();
            float mapRadius = size / 2;
            Vector2 mapCenter = new Vector2(mapRadius, mapRadius);

            float zoneRadius = size / 2 / DifficultyZones;
            for (int i = 0; i < DifficultyZones; i++)
            {
                for (int j = 0; j < (i + 1) * MathHelper.Pi * 5; j++)
                {
                    float thisZoneRadius = (i + 1.0f) * zoneRadius;
                    sites.Add(mapCenter + Rand.Vector(thisZoneRadius - Rand.Range(0.0f, zoneRadius, Rand.RandSync.Server), Rand.RandSync.Server));
                }
            }

            Voronoi voronoi = new Voronoi(0.5f);
            List<GraphEdge> edges = voronoi.MakeVoronoiGraph(sites, size, size);

            sites.Clear();
            foreach (GraphEdge edge in edges)
            {
                if (edge.point1 == edge.point2) continue;

                //remove points from the edge of the map
                /*if (edge.point1.X == 0 || edge.point1.X == size) continue;
                if (edge.point1.Y == 0 || edge.point1.Y == size) continue;
                if (edge.point2.X == 0 || edge.point2.X == size) continue;
                if (edge.point2.Y == 0 || edge.point2.Y == size) continue;*/

                if (Vector2.DistanceSquared(edge.point1, mapCenter) >= mapRadius * mapRadius ||
                    Vector2.DistanceSquared(edge.point2, mapCenter) >= mapRadius * mapRadius) continue;

                Location[] newLocations = new Location[2];
                newLocations[0] = locations.Find(l => l.MapPosition == edge.point1 || l.MapPosition == edge.point2);
                newLocations[1] = locations.Find(l => l != newLocations[0] && (l.MapPosition == edge.point1 || l.MapPosition == edge.point2));

                for (int i = 0; i < 2; i++)
                {
                    if (newLocations[i] != null) continue;

                    Vector2[] points = new Vector2[] { edge.point1, edge.point2 };

                    int positionIndex = Rand.Int(1, Rand.RandSync.Server);

                    Vector2 position = points[positionIndex];
                    if (newLocations[1 - i] != null && newLocations[1 - i].MapPosition == position) position = points[1 - positionIndex];
                    float dddd = Vector2.Distance(position, mapCenter);
                    int zone = MathHelper.Clamp(DifficultyZones - (int)Math.Floor(Vector2.Distance(position, mapCenter) / zoneRadius), 1, DifficultyZones);
                    newLocations[i] = Location.CreateRandom(position, zone);
                    locations.Add(newLocations[i]);
                }
                //int seed = (newLocations[0].GetHashCode() | newLocations[1].GetHashCode());
                connections.Add(new LocationConnection(newLocations[0], newLocations[1]));
            }

            //remove connections that are too short
            float minDistance = 50.0f;
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                LocationConnection connection = connections[i];

                if (Vector2.Distance(connection.Locations[0].MapPosition, connection.Locations[1].MapPosition) > minDistance)
                {
                    continue;
                }

                //locations.Remove(connection.Locations[0]);
                connections.Remove(connection);

                foreach (LocationConnection connection2 in connections)
                {
                    if (connection2.Locations[0] == connection.Locations[0]) connection2.Locations[0] = connection.Locations[1];
                    if (connection2.Locations[1] == connection.Locations[0]) connection2.Locations[1] = connection.Locations[1];
                }
            }

            HashSet<Location> connectedLocations = new HashSet<Location>();
            foreach (LocationConnection connection in connections)
            {
                connection.Locations[0].Connections.Add(connection);
                connection.Locations[1].Connections.Add(connection);

                connectedLocations.Add(connection.Locations[0]);
                connectedLocations.Add(connection.Locations[1]);
            }

            //remove orphans
            locations.RemoveAll(c => !connectedLocations.Contains(c));
            
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                i = Math.Min(i, connections.Count - 1);

                LocationConnection connection = connections[i];

                for (int n = Math.Min(i - 1, connections.Count - 1); n >= 0; n--)
                {
                    if (connection.Locations.Contains(connections[n].Locations[0])
                        && connection.Locations.Contains(connections[n].Locations[1]))
                    {
                        connections.RemoveAt(n);
                    }
                }
            }

            foreach (LocationConnection connection in connections)
            {
                float centerDist = Vector2.Distance(connection.CenterPos, mapCenter);
                connection.Difficulty = MathHelper.Clamp(((1.0f - centerDist / mapRadius) * 100) + Rand.Range(-10.0f, 10.0f, Rand.RandSync.Server),0, 100);

                Vector2 start = connection.Locations[0].MapPosition;
                Vector2 end = connection.Locations[1].MapPosition;
                int generations = (int)(Math.Sqrt(Vector2.Distance(start, end) / 10.0f));
                connection.CrackSegments = MathUtils.GenerateJaggedLine(start, end, generations, 5.0f);
            }
            
            AssignBiomes();
        }

        private void AssignBiomes()
        {
            var biomes = LevelGenerationParams.GetBiomes();
            Vector2 centerPos = new Vector2(size, size) / 2;
            for (int i = 0; i<DifficultyZones; i++)
            {
                List<Biome> allowedBiomes = biomes.FindAll(b => b.AllowedZones.Contains(DifficultyZones - i + 1));
                float zoneRadius = size / 2 * ((i + 1.0f) / DifficultyZones);
                foreach (LocationConnection connection in connections)
                {
                    if (connection.Biome != null) continue;

                    if (i == DifficultyZones - 1 ||
                        Vector2.Distance(connection.Locations[0].MapPosition, centerPos) < zoneRadius ||
                        Vector2.Distance(connection.Locations[1].MapPosition, centerPos) < zoneRadius)
                    {
                        connection.Biome = allowedBiomes[Rand.Range(0, allowedBiomes.Count, Rand.RandSync.Server)];
                    }
                }
            }
        }

        private void ExpandBiomes(List<LocationConnection> seeds)
        {
            List<LocationConnection> nextSeeds = new List<LocationConnection>(); 
            foreach (LocationConnection connection in seeds)
            {
                foreach (Location location in connection.Locations)
                {
                    foreach (LocationConnection otherConnection in location.Connections)
                    {
                        if (otherConnection == connection) continue;                        
                        if (otherConnection.Biome != null) continue; //already assigned

                        otherConnection.Biome = connection.Biome;
                        nextSeeds.Add(otherConnection);                        
                    }
                }
            }

            if (nextSeeds.Count > 0)
            {
                ExpandBiomes(nextSeeds);
            }
        }

        private List<LocationConnection> GetMapEdges()
        {
            List<Vector2> verts = locations.Select(l => l.MapPosition).ToList();

            List<Vector2> giftWrappedVerts = MathUtils.GiftWrap(verts);

            List<LocationConnection> edges = new List<LocationConnection>();
            foreach (LocationConnection connection in connections)
            {
                if (giftWrappedVerts.Contains(connection.Locations[0].MapPosition) || 
                    giftWrappedVerts.Contains(connection.Locations[1].MapPosition))
                {
                    edges.Add(connection);
                }
            }
            
            return edges;
        }
        
        public void MoveToNextLocation()
        {
            selectedConnection.Passed = true;

            currentLocation = selectedLocation;
            currentLocation.Discovered = true;
            selectedLocation = null;

            OnLocationChanged?.Invoke(currentLocation);
        }

        public void SetLocation(int index)
        {
            if (index == -1)
            {
                currentLocation = null;
                return;
            }

            if (index < 0 || index >= locations.Count)
            {
                DebugConsole.ThrowError("Location index out of bounds");
                return;
            }

            currentLocation = locations[index];
            currentLocation.Discovered = true;

            OnLocationChanged?.Invoke(currentLocation);
        }

        public void SelectLocation(int index)
        {
            if (index == -1)
            {
                selectedLocation = null;
                selectedConnection = null;

                OnLocationSelected?.Invoke(null, null);
                return;
            }

            if (index < 0 || index >= locations.Count)
            {
                DebugConsole.ThrowError("Location index out of bounds");
                return;
            }

            selectedLocation = locations[index];
            selectedConnection = connections.Find(c => c.Locations.Contains(currentLocation) && c.Locations.Contains(selectedLocation));
            OnLocationSelected?.Invoke(selectedLocation, selectedConnection);
        }

        public void SelectLocation(Location location)
        {
            if (!locations.Contains(location))
            {
                DebugConsole.ThrowError("Failed to select a location. "+location.Name+" not found in the map.");
                return;
            }

            selectedLocation = location;
            selectedConnection = connections.Find(c => c.Locations.Contains(currentLocation) && c.Locations.Contains(selectedLocation));
            OnLocationSelected?.Invoke(selectedLocation, selectedConnection);
        }

        public void SelectRandomLocation(bool preferUndiscovered)
        {
            List<Location> nextLocations = currentLocation.Connections.Select(c => c.OtherLocation(currentLocation)).ToList();            
            List<Location> undiscoveredLocations = nextLocations.FindAll(l => !l.Discovered);
            
            if (undiscoveredLocations.Count > 0 && preferUndiscovered)
            {
                SelectLocation(undiscoveredLocations[Rand.Int(undiscoveredLocations.Count, Rand.RandSync.Unsynced)]);
            }
            else
            {
                SelectLocation(nextLocations[Rand.Int(nextLocations.Count, Rand.RandSync.Unsynced)]);
            }
        }

        public void ProgressWorld()
        {
            foreach (Location location in locations)
            {
                if (!location.Discovered) continue;
                
                var allowedTypeChanges = location.Type.CanChangeTo.FindAll(c => location.TypeChangeTimer >= c.RequiredDuration);

                float probabilitySum = allowedTypeChanges.Sum(m => m.Probability);
                float randomNumber = Rand.Range(0.0f, 1.0f);

                foreach (LocationTypeChange typeChange in allowedTypeChanges)
                {
                    if (randomNumber <= typeChange.Probability)
                    {
                        location.ChangeType(LocationType.List.Find(lt => lt.Name.ToLowerInvariant() == typeChange.ChangeTo.ToLowerInvariant()));
                        location.TypeChangeTimer = -1;
                        break;
                    }

                    randomNumber -= typeChange.Probability;
                }

                location.TypeChangeTimer++;
            }
        }

        public static Map LoadNew(XElement element)
        {
            string mapSeed = element.GetAttributeString("seed", "a");

            int size = element.GetAttributeInt("size", DefaultSize);
            Map map = new Map(mapSeed, size);
            map.Load(element);

            return map;
        }

        public void Load(XElement element)
        {
            SetLocation(element.GetAttributeInt("currentlocation", 0));

            Version saveVersion;
            if (!Version.TryParse(element.GetAttributeString("version", ""), out saveVersion))
            {
                DebugConsole.ThrowError("Incompatible map save file, loading the game failed.");
                return;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "location":
                        string locationType = subElement.GetAttributeString("type", "");
                        int locationIndex = subElement.GetAttributeInt("i", 0);
                        int typeChangeTimer = subElement.GetAttributeInt("changetimer", 0);

                        locations[locationIndex].Discovered = true;
                        locations[locationIndex].ChangeType(LocationType.List.Find(lt => lt.Name.ToLowerInvariant() == locationType.ToLowerInvariant()));
                        locations[locationIndex].TypeChangeTimer = typeChangeTimer;
                        break;
                    case "connection":
                        int connectionIndex = subElement.GetAttributeInt("i", 0);
                        int missionsCompleted = subElement.GetAttributeInt("missionscompleted", 0);

                        connections[connectionIndex].Passed = true;
                        connections[connectionIndex].MissionsCompleted = missionsCompleted;
                        break;
                }
            }
        }

        public void Save(XElement element)
        {
            XElement mapElement = new XElement("map");

            mapElement.Add(new XAttribute("version", GameMain.Version.ToString()));
            mapElement.Add(new XAttribute("currentlocation", CurrentLocationIndex));
            mapElement.Add(new XAttribute("seed", Seed));
            mapElement.Add(new XAttribute("size", size));

            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (!location.Discovered) continue;

                var locationElement = new XElement("location", new XAttribute("i", i));
                locationElement.Add(new XAttribute("type", location.Type.Name));

                if (location.TypeChangeTimer > 0)
                {
                    locationElement.Add(new XAttribute("changetimer", location.TypeChangeTimer));
                }

                mapElement.Add(locationElement);
            }

            for (int i = 0; i < connections.Count; i++)
            {
                var connection = connections[i];
                if (!connection.Passed) continue;

                var connectionElement = new XElement("connection", new XAttribute("i", i));
                if (connection.MissionsCompleted > 0)
                {
                    connectionElement.Add(new XAttribute("missionscompleted", connection.MissionsCompleted));
                }

                mapElement.Add(connectionElement);
            }

            element.Add(mapElement);
        }
    }
}
