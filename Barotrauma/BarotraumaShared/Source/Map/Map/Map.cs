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
        Vector2 difficultyIncrease = new Vector2(5.0f, 10.0f);
        Vector2 difficultyCutoff = new Vector2(80.0f, 100.0f);

        private List<Level> levels;

        private List<Location> locations;

        private List<LocationConnection> connections;

        private string seed;
        private int size;

        private Location currentLocation;
        private Location selectedLocation;

        private Location highlightedLocation;

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

        public static Map Load(XElement element)
        {
            string mapSeed = ToolBox.GetAttributeString(element, "seed", "a");

            int size = ToolBox.GetAttributeInt(element, "size", 1000);
            Map map = new Map(mapSeed, size);

            map.SetLocation(ToolBox.GetAttributeInt(element, "currentlocation", 0));

            string discoveredStr = ToolBox.GetAttributeString(element, "discovered", "");

            string[] discoveredStrs = discoveredStr.Split(',');
            for (int i = 0; i < discoveredStrs.Length; i++)
            {
                int index = -1;
                if (int.TryParse(discoveredStrs[i], out index)) map.locations[index].Discovered = true;
            }

            string passedStr = ToolBox.GetAttributeString(element, "passed", "");
            string[] passedStrs = passedStr.Split(',');
            for (int i = 0; i < passedStrs.Length; i++)
            {
                int index = -1;
                if (int.TryParse(passedStrs[i], out index)) map.connections[index].Passed = true;
            }

            return map;
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
#endif

            Rand.SetSyncedSeed(ToolBox.StringToInt(this.seed));

            GenerateLocations();

            currentLocation = locations[locations.Count / 2];
            currentLocation.Discovered = true;
            GenerateDifficulties(currentLocation, new List<LocationConnection>(connections), 10.0f);

            foreach (LocationConnection connection in connections)
            {
                connection.Level = Level.CreateRandom(connection);
            }
        }

        private void GenerateLocations()
        {
            Voronoi voronoi = new Voronoi(0.5f);

            List<Vector2> sites = new List<Vector2>();
            for (int i = 0; i < 100; i++)
            {
                sites.Add(new Vector2(Rand.Range(0.0f, size, Rand.RandSync.Server), Rand.Range(0.0f, size, Rand.RandSync.Server)));
            }

            List<GraphEdge> edges = voronoi.MakeVoronoiGraph(sites, size, size);

            sites.Clear();
            foreach (GraphEdge edge in edges)
            {
                if (edge.point1 == edge.point2) continue;

                //remove points from the edge of the map
                if (edge.point1.X == 0 || edge.point1.X == size) continue;
                if (edge.point1.Y == 0 || edge.point1.Y == size) continue;
                if (edge.point2.X == 0 || edge.point2.X == size) continue;
                if (edge.point2.Y == 0 || edge.point2.Y == size) continue;

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

                    newLocations[i] = Location.CreateRandom(position);
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

                locations.Remove(connection.Locations[0]);
                connections.Remove(connection);

                foreach (LocationConnection connection2 in connections)
                {
                    if (connection2.Locations[0] == connection.Locations[0]) connection2.Locations[0] = connection.Locations[1];
                    if (connection2.Locations[1] == connection.Locations[0]) connection2.Locations[1] = connection.Locations[1];
                }
            }

            foreach (LocationConnection connection in connections)
            {
                connection.Locations[0].Connections.Add(connection);
                connection.Locations[1].Connections.Add(connection);
            }

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
                Vector2 start = connection.Locations[0].MapPosition;
                Vector2 end = connection.Locations[1].MapPosition;
                int generations = (int)(Math.Sqrt(Vector2.Distance(start, end) / 10.0f));
                connection.CrackSegments = MathUtils.GenerateJaggedLine(start, end, generations, 5.0f);
            }
            
            AssignBiomes();
        }

        private void AssignBiomes()
        {
            List<LocationConnection> biomeSeeds = new List<LocationConnection>();

            List<Biome> centerBiomes = LevelGenerationParams.GetBiomes().FindAll(b => b.Placement.HasFlag(Biome.MapPlacement.Center));
            if (centerBiomes.Count > 0)
            {
                Vector2 mapCenter = new Vector2(locations.Sum(l => l.MapPosition.X), locations.Sum(l => l.MapPosition.Y)) / locations.Count;
                foreach (Biome centerBiome in centerBiomes)
                {
                    LocationConnection closestConnection = null;
                    float closestDist = float.PositiveInfinity;
                    foreach (LocationConnection connection in connections)
                    {
                        if (connection.Biome != null) continue;

                        float dist = Vector2.Distance(connection.CenterPos, mapCenter);
                        if (closestConnection == null || dist < closestDist)
                        {
                            closestConnection = connection;
                            closestDist = dist;
                        }
                    }

                    closestConnection.Biome = centerBiome;
                    biomeSeeds.Add(closestConnection);
                }
            }

            List<Biome> edgeBiomes = LevelGenerationParams.GetBiomes().FindAll(b => b.Placement.HasFlag(Biome.MapPlacement.Edge));
            if (edgeBiomes.Count > 0)
            {
                List<LocationConnection> edges = GetMapEdges();
                foreach (LocationConnection edge in edges)
                {
                    edge.Biome = edgeBiomes[Rand.Range(0, edgeBiomes.Count, Rand.RandSync.Server)];
                }
            }
            
            List<Biome> randomBiomes = LevelGenerationParams.GetBiomes().FindAll(b => b.Placement.HasFlag(Biome.MapPlacement.Random));
            foreach (Biome biome in randomBiomes)
            {
                LocationConnection seed = connections[0];
                while (seed.Biome != null)
                {
                    seed = connections[Rand.Range(0, connections.Count, Rand.RandSync.Server)];
                }
                seed.Biome = biome;
                biomeSeeds.Add(seed);
            }

            ExpandBiomes(biomeSeeds);
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
        

        private void GenerateDifficulties(Location start, List<LocationConnection> locations, float currDifficulty)
        {
            //start.Difficulty = currDifficulty;
            currDifficulty += Rand.Range(difficultyIncrease.X, difficultyIncrease.Y, Rand.RandSync.Server);
            if (currDifficulty > Rand.Range(difficultyCutoff.X, difficultyCutoff.Y, Rand.RandSync.Server)) currDifficulty = 10.0f;

            foreach (LocationConnection connection in start.Connections)
            {
                if (!locations.Contains(connection)) continue;

                Location nextLocation = connection.OtherLocation(start);
                locations.Remove(connection);

                connection.Difficulty = currDifficulty;

                GenerateDifficulties(nextLocation, locations, currDifficulty);
            }
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

        public void Save(XElement element)
        {
            XElement mapElement = new XElement("map");

            mapElement.Add(new XAttribute("currentlocation", CurrentLocationIndex));
            mapElement.Add(new XAttribute("seed", Seed));
            mapElement.Add(new XAttribute("size", size));

            List<int> discoveredLocations = new List<int>();
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i].Discovered) discoveredLocations.Add(i);
            }
            mapElement.Add(new XAttribute("discovered", string.Join(",", discoveredLocations)));

            List<int> passedConnections = new List<int>();
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i].Passed) passedConnections.Add(i);
            }
            mapElement.Add(new XAttribute("passed", string.Join(",", passedConnections)));

            element.Add(mapElement);
        }
    }


    class LocationConnection
    {
        private Location[] locations;
        private Level level;

        public Biome Biome;

        public float Difficulty;

        public List<Vector2[]> CrackSegments;

        public bool Passed;

        private int missionsCompleted;

        private Mission mission;
        public Mission Mission
        {
            get 
            {
                if (mission == null || mission.Completed)
                {
                    if (mission != null && mission.Completed) missionsCompleted++;

                    long seed = (long)locations[0].MapPosition.X + (long)locations[0].MapPosition.Y * 100;
                    seed += (long)locations[1].MapPosition.X * 10000 + (long)locations[1].MapPosition.Y * 1000000;

                    MTRandom rand = new MTRandom((int)((seed + missionsCompleted) % int.MaxValue));

                    if (rand.NextDouble() < 0.3f) return null;

                    mission = Mission.LoadRandom(locations, rand, "", true);
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
        
        public LocationConnection(Location location1, Location location2)
        {
            locations = new Location[] { location1, location2 };

            missionsCompleted = 0;
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
