using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        [Serialize(false, true), Editable]
        public bool ShowNoiseMap { get; set; }

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

        [Serialize(2000, true)]
        public int Size { get; set; }

        [Serialize(20.0f, true, description: "Connections with a length smaller or equal to this generate the smallest possible levels (using the MinWidth parameter in the level generation paramaters)."), Editable(0.0f, 5000.0f)]
        public float SmallLevelConnectionLength { get; set; }

        [Serialize(200.0f, true, description: "Connections with a length larger or equal to this generate the largest possible levels (using the MaxWidth parameter in the level generation paramaters)."), Editable(0.0f, 5000.0f)]
        public float LargeLevelConnectionLength { get; set; }

        [Serialize(1024, true)]
        public int NoiseResolution { get; set; } //Resolution of the noisemap overlay

        [Serialize(10.0f, true), Editable(0.0f, 1000.0f)]
        public float NoiseFrequency { get; set; }

        [Serialize(8, true), Editable(1, 100)]
        public int NoiseOctaves { get; set; }

        [Serialize(0.5f, true), Editable(0.0f, 1.0f)]
        public float NoisePersistence { get; set; }

        [Serialize("200,200", true), Editable]
        public Vector2 TileSpriteSize { get; set; }
        [Serialize("280,80", true), Editable]
        public Vector2 TileSpriteSpacing { get; set; }

        [Serialize(1.0f, true, description: "How dark the center of the map is (1.0f = black)."), Editable(0.0f, 1.0f)]
        public float CenterDarkenStrength { get; set; }

        [Serialize(0.9f, true, description: "How close to the center the darkening starts (0.8f = 20% from the edge)."), Editable(0.0f, 1.0f)]
        public float CenterDarkenRadius { get; set; }

        [Serialize(5, true, description: "The edge of the dark center area is wave-shaped, and the frequency is determined by this value." +
            " I.e. how many points does the star-shaped dark area in the center have."), Editable(0, 1000)]
        public int CenterDarkenWaveFrequency { get; set; }

        [Serialize(15.0f, true, description: "How heavily the noise map affects the phase of the edge wave (higher value = more irregular shape)."), Editable(0, 1000.0f)]
        public float CenterDarkenWavePhaseNoise { get; set; }

        [Serialize(0.8f, true, description: "How dark the edges of the map are (1.0f = black)."), Editable(0.0f, 1.0f)]
        public float EdgeDarkenStrength { get; set; }

        [Serialize(0.9f, true, description: "How far from the center the darkening starts (0.95f = 5% from the edge)."), Editable(0.0f, 1.0f)]
        public float EdgeDarkenRadius { get; set; }

        [Serialize(0.9f, true, description: "How far from the center locations can be placed."), Editable(0.0f, 1.0f)]
        public float LocationRadius { get; set; }

        [Serialize(20.0f, true, description: "How far from each other voronoi sites are placed. " +
            "Sites determine shape of the voronoi graph. Locations are placed at the vertices of the voronoi cells. " +
            "(Decreasing this value causes the number of sites, and the complexity of the map, to increase exponentially - be careful when adjusting)"), Editable(1.0f, 100.0f)]
        public float VoronoiSiteInterval { get; set; }

        [Serialize(0.3f, true, description: "How likely it is for a site to be placed at a given spot (e.g. 20% probability for a site to be placed every 5 units of the map). " +
            "Multiplied with the noise value in the spot, meaning that sites are less likely to appear in dark spots."), Editable(0.01f, 1.0f)]
        public float VoronoiSitePlacementProbability { get; set; }

        [Serialize(0.1f, true, description: "Probability * noise ^ 2 must be higher than this for a site to be placed. " +
            "= How bright the noise map must be at a given spot for a location to be placed there"), Editable(0.01f, 1.0f)]
        public float VoronoiSitePlacementMinVal { get; set; }

        [Serialize(10.0f, true, description: "Connections smaller than this are removed."), Editable(0.0f, 500.0f)]
        public float MinConnectionDistance { get; set; }

        [Serialize(5.0f, true, description: "Locations that are closer than this to another location are removed."), Editable(0.0f, 100.0f)]
        public float MinLocationDistance { get; set; }

        [Serialize(0.2f, true, description: "Affects how many iterations are done when generating the jagged shape of the connections (iterations = Sqrt(connectionLength * multiplier))."), Editable(0.0f, 10.0f)]
        public float ConnectionIterationMultiplier { get; set; }

        [Serialize(0.5f, true, description: "How large the \"bends\" in the connections are (displacement = connectionLength * multiplier)."), Editable(0.0f, 10.0f)]
        public float ConnectionDisplacementMultiplier { get; set; }

        [Serialize(0.1f, true, description: "ConnectionIterationMultiplier for the UI indicator lines between locations."), Editable(0.0f, 10.0f)]
        public float ConnectionIndicatorIterationMultiplier { get; set; }

        [Serialize(0.1f, true, description: "ConnectionDisplacementMultiplier for the UI indicator lines between locations."), Editable(0.0f, 10.0f)]
        public float ConnectionIndicatorDisplacementMultiplier { get; set; }

        public Sprite ConnectionSprite { get; private set; }

#if CLIENT

        [Serialize(15.0f, true, description: "Size of the location icons in pixels when at 100% zoom."), Editable(1.0f, 1000.0f)]
        public float LocationIconSize { get; set; }

        [Serialize("150,150,150,255", true, description: "The color used to display the low-difficulty connections on the map."), Editable()]
        public Color LowDifficultyColor { get; set; }
        [Serialize("210,143,83,255", true, description: "The color used to display the medium-difficulty connections on the map."), Editable()]
        public Color MediumDifficultyColor { get; set; }
        [Serialize("216,154,138", true, description: "The color used to display the high-difficulty connections on the map."), Editable()]
        public Color HighDifficultyColor { get; set; }

        public SpriteSheet DecorativeMapSprite { get; private set; }
        public SpriteSheet DecorativeGraphSprite { get; private set; }
        public SpriteSheet DecorativeLineTop { get; private set; }
        public SpriteSheet DecorativeLineBottom { get; private set; }
        public SpriteSheet DecorativeLineCorner { get; private set; }

        public SpriteSheet ReticleLarge { get; private set; }
        public SpriteSheet ReticleMedium { get; private set; }
        public SpriteSheet ReticleSmall { get; private set; }

        public Sprite MapCircle { get; private set; }
        public Sprite LocationIndicator { get; private set; }
#endif

        public List<Sprite> BackgroundTileSprites { get; private set; }

        public string Name
        {
            get { return GetType().ToString(); } 
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get; private set;
        }

        public static void Init()
        {

            var files = ContentPackage.GetFilesOfType(GameMain.Config.SelectedContentPackages, ContentType.MapGenerationParameters);
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

            instance?.ConnectionSprite?.Remove();
            instance?.BackgroundTileSprites.ForEach(s => s.Remove());
#if CLIENT
            instance?.MapCircle?.Remove();
            instance?.LocationIndicator?.Remove();
            instance?.DecorativeMapSprite?.Remove();
            instance?.DecorativeGraphSprite?.Remove();
            instance?.DecorativeLineTop?.Remove();
            instance?.DecorativeLineBottom?.Remove();
            instance?.DecorativeLineCorner?.Remove();
            instance?.ReticleLarge?.Remove();
            instance?.ReticleMedium?.Remove();
            instance?.ReticleSmall?.Remove();
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
            BackgroundTileSprites = new List<Sprite>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "connectionsprite":
                        ConnectionSprite = new Sprite(subElement);
                        break;
                    case "backgroundtile":
                        BackgroundTileSprites.Add(new Sprite(subElement));
                        break;
#if CLIENT
                    case "mapcircle":
                        MapCircle = new Sprite(subElement);
                        break;
                    case "locationindicator":
                        LocationIndicator = new Sprite(subElement);
                        break;
                    case "decorativemapsprite":
                        DecorativeMapSprite = new SpriteSheet(subElement);
                        break;
                    case "decorativegraphsprite":
                        DecorativeGraphSprite = new SpriteSheet(subElement);
                        break;
                    case "decorativelinetop":
                        DecorativeLineTop = new SpriteSheet(subElement);
                        break;
                    case "decorativelinebottom":
                        DecorativeLineBottom = new SpriteSheet(subElement);
                        break;
                    case "decorativelinecorner":
                        DecorativeLineCorner = new SpriteSheet(subElement);
                        break;
                    case "reticlelarge":
                        ReticleLarge = new SpriteSheet(subElement);
                        break;
                    case "reticlemedium":
                        ReticleMedium = new SpriteSheet(subElement);
                        break;
                    case "reticlesmall":
                        ReticleSmall = new SpriteSheet(subElement);
                        break;
#endif
                }
            }
        }
    }
}
