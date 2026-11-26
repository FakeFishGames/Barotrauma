using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    public partial class Map
    {
        public bool AllowDebugTeleport;

        private readonly MapGenerationParams generationParams;
        public MapGenerationParams GenerationParams => generationParams;

        private Location furthestDiscoveredLocation;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public Action<Location, LocationConnection> OnLocationSelected;
        public Action<LocationConnection, IEnumerable<Mission>> OnMissionsSelected;

        public int WorldStepCount { get; private set; }

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

        public Location LocationToTeleportTo { get; set; }

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

        /// <summary>
        /// Discovered location = location that's visible on the map, not necessarily visited. Locations can be discovered by e.g. a mission unlocking in some remote location.
        /// </summary>
        private readonly List<Location> locationsDiscovered = [];
        /// <summary>
        /// Visited location = a location the player has either actually visited, or a "remotely visited" location.
        /// </summary>
        private readonly List<Location> locationsVisited = [];
        /// <summary>
        /// Locations that have been marked as visited without the players actually going there (e.g. reached by an expedition).
        /// Subset of <see cref="locationsVisited"/>. Removed from here when the players visit the location themselves.
        /// </summary>
        private readonly HashSet<Location> locationsVisitedRemotely = [];

        public List<LocationConnection> Connections { get; private set; }

        public Radiation Radiation;

        private bool trackedLocationDiscoveryAndVisitOrder = true;
        
        private IOrderedEnumerable<Biome> _orderedBiomes;
        public IOrderedEnumerable<Biome> OrderedBiomes => _orderedBiomes ??= Biome.Prefabs.GetOrdered();

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
                    Enabled = settings.RadiationEnabled && !DetermineRadiationDisabledByPlugin()
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
                            Enabled = campaign.Settings.RadiationEnabled && !DetermineRadiationDisabledByPlugin()
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
            if (startLocationindex >= 0 && startLocationindex < Locations.Count)
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
                if (endLocationIndex >= 0 && endLocationIndex < Locations.Count)
                {
                    endLocations.Add(Locations[endLocationIndex]);
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
                    if (endLocationIndex >= 0 && endLocationIndex < Locations.Count)
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
            Location firstEndLocation = EndLocations[0];
            Biome endBiome = firstEndLocation.Biome;
            int missingOutpostCount = endBiome.EndBiomeLocationCount - endLocations.Count;

            for (int i = 0; i < missingOutpostCount; i++)
            {
                Vector2 mapPos = new Vector2(
                    MathHelper.Lerp(firstEndLocation.MapPosition.X, Width, MathHelper.Lerp(0.2f, 0.8f, i / (float)missingOutpostCount)),
                    Height * MathHelper.Lerp(0.2f, 1.0f, (float)rand.NextDouble()));
                var newEndLocation = new Location(mapPos, generationParams.DifficultyZones, endBiome.Identifier, rand, forceLocationType: firstEndLocation.Type, existingLocations: Locations);
                newEndLocation.Biome = endBiome;
                newEndLocation.LevelData = new LevelData(newEndLocation, this, difficulty: 100.0f);
                Locations.Add(newEndLocation);
                endLocations.Add(newEndLocation);
            }

            //backwards compatibility: if the map contained the now-removed lairs and has no hunting grounds, create some hunting grounds
            if (lairsFound && !Connections.Any(c => c.LevelData.HasHuntingGrounds))
            {
                for (int i = 0; i < Connections.Count; i++)
                {
                    Connections[i].LevelData.HasHuntingGrounds = Rand.Range(0.0f, 1.0f) < Connections[i].Difficulty / 100.0f * generationParams.MaxHuntingGroundsProbability;
                    connectionElements[i].SetAttributeValue("hashuntinggrounds", true);
                }
            }

            AssignEndLocationLevelData(campaign);

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

            FindStartLocation(l => l.Type.Identifier == "outpost");
            //if no outpost was found (using a mod that replaces the outpost location type?), find any type of outpost
            if (CurrentLocation == null)
            {
                FindStartLocation(l => l.Type.HasOutpost && l.Type.OutpostTeam == CharacterTeamType.FriendlyNPC);
            }

            void FindStartLocation(Func<Location, bool> predicate)
            {
                foreach (Location location in Locations)
                {
                    if (!predicate(location)) { continue; }
                    if (CurrentLocation == null || location.MapPosition.X < CurrentLocation.MapPosition.X)
                    {
                        CurrentLocation = StartLocation = furthestDiscoveredLocation = location;
                    }
                }
            }

            StartLocation.SecondaryFaction = null;
            var startOutpostFaction = campaign?.Factions.FirstOrDefault(f => f.Prefab.StartOutpost);
            if (startOutpostFaction != null)
            {
                StartLocation.Faction = startOutpostFaction;
            }
            foreach (var connection in StartLocation.Connections)
            {
                //force locations adjacent to the start location to have an outpost
                //non-inhabited locations seem to be confusing to new players, particularly
                //on the first round/mission when they still don't know how transitions between levels work
                var otherLocation = connection.OtherLocation(StartLocation);
                if (!otherLocation.HasOutpost())
                {
                    if (LocationType.Prefabs.TryGet("outpost".ToIdentifier(), out LocationType outpostLocationType))
                    {
                        otherLocation.ChangeType(campaign, outpostLocationType, createStores: false);
                    }
                }

                if (otherLocation.HasOutpost() &&
                    otherLocation.Type.OutpostTeam == CharacterTeamType.FriendlyNPC &&
                    otherLocation.Type.Faction.IsEmpty)
                {
                    otherLocation.Faction = startOutpostFaction;
                }
            }

            System.Diagnostics.Debug.Assert(StartLocation != null, "Start location not assigned after level generation.");

            int loops = campaign.CampaignMetadata.GetInt("campaign.endings".ToIdentifier(), 0);
            if (loops == 0 && (campaign.Settings.WorldHostility == WorldHostilityOption.Low || campaign.Settings.WorldHostility == WorldHostilityOption.Medium))
            {
                if (StartLocation != null)
                {
                    StartLocation.LevelData = new LevelData(StartLocation, this, 0);
                }

                //ensure all paths from the starting location have 0 difficulty to make the 1st campaign round very easy
                foreach (var locationConnection in StartLocation.Connections)
                {
                    if (locationConnection.Difficulty > 0.0f)
                    {
                        locationConnection.Difficulty = 0.0f;
                        locationConnection.LevelData = new LevelData(locationConnection, this);
                    }
                }
            }

            if (campaign.IsSinglePlayer && campaign.Settings.TutorialEnabled && LocationType.Prefabs.TryGet("tutorialoutpost", out var tutorialOutpost))
            {
                CurrentLocation.ChangeType(campaign, tutorialOutpost);
            }
            else
            {
                var forceStartOutpostType = LocationType.Prefabs.Where(lt => lt.ForceAsStartOutpost).GetRandom(Rand.RandSync.ServerAndClient);
                if (forceStartOutpostType != null)
                {
                    CurrentLocation.ChangeType(campaign, forceStartOutpostType);
                }
            }

            // Inform plugins of map generation having finished
            foreach (var hook in PluginData.GetHooks<PluginCampaignMapGenerationFinishedDelegate>())
            {
                hook(campaign, this, Locations);
            }

            Discover(CurrentLocation);
            Visit(CurrentLocation);
            CurrentLocation.CreateStores();

            foreach (var location in Locations)
            {
                location.OriginalFaction = location.Faction;
                //the type may have changed during generation (end locations, biome gates, start outpost, etc)
                location.OriginalType = location.Type;
                location.UnlockInitialMissions();
            }

            CheckForErrors();

            InitProjectSpecific();
        }

        private bool DetermineRadiationDisabledByPlugin()
        {
            bool radiationDisabled = false;
            foreach (var hook in PluginData.GetHooks<PluginShouldDisableJovianRadiation>())
            {
                radiationDisabled = hook(out RichString _);
            }

            return radiationDisabled;
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

            //add more dense voronoi sites at the boundaries between biomes, to ensure we get lots of locations and potential connections between them
            //otherwise we may end up with a map with just one valid connection from biome to another
            float zoneWidth = Width / generationParams.DifficultyZones;
            for (int x = 0; x < generationParams.DifficultyZones; x++)
            {
                for (float y = 10.0f; y < Height - 10.0f; y += generationParams.VoronoiSiteInterval.Y * 2)
                {
                    voronoiSites.Add(new Vector2((x + 1) * zoneWidth, y));
                }
            }

            // put some of this stuff in a helper class, this function is getting unwieldy
            MapLocationTypeGenerator mapLocationTypeGenerator = new MapLocationTypeGenerator(campaign, this);

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

                    newLocations[i] = Location.CreateRandom(position, zone, GetBiome(position.X)?.Identifier, Rand.GetRNG(Rand.RandSync.ServerAndClient),
                                                            requireOutpost: false, forceLocationType: null, existingLocations: Locations);
                    
                    mapLocationTypeGenerator.AddToLocationsPerZone(zone, newLocations[i]);
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

                var leftMostLocation =
                        connection.Locations[0].MapPosition.X < connection.Locations[1].MapPosition.X ?
                            connection.Locations[0] :
                            connection.Locations[1];
                var rightMostLocation =
                    leftMostLocation == connection.Locations[0] ?
                    connection.Locations[1] :
                    connection.Locations[0];

                //if the leftmost location doesn't have any connections to the left (e.g. a "deadend location" between biomes),
                //we cannot choose it as the gate location. It must have connections both to the left, and to the right (the next biome)
                if (leftMostLocation.Connections.None(c => c.OtherLocation(leftMostLocation).MapPosition.X < leftMostLocation.MapPosition.X))
                {
                    continue;
                }
                //same for the rightmost location - it must have connections leading further on the map
                if (rightMostLocation.Connections.None(c => c.OtherLocation(rightMostLocation).MapPosition.X > rightMostLocation.MapPosition.X))
                {
                    continue;
                }

                if (!connectionsBetweenZones[zone1].Any())
                {
                    connectionsBetweenZones[zone1].Add(connection);
                }
                else if (generationParams.GateCount[zone1] == 1)
                {
                    //if we only want one connection between the biomes, place it at the center of the map
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
                if (connectionsBetweenZones[zone1].None())
                {
                    DebugConsole.ThrowError($"Potential error during map generation: no connections between zones {zone1} and {zone2} found. Traversing through to the end of the map may be impossible.");
                }
            }

            var orderedPrefabs = LocationType.Prefabs.GetOrdered();
            List<Location> forciblyReassignedGateLocations = new List<Location>();
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
                    if (!AllowAsBiomeGate(leftMostLocation.Type))
                    {
                        forciblyReassignedGateLocations.Add(leftMostLocation);
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

                //if all of the other connected locations are to the left (= if there's no path forwards from the outpost),
                //create a new connection to the closest location to the right 
                if (rightMostLocation.Connections.All(c => c.OtherLocation(rightMostLocation).MapPosition.X < rightMostLocation.MapPosition.X))
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
            
            AssignBiomes(Rand.GetRNG(Rand.RandSync.ServerAndClient));

            var gateLocations = Locations.Where(l => l.IsGateBetweenBiomes);
            foreach (var gateLocation in forciblyReassignedGateLocations)
            {
                var potentialGateLocationTypes = orderedPrefabs.Where(AllowAsBiomeGate);
                LocationType gateLocationType =
                    //choose some location type that's allowed in this zone/biome, and has a non zero commonness (instead of some fixed count)
                    potentialGateLocationTypes.Where(lt => lt.AreaSettings.Any(areaSettings => areaSettings.MatchesLocation(this, gateLocation) && areaSettings.Commonness > 0)).GetRandom(Rand.RandSync.ServerAndClient) ??
                    //if not found, use something with a fixed count
                    potentialGateLocationTypes.Where(lt => lt.AreaSettings.Any(areaSettings => areaSettings.MatchesLocation(this, gateLocation) && areaSettings.MinCount > 0)).GetRandom(Rand.RandSync.ServerAndClient) ??
                    //if that's not found either, try finding a type that doesn't spawn in any biome, but is allowed as a biome gate
                    //(a mod might have some special "biome gate" location types that are meant just for the gate locations)
                    potentialGateLocationTypes.Where(lt => lt.AreaSettings.None(areaSettings => areaSettings.Commonness > 0.0f || areaSettings.HasCounts)).GetRandom(Rand.RandSync.ServerAndClient);

                if (gateLocation == null)
                {
                    DebugConsole.ThrowError($"Failed to find a suitable location type for a gate location at the end of the biome {gateLocation.Biome}.");
                    continue;
                }
                gateLocation.ChangeType(
                    campaign,
                    gateLocationType,
                    createStores: false,
                    unlockInitialMissions: false);

                //remove the gate locations who's types we've reassigned from the remaining types left to assign,
                //(i.e. if we want just 1 of some location type, and we were forced to choose it as a gate, don't use that type again)
                //must be done after assigning biomes
                mapLocationTypeGenerator.RemoveOneFromTotals(gateLocation.Type, gateLocation);
            }
            mapLocationTypeGenerator.AssignForcedBiomeGateTypes(gateLocations);

            foreach (LocationConnection connection in Connections)
            {
                if (connection.Locations.Any(l => l.IsGateBetweenBiomes))
                {
                    connection.Difficulty = Math.Min(connection.Locations.Min(l => l.Biome.ActualMaxDifficulty), connection.Biome.AdjustedMaxDifficulty);
                }
                else
                {
                    connection.Difficulty = CalculateDifficultyForConnection(connection, campaign);
                }
            }

            //ensure there's an outpost (a valid starting location) at the very left side of the map
            Location startLocation = Locations.MinBy(l => l.MapPosition.X);
            if (LocationType.Prefabs.TryGet("outpost", out LocationType startLocationType))
            {
                mapLocationTypeGenerator.ChangeLocationTypeAndName(campaign, startLocation, startLocationType);
                mapLocationTypeGenerator.AddToFilled(startLocation);
            }

            mapLocationTypeGenerator.AssignLocationTypesBasedOnDesiredPosition(gateLocations);

            //create proper level data and stores for all locations
            //(needs to be done before AssignLocationCounts, since LevelData may be required for the location type changes)
            foreach (Location location in Locations)
            {
                location.LevelData = new LevelData(location, this, CalculateDifficultyForLocation(location, campaign));
                location.TryAssignFactionBasedOnLocationType(campaign);
                if (location.Type.HasOutpost && campaign != null && location.Type.OutpostTeam == CharacterTeamType.FriendlyNPC)
                {
                    if (location.Type.Faction.IsEmpty)
                    {
                        //no faction defined in the location type, assign a random one
                        location.Faction ??= campaign.GetRandomFaction(Rand.RandSync.ServerAndClient);
                    }
                    if (location.Type.SecondaryFaction.IsEmpty)
                    {
                        //no secondary faction defined in the location type, assign a random one
                        location.SecondaryFaction ??= campaign.GetRandomSecondaryFaction(Rand.RandSync.ServerAndClient);
                    }
                }
            }
            //needs to be done after the LevelData has been assigned above
            foreach (var gateLocation in forciblyReassignedGateLocations)
            {
                gateLocation.UnlockInitialMissions(Rand.RandSync.ServerAndClient);
            }

            List<Location> locationsToAssign = Locations.ToList();
            locationsToAssign.Remove(GetPreviousToEndLocation());
            mapLocationTypeGenerator.AssignLocationTypesBasedOnCount(gateLocations, locations: locationsToAssign);

            foreach (LocationConnection connection in Connections) 
            { 
                connection.LevelData = new LevelData(connection, this);
            }

            CreateEndLocation(campaign);
        }

        private static bool AllowAsBiomeGate(LocationType lt)
        {
            //checking for "abandoned" is not strictly necessary here because it's now configured to not be allowed as a biome gate
            //but might be better to keep it for backwards compatibility (previously we relied only on that check)
            return lt.HasOutpost && lt.Identifier != "abandoned" && lt.BiomeGate != LocationType.BiomeGateSetting.Deny;
        }

        public void CheckForErrors()
        {
            //check path to the first end location, because there are no normal connections between the end locations
            var lastLocation = EndLocations[0];
            int endDistance = GetDistanceToClosestLocationOrConnection(StartLocation, maxDistance: int.MaxValue, criteria: (Location location) => location == lastLocation);
            if (endDistance == int.MaxValue)
            {
                DebugConsole.ThrowError($"No path to the end of the map found. Seed: {Seed}");
            }

            if (Locations.None(l => l.Type.Identifier == "outpost" && GetZoneIndex(l.MapPosition.X) == 1))
            {
                DebugConsole.ThrowError($"No outpost in the first zone of the map. Seed: {Seed}");
            }
        }

        public float CalculateDifficultyForLocation(Location location, CampaignMode campaign)
        {
            return CalculateDifficultyForMapPosition(location.MapPosition.X, campaign, location.Biome);
        }

        public float CalculateDifficultyForConnection(LocationConnection connection, CampaignMode campaign)
        {
            return CalculateDifficultyForMapPosition(connection.CenterPos.X, campaign, connection.Biome);
        }

        private float CalculateDifficultyForMapPosition(float mapPosition, CampaignMode campaign, Biome biome)
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


        partial void GenerateAllLocationConnectionVisuals();

        partial void GenerateLocationConnectionVisuals(LocationConnection connection);

        public int GetZoneIndex(float xPos)
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
            return OrderedBiomes.FirstOrDefault(b => b.AllowedZones.Contains(zoneIndex));
        }

        /// <summary>
        /// Assign biomes for the connections between locations, ad for the locations that don't yet have a biome assigned.
        /// </summary>
        private void AssignBiomes(Random rand)
        {
            float zoneWidth = Width / generationParams.DifficultyZones;

            List<Biome> allowedBiomes = new List<Biome>(10);
            for (int i = 0; i < generationParams.DifficultyZones; i++)
            {
                int zoneIndex = i + 1;
                allowedBiomes.Clear();
                allowedBiomes.AddRange(OrderedBiomes.Where(b => b.AllowedZones.Contains(zoneIndex)));
                float zoneX = zoneWidth * (zoneIndex);

                foreach (Location location in Locations)
                {
                    if (location.Biome != null) { continue; }
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

        /// <summary>
        /// Returns the location prior to the final location. The type of this location is hard-coded just as that of the final location.
        /// </summary>
        private Location GetPreviousToEndLocation()
        {
            Location previousToEndLocation = null;
            foreach (Location location in Locations)
            {
                if (!location.Biome.IsEndBiome && (previousToEndLocation == null || location.MapPosition.X > previousToEndLocation.MapPosition.X))
                {
                    previousToEndLocation = location;
                }
            }
            return previousToEndLocation;
        }

        private void ForceLocationTypeToNone(CampaignMode campaign, Location location)
        {
            if (LocationType.Prefabs.TryGet("none", out LocationType locationType))
            {
                location.ChangeType(campaign, locationType, createStores: false);
            }
            location.DisallowLocationTypeChanges = true;
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

            var previousToEndLocation = GetPreviousToEndLocation();
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

            ForceLocationTypeToNone(campaign, previousToEndLocation);

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
                Location connectTo = null;
                foreach (Location location in Locations)
                {
                    if (!location.Biome.IsEndBiome && location != previousToEndLocation && 
                        (connectTo == null || location.MapPosition.X > connectTo.MapPosition.X))
                    {
                        connectTo = location;
                    }
                }
                if (connectTo == null)
                {
                    DebugConsole.ThrowError("Something went wrong when generating the map! Tried to make sure the location leading to the end locations is connected to earlier biomes, but couldn't find any location to connect to.");
                }
                else
                {
                    var newConnection = new LocationConnection(previousToEndLocation, connectTo)
                    {
                        Biome = endLocation.Biome,
                        Difficulty = 100.0f
                    };
                    newConnection.LevelData = new LevelData(newConnection, this);
                    Connections.Add(newConnection);
                    previousToEndLocation.Connections.Add(newConnection);
                    connectTo.Connections.Add(newConnection);
                }
            }

            var endConnection = new LocationConnection(previousToEndLocation, endLocation)
            {
                Biome = endLocation.Biome,
                Difficulty = 100.0f
            };
            endConnection.LevelData = new LevelData(endConnection, this);
            Connections.Add(endConnection);
            previousToEndLocation.Connections.Add(endConnection);
            endLocation.Connections.Add(endConnection);

            AssignEndLocationLevelData(campaign);
        }

        /// <summary>
        /// Assigns the correct outpost generation parameters to the end locations. Also checks and ensures that all of them are correctly assigned to the end biome, and have a location type that can be generated in the end biome.
        /// Strangely shaped custom maps may sometimes generate in a way that there aren't enough locations in the last biome to assign as the end locations, and we may end up choosing locations in the second-to-last biome instead - let's correct that here.
        /// </summary>
        /// <param name="campaign"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void AssignEndLocationLevelData(CampaignMode campaign)
        {
            Biome endBiome =  Biome.Prefabs.OrderBy(p => p.UintIdentifier).FirstOrDefault(b => b.IsEndBiome) ?? throw new InvalidOperationException("Could not find an end biome to assign to the end locations.");
            LocationType endLocationType =
                LocationType.Prefabs
                    .OrderBy(p => p.UintIdentifier)
                    .FirstOrDefault(IsSuitableEndLocationType)
                        ?? throw new InvalidOperationException("Could not find an a location type to assign to the end locations.");

            bool IsSuitableEndLocationType(LocationType lt)
            {
                return lt.AreaSettings.Any(s => 
                    s.Commonness > 0 && 
                    (s.MatchesBiome(endBiome.Identifier) || s.MatchesZone(generationParams.DifficultyZones)));
            }

            for (int i = 0; i < endLocations.Count; i++)
            {
                if (endLocations[i].Biome != endBiome)
                {
                    endLocations[i].Biome = endBiome;
                    endLocations[i].LevelData = new LevelData(endLocations[i], this, endLocations[i].LevelData.Difficulty);
                }
                endLocations[i].ChangeType(campaign: campaign, endLocationType);
                if (endLocationType.ForceLocationName is { IsEmpty: false })
                {
                    endLocations[i].ForceName(endLocationType.ForceLocationName);
                }
                endLocations[i].LevelData.ReassignGenerationParams(Seed);
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

        public void MoveToNextLocation(bool teleportDirectlyToSelectedLocation = false)
        {
            if (SelectedLocation == null && Level.Loaded?.EndLocation != null)
            {
                //force the location at the end of the level to be selected, even if it's been deselect on the map
                //(e.g. due to returning to an empty location the beginning of the level during the round)
                SelectLocation(Level.Loaded.EndLocation);
            }

            if (SelectedConnection == null && !teleportDirectlyToSelectedLocation)
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
            CurrentLocation.CreateStores();
            Discover(CurrentLocation);
            Visit(CurrentLocation);
            SelectedLocation = null;

            OnLocationChanged?.Invoke(new LocationChangeInfo(prevLocation, CurrentLocation));

            if (GameMain.GameSession is { Campaign.CampaignMetadata: { } metadata })
            {
                metadata.SetValue("campaign.location.id".ToIdentifier(), CurrentLocationIndex);
                //name identifier can be empty if the name has been forced to some non-localized string (e.g. by renaming the outpost in the DLC)
                metadata.SetValue("campaign.location.name".ToIdentifier(), CurrentLocation.NameIdentifier.IsEmpty ? CurrentLocation.DisplayName.Value : CurrentLocation.NameIdentifier);
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
            if (currentDisplayLocation == SelectedLocation)
            {
                SelectedConnection = Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            }
            else
            {
                SelectedConnection =
                    Connections.Find(c => c.Locations.Contains(currentDisplayLocation) && c.Locations.Contains(SelectedLocation)) ??
                    Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            }
            if (SelectedConnection?.Locked ?? false)
            {
                string errorMsg =
                    $"A locked connection was selected ({SelectedConnection.Locations[0].DisplayName} -> {SelectedConnection.Locations[1].DisplayName}." +
                    $" Current location: {CurrentLocation}, current display location: {currentDisplayLocation}).\n"
                    + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("MapSelectLocation:LockedConnectionSelected", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                DebugConsole.ThrowError(errorMsg);
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
                string errorMsg = $"Failed to select a location. {location?.DisplayName ?? "null"} not found in the map.";
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

        /// <param name="roundLocation">The location the players were at during the round that just ended. The steps don't count towards the expiry of the missions available in that location. Defaults to the current location.</param>
        /// <param name="roundMissions">The missions played during the round that just ended. The steps don't count towards the expiry of these missions either.</param>
        public void ProgressWorld(CampaignMode campaign, CampaignMode.TransitionType transitionType, float roundDuration, Location roundLocation = null, IReadOnlyCollection<Mission> roundMissions = null)
        {
            int steps = (int)Math.Floor(CampaignMode.GetElapsedWorldSteps(roundDuration, transitionType));
            ProgressWorld(campaign, transitionType, steps, roundLocation, roundMissions);
        }

        /// <inheritdoc cref="ProgressWorld(CampaignMode, CampaignMode.TransitionType, float, Location, IReadOnlyCollection{Mission})"/>
        public void ProgressWorld(CampaignMode campaign, CampaignMode.TransitionType transitionType, int steps, Location roundLocation = null, IReadOnlyCollection<Mission> roundMissions = null)
        {
            for (int i = 0; i < steps; i++)
            {
                ProgressWorld(campaign, roundLocation, roundMissions);
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

        public void ProgressWorld(CampaignMode campaign)
        {
            ProgressWorld(campaign, roundLocation: CurrentLocation, roundMissions: null);
        }

        /// <inheritdoc cref="ProgressWorld(CampaignMode, CampaignMode.TransitionType, float, Location, IReadOnlyCollection{Mission})"/>
        public void ProgressWorld(CampaignMode campaign, Location roundLocation, IReadOnlyCollection<Mission> roundMissions)
        {
            roundLocation ??= CurrentLocation;
            foreach (Location location in Locations)
            {
                //create a new list because missions that complete/fail based on a timer may trigger other missions
                foreach (Mission mission in location.AvailableMissions.ToList())
                {
                    //missions shouldn't expire while they're available in the players' location or being played
                    //(otherwise they could expire just because the players spent a long time in the outpost or in the mission level)
                    bool progressExpiry = location != roundLocation && (roundMissions == null || !roundMissions.Contains(mission));
                    mission.ProgressWorld(progressExpiry);
                }
                if (location.Visited)
                {
                    location.WorldStepsSinceVisited++;
                    if (location.WorldStepsSinceVisited > Location.ClearStoresDelay)
                    {
                        location.ClearStores();
                    }
                }
                else
                {
                    location.ClearStores();
                }
                location.LevelData.ResetExhaustedEventSets();
                location.LevelData.ResetFinishedEvents();
                if (location.Discovered)
                {
                    if (furthestDiscoveredLocation == null ||
                        location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                    {
                        furthestDiscoveredLocation = location;
                    }
                }
                //don't let the deaths "expire" if the population is too low
                if (!location.IsBelowInsufficientPopulationThreshold)
                {
                    foreach (var killedCharacter in location.KilledCharacters)
                    {
                        killedCharacter.WorldStepsSinceKilled++;
                    }
                }
            }
            foreach (LocationConnection connection in Connections)
            {
                connection.LevelData.ResetExhaustedEventSets();
                connection.LevelData.ResetFinishedEvents();
                //note that we shouldn't progress the missions here: they're handled in the Locations foreach above,
                //since the missions are also registered to the locations
                //connection.AvailableMissions.ForEach(m => m.ProgressWorld());
            }

            foreach (Location location in Locations)
            {
                //don't update location type changes on the current location
                //don't update stores at this point either, that's handled later in the method
                if (location == CurrentLocation) { continue; }
                if (location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                {
                    continue;
                }

                bool shouldUpdateStores = location.Discovered;
                //don't allow the type of the destination to change (it'd be weird to arrive at a different type of location than the one you were travelling to)
                //biome gates should also remain unchanged
                bool shouldProcessLocationTypeChanges = location != SelectedLocation && !location.IsGateBetweenBiomes;
                if (shouldProcessLocationTypeChanges &&
                    ProgressLocationTypeChanges(campaign, location)) 
                {
                    //don't update stores if the location type changed (that recreates the stores anyway)
                    shouldUpdateStores = false;
                }

                if (shouldUpdateStores)
                {
                    location.UpdateStores(createStoresIfNotCreated: false);
                }
            }

            foreach (Faction faction in campaign.Factions)
            {
                faction.ProgressWorld();
            }

            AutomaticMission.CreateAcrossMap(campaign, campaign.Map);

            if (CurrentLocation != null)
            {
                CurrentLocation.UpdateStores(createStoresIfNotCreated: true);
                CurrentLocation.WorldStepsSinceVisited = 0;
            }

            foreach (var hook in PluginData.GetHooks<PluginOnProgressWorldDelegate>())
            {
                hook(this, campaign);
            }

            WorldStepCount++;
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
                return false;
            }

            return ProcessXmlBasedTypeChange(campaign, location);
        }

        /// <summary>
        /// The original location type change logic.
        /// </summary>
        private bool ProcessXmlBasedTypeChange(CampaignMode campaign, Location location)
        {
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
                                parentMission: null);
                    }
                    else
                    {
                        return ChangeLocationType(campaign, location, selectedTypeChange);
                    }
                    return false;
                }
            }

            UpdateProximityTimers(location);

            return false;
        }

        private void UpdateProximityTimers(Location location)
        {
            foreach (LocationTypeChange typeChange in location.Type.CanChangeTo)
            {
                foreach (var requirement in typeChange.Modifiers)
                {
                    if (requirement.AnyWithinDistance(location, requirement.RequiredProximityForProbabilityIncrease))
                    {
                        if (!location.ProximityTimer.ContainsKey(requirement))
                        {
                            location.ProximityTimer[requirement] = 0;
                        }
                        location.ProximityTimer[requirement] += 1;
                    }
                    else
                    {
                        location.ProximityTimer.Remove(requirement);
                    }
                }
            }
        }

        /// <summary>
        /// Changes the type of a location. Can be used by plugins to change location types.
        /// </summary>
        /// <param name="campaign">The campaign mode instance.</param>
        /// <param name="location">The location to change.</param>
        /// <param name="newType">The target location type.</param>
        /// <param name="cooldownAfterChange">Number of turns before this location can change type again.</param>
        /// <returns>True if the type was changed, false if blocked.</returns>
        public bool ChangeLocationType(CampaignMode campaign, Location location, LocationType newType, int cooldownAfterChange = 0)
        {
            if (location.LocationTypeChangesBlocked) { return false; }

            if (newType.OutpostTeam != location.Type.OutpostTeam ||
                newType.HasOutpost != location.Type.HasOutpost)
            {
                location.ClearMissions();
            }
            location.ChangeType(campaign, newType, createStores: false);
            location.TimeSinceLastTypeChange = 0;
            location.LocationTypeChangeCooldown = cooldownAfterChange;
            location.PendingLocationTypeChange = null;
            return true;
        }

        /// <summary>
        /// Changes the type of a location based on an XML-defined <see cref="LocationTypeChange"/> rule.
        /// Wraps the public overload with additional behavior (map notifications and proximity timer cleanup).
        /// </summary>
        private bool ChangeLocationType(CampaignMode campaign, Location location, LocationTypeChange change)
        {
            if (!LocationType.Prefabs.TryGet(change.ChangeToType, out var newType))
            {
                DebugConsole.ThrowError($"Failed to change the type of the location \"{location.DisplayName}\". Location type \"{change.ChangeToType}\" not found.");
                return false;
            }
            LocalizedString prevName = location.DisplayName;
            if (!ChangeLocationType(campaign, location, newType, change.CooldownAfterChange)) { return false; }
            // Show a notification about the type change on the client
            ChangeLocationTypeProjSpecific(location, prevName, change);
            foreach (var requirement in change.Modifiers)
            {
                location.ProximityTimer.Remove(requirement);
            }
            return true;
        }

        /// <summary>
        /// Difficulty of the most difficult level the player has visited themselves (ignores locations only visited remotely, e.g. by an expedition).
        /// </summary>
        public float GetHighestVisitedLevelDifficulty()
        {
            float highestDifficulty = 0.0f;
            foreach (var location in locationsVisited)
            {
                if (locationsVisitedRemotely.Contains(location)) { continue; }
                if (location.LevelData.Difficulty > highestDifficulty)
                {
                    highestDifficulty = location.LevelData.Difficulty;
                }
            }
            return highestDifficulty;
        }

        /// <summary>
        /// Get all the locations within the specified distance from the start location (including the start location itself).
        /// </summary>
        /// <param name="connectionFilter">Optional filter that determines whether a connection can be traversed.</param>
        public static IEnumerable<Location> GetLocationsWithinDistance(Location startLocation, int maxDistance, Func<LocationConnection, bool> connectionFilter = null)
        {
            var checkedLocations = new HashSet<Location>() { startLocation };
            var currentLocations = new List<Location>() { startLocation };
            //breadth-first, so each location is reached via the shortest path
            for (int distance = 1; distance <= maxDistance && currentLocations.Any(); distance++)
            {
                var nextLocations = new List<Location>();
                foreach (var location in currentLocations)
                {
                    foreach (var connection in location.Connections)
                    {
                        if (connectionFilter != null && !connectionFilter(connection)) { continue; }
                        var otherLocation = connection.OtherLocation(location);
                        if (checkedLocations.Add(otherLocation))
                        {
                            nextLocations.Add(otherLocation);
                        }
                    }
                }
                currentLocations = nextLocations;
            }
            return checkedLocations;
        }

        public static bool LocationOrConnectionWithinDistance(Location startLocation, int maxDistance, Func<Location, bool> criteria, Func<LocationConnection, bool> connectionCriteria = null)
        {
            return GetDistanceToClosestLocationOrConnection(startLocation, maxDistance, criteria, connectionCriteria) <= maxDistance;
        }

        /// <summary>
        /// Get the shortest distance from the start location to another location that satisfies the specified criteria.
        /// </summary>
        /// <returns>The distance to a matching location, or int.MaxValue if none are found.</returns>
        public static int GetDistanceToClosestLocationOrConnection(Location startLocation, int maxDistance, Func<Location, bool> criteria, Func<LocationConnection, bool> connectionCriteria = null)
        {
            int distance = 0;
            var locationsToTest = new List<Location>() { startLocation };
            var nextBatchToTest = new HashSet<Location>();
            var checkedLocations = new HashSet<Location>();
            while (locationsToTest.Any())
            {
                foreach (var location in locationsToTest)
                {
                    checkedLocations.Add(location);
                    if (criteria(location)) { return distance; }
                    foreach (var connection in location.Connections)
                    {
                        if (connectionCriteria != null && connectionCriteria(connection))
                        {
                            return distance;
                        }
                        var otherLocation = connection.OtherLocation(location);
                        if (!checkedLocations.Contains(otherLocation))
                        {
                            nextBatchToTest.Add(otherLocation);
                        }
                    }
                    if (distance > maxDistance) { return int.MaxValue; }
                }
                distance++;
                locationsToTest.Clear();
                locationsToTest.AddRange(nextBatchToTest);
                nextBatchToTest.Clear();
            }
            return int.MaxValue;
        }

        /// <summary>  
        /// Get the path from the start location to a destination location. Uses a simple flood fill algorithm.
        /// </summary>  
        /// <returns>The path as a list of locations, or an empty list if no path is found.</returns>  
        /// <param name="connectionFilter">Optional filter that determines whether a connection can be traversed.</param>
        public static List<Location> GetPathToLocation(Location startLocation, Location destination, int maxDistance, Func<LocationConnection, bool> connectionFilter = null)
        {
            var locationsToTest = new Queue<List<Location>>();
            var checkedLocations = new HashSet<Location>();

            locationsToTest.Enqueue([startLocation]);

            while (locationsToTest.Count != 0)
            {
                var currentPath = locationsToTest.Dequeue();
                var currentLocation = currentPath.Last();

                if (checkedLocations.Contains(currentLocation))
                {
                    continue;
                }

                checkedLocations.Add(currentLocation);

                if (currentLocation == destination)
                {
                    return currentPath;
                }

                foreach (var connection in currentLocation.Connections)
                {
                    if (connectionFilter != null && !connectionFilter(connection)) { continue; }
                    var otherLocation = connection.OtherLocation(currentLocation);
                    if (!checkedLocations.Contains(otherLocation) && currentPath.Count <= maxDistance)
                    {
                        var newPath = new List<Location>(currentPath) { otherLocation };
                        locationsToTest.Enqueue(newPath);
                    }
                }
            }

            //return empty list if no path was found
            return [];
        }


        partial void ChangeLocationTypeProjSpecific(Location location, LocalizedString prevName, LocationTypeChange change);

        partial void ClearAnimQueue();

        public void Discover(Location location, bool checkTalents = true)
        {
            if (location is null) { return; }
            if (locationsDiscovered.Contains(location)) { return; }
            locationsDiscovered.Add(location);
            if (furthestDiscoveredLocation == null || location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
            {
                furthestDiscoveredLocation = location;
            }
            if (checkTalents)
            {
                GameSession.GetSessionCrewCharacters(CharacterType.Both).ForEach(c => c.CheckTalents(AbilityEffectType.OnLocationDiscovered, new Location.AbilityLocation(location)));
            }
        }

        /// <param name="visitedRemotely">Is the location marked as visited without the players actually going there (e.g. reached by an expedition)?
        /// Remote visits don't count towards <see cref="GetHighestVisitedLevelDifficulty"/>.</param>
        /// <param name="checkTalents">Should <see cref="AbilityEffectType.OnLocationVisited"/> be triggered if this is the first time the players visit the location in person?</param>
        public void Visit(Location location, bool resetTimeSinceVisited = true, bool incrementTimesVisited = true, bool visitedRemotely = false, bool checkTalents = true)
        {
            if (location is null) { return; }
            //a location visited remotely before (e.g. by an expedition) hasn't been visited in person yet
            bool firstVisitInPerson = !visitedRemotely && (!locationsVisited.Contains(location) || locationsVisitedRemotely.Contains(location));
            if (resetTimeSinceVisited)
            {
                location.WorldStepsSinceVisited = 0;
            }
            if (incrementTimesVisited)
            {
                location.TimesVisited++;
            }
            if (!visitedRemotely)
            {
                locationsVisitedRemotely.Remove(location);
            }
            if (!locationsVisited.Contains(location))
            {
                locationsVisited.Add(location);
                if (visitedRemotely)
                {
                    locationsVisitedRemotely.Add(location);
                }
                RemoveFogOfWarProjSpecific(location);
            }
            if (checkTalents && firstVisitInPerson)
            {
                GameSession.GetSessionCrewCharacters(CharacterType.Both).ForEach(c => c.CheckTalents(AbilityEffectType.OnLocationVisited, new Location.AbilityLocation(location)));
            }
        }

        /// <summary>
        /// Clears the history of discovered and visited locations. Used when loading a save file to reset the state before applying the saved data,
        /// and when resetting the campaign map after finishing the campaign.
        /// </summary>
        public void ClearLocationHistory()
        {
            locationsDiscovered.Clear();
            locationsVisited.Clear();
            locationsVisitedRemotely.Clear();
            furthestDiscoveredLocation = StartLocation;
            ClearLocationHistoryProjSpecific();
        }

        partial void ClearLocationHistoryProjSpecific();

        public int? GetDiscoveryIndex(Location location)
        {
            if (!trackedLocationDiscoveryAndVisitOrder) { return null; }
            if (location is null) { return -1; }
            return locationsDiscovered.IndexOf(location);
        }

        public int? GetVisitIndex(Location location, bool includeLocationsWithoutOutpost = false)
        {
            if (!trackedLocationDiscoveryAndVisitOrder) { return null; }
            if (location is null) { return -1; }
            int index = locationsVisited.IndexOf(location);
            if (includeLocationsWithoutOutpost) { return index; }
            int noOutpostLocations = 0;
            for (int i = 0; i < index; i++)
            {
                if (locationsVisited[i] is not Location l) { continue; }
                if (l.HasOutpost()) { continue; }
                noOutpostLocations++;
            }
            return index - noOutpostLocations;
        }

        public bool IsDiscovered(Location location)
        {
            if (location is null) { return false; }
            return locationsDiscovered.Contains(location);
        }

        public bool IsVisited(Location location)
        {
            if (location is null) { return false; }
            return locationsVisited.Contains(location);
        }

        partial void RemoveFogOfWarProjSpecific(Location location);

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
            WorldStepCount = element.GetAttributeInt("worldstepcount", 0);

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
                        int locationIndex = subElement.GetAttributeInt("i", -1);
                        if (locationIndex < 0 || locationIndex >= Locations.Count)
                        {
                            DebugConsole.AddWarning($"Error while loading the campaign map: location index out of bounds ({locationIndex})");
                            continue;
                        }
                        Location location = Locations[locationIndex];
                        location.ProximityTimer.Clear();
                        for (int i = 0; i < location.Type.CanChangeTo.Count; i++)
                        {
                            for (int j = 0; j < location.Type.CanChangeTo[i].Modifiers.Count; j++)
                            {
                                location.ProximityTimer.Add(location.Type.CanChangeTo[i].Modifiers[j], subElement.GetAttributeInt("changetimer" + i + "-" + j, 0));
                            }
                        }
                        location.LoadLocationTypeChange(subElement);

                        location.LoadChangingProperties(subElement, campaign);

                        // Backwards compatibility: if the discovery status is defined in the location element,
                        // the game was saved using when the discovery order still wasn't being tracked
                        if (subElement.GetAttributeBool("discovered", false))
                        {
                            Discover(location);
                            Visit(location, resetTimeSinceVisited: false, incrementTimesVisited: false, checkTalents: false);
                            trackedLocationDiscoveryAndVisitOrder = false;
                        }

                        Identifier locationType = subElement.GetAttributeIdentifier("type", Identifier.Empty);
                        LocalizedString prevLocationName = location.DisplayName;
                        LocationType prevLocationType = location.Type;
                        LocationType newLocationType = LocationType.Prefabs.Find(lt => lt.Identifier == locationType) ?? LocationType.Prefabs.GetOrdered().First();
                        location.ChangeType(campaign, newLocationType);

                        if (showNotifications && prevLocationType != location.Type)
                        {
                            var change = prevLocationType.CanChangeTo.Find(c => c.ChangeToType == location.Type.Identifier);
                            if (change != null)
                            {
                                ChangeLocationTypeProjSpecific(location, prevLocationName, change);
                                location.TimeSinceLastTypeChange = 0;
                            }
                            else if (!string.IsNullOrEmpty(location.LastTypeChangeMessage))
                            {
                                // No XML-defined type change rule but a message was stored, show it as a map notification on the client
#if CLIENT
                                AddMapNotification(location.LastTypeChangeMessage, location);
#endif
                            }
                        }

                        location.LoadStores(subElement);
                        location.LoadMissions(subElement);

                        foreach (var hook in PluginData.GetHooks<PluginLocationLoadDelegate>())
                        {
                            hook(location, campaign, subElement);
                        }

                        break;
                    case "connection":
                        //the index wasn't saved previously, skip if that's the case
                        if (subElement.Attribute("i") == null) { continue; }

                        int connectionIndex = subElement.GetAttributeInt("i", -1);
                        if (connectionIndex < 0 || connectionIndex >= Connections.Count)
                        {
                            DebugConsole.AddWarning($"Error while loading the campaign map: connection index out of bounds ({connectionIndex})");
                            continue;
                        }
                        Connections[connectionIndex].Passed = subElement.GetAttributeBool("passed", false);
                        Connections[connectionIndex].Locked = subElement.GetAttributeBool("locked", false);                        
                        break;
                    case "radiation":
                        Radiation = new Radiation(this, generationParams.RadiationParams, subElement);
                        
                        if (DetermineRadiationDisabledByPlugin())
                        {
                            Radiation.Enabled = false;
                        }
                        
                        break;
                    case "discovered":
                        bool trackedVisitedEmptyLocations = subElement.GetAttributeBool("trackedvisitedemptylocations", false);
                        int[] discoveredIndices = subElement.GetAttributeIntArray("indices", Array.Empty<int>());
                        foreach (int discoveredIndex in discoveredIndices)
                        {
                            Discover(Locations[discoveredIndex]);
                        }
                        //backwards compatibility
                        foreach (var childElement in subElement.GetChildElements("location"))
                        {
                            if (GetLocation(childElement) is Location l)
                            {
                                Discover(l);
                                //even older backwards compatibility: previously we didn't track whether you've "visited" empty locations,
                                //nor the order in which locations are visited - we need to handle that here
                                if (!trackedVisitedEmptyLocations)
                                {
                                    if (!l.HasOutpost())
                                    {
                                        Visit(l, resetTimeSinceVisited: false, checkTalents: false);
                                    }
                                    trackedLocationDiscoveryAndVisitOrder = false;
                                }
                            }
                        }
                        break;
                    case "visited":
                        int[] visitedIndices = subElement.GetAttributeIntArray("indices", Array.Empty<int>());
                        foreach (int visitedIndex in visitedIndices)
                        {
                            Visit(Locations[visitedIndex], resetTimeSinceVisited: false, incrementTimesVisited: false, checkTalents: false);
                        }
                        int[] visitedRemotelyIndices = subElement.GetAttributeIntArray("visitedRemotelyIndices", Array.Empty<int>());
                        foreach (int visitedIndex in visitedRemotelyIndices)
                        {
                            if (visitedIndex < 0 || visitedIndex >= Locations.Count) { continue; }
                            Location remoteLocation = Locations[visitedIndex];
                            if (locationsVisited.Contains(remoteLocation)) { locationsVisitedRemotely.Add(remoteLocation); }
                        }
                        //backwards compatibility
                        foreach (var childElement in subElement.GetChildElements("location"))
                        {
                            if (GetLocation(childElement) is Location l)
                            {
                                Visit(l, resetTimeSinceVisited: false, incrementTimesVisited: false, checkTalents: false);
                            }
                        }
                        break;
                }

                Location GetLocation(XElement element)
                {
                    int index = element.GetAttributeInt("i", -1);
                    if (index < 0) { return null; }
                    return Locations[index];
                }
                
            }

            void Discover(Location location)
            {
                this.Discover(location, checkTalents: false);
                if (furthestDiscoveredLocation == null || location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                {
                    furthestDiscoveredLocation = location;
                }
            }

            foreach (Location location in Locations)
            {
                location?.InstantiateLoadedMissions(this);
            }

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
                    DebugConsole.AddWarning($"Error while loading campaign map state. Submarine in a location with no outpost ({CurrentLocation.DisplayName}). Loading the first adjacent connection...");
                    SelectLocation(CurrentLocation.Connections[0].OtherLocation(CurrentLocation));
                }
            }

            var previousToEndLocation = GetPreviousToEndLocation();
            if (previousToEndLocation != null)
            {
                ForceLocationTypeToNone(campaign, previousToEndLocation);
            }
        }

        public void Save(XElement element)
        {
            XElement mapElement = new XElement("map");

            mapElement.Add(new XAttribute("version", GameMain.Version.ToString()));
            mapElement.Add(new XAttribute("currentlocation", CurrentLocationIndex));
            mapElement.Add(new XAttribute("worldstepcount", WorldStepCount));
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
                    new XAttribute("i", i),
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
                var discoveryElement = new XElement("discovered",
                    new XAttribute("trackedvisitedemptylocations", true),
                    new XAttribute("indices", string.Join(',', locationsDiscovered.Select(l => Locations.IndexOf(l)))));
                mapElement.Add(discoveryElement);
            }

            if (locationsVisited.Any())
            {
                var visitElement = new XElement("visited",
                    new XAttribute("indices", string.Join(',', locationsVisited.Select(l => Locations.IndexOf(l)))));
                if (locationsVisitedRemotely.Any())
                {
                    visitElement.Add(new XAttribute("visitedRemotelyIndices", string.Join(',', locationsVisitedRemotely.Select(l => Locations.IndexOf(l)))));
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