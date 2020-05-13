using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class WreckAIConfig : ISerializableEntity
    {
        public string Name => "Wreck AI Config";

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        [Serialize("", false)]
        public string Entity { get; private set; }

        [Serialize("", false)]
        public string DefensiveAgent { get; private set; }

        [Serialize("", false)]
        public string OffensiveAgent { get; private set; }

        [Serialize("", false)]
        public string Brain { get; private set; }

        [Serialize("", false)]
        public string Spawner { get; private set; }

        [Serialize("", false)]
        public string BrainRoomBackground { get; private set; }

        [Serialize("", false)]
        public string BrainRoomVerticalWall { get; private set; }

        [Serialize("", false)]
        public string BrainRoomHorizontalWall { get; private set; }

        [Serialize(60f, false)]
        public float AgentSpawnDelay { get; private set; }

        [Serialize(0.5f, false)]
        public float AgentSpawnDelayRandomFactor { get; private set; }

        [Serialize(0, false)]
        public int MinAgentsPerBrainRoom { get; private set; }

        [Serialize(3, false)]
        public int MaxAgentsPerRoom { get; private set; }

        [Serialize(2, false)]
        public int MinAgentsOutside { get; private set; }

        [Serialize(5, false)]
        public int MaxAgentsOutside { get; private set; }

        [Serialize(3, false)]
        public int MinAgentsInside { get; private set; }

        [Serialize(10, false)]
        public int MaxAgentsInside { get; private set; }

        [Serialize(15, false)]
        public int MaxAgentCount { get; private set; }

        [Serialize(100f, false)]
        public float MinWaterLevel { get; private set; }

        [Serialize(true, false)]
        public bool KillAgentsWhenEntityDies { get; private set; }

        [Serialize(1f, false)]
        public float DeadEntityColorMultiplier { get; private set; }

        [Serialize(1f, false)]
        public float DeadEntityColorFadeOutTime { get; private set; }

        public readonly string[] ForbiddenAmmunition;

        public static List<WreckAIConfig> List
        {
            get
            {
                if (paramsList == null)
                {
                    LoadAll();
                }
                return paramsList;
            }
        }

        private static List<WreckAIConfig> paramsList;

        public static WreckAIConfig GetRandom() => List.GetRandom(Rand.RandSync.Server);

        public WreckAIConfig(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            ForbiddenAmmunition = XMLExtensions.GetAttributeStringArray(element, "ForbiddenAmmunition", new string[0], convertToLowerInvariant: true);
        }

        public static void LoadAll()
        {
            paramsList = new List<WreckAIConfig>();
            var files = GameMain.Instance.GetFilesOfType(ContentType.WreckAIConfig);
            if (files.None())
            {
                DebugConsole.ThrowError("Cannot find any Wreck AI config!");
                return;
            }
            foreach (ContentFile file in files)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (mainElement.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    paramsList.Clear();
                    DebugConsole.NewMessage($"Overriding the wreck ai config with '{file.Path}'", Color.Yellow);
                }
                else if (paramsList.Any())
                {
                    DebugConsole.NewMessage($"Adding additional wreck ai config from file '{file.Path}'");
                }
                paramsList.Add(new WreckAIConfig(mainElement));
            }
        }
    }
}
