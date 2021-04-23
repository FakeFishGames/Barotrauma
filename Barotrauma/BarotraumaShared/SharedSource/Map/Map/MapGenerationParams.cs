using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class MapGenerationParams : ISerializableEntity
    {
        private static MapGenerationParams instance;
        private static string loadedFile;
        public static MapGenerationParams Instance
        {
            get
            {
                return instance;
            }
        }

#if DEBUG
        [Serialize(true, true), Editable]
        public bool ShowLocations { get; set; }

        [Serialize(true, true), Editable]
        public bool ShowLevelTypeNames { get; set; }

        [Serialize(true, true), Editable]
        public bool ShowOverlay { get; set; }
#else
        public readonly bool ShowLocations = true;
        public readonly bool ShowLevelTypeNames = false;
        public readonly bool ShowOverlay = true;
#endif

        [Serialize(6, true)]
        public int DifficultyZones { get; set; } //Number of difficulty zones

        [Serialize(8000, true), Editable]
        public int Width { get; set; }

        [Serialize(500, true), Editable]
        public int Height { get; set; }

        [Serialize(20.0f, true, description: "Connections with a length smaller or equal to this generate the smallest possible levels (using the MinWidth parameter in the level generation paramaters)."), Editable(0.0f, 5000.0f)]
        public float SmallLevelConnectionLength { get; set; }

        [Serialize(200.0f, true, description: "Connections with a length larger or equal to this generate the largest possible levels (using the MaxWidth parameter in the level generation paramaters)."), Editable(0.0f, 5000.0f)]
        public float LargeLevelConnectionLength { get; set; }

        [Serialize("20,20", true, description: "How far from each other voronoi sites are placed. " +
            "Sites determine shape of the voronoi graph. Locations are placed at the vertices of the voronoi cells. " +
            "(Decreasing this value causes the number of sites, and the complexity of the map, to increase exponentially - be careful when adjusting)"), Editable]
        public Point VoronoiSiteInterval { get; set; }

        [Serialize("5,5", true), Editable]
        public Point VoronoiSiteVariance { get; set; }

        [Serialize(10.0f, true, description: "Connections smaller than this are removed."), Editable(0.0f, 500.0f)]
        public float MinConnectionDistance { get; set; }

        [Serialize(5.0f, true, description: "Locations that are closer than this to another location are removed."), Editable(0.0f, 100.0f)]
        public float MinLocationDistance { get; set; }

        [Serialize(0.1f, true, description: "ConnectionIterationMultiplier for the UI indicator lines between locations."), Editable(0.0f, 10.0f, DecimalCount = 2)]
        public float ConnectionIndicatorIterationMultiplier { get; set; }

        [Serialize(0.1f, true, description: "ConnectionDisplacementMultiplier for the UI indicator lines between locations."), Editable(0.0f, 10.0f, DecimalCount = 2)]
        public float ConnectionIndicatorDisplacementMultiplier { get; set; }

#if CLIENT

        [Serialize(0.75f, true), Editable(DecimalCount = 2)]
        public float MinZoom { get; set; }

        [Serialize(1.5f, true), Editable(DecimalCount = 2)]
        public float MaxZoom { get; set; }

        [Serialize(1.0f, true), Editable(DecimalCount = 2)]
        public float MapTileScale { get; set; }

        [Serialize(15.0f, true, description: "Size of the location icons in pixels when at 100% zoom."), Editable(1.0f, 1000.0f)]
        public float LocationIconSize { get; set; }

        [Serialize(5.0f, true, description: "Width of the connections between locations, in pixels when at 100% zoom."), Editable(1.0f, 1000.0f)]
        public float LocationConnectionWidth { get; set; }

        [Serialize("220,220,100,255", true, description: "The color used to display the indicators (current location, selected location, etc)."), Editable()]
        public Color IndicatorColor { get; set; }

        [Serialize("150,150,150,255", true, description: "The color used to display the connections between locations."), Editable()]
        public Color ConnectionColor { get; set; }

        [Serialize("150,150,150,255", true, description: "The color used to display the connections between locations when they're highlighted."), Editable()]
        public Color HighlightedConnectionColor { get; set; }

        [Serialize("150,150,150,255", true, description: "The color used to display the connections the player hasn't travelled through."), Editable()]
        public Color UnvisitedConnectionColor { get; set; }

        public Sprite ConnectionSprite { get; private set; }
        public Sprite PassedConnectionSprite { get; private set; }

        public SpriteSheet DecorativeGraphSprite { get; private set; }

        public Sprite MissionIcon { get; private set; }
        public Sprite TypeChangeIcon { get; private set; }

        public Sprite FogOfWarSprite { get; private set; }
        public Sprite CurrentLocationIndicator { get; private set; }
        public Sprite SelectedLocationIndicator { get; private set; }

        private readonly Dictionary<string, List<Sprite>> mapTiles = new Dictionary<string, List<Sprite>>();
        public Dictionary<string, List<Sprite>> MapTiles
        {
            get { return mapTiles; }
        }
#endif

        public string Name
        {
            get { return GetType().ToString(); } 
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get; private set;
        }

        public RadiationParams RadiationParams;

        public static void Init()
        {

            var files = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.MapGenerationParameters);
            if (!files.Any())
            {
                DebugConsole.ThrowError("No map generation parameters found in the selected content packages!");
                return;
            }
            // Let's not actually load the parameters until we have solved which file is the last, because loading the parameters takes some resources that would also need to be released.
            XElement selectedElement = null;
            string selectedFile = null;
            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    if (selectedElement != null)
                    {
                        DebugConsole.NewMessage($"Overriding the map generation parameters with '{file.Path}'", Color.Yellow);
                    }
                }
                else if (selectedElement != null)
                {
                    DebugConsole.ThrowError($"Error in {file.Path}: Another map generation parameter file already loaded! Use <override></override> tags to override it.");
                    break;
                }
                selectedElement = mainElement;
                selectedFile = file.Path;
            }

            if (selectedFile == loadedFile) { return; }

#if CLIENT
            if (instance != null)
            {
                instance?.ConnectionSprite?.Remove();
                instance?.PassedConnectionSprite?.Remove();
                instance?.SelectedLocationIndicator?.Remove();
                instance?.CurrentLocationIndicator?.Remove();
                instance?.DecorativeGraphSprite?.Remove();
                instance?.MissionIcon?.Remove();
                instance?.TypeChangeIcon?.Remove();
                instance?.FogOfWarSprite?.Remove();
                foreach (List<Sprite> spriteList in instance.mapTiles.Values)
                {
                    foreach (Sprite sprite in spriteList)
                    {
                        sprite.Remove();
                    }
                }
                instance.mapTiles.Clear();
            }
#endif
            instance = null;

            if (selectedElement == null)
            {
                DebugConsole.ThrowError("Could not find a valid element in the map generation parameter files!");
            }
            else
            {
                instance = new MapGenerationParams(selectedElement);
                loadedFile = selectedFile;
            }
        }

        private MapGenerationParams(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            foreach (XElement subElement in element.Elements())
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
                        string biome = subElement.GetAttributeString("biome", "");
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
        }
    }
}
