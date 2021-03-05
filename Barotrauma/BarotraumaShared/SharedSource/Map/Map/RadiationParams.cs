using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class RadiationParams: ISerializableEntity
    {
        public string Name => nameof(RadiationParams);
        public Dictionary<string, SerializableProperty> SerializableProperties { get; }

        [Serialize(defaultValue: -100f, isSaveable: false, "How much radiation the world starts with.")]
        public float StartingRadiation { get; set; }

        [Serialize(defaultValue: 100f, isSaveable: false, "How much radiation is added on each step.")]
        public float RadiationStep { get; set; }

        [Serialize(defaultValue: 10, isSaveable: false, "How many turns in radiation does it take for an outpost to be removed from the map.")]
        public int CriticalRadiationThreshold { get; set; }
        
        [Serialize(defaultValue: 3, isSaveable: false, "Minimum amount of outposts in the level that cannot be removed due to radiation.")]
        public int MinimumOutpostAmount { get; set; }

        [Serialize(defaultValue: 3f, isSaveable: false, "How fast the radiation increase animation goes.")]
        public float AnimationSpeed { get; set; }

        [Serialize(defaultValue: 10f, isSaveable: false, "How long it takes to apply more radiation damage while in a radiated zone.")]
        public float RadiationDamageDelay { get; set; }

        [Serialize(defaultValue: 1f, isSaveable: false, "How much is the radiation affliction increased by while in a radiated zone.")]
        public float RadiationDamageAmount { get; set; }

        [Serialize(defaultValue: "139,0,0,85", isSaveable: false, "The color of the radiated area.")]
        public Color RadiationAreaColor { get; set; }

        [Serialize(defaultValue: "255,0,0,255", isSaveable: false, "The tint of the radiation border sprites.")]
        public Color RadiationBorderTint { get; set; }

        [Serialize(defaultValue: 16.66f, isSaveable: false, "Speed of the border spritesheet animation.")]
        public float BorderAnimationSpeed { get; set; }

        public RadiationParams(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }
}