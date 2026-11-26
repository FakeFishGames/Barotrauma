using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    /// <summary>
    /// Modifies a turret when this item is the ammunition the turret would fire next (e.g. an ammo box in a linked loader).
    /// The turret's own values are never modified, so the multipliers stack with customized values, upgrades and skill bonuses.
    /// </summary>
    public class TurretAmmo : ItemComponent
    {
        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the turret's reload time when firing this ammunition.")]
        public float ReloadMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the time it takes for a charge-type turret to charge up when this ammunition is loaded.")]
        public float MaxChargeTimeMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the power the turret needs to fire this ammunition.")]
        public float PowerConsumptionMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the structure fix amount of the turret's RepairTool (e.g. how much a cutting laser damages walls).")]
        public float StructureFixAmountMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the range of the turret's ElectricalDischarger (how far the discharge branches can travel from the main arc).")]
        public float DischargeRangeMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the raycast range of the turret's ElectricalDischarger (how far the main arc travels).")]
        public float DischargeRaycastRangeMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the duration of the turret's ElectricalDischarger's discharges.")]
        public float DischargeDurationMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier for the RangeMultiplierInWalls of the turret's ElectricalDischarger (how much further the discharge can travel across walls).")]
        public float DischargeWallRangeMultiplier { get; set; }

        /// <summary>
        /// Colors to use for the turret's (or the components it drives, like a RepairTool) particle emitters by emitter id, instead of the emitters' own colors.
        /// Defined with ParticleEmitterColor child elements.
        /// </summary>
        private readonly Dictionary<Identifier, Color> particleEmitterColors = new Dictionary<Identifier, Color>();

        /// <summary>
        /// Colors to use for the turret's light components (by component identifier) instead of the lights' own colors.
        /// Defined with LightColor child elements.
        /// </summary>
        private readonly Dictionary<Identifier, Color> lightColors = new Dictionary<Identifier, Color>();

        /// <summary>
        /// Color to use for the line drawn by the turret's LineDrawer (e.g. the cutting laser's beam) instead of the LineDrawer's own color. Defined with a Line child element.
        /// </summary>
        public Color? LineColor { get; private set; }

        /// <summary>
        /// Multiplier for the width of the line drawn by the turret's LineDrawer. Defined with a Line child element.
        /// </summary>
        public float LineWidthMultiplier { get; private set; } = 1.0f;

        /// <summary>
        /// Particle prefab whose sprite is used to render the line drawn by the turret's LineDrawer, instead of the LineDrawer's own. Empty = use the LineDrawer's own. Defined with a Line child element.
        /// </summary>
        public Identifier LineParticle { get; private set; } = Identifier.Empty;

        /// <summary>
        /// Colors and arc shapes to use for the turret's ElectricalDischarger instead of the discharger's own (null = use the discharger's own).
        /// Defined with a Discharge child element.
        /// </summary>
        public Color? DischargeColor1 { get; private set; }
        /// <inheritdoc cref="DischargeColor1"/>
        public Color? DischargeColor2 { get; private set; }
        /// <inheritdoc cref="DischargeColor1"/>
        public Color? DischargeSparkColor { get; private set; }
        /// <inheritdoc cref="DischargeColor1"/>
        public Vector2? DischargeElectricityScale { get; private set; }
        /// <inheritdoc cref="DischargeColor1"/>
        public float? DischargeMainArcVariance { get; private set; }
        /// <inheritdoc cref="DischargeColor1"/>
        public float? DischargeChildArcVariance { get; private set; }

        /// <summary>
        /// Status effects the turret's RepairTool applies on the targets it hits, in addition to its own effects. Defined with HitStatusEffect child elements.
        /// Not defined as regular StatusEffects, because those would be applied on the ammo item itself when the turret uses it.
        /// </summary>
        private readonly List<StatusEffect> hitStatusEffects = new List<StatusEffect>();

        public TurretAmmo(Item item, ContentXElement element) : base(item, element)
        {
            //only holds values the turret reads, no need to update
            IsActive = false;

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemittercolor":
                        Identifier emitterId = subElement.GetAttributeIdentifier("id", Identifier.Empty);
                        if (emitterId.IsEmpty)
                        {
                            DebugConsole.ThrowError($"Error in item \"{item.Prefab.Identifier}\": ParticleEmitterColor in TurretAmmo has no emitter id defined.",
                                contentPackage: element.ContentPackage);
                            continue;
                        }
                        particleEmitterColors[emitterId] = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "lightcolor":
                        Identifier lightId = subElement.GetAttributeIdentifier("id", Identifier.Empty);
                        if (lightId.IsEmpty)
                        {
                            DebugConsole.ThrowError($"Error in item \"{item.Prefab.Identifier}\": LightColor in TurretAmmo has no light component identifier defined.",
                                contentPackage: element.ContentPackage);
                            continue;
                        }
                        lightColors[lightId] = subElement.GetAttributeColor("color", Color.White);
                        break;
                    //not named "LineDrawer", because the base class would interpret that as a LineDrawer component and add it to the item
                    case "line":
                        LineColor = subElement.GetAttributeColor("color");
                        LineWidthMultiplier = subElement.GetAttributeFloat("widthmultiplier", 1.0f);
                        LineParticle = subElement.GetAttributeIdentifier("lineparticle", Identifier.Empty);
                        break;
                    //not named "ElectricalDischarger", because the base class would interpret that as an ElectricalDischarger component and add it to the item
                    case "discharge":
                        DischargeColor1 = subElement.GetAttributeColor("color1");
                        DischargeColor2 = subElement.GetAttributeColor("color2");
                        DischargeSparkColor = subElement.GetAttributeColor("sparkcolor");
                        if (subElement.GetAttribute("electricityscale") != null) { DischargeElectricityScale = subElement.GetAttributeVector2("electricityscale", Vector2.One); }
                        if (subElement.GetAttribute("mainarcvariance") != null) { DischargeMainArcVariance = subElement.GetAttributeFloat("mainarcvariance", 0.0f); }
                        if (subElement.GetAttribute("childarcvariance") != null) { DischargeChildArcVariance = subElement.GetAttributeFloat("childarcvariance", 0.0f); }
                        break;
                    case "hitstatuseffect":
                        hitStatusEffects.Add(StatusEffect.Load(subElement, $"{item.Prefab.Identifier}, TurretAmmo"));
                        break;
                }
            }
        }

        /// <summary>
        /// Returns the color defined for the particle emitter, or null if the emitter should use its own color.
        /// </summary>
        public Color? GetParticleEmitterColor(Identifier emitterId) => particleEmitterColors.TryGetValue(emitterId, out Color color) ? color : null;

        /// <summary>
        /// Returns the color defined for the light component with the given identifier, or null if the light should use its own color.
        /// </summary>
        public Color? GetLightColor(Identifier lightComponentId) => lightColors.TryGetValue(lightComponentId, out Color color) ? color : null;

        public IEnumerable<StatusEffect> GetHitStatusEffects(ActionType type)
        {
            foreach (StatusEffect statusEffect in hitStatusEffects)
            {
                if (statusEffect.Type == type) { yield return statusEffect; }
            }
        }
    }
}
