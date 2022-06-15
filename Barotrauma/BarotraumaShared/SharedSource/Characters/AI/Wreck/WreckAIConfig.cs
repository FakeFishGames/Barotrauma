using System;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class WreckAIConfig : PrefabWithUintIdentifier, ISerializableEntity
    {
        public readonly static PrefabCollection<WreckAIConfig> Prefabs = new PrefabCollection<WreckAIConfig>();

        public string Name => "Wreck AI Config";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        public Identifier Entity => Identifier;

        [Serialize("", IsPropertySaveable.No)]
        public Identifier DefensiveAgent { get; private set; }

        [Serialize("", IsPropertySaveable.No)]
        public string OffensiveAgent { get; private set; }

        [Serialize("", IsPropertySaveable.No)]
        public string Brain { get; private set; }

        [Serialize("", IsPropertySaveable.No)]
        public string Spawner { get; private set; }

        [Serialize("", IsPropertySaveable.No)]
        public string BrainRoomBackground { get; private set; }

        [Serialize("", IsPropertySaveable.No)]
        public string BrainRoomVerticalWall { get; private set; }

        [Serialize("", IsPropertySaveable.No)]
        public string BrainRoomHorizontalWall { get; private set; }

        [Serialize(60f, IsPropertySaveable.No)]
        public float AgentSpawnDelay { get; private set; }

        [Serialize(0.5f, IsPropertySaveable.No)]
        public float AgentSpawnDelayRandomFactor { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float AgentSpawnDelayDifficultyMultiplier { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float AgentSpawnCountDifficultyMultiplier { get; private set; }

        [Serialize(0, IsPropertySaveable.No)]
        public int MinAgentsPerBrainRoom { get; private set; }

        [Serialize(3, IsPropertySaveable.No)]
        public int MaxAgentsPerRoom { get; private set; }

        [Serialize(2, IsPropertySaveable.No)]
        public int MinAgentsOutside { get; private set; }

        [Serialize(5, IsPropertySaveable.No)]
        public int MaxAgentsOutside { get; private set; }

        [Serialize(3, IsPropertySaveable.No)]
        public int MinAgentsInside { get; private set; }

        [Serialize(10, IsPropertySaveable.No)]
        public int MaxAgentsInside { get; private set; }

        [Serialize(15, IsPropertySaveable.No)]
        public int MaxAgentCount { get; private set; }

        [Serialize(100f, IsPropertySaveable.No)]
        public float MinWaterLevel { get; private set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool KillAgentsWhenEntityDies { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float DeadEntityColorMultiplier { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float DeadEntityColorFadeOutTime { get; private set; }

        public readonly Identifier[] ForbiddenAmmunition;

        public static WreckAIConfig GetRandom() => Prefabs.OrderBy(p => p.UintIdentifier).GetRandom(Rand.RandSync.ServerAndClient);

        protected override Identifier DetermineIdentifier(XElement element)
        {
            return element.GetAttributeIdentifier("Entity", base.DetermineIdentifier(element));
        }

        public WreckAIConfig(ContentXElement element, WreckAIConfigFile file) : base(file, element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            ForbiddenAmmunition = XMLExtensions.GetAttributeIdentifierArray(element, "ForbiddenAmmunition", Array.Empty<Identifier>());
        }

        public override void Dispose() { }
    }
}
