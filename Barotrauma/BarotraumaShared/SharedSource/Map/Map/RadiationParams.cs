using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class RadiationParams: ISerializableEntity
    {
        public string Name => nameof(RadiationParams);
        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        [Serialize(defaultValue: -100f, isSaveable: IsPropertySaveable.No, "How much radiation the world starts with.")]
        public float StartingRadiation { get; set; }

        [Serialize(defaultValue: 100f, isSaveable: IsPropertySaveable.No, "How much radiation is added on each step.")]
        public float RadiationStep { get; set; }

        [Serialize(defaultValue: 10, isSaveable: IsPropertySaveable.No, "How many turns in radiation does it take for an outpost to be removed from the map.")]
        public int CriticalRadiationThreshold { get; set; }
        
        [Serialize(defaultValue: 3, isSaveable: IsPropertySaveable.No, "Minimum amount of outposts in the level that cannot be removed due to radiation.")]
        public int MinimumOutpostAmount { get; set; }

        [Serialize(defaultValue: 3f, isSaveable: IsPropertySaveable.No, "How fast the radiation increase animation goes.")]
        public float AnimationSpeed { get; set; }

        [Serialize(defaultValue: 10f, isSaveable: IsPropertySaveable.No, "How long it takes to apply more radiation damage while in a radiated zone.")]
        public float RadiationDamageDelay { get; set; }

        [Serialize(defaultValue: 1f, isSaveable: IsPropertySaveable.No, "How much is the radiation affliction increased by while in a radiated zone.")]
        public float RadiationDamageAmount { get; set; }

        [Serialize(defaultValue: -1.0f, isSaveable: IsPropertySaveable.No, "Maximum amount of radiation.")]
        public float MaxRadiation { get; set; }

        [Serialize(defaultValue: "139,0,0,85", isSaveable: IsPropertySaveable.No, "The color of the radiated area.")]
        public Color RadiationAreaColor { get; set; }

        [Serialize(defaultValue: "255,0,0,255", isSaveable: IsPropertySaveable.No, "The tint of the radiation border sprites.")]
        public Color RadiationBorderTint { get; set; }

        [Serialize(defaultValue: 16.66f, isSaveable: IsPropertySaveable.No, "Speed of the border spritesheet animation.")]
        public float BorderAnimationSpeed { get; set; }

        public RadiationParams(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }
}