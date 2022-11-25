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
        public Action<LocationConnection, IEnumerable<Mission>> OnMissionsSelected;

        public readonly struct LocationChangeInfo
        {
            public readonly Location PrevLocation;
            public readonly Location NewLocation;

            public LocationChangeInfo(Location prevLocation, Location newLocation)
            {
                PrevLocation = prevLocation;
                NewLocation = newLocation;
            }
        }

        /// <summary>
        /// From -> To
        /// </summary>
        public readonly NamedEvent<LocationChangeInfo> OnLocationChanged = new NamedEvent<LocationChangeInfo>();

        private List<Location> endLocations = new List<Location>();
        public IReadOnlyList<Location> EndLocations { get { return endLocations; } }

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

        private readonly List<Location> locationsDiscovered = new List<Location>();
        private readonly List<Location> outpostsVisited = new List<Location>();

        public List<LocationConnection> Connections { get; private set; }

        public Radiation Radiation;

        private bool wasLocationDiscoveryOrderTracked = true;

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
        private Map(CampaignMode campaign, XElement element) : this(campaign.Settings)
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
                        Locations[i] = new Location(campaign, subElement);
                        break;
                    case "radiation":
                        Radiation = new Radiation(this, generationParams.RadiationParams, subElement)
                        {
                            Enabled = campaign.Settings.RadiationEnabled
                        };
                        break;
                }
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
                        string biomeId = subElement.GetAttributeString("biome", "");
                        connection.Biome =
                            Biome.Prefabs.FirstOrDefault(b => b.Identifier == biomeId) ??
                            Biome.Prefabs.FirstOrDefault(b => !b.OldIdentifier.IsEmpty && b.OldIdentifier == biomeId) ??
                            Biome.Prefabs.First();
                        connection.Difficulty = MathHelper.Clamp(connection.Difficulty, connection.Biome.MinDifficulty, connection.Biome.AdjustedMaxDifficulty);
                        connection.LevelData = new LevelData(subElement.Element("Level"), connection.Difficulty);
                        Connections.Add(connection);
                        connectionElements.Add(subElement);
                        break;
                }
            }

            //backwards compatibility: location biomes weren't saved (or used for anything) previously,
            //assign them if they haven't been assigned
            Random rand = new MTRandom(ToolBox.StringToInt(Seed));
            if (Locations.First().Biome == null)
            {
                AssignBiomes(rand);
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

            if (element.GetAttribute("endlocation") != null)
            {
                //backwards compatibility
                int endLocationIndex = element.GetAttributeInt("endlocation", -1);
                if (endLocationIndex > 0 && endLocationIndex < Locations.Count)
                {
                    endLocations.Add(Locations[endLocationIndex]);
                    Locations[endLocationIndex].LevelData.ReassignGenerationParams(Seed);
                }
                else
                {
                    DebugConsole.AddWarning($"Error while loading the map. End location index out of bounds (index: {endLocationIndex}, location count: {Locations.Count}).");
                }
            }
            else
            {
                int[] endLocationindices = element.GetAttributeIntArray("endlocations", Array.Empty<int>());
                foreach (int endLocationIndex in endLocationindices)
                {
                    if (endLocationIndex > 0 && endLocationIndex < Locations.Count)
                    {
                        endLocations.Add(Locations[endLocationIndex]);
                    }
                    else
                    {
                        DebugConsole.AddWarning($"Error while loading the map. End location index out of bounds (index: {endLocationIndex}, location count: {Locations.Count}).");
                    }
                }
            }

            if (!endLocations.Any())
            {
                DebugConsole.AddWarning($"Error while loading the map. No end location(s) found. Choosing the rightmost location as the end location...");
                Location endLocation = null;
                foreach (Location location in Locations)
                {
                    if (endLocation == null || location.MapPosition.X > endLocation.MapPosition.X)
                    {
                        endLocation = location;
                    }
                }
                endLocations.Add(endLocation);
            }

            System.Diagnostics.Debug.Assert(endLocations.First().Biome != null, "End location biome was null.");
            System.Diagnostics.Debug.Assert(endLocations.First().Biome.IsEndBiome, "The biome of the end location isn't the end biome.");

            //backwards compatibility (or support for loading maps created with mods that modify the end biome setup):
            //if there's too few end locations, create more
            int missingOutpostCount = endLocations.First().Biome.EndBiomeLocationCount - endLocations.Count;

            Location firstEndLocation = EndLocations[0];
            for (int i = 0; i < missingOutpostCount; i++)
            {
                Vector2 mapPos = new Vector2(
                    MathHelper.Lerp(firstEndLocation.MapPosition.X, Width, MathHelper.Lerp(0.2f, 0.8f, i / (float)missingOutpostCount)),
                    Height * MathHelper.Lerp(0.2f, 1.0f, (float)rand.NextDouble()));
                var newEndLocation = new Location(mapPos, generationParams.DifficultyZones, rand, forceLocationType: firstEndLocation.Type, existingLocations: Locations)
                {
                    Biome = endLocations.First().Biome
                };
                newEndLocation.LevelData = new LevelData(newEndLocation, difficulty: 100.0f);
                Locations.Add(newEndLocation);
                endLocations.Add(newEndLocation);
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

            foreach (var endLocation in EndLocations)
            {
                if (endLocation.Type?.ForceLocationName != null &&
                    !endLocation.Type.ForceLocationName.IsNullOrEmpty())
                {
                    endLocation.ForceName(endLocation.Type.ForceLocationName.Value);
                }
            }

            AssignEndLocationLevelData();

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
        public Map(CampaignMode campaign, string seed) : this(campaign.Settings)
        {
            Seed = seed;
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));

            Generate(campaign);

            if (Locations.Count == 0)
            {
                throw new Exception($"Generating a campaign map failed (no locations created). Width: {Width}, height: {Height}");
            }

            foreach (Location location in Locations)
            {
                if (location.Type.Identifier != "outpost") { continue; }
                SetStartLocation(location);
            }
            //if no outpost was found (using a mod that replaces the outpost location type?), find any type of outpost
            if (CurrentLocation == null)
            {
                foreach (Location location in Locations)
                {
                    if (!location.Type.HasOutpost) { continue; }
                    SetStartLocation(location);
                }
            }
            
            void SetStartLocation(Location location)
            {
                if (CurrentLocation == null || location.MapPosition.X < CurrentLocation.MapPosition.X)
                {
                    CurrentLocation = StartLocation = furthestDiscoveredLocation = location;
                    StartLocation.SecondaryFaction = null;             
                    var startOutpostFaction = campaign?.Factions.FirstOrDefault(f => f.Prefab.StartOutpost);
                    if (startOutpostFaction != null)
                    {
                        StartLocation.Faction = startOutpostFaction;
                        foreach (var connection in StartLocation.Connections)
                        {
                            var otherLocation = connection.OtherLocation(StartLocation);
                            if (otherLocation.HasOutpost() && otherLocation.Type.OutpostTeam == CharacterTeamType.FriendlyNPC)
                            {
                                otherLocation.Faction = startOutpostFaction;
                            }
                        }
                    }                    
                }
            }

            System.Diagnostics.Debug.Assert(StartLocation != null, "Start location not assigned after level generation.");

            int loops = campaign.CampaignMetadata.GetInt("campaign.endings".ToIdentifier(), 0);
            if (loops == 0 && (campaign.Settings.Difficulty == GameDifficulty.Easy || campaign.Settings.Difficulty == GameDifficulty.Medium))
            {
                if (StartLocation != null)
                {
                    StartLocation.LevelData = new LevelData(StartLocation, 0);
                }

                //ensure all paths from the starting location have 0 difficulty to make the 1st campaign round very easy
                foreach (var locationConnection in StartLocation.Connections)
                {
                    if (locationConnection.Difficulty > 0.0f)
                    {
                        locationConnection.Difficulty = 0.0f;
                        locationConnection.LevelData = new LevelData(locationConnection);
                    }
                }
            }

            if (campaign.IsSinglePlayer && campaign.Settings.TutorialEnabled && LocationType.Prefabs.TryGet("tutorialoutpost", out var tutorialOutpost))
            {
                CurrentLocation.ChangeType(campaign, tutorialOutpost);
            }
            Discover(CurrentLocation);
            Visit(CurrentLocation);
            CurrentLocation.CreateStores();

            foreach (var location in Locations)
            {
                location.UnlockInitialMissions();
            }

            InitProjectSpecific();
        }

        partial void InitProjectSpecific();

        #region Generation

        private void Generate(CampaignMode campaign)
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
            bool possibleStartOutpostCreated = false;
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
                    if (!possibleStartOutpostCreated)
                    {
                        float zoneWidth = Width / generationParams.DifficultyZones;
                        float threshold = zoneWidth * 0.1f;
                        if (position.X < threshold)
                        {
                            LocationType.Prefabs.TryGet("outpost", out forceLocationType);
                            possibleStartOutpostCreated = true;
                        }
                    }

                    if (forceLocationType == null)
                    {
                        foreach (LocationType locationType in LocationType.Prefabs.OrderBy(lt => lt.Identifier))
                        {
                            if (locationType.MinCountPerZone.TryGetValue(zone, out int minCount) && locationsPerZone[zone].Count(l => l.Type == locationType) < minCount)
                            {
                                forceLocationType = locationType;
                                break;
                            }
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

            //make sure the connections are in the same order on the locations and the Connections list
            //otherwise their order will change when loading the game (as they're added to the locations in the same order they're loaded)
            foreach (var location in Locations)
            {
                location.Connections.Sort((c1, c2) => Connections.IndexOf(c1).CompareTo(Connections.IndexOf(c2)));
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
                    (zone1, zone2) = (zone2, zone1);
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
                else if (connectionsBetweenZones[zone1].Count() < generationParams.GateCount[zone1] && 
                    connectionsBetweenZones[zone1].None(c => c.Locations.Contains(connection.Locations[0]) || c.Locations.Contains(connection.Locations[1])))
                {
                    connectionsBetweenZones[zone1].Add(connection);
                }
            }

            var gateFactions = campaign.Factions.Where(f => f.Prefab.ControlledOutpostPercentage > 0).OrderBy(f => f.Prefab.Identifier).ToList();
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                int zone1 = GetZoneIndex(Connections[i].Locations[0].MapPosition.X);
                int zone2 = GetZoneIndex(Connections[i].Locations[1].MapPosition.X);
                if (zone1 == zone2) { continue; }
                if (zone1 == generationParams.DifficultyZones || zone2 == generationParams.DifficultyZones) { continue; }

                int leftZone = Math.Min(zone1, zone2);
                if (generationParams.GateCount[leftZone] == 0) { continue; }
                if (!connectionsBetweenZones[leftZone].Contains(Connections[i]))
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
                        leftMostLocation.ChangeType(
                            campaign,
                            LocationType.Prefabs.OrderBy(lt => lt.Identifier).First(lt => lt.HasOutpost && lt.Identifier != "abandoned"),
                            createStores: false);
                    }
                    leftMostLocation.IsGateBetweenBiomes = true;
                    Connections[i].Locked = true;

                    if (leftMostLocation.Type.HasOutpost && campaign != null && gateFactions.Any())
                    {
                        leftMostLocation.Faction = gateFactions[connectionsBetweenZones[leftZone].IndexOf(Connections[i]) % gateFactions.Count];
                    }
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

            //make sure the location at the right side of the gate between biomes isn't a dead-end
            //those may sometimes generate if all the connections of the right-side location lead to the previous biome
            //(i.e. a situation where the adjacent locations happen to be at the left side of the border of the biomes, see see Regalis11/Barotrauma#10047)
            for (int i = 0; i < Connections.Count; i++)
            {
                var connection = Connections[i];
                if (!connection.Locked) { continue; }
                var rightMostLocation =
                    connection.Locations[0].MapPosition.X > connection.Locations[1].MapPosition.X ?
                    connection.Locations[0] :
                    connection.Locations[1];

                //if there's only one connection (= the connection between biomes), create a new connection to the closest location to the right
                if (rightMostLocation.Connections.Count == 1)
                {
                    Location closestLocation = null;
                    float closestDist = float.PositiveInfinity;
                    foreach (Location otherLocation in Locations)
                    {
                        if (otherLocation == rightMostLocation || otherLocation.MapPosition.X < rightMostLocation.MapPosition.X) { continue; }
                        float dist = Vector2.DistanceSquared(rightMostLocation.MapPosition, otherLocation.MapPosition);
                        if (dist < closestDist || closestLocation == null)
                        {
                            closestLocation = otherLocation;
                            closestDist = dist;
                        }
                    }

                    var newConnection = new LocationConnection(rightMostLocation, closestLocation);
                    rightMostLocation.Connections.Add(newConnection);
                    closestLocation.Connections.Add(newConnection);
                    Connections.Add(newConnection);
                    GenerateLocationConnectionVisuals(newConnection);
                }
            }

            //remove orphans
            Locations.RemoveAll(l => !Connections.Any(c => c.Locations.Contains(l)));

            AssignBiomes(new MTRandom(ToolBox.StringToInt(Seed)));

            foreach (LocationConnection connection in Connections)
            {
                if (connection.Locations.Any(l => l.IsGateBetweenBiomes))
                {
                    connection.Difficulty = Math.Min(connection.Locations.Min(l => l.Biome.ActualMaxDifficulty), connection.Biome.AdjustedMaxDifficulty);
                }
                else
                {
                    connection.Difficulty = CalculateDifficulty(connection.CenterPos.X, connection.Biome);
                }
            }

            foreach (Location location in Locations)
            {
                location.LevelData = new LevelData(location, CalculateDifficulty(location.MapPosition.X, location.Biome));
                if (location.Type.HasOutpost && campaign != null && location.Type.OutpostTeam == CharacterTeamType.FriendlyNPC)
                {
                    location.Faction ??= campaign.GetRandomFaction(Rand.RandSync.ServerAndClient);
                    location.SecondaryFaction ??= campaign.GetRandomSecondaryFaction(Rand.RandSync.ServerAndClient);
                }
                location.CreateStores(force: true);
            }

            foreach (LocationConnection connection in Connections) 
            { 
                connection.LevelData = new LevelData(connection);
            }

            CreateEndLocation(campaign);
            
            float CalculateDifficulty(float mapPosition, Biome biome)
            {
                float settingsFactor = campaign.Settings.LevelDifficultyMultiplier;
                float minDifficulty = 0;
                float maxDifficulty = 100;
                float difficulty = mapPosition / Width * 100;
                System.Diagnostics.Debug.Assert(biome != null);
                if (biome != null)
                {
                    minDifficulty = biome.MinDifficulty;
                    maxDifficulty = biome.AdjustedMaxDifficulty;
                    float diff = 1 - settingsFactor;
                    difficulty *= 1 - (1f / biome.AllowedZones.Max() * diff);
                }
                return MathHelper.Clamp(difficulty, minDifficulty, maxDifficulty);
            }
        }

        partial void GenerateAllLocationConnectionVisuals();

        partial void GenerateLocationConnectionVisuals(LocationConnection connection);

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

        private void AssignBiomes(Random rand)
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
                        location.Biome = allowedBiomes[rand.Next() % allowedBiomes.Count];
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

        private void CreateEndLocation(CampaignMode campaign)
        {
            float zoneWidth = Width / generationParams.DifficultyZones;
            Vector2 endPos = new Vector2(Width - zoneWidth * 0.7f, Height / 2);
            float closestDist = float.MaxValue;
            var endLocation = Locations.First();
            foreach (Location location in Locations)
            {
                float dist = Vector2.DistanceSquared(endPos, location.MapPosition);
                if (location.Biome.IsEndBiome && dist < closestDist)
                {
                    endLocation = location;
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

            if (endLocation == null || previousToEndLocation == null) { return; }

            endLocations = new List<Location>() { endLocation };
            if (endLocation.Biome.EndBiomeLocationCount > 1)
            {
                FindConnectedEndLocations(endLocation);

                void FindConnectedEndLocations(Location currLocation)
                {
                    if (endLocations.Count >= endLocation.Biome.EndBiomeLocationCount) { return; }
                    foreach (var connection in currLocation.Connections)
                    {
                        if (connection.Biome != endLocation.Biome) { continue; }
                        var otherLocation = connection.OtherLocation(currLocation);
                        if (otherLocation != null && !endLocations.Contains(otherLocation))
                        {
                            if (endLocations.Count >= endLocation.Biome.EndBiomeLocationCount) { return; }  
                            endLocations.Add(otherLocation);                          
                            FindConnectedEndLocations(otherLocation);                            
                        }
                    }
                }
            }

            if (LocationType.Prefabs.TryGet("none", out LocationType locationType))
            {
                previousToEndLocation.ChangeType(campaign, locationType, createStores: false);
            }

            //remove all locations from the end biome except the end location
            for (int i = Locations.Count - 1; i >= 0; i--)
            {
                if (Locations[i].Biome.IsEndBiome)
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
                    if (!endLocations.Contains(Locations[i]))
                    {
                        Locations.RemoveAt(i);
                    }
                }
            }

            //removed all connections from the second-to-last location, need to reconnect it
            if (previousToEndLocation.Connections.None())
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
                    Biome = endLocation.Biome,
                    Difficulty = 100.0f
                };
                newConnection.LevelData = new LevelData(newConnection);
                Connections.Add(newConnection);
                previousToEndLocation.Connections.Add(newConnection);
                connectTo.Connections.Add(newConnection);
            }

            var endConnection = new LocationConnection(previousToEndLocation, endLocation)
            {
                Biome = endLocation.Biome,
                Difficulty = 100.0f
            };
            endConnection.LevelData = new LevelData(endConnection);
            Connections.Add(endConnection);
            previousToEndLocation.Connections.Add(endConnection);
            endLocation.Connections.Add(endConnection);

            AssignEndLocationLevelData();
        }

        private void AssignEndLocationLevelData()
        {
            for (int i = 0; i < endLocations.Count; i++)
            {
                var outpostParams = OutpostGenerationParams.OutpostParams.FirstOrDefault(p => p.ForceToEndLocationIndex == i);
                if (outpostParams != null)
                {
                    endLocations[i].LevelData.ForceOutpostGenerationParams = outpostParams;
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


        #endregion Generation
        
        public void MoveToNextLocation()
        {
            if (SelectedConnection == null)
            {
                if (!endLocations.Contains(CurrentLocation))
                {
                    DebugConsole.ThrowError("Could not move to the next location (no connection selected).\n" + Environment.StackTrace.CleanupStackTrace());
                    return;
                }
            }
            if (SelectedLocation == null)
            {
                if (endLocations.Contains(CurrentLocation))
                {
                    int currentEndLocationIndex = endLocations.IndexOf(CurrentLocation);
                    if (currentEndLocationIndex < endLocations.Count - 1)
                    {
                        //more end locations to go, progress to the next one
                        SelectedLocation = endLocations[currentEndLocationIndex + 1];
                    }
                    else
                    {
                        //at the last end location, end of campaign
                        SelectedLocation = StartLocation;
                    }
                }
                else
                {
                    DebugConsole.ThrowError("Could not move to the next location (no connection selected).\n" + Environment.StackTrace.CleanupStackTrace());
                    return;
                }
            }

            Location prevLocation = CurrentLocation;
            if (SelectedConnection != null)
            {
                SelectedConnection.Passed = true;
            }

            CurrentLocation = SelectedLocation;
            Discover(CurrentLocation);
            Visit(CurrentLocation);
            SelectedLocation = null;

            CurrentLocation.CreateStores();
            OnLocationChanged?.Invoke(new LocationChangeInfo(prevLocation, CurrentLocation));

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
            Discover(CurrentLocation);

            CurrentLocation.CreateStores();
            if (prevLocation != CurrentLocation)
            {
                var connection = CurrentLocation.Connections.Find(c => c.Locations.Contains(prevLocation));
                if (connection != null)
                {
                    connection.Passed = true;
                }
                OnLocationChanged?.Invoke(new LocationChangeInfo(prevLocation, CurrentLocation));
            }
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

            Location prevSelected = SelectedLocation;
            SelectedLocation = Locations[index];
            var currentDisplayLocation = GameMain.GameSession?.Campaign?.GetCurrentDisplayLocation();
            SelectedConnection = 
                Connections.Find(c => c.Locations.Contains(currentDisplayLocation) && c.Locations.Contains(SelectedLocation)) ??
                Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            if (SelectedConnection?.Locked ?? false)
            {
                DebugConsole.ThrowError("A locked connection was selected - this should not be possible.\n" + Environment.StackTrace.CleanupStackTrace());
            }
            if (prevSelected != SelectedLocation)
            {
                OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
            }
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

            Location prevSelected = SelectedLocation;
            SelectedLocation = location;
            SelectedConnection = Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            if (SelectedConnection?.Locked ?? false)
            {
                DebugConsole.ThrowError("A locked connection was selected - this should not be possible.\n" + Environment.StackTrace.CleanupStackTrace());
            }
            if (prevSelected != SelectedLocation)
            {
                OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
            }
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

            if (!missionIndices.SequenceEqual(GetSelectedMissionIndices()))
            {
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

        public void ProgressWorld(CampaignMode campaign, CampaignMode.TransitionType transitionType, float roundDuration)
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
                ProgressWorld(campaign);
            }

            // always update specials every step
            for (int i = 0; i < Math.Max(1, steps); i++)
            {
                foreach (Location location in Locations)
                {
                    if (!location.Discovered) { continue; }
                    location.UpdateSpecials();
                }
            }

            Radiation?.OnStep(steps);
        }

        private void ProgressWorld(CampaignMode campaign)
        {
            foreach (Location location in Locations)
            {
                location.LevelData.EventsExhausted = false;
                if (location.Discovered)
                {
                    if (furthestDiscoveredLocation == null ||
                        location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                    {
                        furthestDiscoveredLocation = location;
                    }
                }
            }
            foreach (LocationConnection connection in Connections)
            {
                connection.LevelData.EventsExhausted = false;
            }

            foreach (Location location in Locations)
            {
                if (location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                {
                    continue;
                }

                if (location == CurrentLocation || location == SelectedLocation || location.IsGateBetweenBiomes) { continue; }

                if (!ProgressLocationTypeChanges(campaign, location) && location.Discovered)
                {
                    location.UpdateStores();
                }
            }
        }

        private bool ProgressLocationTypeChanges(CampaignMode campaign, Location location)
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
                        return ChangeLocationType(campaign, location, location.PendingLocationTypeChange.Value.typeChange);
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
                        return ChangeLocationType(campaign, location, selectedTypeChange);
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

        private bool ChangeLocationType(CampaignMode campaign, Location location, LocationTypeChange change)
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
            location.ChangeType(campaign, newType);
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

        public void Discover(Location location, bool checkTalents = true)
        {
            if (location is null) { return; }
            if (locationsDiscovered.Contains(location)) { return; }
            locationsDiscovered.Add(location);
            if (checkTalents)
            {
                GameSession.GetSessionCrewCharacters(CharacterType.Both).ForEach(c => c.CheckTalents(AbilityEffectType.OnLocationDiscovered, new Location.AbilityLocation(location)));
            }
        }

        public void Visit(Location location)
        {
            if (location is null) { return; }
            if (!location.HasOutpost()) { return; }
            if (outpostsVisited.Contains(location)) { return; }
            outpostsVisited.Add(location);
        }

        public void ClearLocationHistory()
        {
            locationsDiscovered.Clear();
            outpostsVisited.Clear();
        }

        public int? GetDiscoveryIndex(Location location)
        {
            if (!wasLocationDiscoveryOrderTracked) { return null; }
            if (location is null) { return -1; }
            return locationsDiscovered.IndexOf(location);
        }

        public int? GetVisitIndex(Location location)
        {
            if (!wasLocationDiscoveryOrderTracked) { return null; }
            if (location is null) { return -1; }
            return outpostsVisited.IndexOf(location);
        }

        public bool IsDiscovered(Location location)
        {
            if (location is null) { return false; }
            return locationsDiscovered.Contains(location);
        }

        /// <summary>
        /// Load a previously saved map from an xml element
        /// </summary>
        public static Map Load(CampaignMode campaign, XElement element)
        {
            Map map = new Map(campaign, element);
            map.LoadState(campaign, element, false);
#if CLIENT
            map.DrawOffset = -map.CurrentLocation.MapPosition;
#endif
            return map;
        }

        /// <summary>
        /// Load the state of an existing map from xml (current state of locations, where the crew is now, etc).
        /// </summary>
        public void LoadState(CampaignMode campaign, XElement element, bool showNotifications)
        {
            ClearAnimQueue();
            SetLocation(element.GetAttributeInt("currentlocation", 0));

            if (!Version.TryParse(element.GetAttributeString("version", ""), out Version version))
            {
                DebugConsole.ThrowError("Incompatible map save file, loading the game failed.");
                return;
            }

            ClearLocationHistory();
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

                        // Backwards compatibility
                        if (subElement.GetAttributeBool("discovered", false))
                        {
                            Discover(location);
                            wasLocationDiscoveryOrderTracked = false;
                        }

                        Identifier locationType = subElement.GetAttributeIdentifier("type", Identifier.Empty);
                        string prevLocationName = location.Name;
                        LocationType prevLocationType = location.Type;
                        LocationType newLocationType = LocationType.Prefabs.Find(lt => lt.Identifier == locationType) ?? LocationType.Prefabs.First();
                        location.ChangeType(campaign, newLocationType);
                        if (showNotifications && prevLocationType != location.Type)
                        {
                            var change = prevLocationType.CanChangeTo.Find(c => c.ChangeToType == location.Type.Identifier);
                            if (change != null)
                            {
                                ChangeLocationTypeProjSpecific(location, prevLocationName, change);
                                location.TimeSinceLastTypeChange = 0;
                            }
                        }

                        var factionIdentifier = subElement.GetAttributeIdentifier("faction", Identifier.Empty);
                        location.Faction = factionIdentifier.IsEmpty ? null : campaign.Factions.Find(f => f.Prefab.Identifier == factionIdentifier);

                        var secondaryFactionIdentifier = subElement.GetAttributeIdentifier("secondaryfaction", Identifier.Empty);
                        location.SecondaryFaction = secondaryFactionIdentifier.IsEmpty ? null : campaign.Factions.Find(f => f.Prefab.Identifier == secondaryFactionIdentifier);

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
                    case "discovered":
                        foreach (var childElement in subElement.GetChildElements("location"))
                        {
                            int index = childElement.GetAttributeInt("i", -1);
                            if (index < 0) { continue; }
                            if (Locations[index] is not Location l) { continue; }
                            Discover(l);
                        }
                        break;
                    case "visited":
                        foreach (var childElement in subElement.GetChildElements("location"))
                        {
                            int index = childElement.GetAttributeInt("i", -1);
                            if (index < 0) { continue; }
                            if (Locations[index] is not Location l) { continue; }
                            Visit(l);
                        }
                        break;
                }
            }

            void Discover(Location location)
            {
                this.Discover(location, checkTalents: false);
#if CLIENT
                RemoveFogOfWar(location);
#endif
                if (furthestDiscoveredLocation == null || location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                {
                    furthestDiscoveredLocation = location;
                }
            }

            foreach (Location location in Locations)
            {
                location?.InstantiateLoadedMissions(this);
            }

#if RELEASE
           TODO: MAKE SURE THE VERSION NUMBER BELOW IS CORRECT FOR THE FULL RELEASE (OR WHICHEVER UPDATE WE ADD THE FACTIONS IN)
#endif
            //backwards compatibility:
            //if the save is from a version prior to the addition of faction-specific outposts, assign factions
            if (version < new Version(1, 0) && Locations.None(l => l.Faction != null || l.SecondaryFaction != null))
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));
                foreach (Location location in Locations)
                {
                    if (location.Type.HasOutpost && campaign != null && location.Type.OutpostTeam == CharacterTeamType.FriendlyNPC)
                    {
                        location.Faction = campaign.GetRandomFaction(Rand.RandSync.ServerAndClient);
                        if (location != StartLocation)
                        {
                            location.SecondaryFaction = campaign.GetRandomSecondaryFaction(Rand.RandSync.ServerAndClient);
                        }
                    }
                }
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
            mapElement.Add(new XAttribute("endlocations", string.Join(',', EndLocations.Select(e => Locations.IndexOf(e)))));
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

            if (locationsDiscovered.Any())
            {
                var discoveryElement = new XElement("discovered");
                foreach (Location location in locationsDiscovered)
                {
                    int index = Locations.IndexOf(location);
                    var locationElement = new XElement("location", new XAttribute("i", index));
                    discoveryElement.Add(locationElement);
                }
                mapElement.Add(discoveryElement);
            }

            if (outpostsVisited.Any())
            {
                var visitElement = new XElement("visited");
                foreach (Location location in outpostsVisited)
                {
                    int index = Locations.IndexOf(location);
                    var locationElement = new XElement("location", new XAttribute("i", index));
                    visitElement.Add(locationElement);
                }
                mapElement.Add(visitElement);
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
