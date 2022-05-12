using Barotrauma.Extensions;
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
        public bool AllowDebugTeleport;

        private readonly MapGenerationParams generationParams;

        private Location furthestDiscoveredLocation;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public Action<Location, LocationConnection> OnLocationSelected;
        /// <summary>
        /// From -> To
        /// </summary>
        public Action<Location, Location> OnLocationChanged;
        public Action<LocationConnection, IEnumerable<Mission>> OnMissionsSelected;

        public Location EndLocation { get; private set; }

        public Location StartLocation { get; private set; }

        public Location CurrentLocation { get; private set; }

        public int CurrentLocationIndex
        {
            get { return Locations.IndexOf(CurrentLocation); }
        }

        public Location SelectedLocation { get; private set; }

        public int SelectedLocationIndex
        {
            get { return Locations.IndexOf(SelectedLocation); }
        }

        public IEnumerable<int> GetSelectedMissionIndices()
        {
            return SelectedConnection == null ? Enumerable.Empty<int>() : CurrentLocation.GetSelectedMissionIndices();
        }

        public LocationConnection SelectedConnection { get; private set; }

        public string Seed { get; private set; }

        public List<Location> Locations { get; private set; }

        public List<LocationConnection> Connections { get; private set; }

        public Radiation Radiation;

        public Map(CampaignSettings settings)
        {
            generationParams = MapGenerationParams.Instance;
            Width = generationParams.Width;
            Height = generationParams.Height;
            Locations = new List<Location>();
            Connections = new List<LocationConnection>();
            if (generationParams.RadiationParams != null)
            {
                Radiation = new Radiation(this, generationParams.RadiationParams)
                {
                    Enabled = settings.RadiationEnabled
                };
            }
        }

        /// <summary>
        /// Load a previously saved campaign map from XML
        /// </summary>
        private Map(CampaignMode campaign, XElement element, CampaignSettings settings) : this(settings)
        {
            Seed = element.GetAttributeString("seed", "a");
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));

            Width = element.GetAttributeInt("width", Width);
            Height = element.GetAttributeInt("height", Height);

            bool lairsFound = false;

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "location":
                        int i = subElement.GetAttributeInt("i", 0);
                        while (Locations.Count <= i)
                        {
                            Locations.Add(null);
                        }
                        lairsFound |= subElement.GetAttributeString("type", "").Equals("lair", StringComparison.OrdinalIgnoreCase);
                        Locations[i] = new Location(subElement);
                        break;
                    case "radiation":
                        Radiation = new Radiation(this, generationParams.RadiationParams, subElement)
                        {
                            Enabled = settings.RadiationEnabled
                        };
                        break;
                }
            }
            System.Diagnostics.Debug.Assert(!Locations.Contains(null));
            for (int i = 0; i < Locations.Count; i++)
            {
                Locations[i].Reputation ??= new Reputation(campaign.CampaignMetadata, Locations[i], $"location.{i}".ToIdentifier(), -100, 100, Rand.Range(-10, 11, Rand.RandSync.ServerAndClient));
            }

            List<XElement> connectionElements = new List<XElement>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "connection":
                        Point locationIndices = subElement.GetAttributePoint("locations", new Point(0, 1));
                        if (locationIndices.X == locationIndices.Y) { continue; }
                        var connection = new LocationConnection(Locations[locationIndices.X], Locations[locationIndices.Y])
                        {
                            Passed = subElement.GetAttributeBool("passed", false),
                            Locked = subElement.GetAttributeBool("locked", false),
                            Difficulty = subElement.GetAttributeFloat("difficulty", 0.0f)
                        };
                        Locations[locationIndices.X].Connections.Add(connection);
                        Locations[locationIndices.Y].Connections.Add(connection);
                        connection.LevelData = new LevelData(subElement.Element("Level"));
                        string biomeId = subElement.GetAttributeString("biome", "");
                        connection.Biome =
                            Biome.Prefabs.FirstOrDefault(b => b.Identifier == biomeId) ??
                            Biome.Prefabs.FirstOrDefault(b => !b.OldIdentifier.IsEmpty && b.OldIdentifier == biomeId) ??
                            Biome.Prefabs.First();
                        Connections.Add(connection);
                        connectionElements.Add(subElement);
                        break;
                }
            }

            int startLocationindex = element.GetAttributeInt("startlocation", -1);
            if (startLocationindex > 0 && startLocationindex < Locations.Count)
            {
                StartLocation = Locations[startLocationindex];
            }
            else
            {
                DebugConsole.AddWarning($"Error while loading the map. Start location index out of bounds (index: {startLocationindex}, location count: {Locations.Count}).");
                foreach (Location location in Locations)
                {
                    if (!location.Type.HasOutpost) { continue; }
                    if (StartLocation == null || location.MapPosition.X < StartLocation.MapPosition.X)
                    {
                        StartLocation = location;
                    }
                }
            }
            int endLocationindex = element.GetAttributeInt("endlocation", -1);
            if (endLocationindex > 0 && endLocationindex < Locations.Count)
            {
                EndLocation = Locations[endLocationindex];
            }
            else
            {
                DebugConsole.AddWarning($"Error while loading the map. End location index out of bounds (index: {endLocationindex}, location count: {Locations.Count}).");
                foreach (Location location in Locations)
                {
                    if (EndLocation == null || location.MapPosition.X > EndLocation.MapPosition.X)
                    {
                        EndLocation = location;
                    }
                }
            }

            //backwards compatibility: if the map contained the now-removed lairs and has no hunting grounds, create some hunting grounds
            if (lairsFound && !Connections.Any(c => c.LevelData.HasHuntingGrounds))
            {
                for (int i = 0; i < Connections.Count; i++)
                {
                    Connections[i].LevelData.HasHuntingGrounds = Rand.Range(0.0f, 1.0f) < Connections[i].Difficulty / 100.0f * LevelData.MaxHuntingGroundsProbability;
                    connectionElements[i].SetAttributeValue("hashuntinggrounds", true);
                }
            }

            //backwards compatibility: if locations go out of bounds (map saved with different generation parameters before width/height were included in the xml)
            float maxX = Locations.Select(l => l.MapPosition.X).Max();
            if (maxX > Width) { Width = (int)(maxX + 10); }
            float maxY = Locations.Select(l => l.MapPosition.Y).Max();
            if (maxY > Height) { Height = (int)(maxY + 10); }

            InitProjectSpecific();
        }

        /// <summary>
        /// Generate a new campaign map from the seed
        /// </summary>
        public Map(CampaignMode campaign, string seed, CampaignSettings settings) : this(settings)
        {
            Seed = seed;
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));

            Generate();

            if (Locations.Count == 0)
            {
                throw new Exception($"Generating a campaign map failed (no locations created). Width: {Width}, height: {Height}");
            }

            for (int i = 0; i < Locations.Count; i++)
            {
                Locations[i].Reputation ??= new Reputation(campaign.CampaignMetadata, Locations[i], $"location.{i}".ToIdentifier(), -100, 100, Rand.Range(-10, 11, Rand.RandSync.ServerAndClient));
            }

            foreach (Location location in Locations)
            {
                if (location.Type.Identifier != "outpost") { continue; }
                if (CurrentLocation == null || location.MapPosition.X < CurrentLocation.MapPosition.X)
                {
                    CurrentLocation = StartLocation = furthestDiscoveredLocation = location;
                }
            }
            //if no outpost was found (using a mod that replaces the outpost location type?), find any type of outpost
            if (CurrentLocation == null)
            {
                foreach (Location location in Locations)
                {
                    if (!location.Type.HasOutpost) { continue; }
                    if (CurrentLocation == null || location.MapPosition.X < CurrentLocation.MapPosition.X)
                    {
                        CurrentLocation = StartLocation = furthestDiscoveredLocation = location;
                    }
                }
            }
            System.Diagnostics.Debug.Assert(StartLocation != null, "Start location not assigned after level generation.");

            //ensure all paths from the starting location have 0 difficulty to make the 1st campaign round very easy
            foreach (var locationConnection in StartLocation.Connections)
            {
                if (locationConnection.Difficulty > 0.0f)
                {
                    locationConnection.Difficulty = 0.0f;
                    locationConnection.LevelData = new LevelData(locationConnection);
                }
            }

            CurrentLocation.Discover(true);
            CurrentLocation.CreateStores();

            InitProjectSpecific();
        }

        partial void InitProjectSpecific();

        #region Generation

        private void Generate()
        {
            Connections.Clear();
            Locations.Clear();

            List<Vector2> voronoiSites = new List<Vector2>();
            for (float x = 10.0f; x < Width - 10.0f; x += generationParams.VoronoiSiteInterval.X)
            {
                for (float y = 10.0f; y < Height - 10.0f; y += generationParams.VoronoiSiteInterval.Y)
                {
                    voronoiSites.Add(new Vector2(
                        x + generationParams.VoronoiSiteVariance.X * Rand.Range(-0.5f, 0.5f, Rand.RandSync.ServerAndClient),
                        y + generationParams.VoronoiSiteVariance.Y * Rand.Range(-0.5f, 0.5f, Rand.RandSync.ServerAndClient)));
                }
            }

            Voronoi voronoi = new Voronoi(0.5f);
            List<GraphEdge> edges = voronoi.MakeVoronoiGraph(voronoiSites, Width, Height);
            float zoneWidth = Width / generationParams.DifficultyZones;

            Vector2 margin = new Vector2(
               Math.Min(10, Width * 0.1f),
               Math.Min(10, Height * 0.2f));

            float startX = margin.X, endX = Width - margin.X;
            float startY = margin.Y, endY = Height - margin.Y;

            if (!edges.Any())
            {
                throw new Exception($"Generating a campaign map failed (no edges in the voronoi graph). Width: {Width}, height: {Height}, margin: {margin}");
            }

            voronoiSites.Clear();
            Dictionary<int, List<Location>> locationsPerZone = new Dictionary<int, List<Location>>();
            foreach (GraphEdge edge in edges)
            {
                if (edge.Point1 == edge.Point2) { continue; }

                if (edge.Point1.X < margin.X || edge.Point1.X > Width - margin.X || edge.Point1.Y < startY || edge.Point1.Y > endY) 
                {
                    continue;
                }
                if (edge.Point2.X < margin.X || edge.Point2.X > Width - margin.X || edge.Point2.Y < startY || edge.Point2.Y > endY)
                {
                    continue;
                }

                Location[] newLocations = new Location[2];
                newLocations[0] = Locations.Find(l => l.MapPosition == edge.Point1 || l.MapPosition == edge.Point2);
                newLocations[1] = Locations.Find(l => l != newLocations[0] && (l.MapPosition == edge.Point1 || l.MapPosition == edge.Point2));

                for (int i = 0; i < 2; i++)
                {
                    if (newLocations[i] != null) { continue; }

                    Vector2[] points = new Vector2[] { edge.Point1, edge.Point2 };

                    int positionIndex = Rand.Int(1, Rand.RandSync.ServerAndClient);

                    Vector2 position = points[positionIndex];
                    if (newLocations[1 - i] != null && newLocations[1 - i].MapPosition == position) { position = points[1 - positionIndex]; }
                    int zone = GetZoneIndex(position.X);
                    if (!locationsPerZone.ContainsKey(zone))
                    {
                        locationsPerZone[zone] = new List<Location>();
                    }

                    LocationType forceLocationType = null;
                    foreach (LocationType locationType in LocationType.Prefabs.OrderBy(lt => lt.Identifier))
                    {
                        if (locationType.MinCountPerZone.TryGetValue(zone, out int minCount) && locationsPerZone[zone].Count(l => l.Type == locationType) < minCount)
                        {
                            forceLocationType = locationType;
                            break;
                        }
                    }

                    newLocations[i] = Location.CreateRandom(position, zone, Rand.GetRNG(Rand.RandSync.ServerAndClient), 
                        requireOutpost: false, forceLocationType: forceLocationType, existingLocations: Locations);
                    locationsPerZone[zone].Add(newLocations[i]);
                    Locations.Add(newLocations[i]);
                }

                var newConnection = new LocationConnection(newLocations[0], newLocations[1]);
                Connections.Add(newConnection);                
            }

            //remove connections that are too short
            float minConnectionDistanceSqr = generationParams.MinConnectionDistance * generationParams.MinConnectionDistance;
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                LocationConnection connection = Connections[i];

                if (Vector2.DistanceSquared(connection.Locations[0].MapPosition, connection.Locations[1].MapPosition) > minConnectionDistanceSqr)
                {
                    continue;
                }
                
                //locations.Remove(connection.Locations[0]);
                Connections.Remove(connection);

                foreach (LocationConnection connection2 in Connections)
                {
                    if (connection2.Locations[0] == connection.Locations[0]) { connection2.Locations[0] = connection.Locations[1]; }
                    if (connection2.Locations[1] == connection.Locations[0]) { connection2.Locations[1] = connection.Locations[1]; }
                }
            }

            foreach (LocationConnection connection in Connections)
            {
                connection.Locations[0].Connections.Add(connection);
                connection.Locations[1].Connections.Add(connection);
            }

            //remove locations that are too close to each other
            float minLocationDistanceSqr = generationParams.MinLocationDistance * generationParams.MinLocationDistance;
            for (int i = Locations.Count - 1; i >= 0; i--)
            {
                for (int j = Locations.Count - 1; j > i; j--)
                {
                    float dist = Vector2.DistanceSquared(Locations[i].MapPosition, Locations[j].MapPosition);
                    if (dist > minLocationDistanceSqr)
                    {
                        continue;
                    }
                    //move connections from Locations[j] to Locations[i]
                    foreach (LocationConnection connection in Locations[j].Connections)
                    {
                        if (connection.Locations[0] == Locations[j])
                        {
                            connection.Locations[0] = Locations[i];
                        }
                        else
                        {
                            connection.Locations[1] = Locations[i];
                        }

                        if (connection.Locations[0] != connection.Locations[1])
                        {
                            Locations[i].Connections.Add(connection);
                        }
                        else
                        {
                            Connections.Remove(connection);
                        }
                    }
                    Locations[i].Connections.RemoveAll(c => c.OtherLocation(Locations[i]) == Locations[j]);
                    Locations.RemoveAt(j);
                }
            }

            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                i = Math.Min(i, Connections.Count - 1);
                LocationConnection connection = Connections[i];
                for (int n = Math.Min(i - 1, Connections.Count - 1); n >= 0; n--)
                {
                    if (connection.Locations.Contains(Connections[n].Locations[0])
                        && connection.Locations.Contains(Connections[n].Locations[1]))
                    {
                        Connections.RemoveAt(n);
                    }
                }
            }

            List<LocationConnection>[] connectionsBetweenZones = new List<LocationConnection>[generationParams.DifficultyZones];
            for (int i = 0; i < generationParams.DifficultyZones; i++)
            {
                connectionsBetweenZones[i] = new List<LocationConnection>();
            }
            var shuffledConnections = Connections.ToList();
            shuffledConnections.Shuffle(Rand.RandSync.ServerAndClient);
            foreach (var connection in shuffledConnections)
            {
                int zone1 = GetZoneIndex(connection.Locations[0].MapPosition.X);
                int zone2 = GetZoneIndex(connection.Locations[1].MapPosition.X);
                if (zone1 == zone2) { continue; }
                if (zone1 > zone2)
                {
                    int temp = zone2;
                    zone2 = zone1;
                    zone1 = temp;
                }

                if (generationParams.GateCount[zone1] == 0) { continue; }

                if (!connectionsBetweenZones[zone1].Any())
                {
                    connectionsBetweenZones[zone1].Add(connection);
                }
                else if (generationParams.GateCount[zone1] == 1)
                {
                    //if there's only one connection, place it at the center of the map
                    if (Math.Abs(connection.CenterPos.Y - Height / 2) < Math.Abs(connectionsBetweenZones[zone1].First().CenterPos.Y - Height / 2))
                    {
                        connectionsBetweenZones[zone1].Clear();
                        connectionsBetweenZones[zone1].Add(connection);
                    }
                }
                else if (connectionsBetweenZones[zone1].Count() < generationParams.GateCount[zone1])
                {
                    connectionsBetweenZones[zone1].Add(connection);
                }
            }

            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                int zone1 = GetZoneIndex(Connections[i].Locations[0].MapPosition.X);
                int zone2 = GetZoneIndex(Connections[i].Locations[1].MapPosition.X);
                if (zone1 == zone2) { continue; }
                if (zone1 == generationParams.DifficultyZones || zone2 == generationParams.DifficultyZones) { continue; }

                if (generationParams.GateCount[Math.Min(zone1, zone2)] == 0) { continue; }

                if (!connectionsBetweenZones[Math.Min(zone1, zone2)].Contains(Connections[i]))
                {
                    Connections.RemoveAt(i);
                }
                else
                {
                    var leftMostLocation =
                        Connections[i].Locations[0].MapPosition.X < Connections[i].Locations[1].MapPosition.X ?
                        Connections[i].Locations[0] :
                        Connections[i].Locations[1];
                    if (!leftMostLocation.Type.HasOutpost || leftMostLocation.Type.Identifier == "abandoned")
                    {
                        leftMostLocation.ChangeType(LocationType.Prefabs.OrderBy(lt => lt.Identifier).First(lt => lt.HasOutpost && lt.Identifier != "abandoned"));
                    }
                    leftMostLocation.IsGateBetweenBiomes = true;
                    Connections[i].Locked = true;
                }
            }

            foreach (Location location in Locations)
            {
                for (int i = location.Connections.Count - 1; i >= 0; i--)
                {
                    if (!Connections.Contains(location.Connections[i]))
                    {
                        location.Connections.RemoveAt(i);
                    }
                }
            }

            //remove orphans
            Locations.RemoveAll(l => !Connections.Any(c => c.Locations.Contains(l)));

            foreach (LocationConnection connection in Connections)
            {
                //float difficulty = GetLevelDifficulty(connection.CenterPos.X / Width);
                //connection.Difficulty = MathHelper.Clamp(difficulty + Rand.Range(-10.0f, 0.0f, Rand.RandSync.ServerAndClient), 1.2f, 100.0f);
                float difficulty = connection.CenterPos.X / Width * 100;
                float random = difficulty > 10 ? 5 : 0;
                connection.Difficulty = MathHelper.Clamp(difficulty + Rand.Range(-random, random, Rand.RandSync.ServerAndClient), 1.0f, 100.0f);
            }

            AssignBiomes();
            CreateEndLocation();
            
            foreach (Location location in Locations)
            {
                location.LevelData = new LevelData(location, MathHelper.Clamp(location.MapPosition.X / Width * 100, 0.0f, 100.0f));
                location.UnlockInitialMissions();
            }
            foreach (LocationConnection connection in Connections) 
            { 
                connection.LevelData = new LevelData(connection);
            }
        }

        partial void GenerateLocationConnectionVisuals();

        private int GetZoneIndex(float xPos)
        {
            float zoneWidth = Width / generationParams.DifficultyZones;
            return MathHelper.Clamp((int)Math.Floor(xPos / zoneWidth) + 1, 1, generationParams.DifficultyZones);
        }

        public Biome GetBiome(Vector2 mapPos)
        {
            return GetBiome(mapPos.X);
        }

        public Biome GetBiome(float xPos)
        {
            float zoneWidth = Width / generationParams.DifficultyZones;
            int zoneIndex = (int)Math.Floor(xPos / zoneWidth) + 1;
            zoneIndex = Math.Clamp(zoneIndex, 1, generationParams.DifficultyZones - 1);
            return Biome.Prefabs.FirstOrDefault(b => b.AllowedZones.Contains(zoneIndex));
        }

        private void AssignBiomes()
        {
            var biomes = Biome.Prefabs;
            float zoneWidth = Width / generationParams.DifficultyZones;

            List<Biome> allowedBiomes = new List<Biome>(10);
            for (int i = 0; i < generationParams.DifficultyZones; i++)
            {
                allowedBiomes.Clear();
                allowedBiomes.AddRange(biomes.Where(b => b.AllowedZones.Contains(generationParams.DifficultyZones - i)));
                float zoneX = zoneWidth * (generationParams.DifficultyZones - i);

                foreach (Location location in Locations)
                {
                    if (location.MapPosition.X < zoneX)
                    {
                        location.Biome = allowedBiomes[Rand.Range(0, allowedBiomes.Count, Rand.RandSync.ServerAndClient)];
                    }
                }
            }
            foreach (LocationConnection connection in Connections)
            {
                if (connection.Biome != null) { continue; }
                connection.Biome = connection.Locations[0].MapPosition.X > connection.Locations[1].MapPosition.X ? connection.Locations[0].Biome : connection.Locations[1].Biome;
            }

            System.Diagnostics.Debug.Assert(Locations.All(l => l.Biome != null));
            System.Diagnostics.Debug.Assert(Connections.All(c => c.Biome != null));
        }

        private void CreateEndLocation()
        {
            float zoneWidth = Width / generationParams.DifficultyZones;
            Vector2 endPos = new Vector2(Width - zoneWidth / 2, Height / 2);
            float closestDist = float.MaxValue;
            EndLocation = Locations.First();
            foreach (Location location in Locations)
            {
                float dist = Vector2.DistanceSquared(endPos, location.MapPosition);
                if (location.Biome.IsEndBiome && dist < closestDist)
                {
                    EndLocation = location;
                    closestDist = dist;
                }
            }

            Location previousToEndLocation = null;
            foreach (Location location in Locations)
            {
                if (!location.Biome.IsEndBiome && (previousToEndLocation == null || location.MapPosition.X > previousToEndLocation.MapPosition.X))
                {
                    previousToEndLocation = location;
                }
            }

            if (EndLocation == null || previousToEndLocation == null) { return; }

            //remove all locations from the end biome except the end location
            for (int i = Locations.Count - 1; i >= 0; i--)
            {
                if (Locations[i].Biome.IsEndBiome && Locations[i] != EndLocation)
                {
                    for (int j = Locations[i].Connections.Count - 1; j >= 0; j--)
                    {
                        if (j >= Locations[i].Connections.Count) { continue; }
                        var connection = Locations[i].Connections[j];
                        var otherLocation = connection.OtherLocation(Locations[i]);
                        Locations[i].Connections.RemoveAt(j);
                        otherLocation?.Connections.Remove(connection);
                        Connections.Remove(connection);
                    }
                    Locations.RemoveAt(i);
                }
            }

            //removed all connections from the second-to-last location, need to reconnect it
            if (!previousToEndLocation.Connections.Any())
            {
                Location connectTo = Locations.First();
                foreach (Location location in Locations)
                {
                    if (!location.Biome.IsEndBiome && location != previousToEndLocation && location.MapPosition.X > connectTo.MapPosition.X)
                    {
                        connectTo = location;
                    }
                }
                var newConnection = new LocationConnection(previousToEndLocation, connectTo)
                {
                    Biome = EndLocation.Biome,
                    Difficulty = 100.0f
                };
                Connections.Add(newConnection);
                previousToEndLocation.Connections.Add(newConnection);
                connectTo.Connections.Add(newConnection);
            }

            var endConnection = new LocationConnection(previousToEndLocation, EndLocation)
            {
                Biome = EndLocation.Biome,
                Difficulty = 100.0f
            };
            Connections.Add(endConnection);
            previousToEndLocation.Connections.Add(endConnection);
            EndLocation.Connections.Add(endConnection);
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


        #endregion Generation
        
        public void MoveToNextLocation()
        {
            if (SelectedConnection == null)
            {
                DebugConsole.ThrowError("Could not move to the next location (no connection selected).\n"+Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (SelectedLocation == null)
            {
                DebugConsole.ThrowError("Could not move to the next location (no location selected).\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            Location prevLocation = CurrentLocation;
            SelectedConnection.Passed = true;

            CurrentLocation = SelectedLocation;
            CurrentLocation.Discover();
            SelectedLocation = null;

            CurrentLocation.CreateStores();
            OnLocationChanged?.Invoke(prevLocation, CurrentLocation);

            if (GameMain.GameSession is { Campaign: { CampaignMetadata: { } metadata } })
            {
                metadata.SetValue("campaign.location.id".ToIdentifier(), CurrentLocationIndex);
                metadata.SetValue("campaign.location.name".ToIdentifier(), CurrentLocation.Name);
                metadata.SetValue("campaign.location.biome".ToIdentifier(), CurrentLocation.Biome?.Identifier ?? "null".ToIdentifier());
                metadata.SetValue("campaign.location.type".ToIdentifier(), CurrentLocation.Type?.Identifier ?? "null".ToIdentifier());
            }
        }

        public void SetLocation(int index)
        {
            if (index == -1)
            {
                CurrentLocation = null;
                return;
            }

            if (index < 0 || index >= Locations.Count)
            {
                DebugConsole.ThrowError("Location index out of bounds");
                return;
            }

            Location prevLocation = CurrentLocation;
            CurrentLocation = Locations[index];
            CurrentLocation.Discover();

            if (prevLocation != CurrentLocation)
            {
                var connection = CurrentLocation.Connections.Find(c => c.Locations.Contains(prevLocation));
                if (connection != null)
                {
                    connection.Passed = true;
                }
            }

            CurrentLocation.CreateStores();
            OnLocationChanged?.Invoke(prevLocation, CurrentLocation);
        }

        public void SelectLocation(int index)
        {
            if (index == -1)
            {
                SelectedLocation = null;
                SelectedConnection = null;

                OnLocationSelected?.Invoke(null, null);
                return;
            }

            if (index < 0 || index >= Locations.Count)
            {
                DebugConsole.ThrowError("Location index out of bounds");
                return;
            }

            SelectedLocation = Locations[index];
            var currentDisplayLocation = GameMain.GameSession?.Campaign?.GetCurrentDisplayLocation();
            SelectedConnection = 
                Connections.Find(c => c.Locations.Contains(currentDisplayLocation) && c.Locations.Contains(SelectedLocation)) ??
                Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            if (SelectedConnection?.Locked ?? false)
            {
                DebugConsole.ThrowError("A locked connection was selected - this should not be possible.\n" + Environment.StackTrace.CleanupStackTrace());
            }
            OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
        }

        public void SelectLocation(Location location)
        {
            if (!Locations.Contains(location))
            {
                string errorMsg = "Failed to select a location. " + (location?.Name ?? "null") + " not found in the map.";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Map.SelectLocation:LocationNotFound", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }

            SelectedLocation = location;
            SelectedConnection = Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            if (SelectedConnection?.Locked ?? false)
            {
                DebugConsole.ThrowError("A locked connection was selected - this should not be possible.\n" + Environment.StackTrace.CleanupStackTrace());
            }
            OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
        }

        public void SelectMission(IEnumerable<int> missionIndices)
        {
            if (CurrentLocation == null)
            {
                string errorMsg = "Failed to select a mission (current location not set).";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Map.SelectMission:CurrentLocationNotSet", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }

            CurrentLocation.SetSelectedMissionIndices(missionIndices);

            foreach (Mission selectedMission in CurrentLocation.SelectedMissions.ToList())
            {
                if (selectedMission.Locations[0] != CurrentLocation ||
                    selectedMission.Locations[1] != CurrentLocation)
                {
                    if (SelectedConnection == null) { return; }
                    //the destination must be the same as the destination of the mission
                    if (selectedMission.Locations[1] != SelectedLocation)
                    {
                        CurrentLocation.DeselectMission(selectedMission);
                    }
                }
            }

            OnMissionsSelected?.Invoke(SelectedConnection, CurrentLocation.SelectedMissions);
        }

        public void SelectRandomLocation(bool preferUndiscovered)
        {
            List<Location> nextLocations = CurrentLocation.Connections.Where(c => !c.Locked).Select(c => c.OtherLocation(CurrentLocation)).ToList();            
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

        public void ProgressWorld(CampaignMode.TransitionType transitionType, float roundDuration)
        {
            //one step per 10 minutes of play time
            int steps = (int)Math.Floor(roundDuration / (60.0f * 10.0f));
            if (transitionType == CampaignMode.TransitionType.ProgressToNextLocation || 
                transitionType == CampaignMode.TransitionType.ProgressToNextEmptyLocation)
            {
                //at least one step when progressing to the next location, regardless of how long the round took
                steps = Math.Max(1, steps);
            }
            steps = Math.Min(steps, 5);
            for (int i = 0; i < steps; i++)
            {
                ProgressWorld();
            }

            Radiation?.OnStep(steps);
        }

        private void ProgressWorld()
        {
            foreach (Location location in Locations)
            {
                if (location.Discovered)
                {
                    if (furthestDiscoveredLocation == null || 
                        location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                    {
                        furthestDiscoveredLocation = location;
                    }
                }
            }

            foreach (Location location in Locations)
            {
                if (location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                {
                    continue;
                }

                if (location == CurrentLocation || location == SelectedLocation || location.IsGateBetweenBiomes) { continue; }

                if (!ProgressLocationTypeChanges(location) && location.Discovered)
                {
                    location.UpdateStores();
                }
            }
        }

        private bool ProgressLocationTypeChanges(Location location)
        {
            location.TimeSinceLastTypeChange++;
            location.LocationTypeChangeCooldown--;

            if (location.PendingLocationTypeChange != null)
            {
                if (location.PendingLocationTypeChange.Value.typeChange.DetermineProbability(location) <= 0.0f)
                {
                    //remove pending type change if it's no longer allowed
                    location.PendingLocationTypeChange = null;
                }
                else
                {
                    location.PendingLocationTypeChange =
                        (location.PendingLocationTypeChange.Value.typeChange,
                        location.PendingLocationTypeChange.Value.delay - 1,
                        location.PendingLocationTypeChange.Value.parentMission);
                    if (location.PendingLocationTypeChange.Value.delay <= 0)
                    {
                        return ChangeLocationType(location, location.PendingLocationTypeChange.Value.typeChange);
                    }
                }
            }

            //find which types of locations this one can change to
            Dictionary<LocationTypeChange, float> allowedTypeChanges = new Dictionary<LocationTypeChange, float>();
            foreach (LocationTypeChange typeChange in location.Type.CanChangeTo)
            {
                float probability = typeChange.DetermineProbability(location);
                if (probability <= 0.0f) { continue; }
                allowedTypeChanges.Add(typeChange, probability);
            }

            //select a random type change
            if (Rand.Range(0.0f, 1.0f) < allowedTypeChanges.Sum(change => change.Value))
            {
                var selectedTypeChange =
                    ToolBox.SelectWeightedRandom(
                        allowedTypeChanges.Keys.ToList(),
                        allowedTypeChanges.Values.ToList(),
                        Rand.RandSync.Unsynced);
                if (selectedTypeChange != null)
                {
                    if (selectedTypeChange.RequiredDurationRange.X > 0)
                    {
                        location.PendingLocationTypeChange = 
                            (selectedTypeChange,
                            Rand.Range(selectedTypeChange.RequiredDurationRange.X, selectedTypeChange.RequiredDurationRange.Y),
                            null);
                    }
                    else
                    {
                        return ChangeLocationType(location, selectedTypeChange);
                    }
                    return false;
                }
            }

            foreach (LocationTypeChange typeChange in location.Type.CanChangeTo)
            {
                foreach (var requirement in typeChange.Requirements)
                {
                    if (requirement.AnyWithinDistance(location, requirement.RequiredProximityForProbabilityIncrease))
                    {
                        if (!location.ProximityTimer.ContainsKey(requirement)) { location.ProximityTimer[requirement] = 0; }
                        location.ProximityTimer[requirement] += 1;
                    }
                    else
                    {
                        location.ProximityTimer.Remove(requirement);
                    }
                }
            }

            return false;
        }

        public int DistanceToClosestLocationWithOutpost(Location startingLocation, out Location endingLocation)
        {
            if (startingLocation.Type.HasOutpost)
            {
                endingLocation = startingLocation;
                return 0;
            }

            int iterations = 0;
            int distance = 0;
            endingLocation = null;

            List<Location> testedLocations = new List<Location>();
            List<Location> locationsToTest = new List<Location> { startingLocation };

            while (endingLocation == null && iterations < 100)
            {
                List<Location> nextTestingBatch = new List<Location>();
                for (int i = 0; i < locationsToTest.Count; i++)
                {
                    Location testLocation = locationsToTest[i];
                    for (int j = 0; j < testLocation.Connections.Count; j++)
                    {
                        Location potentialOutpost = testLocation.Connections[j].OtherLocation(testLocation);
                        if (potentialOutpost.Type.HasOutpost)
                        {
                            distance = iterations + 1;
                            endingLocation = potentialOutpost;
                        }
                        else if (!testedLocations.Contains(potentialOutpost))
                        {
                            nextTestingBatch.Add(potentialOutpost);
                        }
                    }

                    testedLocations.Add(testLocation);
                }

                locationsToTest = nextTestingBatch;
                iterations++;
            }

            return distance;
        }

        private bool ChangeLocationType(Location location, LocationTypeChange change)
        {
            string prevName = location.Name;

            if (!LocationType.Prefabs.TryGet(change.ChangeToType, out var newType))
            {
                DebugConsole.ThrowError($"Failed to change the type of the location \"{location.Name}\". Location type \"{change.ChangeToType}\" not found.");
                return false;
            }

            if (newType.OutpostTeam != location.Type.OutpostTeam ||
                newType.HasOutpost != location.Type.HasOutpost)
            {
                location.ClearMissions();
            }
            location.ChangeType(newType);
            ChangeLocationTypeProjSpecific(location, prevName, change);
            foreach (var requirement in change.Requirements)
            {
                location.ProximityTimer.Remove(requirement);
            }
            location.TimeSinceLastTypeChange = 0;
            location.LocationTypeChangeCooldown = change.CooldownAfterChange;
            location.PendingLocationTypeChange = null;
            return true;
        }

        partial void ChangeLocationTypeProjSpecific(Location location, string prevName, LocationTypeChange change);

        partial void ClearAnimQueue();

        /// <summary>
        /// Load a previously saved map from an xml element
        /// </summary>
        public static Map Load(CampaignMode campaign, XElement element, CampaignSettings settings)
        {
            Map map = new Map(campaign, element, settings);
            map.LoadState(element, false);
#if CLIENT
            map.DrawOffset = -map.CurrentLocation.MapPosition;
#endif
            return map;
        }

        /// <summary>
        /// Load the state of an existing map from xml (current state of locations, where the crew is now, etc).
        /// </summary>
        public void LoadState(XElement element, bool showNotifications)
        {
            ClearAnimQueue();
            SetLocation(element.GetAttributeInt("currentlocation", 0));

            if (!Version.TryParse(element.GetAttributeString("version", ""), out _))
            {
                DebugConsole.ThrowError("Incompatible map save file, loading the game failed.");
                return;
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "location":
                        Location location = Locations[subElement.GetAttributeInt("i", 0)];
                        location.ProximityTimer.Clear();
                        for (int i = 0; i < location.Type.CanChangeTo.Count; i++)
                        {
                            for (int j = 0; j < location.Type.CanChangeTo[i].Requirements.Count; j++)
                            {
                                location.ProximityTimer.Add(location.Type.CanChangeTo[i].Requirements[j], subElement.GetAttributeInt("changetimer" + i + "-" + j, 0));
                            }
                        }
                        location.LoadLocationTypeChange(subElement);
                        if (subElement.GetAttributeBool("discovered", false))
                        {
                            location.Discover(checkTalents: false);
                        }
                        if (location.Discovered)
                        {
#if CLIENT
                            RemoveFogOfWar(location);
#endif
                            if (furthestDiscoveredLocation == null || location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                            {
                                furthestDiscoveredLocation = location;
                            }
                        }

                        Identifier locationType = subElement.GetAttributeIdentifier("type", Identifier.Empty);
                        string prevLocationName = location.Name;
                        LocationType prevLocationType = location.Type;
                        LocationType newLocationType = LocationType.Prefabs.Find(lt => lt.Identifier == locationType) ?? LocationType.Prefabs.First();
                        location.ChangeType(newLocationType);
                        if (showNotifications && prevLocationType != location.Type)
                        {
                            var change = prevLocationType.CanChangeTo.Find(c => c.ChangeToType == location.Type.Identifier);
                            if (change != null)
                            {
                                ChangeLocationTypeProjSpecific(location, prevLocationName, change);
                                location.TimeSinceLastTypeChange = 0;
                            }
                        }

                        location.LoadStores(subElement);
                        location.LoadMissions(subElement);

                        break;
                    case "connection":
                        int connectionIndex = subElement.GetAttributeInt("i", 0);
                        Connections[connectionIndex].Passed = subElement.GetAttributeBool("passed", false);
                        Connections[connectionIndex].Locked = subElement.GetAttributeBool("locked", false);
                        break;
                    case "radiation":
                        Radiation = new Radiation(this, generationParams.RadiationParams, subElement);
                        break;
                }
            }

            foreach (Location location in Locations)
            {
                location?.InstantiateLoadedMissions(this);
            }

            int currentLocationConnection = element.GetAttributeInt("currentlocationconnection", -1);
            if (currentLocationConnection >= 0)
            {
                Connections[currentLocationConnection].Locked = false;
                SelectLocation(Connections[currentLocationConnection].OtherLocation(CurrentLocation));
            }
            else
            {
                //this should not be possible, you can't enter non-outpost locations (= natural formations)
                if (CurrentLocation != null && !CurrentLocation.Type.HasOutpost && SelectedConnection == null)
                {
                    DebugConsole.AddWarning($"Error while loading campaign map state. Submarine in a location with no outpost ({CurrentLocation.Name}). Loading the first adjacent connection...");
                    SelectLocation(CurrentLocation.Connections[0].OtherLocation(CurrentLocation));
                }
            }
        }

        public void Save(XElement element)
        {
            XElement mapElement = new XElement("map");

            mapElement.Add(new XAttribute("version", GameMain.Version.ToString()));
            mapElement.Add(new XAttribute("currentlocation", CurrentLocationIndex));
            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                if (campaign.NextLevel != null && campaign.NextLevel.Type == LevelData.LevelType.LocationConnection)
                {
                    mapElement.Add(new XAttribute("currentlocationconnection", Connections.IndexOf(CurrentLocation.Connections.Find(c => c.LevelData == campaign.NextLevel))));
                }
                else if (Level.Loaded != null && Level.Loaded.Type == LevelData.LevelType.LocationConnection && !CurrentLocation.Type.HasOutpost)
                {
                    mapElement.Add(new XAttribute("currentlocationconnection", Connections.IndexOf(Connections.Find(c => c.LevelData == Level.Loaded.LevelData))));
                }
            }
            mapElement.Add(new XAttribute("width", Width));
            mapElement.Add(new XAttribute("height", Height));
            mapElement.Add(new XAttribute("selectedlocation", SelectedLocationIndex));
            mapElement.Add(new XAttribute("startlocation", Locations.IndexOf(StartLocation)));
            mapElement.Add(new XAttribute("endlocation", Locations.IndexOf(EndLocation)));
            mapElement.Add(new XAttribute("seed", Seed));

            for (int i = 0; i < Locations.Count; i++)
            {
                var location = Locations[i];
                var locationElement = location.Save(this, mapElement);
                locationElement.Add(new XAttribute("i", i));
            }

            for (int i = 0; i < Connections.Count; i++)
            {
                var connection = Connections[i];

                var connectionElement = new XElement("connection",
                    new XAttribute("passed", connection.Passed),
                    new XAttribute("locked", connection.Locked),
                    new XAttribute("difficulty", connection.Difficulty),
                    new XAttribute("biome", connection.Biome.Identifier),
                    new XAttribute("locations", Locations.IndexOf(connection.Locations[0]) + "," + Locations.IndexOf(connection.Locations[1])));
                connection.LevelData.Save(connectionElement);
                mapElement.Add(connectionElement);
            }

            if (Radiation != null)
            {
                mapElement.Add(Radiation.Save());
            }

            element.Add(mapElement);
        }

        public void Remove()
        {
            foreach (Location location in Locations)
            {
                location.Remove();
            }
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();
    }
}
