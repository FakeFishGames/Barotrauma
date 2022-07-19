using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class MapGenerationParams : Prefab, ISerializableEntity
    {
        public static readonly PrefabSelector<MapGenerationParams> Params = new PrefabSelector<MapGenerationParams>();
        public static MapGenerationParams Instance
        {
            get
            {
                return Params.ActivePrefab;
            }
        }

#if DEBUG
        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool ShowLocations { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool ShowLevelTypeNames { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool ShowOverlay { get; set; }
#else
        public readonly bool ShowLocations = true;
        public readonly bool ShowLevelTypeNames = false;
        public readonly bool ShowOverlay = true;
#endif

        [Serialize(6, IsPropertySaveable.Yes)]
        public int DifficultyZones { get; set; } //Number of difficulty zones

        [Serialize(8000, IsPropertySaveable.Yes), Editable]
        public int Width { get; set; }

        [Serialize(500, IsPropertySaveable.Yes), Editable]
        public int Height { get; set; }

        [Serialize(20.0f, IsPropertySaveable.Yes, description: "Connections with a length smaller or equal to this generate the smallest possible levels (using the MinWidth parameter in the level generation paramaters)."), Editable(0.0f, 5000.0f)]
        public float SmallLevelConnectionLength { get; set; }

        [Serialize(200.0f, IsPropertySaveable.Yes, description: "Connections with a length larger or equal to this generate the largest possible levels (using the MaxWidth parameter in the level generation paramaters)."), Editable(0.0f, 5000.0f)]
        public float LargeLevelConnectionLength { get; set; }

        [Serialize("20,20", IsPropertySaveable.Yes, description: "How far from each other voronoi sites are placed. " +
            "Sites determine shape of the voronoi graph. Locations are placed at the vertices of the voronoi cells. " +
            "(Decreasing this value causes the number of sites, and the complexity of the map, to increase exponentially - be careful when adjusting)"), Editable]
        public Point VoronoiSiteInterval { get; set; }

        [Serialize("5,5", IsPropertySaveable.Yes), Editable]
        public Point VoronoiSiteVariance { get; set; }

        [Serialize(10.0f, IsPropertySaveable.Yes, description: "Connections smaller than this are removed."), Editable(0.0f, 500.0f)]
        public float MinConnectionDistance { get; set; }

        [Serialize(5.0f, IsPropertySaveable.Yes, description: "Locations that are closer than this to another location are removed."), Editable(0.0f, 100.0f)]
        public float MinLocationDistance { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes, description: "ConnectionIterationMultiplier for the UI indicator lines between locations."), Editable(0.0f, 10.0f, DecimalCount = 2)]
        public float ConnectionIndicatorIterationMultiplier { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes, description: "ConnectionDisplacementMultiplier for the UI indicator lines between locations."), Editable(0.0f, 10.0f, DecimalCount = 2)]
        public float ConnectionIndicatorDisplacementMultiplier { get; set; }

        public readonly ImmutableArray<int> GateCount;

#if CLIENT

        [Serialize(0.75f, IsPropertySaveable.Yes), Editable(DecimalCount = 2)]
        public float MinZoom { get; set; }

        [Serialize(1.5f, IsPropertySaveable.Yes), Editable(DecimalCount = 2)]
        public float MaxZoom { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), Editable(DecimalCount = 2)]
        public float MapTileScale { get; set; }

        [Serialize(15.0f, IsPropertySaveable.Yes, description: "Size of the location icons in pixels when at 100% zoom."), Editable(1.0f, 1000.0f)]
        public float LocationIconSize { get; set; }

        [Serialize(5.0f, IsPropertySaveable.Yes, description: "Width of the connections between locations, in pixels when at 100% zoom."), Editable(1.0f, 1000.0f)]
        public float LocationConnectionWidth { get; set; }

        [Serialize("220,220,100,255", IsPropertySaveable.Yes, description: "The color used to display the indicators (current location, selected location, etc)."), Editable()]
        public Color IndicatorColor { get; set; }

        [Serialize("150,150,150,255", IsPropertySaveable.Yes, description: "The color used to display the connections between locations."), Editable()]
        public Color ConnectionColor { get; set; }

        [Serialize("150,150,150,255", IsPropertySaveable.Yes, description: "The color used to display the connections between locations when they're highlighted."), Editable()]
        public Color HighlightedConnectionColor { get; set; }

        [Serialize("150,150,150,255", IsPropertySaveable.Yes, description: "The color used to display the connections the player hasn't travelled through."), Editable()]
        public Color UnvisitedConnectionColor { get; set; }

        public Sprite ConnectionSprite { get; private set; }
        public Sprite PassedConnectionSprite { get; private set; }

        public SpriteSheet DecorativeGraphSprite { get; private set; }

        public Sprite MissionIcon { get; private set; }
        public Sprite TypeChangeIcon { get; private set; }

        public Sprite FogOfWarSprite { get; private set; }
        public Sprite CurrentLocationIndicator { get; private set; }
        public Sprite SelectedLocationIndicator { get; private set; }

        public readonly ImmutableDictionary<Identifier, ImmutableArray<Sprite>> MapTiles;
#endif

        public string Name => GetType().ToString();

        public Dictionary<Identifier, SerializableProperty> SerializableProperties
        {
            get; private set;
        }

        public RadiationParams RadiationParams;

        public MapGenerationParams(ContentXElement element, MapGenerationParametersFile file) : base(file, file.Path.Value.ToIdentifier())
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            var gateCount = element.GetAttributeIntArray("gatecount", null) ?? element.GetAttributeIntArray("GateCount", null);
            if (gateCount == null)
            {
                gateCount = new int[DifficultyZones];
                for (int i = 0; i < DifficultyZones; i++)
                {
                    gateCount[i] = 1;
                }
            }
            GateCount = gateCount.ToImmutableArray();

            Dictionary<Identifier, List<Sprite>> mapTiles = new Dictionary<Identifier, List<Sprite>>();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
#if CLIENT
                    case "connectionsprite":
                        ConnectionSprite = new Sprite(subElement);
                        break;
                    case "passedconnectionsprite":
                        PassedConnectionSprite = new Sprite(subElement);
                        break;
                    case "maptile":
                        Identifier biome = subElement.GetAttributeIdentifier("biome", "");
                        if (!mapTiles.ContainsKey(biome))
                        {
                            mapTiles[biome] = new List<Sprite>();
                        }
                        mapTiles[biome].Add(new Sprite(subElement));
                        break;
                    case "fogofwarsprite":
                        FogOfWarSprite = new Sprite(subElement);
                        break;
                    case "locationindicator":
                    case "currentlocationindicator":
                        CurrentLocationIndicator = new Sprite(subElement);
                        break;
                    case "selectedlocationindicator":
                        SelectedLocationIndicator = new Sprite(subElement);
                        break;
                    case "decorativegraphsprite":
                        DecorativeGraphSprite = new SpriteSheet(subElement);
                        break;
                    case "missionicon":
                        MissionIcon = new Sprite(subElement);
                        break;
                    case "typechangeicon":
                        TypeChangeIcon = new Sprite(subElement);
                        break;
#endif
                    case "radiationparams":
                        RadiationParams = new RadiationParams(subElement);
                        break;
                }
            }
#if CLIENT
            MapTiles = mapTiles.Select(kvp => (kvp.Key, kvp.Value.ToImmutableArray())).ToImmutableDictionary();
#endif
        }

        public override void Dispose()
        {
#if CLIENT
            ConnectionSprite?.Remove();
            PassedConnectionSprite?.Remove();
            SelectedLocationIndicator?.Remove();
            CurrentLocationIndicator?.Remove();
            DecorativeGraphSprite?.Remove();
            MissionIcon?.Remove();
            TypeChangeIcon?.Remove();
            FogOfWarSprite?.Remove();
            foreach (ImmutableArray<Sprite> spriteList in MapTiles.Values)
            {
                foreach (Sprite sprite in spriteList)
                {
                    sprite.Remove();
                }
            }
#endif
        }
    }
}
