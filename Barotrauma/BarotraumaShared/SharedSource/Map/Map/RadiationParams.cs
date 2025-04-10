using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class RadiationParams: ISerializableEntity
    {
        public string Name => nameof(RadiationParams);
        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        [Serialize(defaultValue: -100f, isSaveable: IsPropertySaveable.No, "How much Jovian radiation the world starts with.")]
        public float StartingRadiation { get; set; }

        [Serialize(defaultValue: 100f, isSaveable: IsPropertySaveable.No, "How much Jovian radiation is added on each step.")]
        public float RadiationStep { get; set; }
        
        [Serialize(defaultValue: 250f, isSaveable: IsPropertySaveable.No, "The interval at which Jovian radiation's effect multiplies in intensity, measured in map pixels. For example if this is 200, then 400 map pixels into the radiation its effect will be doubled.")]
        public float RadiationEffectMultipliedPerPixelDistance { get; set; }

        [Serialize(defaultValue: 10, isSaveable: IsPropertySaveable.No, "How many turns in Jovian radiation does it take for an outpost to be removed from the map.")]
        public int CriticalRadiationThreshold { get; set; }
        
        [Serialize(defaultValue: 3, isSaveable: IsPropertySaveable.No, "Minimum amount of outposts in the level that cannot be removed due to Jovian radiation.")]
        public int MinimumOutpostAmount { get; set; }

        [Serialize(defaultValue: 10f, isSaveable: IsPropertySaveable.No, "How long it takes to apply more of the Jovian radiation's effect while in the radiated zone.")]
        public float RadiationDamageDelay { get; set; }

        [Serialize(defaultValue: 1f, isSaveable: IsPropertySaveable.No, "How much is the Jovian radiation affliction increased by while in a radiated zone.")]
        public float RadiationDamageAmount { get; set; }

        [Serialize(defaultValue: -1.0f, isSaveable: IsPropertySaveable.No, "Maximum amount of Jovian radiation.")]
        public float MaxRadiation { get; set; }
        
        [Serialize(defaultValue: 3f, isSaveable: IsPropertySaveable.No, "How fast the Jovian radiation increase animation goes in the map view.")]
        public float AnimationSpeed { get; set; }
        
        [Serialize(defaultValue: "139,0,0,85", isSaveable: IsPropertySaveable.No, "The color of the radiated area in the map view.")]
        public Color RadiationAreaColor { get; set; }

        [Serialize(defaultValue: "255,0,0,255", isSaveable: IsPropertySaveable.No, "The tint of the Jovian radiation border sprites in the map view.")]
        public Color RadiationBorderTint { get; set; }

        [Serialize(defaultValue: 16.66f, isSaveable: IsPropertySaveable.No, "Speed of the border spritesheet animation in the map view.")]
        public float BorderAnimationSpeed { get; set; }

        public RadiationParams(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }
    }
}