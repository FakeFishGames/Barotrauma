using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LevelGenerationParams : PrefabWithUintIdentifier, ISerializableEntity
    {
        public readonly static PrefabCollection<LevelGenerationParams> LevelParams = new PrefabCollection<LevelGenerationParams>();

        public string Name => Identifier.Value;

        public Identifier OldIdentifier { get; }

        private int minWidth, maxWidth, height;

        private Point voronoiSiteInterval;
        //how much the sites are "scattered" on x- and y-axis
        //if Vector2.Zero, the sites will just be placed in a regular grid pattern
        private Point voronoiSiteVariance;

        //how far apart the nodes of the main path can be
        //x = min interval, y = max interval
        private Point mainPathNodeIntervalRange;

        private int caveCount;

        //how large portion of the bottom of the level should be "carved out"
        //if 0.0f, the bottom will be completely solid (making the abyss unreachable)
        //if 1.0f, the bottom will be completely open
        private float bottomHoleProbability;

        //the y-position of the ocean floor (= the position from which the bottom formations extend upwards)
        private int seaFloorBaseDepth;
        //how much random variance there can be in the height of the formations
        private int seaFloorVariance;

        private int cellSubdivisionLength;
        private float cellRoundingAmount;
        private float cellIrregularity;

        private int mountainCountMin, mountainCountMax;

        private int mountainHeightMin, mountainHeightMax;

        private float waterParticleScale;

        private int initialDepthMin, initialDepthMax;

        //which biomes can this type of level appear in
        public readonly ImmutableHashSet<Identifier> AllowedBiomeIdentifiers;
        public readonly bool AnyBiomeAllowed;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get;
            set;
        }

        [Serialize(LevelData.LevelType.LocationConnection, IsPropertySaveable.Yes), Editable]
        public LevelData.LevelType Type
        {
            get;
            set;
        }

        [Serialize("27,30,36", IsPropertySaveable.Yes), Editable]
        public Color AmbientLightColor
        {
            get;
            set;
        }

        [Serialize("20,40,50", IsPropertySaveable.Yes), Editable()]
        public Color BackgroundTextureColor
        {
            get;
            set;
        }

        [Serialize("20,40,50", IsPropertySaveable.Yes), Editable]
        public Color BackgroundColor
        {
            get;
            set;
        }

        [Serialize("255,255,255", IsPropertySaveable.Yes), Editable]
        public Color WallColor
        {
            get;
            set;
        }

        private Vector2 startPosition;
        [Serialize("0,0", IsPropertySaveable.Yes, "Start position of the level (relative to the size of the level. 0,0 = top left corner, 1,1 = bottom right corner)"), Editable(DecimalCount = 2)]
        public Vector2 StartPosition
        {
            get { return startPosition; }
            set 
            { 
                startPosition = new Vector2(
                    MathHelper.Clamp(value.X, 0.0f, 1.0f),
                    MathHelper.Clamp(value.Y, 0.0f, 1.0f));
            }
        }

        private Vector2 endPosition;
        [Serialize("1,0", IsPropertySaveable.Yes, "End position of the level (relative to the size of the level. 0,0 = top left corner, 1,1 = bottom right corner)"), Editable(DecimalCount = 2)]
        public Vector2 EndPosition
        {
            get { return endPosition; }
            set
            {
                endPosition = new Vector2(
                    MathHelper.Clamp(value.X, 0.0f, 1.0f),
                    MathHelper.Clamp(value.Y, 0.0f, 1.0f));
            }
        }

        [Serialize(true, IsPropertySaveable.Yes, "Should there be a hole in the wall next to the end outpost (can be used to prevent players from having to backtrack if they approach the outpost from the wrong side of the main path's walls)."), Editable]
        public bool CreateHoleNextToEnd
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes, "Should the generator force a hole to the bottom of the level to ensure there's a way to the abyss."), Editable]
        public bool CreateHoleToAbyss
        {
            get;
            set;
        }

        [Serialize(1000, IsPropertySaveable.Yes, description: "The total number of level objects (vegetation, vents, etc) in the level."), Editable(MinValueInt = 0, MaxValueInt = 100000)]
        public int LevelObjectAmount
        {
            get;
            set;
        }

        [Serialize(80, IsPropertySaveable.Yes, description: "The total number of decorative background creatures."), Editable(MinValueInt = 0, MaxValueInt = 1000)]
        public int BackgroundCreatureAmount
        {
            get;
            set;
        }

        [Serialize(100000, IsPropertySaveable.Yes), Editable]
        public int MinWidth
        {
            get { return minWidth; }
            set { minWidth = MathHelper.Clamp(value, 2000, 1000000); }
        }

        [Serialize(100000, IsPropertySaveable.Yes), Editable]
        public int MaxWidth
        {
            get { return maxWidth; }
            set { maxWidth = MathHelper.Clamp(value, 2000, 1000000); }
        }

        [Serialize(50000, IsPropertySaveable.Yes), Editable]
        public int Height
        {
            get { return height; }
            set { height = MathHelper.Clamp(value, 2000, 1000000); }
        }

        [Serialize(80000, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 1000000)]
        public int InitialDepthMin
        {
            get { return initialDepthMin; }
            set { initialDepthMin = Math.Max(value, 0); }
        }

        [Serialize(80000, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 1000000)]
        public int InitialDepthMax
        {
            get { return initialDepthMax; }
            set { initialDepthMax = Math.Max(value, initialDepthMin); }
        }

        [Serialize(6500, IsPropertySaveable.Yes), Editable(MinValueInt = 5000, MaxValueInt = 1000000)]
        public int MinTunnelRadius
        {
            get;
            set;
        }


        [Serialize("0,1", IsPropertySaveable.Yes), Editable]
        public Point SideTunnelCount
        {
            get;
            set;
        }


        [Serialize(0.5f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float SideTunnelVariance
        {
            get;
            set;
        }

        [Serialize("2000,6000", IsPropertySaveable.Yes), Editable]
        public Point MinSideTunnelRadius
        {
            get;
            set;
        }

        [Editable, Serialize("3000, 3000", IsPropertySaveable.Yes, description: "How far from each other voronoi sites are placed. " +
            "Sites determine shape of the voronoi graph which the level walls are generated from. " +
            "(Decreasing this value causes the number of sites, and the complexity of the level, to increase exponentially - be careful when adjusting)")]
        public Point VoronoiSiteInterval
        {
            get { return voronoiSiteInterval; }
            set
            {
                voronoiSiteInterval.X = MathHelper.Clamp(value.X, 100, MinWidth / 2);
                voronoiSiteInterval.Y = MathHelper.Clamp(value.Y, 100, height / 2);
            }
        }

        [Editable, Serialize("700,700", IsPropertySaveable.Yes, description: "How much random variation to apply to the positions of the voronoi sites on each axis. " +
            "Small values produce roughly rectangular level walls. The larger the values are, the less uniform the shapes get.")]
        public Point VoronoiSiteVariance
        {
            get { return voronoiSiteVariance; }
            set
            {
                voronoiSiteVariance = new Point(
                    MathHelper.Clamp(value.X, 0, voronoiSiteInterval.X),
                    MathHelper.Clamp(value.Y, 0, voronoiSiteInterval.Y));
            }
        }

        [Editable(MinValueInt = 500, MaxValueInt = 10000), Serialize(5000, IsPropertySaveable.Yes, description: "The edges of the individual wall cells are subdivided into edges of this size. "
            + "Can be used in conjunction with the rounding values to make the cells rounder. Smaller values will make the cells look smoother, " +
            "but make the level more performance-intensive as the number of polygons used in rendering and physics calculations increases.")]
        public int CellSubdivisionLength
        {
            get { return cellSubdivisionLength; }
            set
            {
                cellSubdivisionLength = Math.Max(value, 10);
            }
        }


        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f), Serialize(0.5f, IsPropertySaveable.Yes, description: "How much the individual wall cells are rounded. "
            + "Note that the final shape of the cells is also affected by the CellSubdivisionLength parameter.")]
        public float CellRoundingAmount
        {
            get { return cellRoundingAmount; }
            set
            {
                cellRoundingAmount = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        [Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f), Serialize(0.1f, IsPropertySaveable.Yes, description: "How much random variance is applied to the edges of the cells. "
            + "Note that the final shape of the cells is also affected by the CellSubdivisionLength parameter.")]
        public float CellIrregularity
        {
            get { return cellIrregularity; }
            set
            {
                cellIrregularity = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }


        [Editable(VectorComponentLabels = new string[] { "editable.minvalue", "editable.maxvalue" }), 
            Serialize("5000, 10000", IsPropertySaveable.Yes, description: "The distance between the nodes that are used to generate the main path through the level (min, max). Larger values produce a straighter path.")]
        public Point MainPathNodeIntervalRange
        {
            get { return mainPathNodeIntervalRange; }
            set
            {
                mainPathNodeIntervalRange.X = MathHelper.Clamp(value.X, 100, MinWidth / 2);
                mainPathNodeIntervalRange.Y = MathHelper.Clamp(value.Y, mainPathNodeIntervalRange.X, MinWidth / 2);
            }
        }

        [Serialize(0.5f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float MainPathVariance
        {
            get;
            set;
        }

        [Editable, Serialize(5, IsPropertySaveable.Yes, description: "The number of caves placed along the main path.")]
        public int CaveCount
        {
            get { return caveCount; }
            set { caveCount = MathHelper.Clamp(value, 0, 100); }
        }

        [Serialize(100, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 10000)]
        public int ItemCount
        {
            get;
            set;
        }

        [Serialize("19200,38400", IsPropertySaveable.Yes, description: "The minimum and maximum distance between two resource spawn points on a path."), Editable(100, 100000)]
        public Point ResourceIntervalRange
        {
            get;
            set;
        }

        [Serialize("9600,19200", IsPropertySaveable.Yes, description: "The minimum and maximum distance between two resource spawn points on a cave path."), Editable(100, 100000)]
        public Point CaveResourceIntervalRange
        {
            get;
            set;
        }

        [Serialize("2,8", IsPropertySaveable.Yes, description: "The minimum and maximum amount of resources in a single cluster. " +
            "In addition to this, resource commonness affects the cluster size. Less common resources spawn in smaller clusters."), Editable(1, 20)]
        public Point ResourceClusterSizeRange
        {
            get;
            set;
        }

        [Serialize(0.3f, IsPropertySaveable.Yes, description: "How likely a resource spawn point on a path is to contain resources."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float ResourceSpawnChance { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "How likely a resource spawn point on a cave path is to contain resources."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float CaveResourceSpawnChance { get; set; }

        [Serialize(0, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int FloatingIceChunkCount
        {
            get;
            set;
        }

        [Serialize(0, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 100)]
        public int IslandCount
        {
            get;
            set;
        }

        [Serialize(0, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int IceSpireCount
        {
            get;
            set;
        }

        [Serialize(5, IsPropertySaveable.Yes), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int AbyssIslandCount
        {
            get;
            set;
        }

        [Serialize("4000,7000", IsPropertySaveable.Yes), Editable]
        public Point AbyssIslandSizeMin
        {
            get;
            set;
        }

        [Serialize("8000,10000", IsPropertySaveable.Yes), Editable]
        public Point AbyssIslandSizeMax
        {
            get;
            set;
        }

        [Serialize(0.5f, IsPropertySaveable.Yes), Editable()]
        public float AbyssIslandCaveProbability
        {
            get;
            set;
        }

        [Serialize(-300000, IsPropertySaveable.Yes, description: "How far below the level the sea floor is placed."), Editable(MinValueFloat = Level.MaxEntityDepth, MaxValueFloat = 0.0f)]
        public int SeaFloorDepth
        {
            get { return seaFloorBaseDepth; }
            set { seaFloorBaseDepth = MathHelper.Clamp(value, Level.MaxEntityDepth, 0); }
        }

        [Serialize(1000, IsPropertySaveable.Yes, description: "Variance of the depth of the sea floor. Smaller values produce a smoother sea floor."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100000.0f)]
        public int SeaFloorVariance
        {
            get { return seaFloorVariance; }
            set { seaFloorVariance = value; }
        }

        [Serialize(0, IsPropertySaveable.Yes, description: "The minimum number of mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MountainCountMin
        {
            get { return mountainCountMin; }
            set
            {
                mountainCountMin = Math.Max(value, 0);
            }
        }

        [Serialize(0, IsPropertySaveable.Yes, description: "The maximum number of mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MountainCountMax
        {
            get { return mountainCountMax; }
            set
            {
                mountainCountMax = Math.Max(value, 0);
            }
        }

        [Serialize(1000, IsPropertySaveable.Yes, description: "The minimum height of the mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 1000000)]
        public int MountainHeightMin
        {
            get { return mountainHeightMin; }
            set
            {
                mountainHeightMin = Math.Max(value, 0);
            }
        }

        [Serialize(5000, IsPropertySaveable.Yes, description: "The maximum height of the mountains on the sea floor."), Editable(MinValueInt = 0, MaxValueInt = 1000000)]
        public int MountainHeightMax
        {
            get { return mountainHeightMax; }
            set
            {
                mountainHeightMax = Math.Max(value, 0);
            }
        }

        [Serialize(1, IsPropertySaveable.Yes, description: "The number of alien ruins in the level."), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int RuinCount { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "The minimum number of wrecks in the level. Note that this value cannot be higher than the amount of wreck prefabs (subs)."), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int MinWreckCount { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "The maximum number of wrecks in the level. Note that this value cannot be higher than the amount of wreck prefabs (subs)."), Editable(MinValueInt = 0, MaxValueInt = 10)]
        public int MaxWreckCount { get; set; }

        // TODO: Move the wreck parameters under a separate class?
#region Wreck parameters
        [Serialize(1, IsPropertySaveable.Yes, description: "The minimum number of corpses per wreck."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MinCorpseCount { get; set; }

        [Serialize(5, IsPropertySaveable.Yes, description: "The maximum number of corpses per wreck."), Editable(MinValueInt = 0, MaxValueInt = 20)]
        public int MaxCorpseCount { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "How likely is it that a Thalamus inhabits a wreck. Percentage from 0 to 1 per wreck."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float ThalamusProbability { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, description: "How likely the water level of a hull inside a wreck is randomly set."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float WreckHullFloodingChance { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes, description: "The min water percentage of randomly flooding hulls in wrecks."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float WreckFloodingHullMinWaterPercentage { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The min water percentage of randomly flooding hulls in wrecks."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
        public float WreckFloodingHullMaxWaterPercentage { get; set; }
#endregion

        [Serialize(0.4f, IsPropertySaveable.Yes, description: "The probability for wall cells to be removed from the bottom of the map. A value of 0 will produce a completely enclosed tunnel and 1 will make the entire bottom of the level completely open."), Editable()]
        public float BottomHoleProbability
        {
            get { return bottomHoleProbability; }
            set { bottomHoleProbability = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "Scale of the water particle texture."), Editable]
        public float WaterParticleScale
        {
            get { return waterParticleScale; }
            private set { waterParticleScale = Math.Max(value, 0.01f); }
        }

        [Serialize(2048.0f, IsPropertySaveable.Yes, description: "Size of the level wall texture."), Editable(minValue: 10.0f, maxValue: 10000.0f)]
        public float WallTextureSize
        {
            get;
            private set;
        }

        [Serialize(2048.0f, IsPropertySaveable.Yes), Editable(minValue: 10.0f, maxValue: 10000.0f)]
        public float WallEdgeTextureWidth
        {
            get;
            private set;
        }

        [Serialize(120.0f, IsPropertySaveable.Yes, description: "How far the level walls' edge texture portrudes outside the actual, \"physical\" edge of the cell."), Editable(minValue: 0.0f, maxValue: 1000.0f)]
        public float WallEdgeExpandOutwardsAmount
        {
            get;
            private set;
        }

        [Serialize(1000.0f, IsPropertySaveable.Yes, description: "How far inside the level walls the edge texture continues."), Editable(minValue: 0.0f, maxValue: 10000.0f)]
        public float WallEdgeExpandInwardsAmount
        {
            get;
            private set;
        }

        public Sprite BackgroundSprite { get; private set; }
        public Sprite BackgroundTopSprite { get; private set; }
        public Sprite WallSprite { get; private set; }
        public Sprite WallEdgeSprite { get; private set; }
        public Sprite DestructibleWallSprite { get; private set; }
        public Sprite DestructibleWallEdgeSprite { get; private set; }
        public Sprite WallSpriteDestroyed { get; private set; }
        public Sprite WaterParticles { get; private set; }

        public static LevelGenerationParams GetRandom(string seed, LevelData.LevelType type, Identifier biome = default)
        {
            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            if (!LevelParams.Any())
            {
                throw new InvalidOperationException("Level generation presets not found - using default presets");
            }

            var matchingLevelParams = LevelParams.Where(lp =>
                lp.Type == type &&
                (lp.AnyBiomeAllowed || lp.AllowedBiomeIdentifiers.Any()) &&
                !lp.AllowedBiomeIdentifiers.Contains("None".ToIdentifier()));
            matchingLevelParams = biome.IsEmpty
                ? matchingLevelParams.Where(lp => lp.AnyBiomeAllowed || !lp.AllowedBiomeIdentifiers.All(b => Biome.Prefabs[b].IsEndBiome))
                : matchingLevelParams.Where(lp => lp.AnyBiomeAllowed || lp.AllowedBiomeIdentifiers.Contains(biome));
            
            if (!matchingLevelParams.Any())
            {
                DebugConsole.ThrowError($"Suitable level generation presets not found (biome \"{biome.IfEmpty("null".ToIdentifier())}\", type: \"{type}\")");
                if (!biome.IsEmpty)
                {
                    //try to find params that at least have a suitable type
                    matchingLevelParams = LevelParams.Where(lp => lp.Type == type);
                    if (!matchingLevelParams.Any())
                    {
                        //still not found, give up and choose some params randomly
                        matchingLevelParams = LevelParams;
                    }
                }
            }

            return matchingLevelParams.GetRandom(Rand.RandSync.ServerAndClient);
        }

        public LevelGenerationParams(ContentXElement element, LevelGenerationParametersFile file) : base(file, element.GetAttributeIdentifier("identifier", element.Name.LocalName))
        {
            OldIdentifier = element.GetAttributeIdentifier("oldidentifier", Identifier.Empty);
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            if (element is null) { throw new ArgumentNullException($"{nameof(element)} is null"); }

            var allowedBiomeIdentifiers = element.GetAttributeIdentifierArray("biomes", Array.Empty<Identifier>()).ToHashSet();
            AnyBiomeAllowed = allowedBiomeIdentifiers.Contains("any".ToIdentifier());
            allowedBiomeIdentifiers.Remove("any".ToIdentifier());
            AllowedBiomeIdentifiers = allowedBiomeIdentifiers.ToImmutableHashSet();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "background":
                        BackgroundSprite = new Sprite(subElement);
                        break;
                    case "backgroundtop":
                        BackgroundTopSprite = new Sprite(subElement);
                        break;
                    case "wall":
                        WallSprite = new Sprite(subElement);
                        break;
                    case "walledge":
                        WallEdgeSprite = new Sprite(subElement);
                        break;
                    case "destructiblewall":
                        DestructibleWallSprite = new Sprite(subElement);
                        break;
                    case "destructiblewalledge":
                        DestructibleWallEdgeSprite = new Sprite(subElement);
                        break;
                    case "walldestroyed":
                        WallSpriteDestroyed = new Sprite(subElement);
                        break;
                    case "waterparticles":
                        WaterParticles = new Sprite(subElement);
                        break;
                }
            }
        }

        public override void Dispose() { }
    }
}
