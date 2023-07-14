using Barotrauma.Abilities;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Extensions;
using System.Collections.Immutable;
using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    class CPRSettings : Prefab
    {
        public readonly static PrefabSelector<CPRSettings> Prefabs = new PrefabSelector<CPRSettings>();
        public static CPRSettings Active => Prefabs.ActivePrefab;

        public readonly float ReviveChancePerSkill;
        public readonly float ReviveChanceExponent;
        public readonly float ReviveChanceMin;
        public readonly float ReviveChanceMax;
        public readonly float StabilizationPerSkill;
        public readonly float StabilizationMin;
        public readonly float StabilizationMax;
        public readonly float DamageSkillThreshold;
        public readonly float DamageSkillMultiplier;

        private readonly string insufficientSkillAfflictionIdentifier;
        public AfflictionPrefab InsufficientSkillAffliction
        {
            get
            {
                return
                    AfflictionPrefab.Prefabs.ContainsKey(insufficientSkillAfflictionIdentifier) ?
                    AfflictionPrefab.Prefabs[insufficientSkillAfflictionIdentifier] :
                    AfflictionPrefab.InternalDamage;
            }
        }

        public CPRSettings(XElement element, AfflictionsFile file) : base(file, file.Path.Value.ToIdentifier())
        {
            ReviveChancePerSkill = Math.Max(element.GetAttributeFloat("revivechanceperskill", 0.01f), 0.0f);
            ReviveChanceExponent = Math.Max(element.GetAttributeFloat("revivechanceexponent", 2.0f), 0.0f);
            ReviveChanceMin = MathHelper.Clamp(element.GetAttributeFloat("revivechancemin", 0.05f), 0.0f, 1.0f);
            ReviveChanceMax = MathHelper.Clamp(element.GetAttributeFloat("revivechancemax", 0.9f), ReviveChanceMin, 1.0f);

            StabilizationPerSkill = Math.Max(element.GetAttributeFloat("stabilizationperskill", 0.01f), 0.0f);
            StabilizationMin = MathHelper.Max(element.GetAttributeFloat("stabilizationmin", 0.05f), 0.0f);
            StabilizationMax = MathHelper.Max(element.GetAttributeFloat("stabilizationmax", 2.0f), StabilizationMin);

            DamageSkillThreshold = MathHelper.Clamp(element.GetAttributeFloat("damageskillthreshold", 40.0f), 0.0f, 100.0f);
            DamageSkillMultiplier = MathHelper.Clamp(element.GetAttributeFloat("damageskillmultiplier", 0.1f), 0.0f, 100.0f);

            insufficientSkillAfflictionIdentifier = element.GetAttributeString("insufficientskillaffliction", "");
        }

        public override void Dispose() { }
    }

    /// <summary>
    /// AfflictionPrefabHusk is a special type of affliction that has added functionality for husk infection.
    /// </summary>
    class AfflictionPrefabHusk : AfflictionPrefab
    {
        // Use any of these to define which limb the appendage is attached to.
        // If multiple are defined, the order of preference is: id, name, type.
        public readonly int AttachLimbId;
        public readonly string AttachLimbName;
        public readonly LimbType AttachLimbType;

        /// <summary>
        /// The minimum strength at which husk infection will be in the dormant stage.
        /// It must be less than or equal to ActiveThreshold.
        /// </summary>
        public readonly float DormantThreshold;

        /// <summary>
        /// The minimum strength at which husk infection will be in the active stage.
        /// It must be greater than or equal to DormantThreshold and less than or equal to TransitionThreshold.
        /// </summary>
        public readonly float ActiveThreshold;

        /// <summary>
        /// The minimum strength at which husk infection will be in its final stage.
        /// It must be greater than or equal to ActiveThreshold.
        /// </summary>
        public readonly float TransitionThreshold;

        /// <summary>
        /// The minimum strength the affliction must have for the affected character
        /// to transform into a husk upon death.
        /// </summary>
        public readonly float TransformThresholdOnDeath;

        /// <summary>
        /// The species of husk to convert the affected character to
        /// once husk infection reaches its final stage.
        /// </summary>
        public readonly Identifier HuskedSpeciesName;

        /// <summary>
        /// If set to true, all buffs are transferred to the converted
        /// character after husk transformation is complete.
        /// </summary>
        public readonly bool TransferBuffs;

        /// <summary>
        /// If set to true, the affected player will see on-screen messages describing husk infection symptoms
        /// and affected bots will speak about their current husk infection stage.
        /// </summary>
        public readonly bool SendMessages;

        /// <summary>
        /// If set to true, affected characters will have their speech impeded once the affliction
        /// reaches the dormant stage.
        /// </summary>
        public readonly bool CauseSpeechImpediment;

        /// <summary>
        /// If set to false, affected characters will no longer require air
        /// once the affliction reaches the active stage.
        /// </summary>
        public readonly bool NeedsAir;

        /// <summary>
        /// If set to true, affected players will retain control of their character
        /// after transforming into a husk.
        /// </summary>
        public readonly bool ControlHusk;

        public AfflictionPrefabHusk(ContentXElement element, AfflictionsFile file, Type type = null) : base(element, file, type)
        {
            HuskedSpeciesName = element.GetAttributeIdentifier("huskedspeciesname", Identifier.Empty);
            if (HuskedSpeciesName.IsEmpty)
            {
                DebugConsole.NewMessage($"No 'huskedspeciesname' defined for the husk affliction ({Identifier}) in {element}", Color.Orange);
                HuskedSpeciesName = "husk".ToIdentifier();
            }
            // Remove "[speciesname]" for backward support (we don't use it anymore)
            HuskedSpeciesName = HuskedSpeciesName.Remove("[speciesname]").ToIdentifier();
            if (TargetSpecies.Length == 0)
            {
                DebugConsole.NewMessage($"No 'targets' defined for the husk affliction ({Identifier}) in {element}", Color.Orange);
                TargetSpecies = new Identifier[] { CharacterPrefab.HumanSpeciesName };
            }
            var attachElement = element.GetChildElement("attachlimb");
            if (attachElement != null)
            {
                AttachLimbId = attachElement.GetAttributeInt("id", -1);
                AttachLimbName = attachElement.GetAttributeString("name", null);
                AttachLimbType = attachElement.GetAttributeEnum("type", LimbType.None);
            }
            else
            {
                AttachLimbId = -1;
                AttachLimbName = null;
                AttachLimbType = LimbType.None;
            }

            TransferBuffs = element.GetAttributeBool("transferbuffs", true);
            SendMessages = element.GetAttributeBool("sendmessages", true);
            CauseSpeechImpediment = element.GetAttributeBool("causespeechimpediment", true);
            NeedsAir = element.GetAttributeBool("needsair", false);
            ControlHusk = element.GetAttributeBool("controlhusk", false);

            DormantThreshold = element.GetAttributeFloat("dormantthreshold", MaxStrength * 0.5f);
            ActiveThreshold = element.GetAttributeFloat("activethreshold", MaxStrength * 0.75f);
            TransitionThreshold = element.GetAttributeFloat("transitionthreshold", MaxStrength);

            if (DormantThreshold > ActiveThreshold)
            {
                DebugConsole.ThrowError($"Error in \"{Identifier}\": {nameof(DormantThreshold)} is greater than {nameof(ActiveThreshold)} ({DormantThreshold} > {ActiveThreshold})");
            }
            if (ActiveThreshold > TransitionThreshold)
            {
                DebugConsole.ThrowError($"Error in \"{Identifier}\": {nameof(ActiveThreshold)} is greater than {nameof(TransitionThreshold)} ({ActiveThreshold} > {TransitionThreshold})");
            }

            TransformThresholdOnDeath = element.GetAttributeFloat("transformthresholdondeath", ActiveThreshold);
        }
    }

    /// <summary>
    /// AfflictionPrefab is a prefab that defines a type of affliction that can be applied to a character.
    /// There are multiple sub-types of afflictions such as AfflictionPrefabHusk, AfflictionPsychosis and AfflictionBleeding that can be used for additional functionality.
    /// 
    /// When defining a new affliction, the type will be determined by the element name.
    /// </summary>
    /// <example>
    /// <code language="xml">
    /// <Afflictions>
    ///     <!-- Defines a regular affliction. -->
    ///     <Affliction identifier="mycoolaffliction1" />
    /// 
    ///     <!-- Defines an AfflictionPrefabHusk affliction. -->
    ///     <AfflictionPrefabHusk identifier="mycoolaffliction2"/>
    /// 
    ///     <!-- Defines an AfflictionBleeding affliction. -->
    ///     <AfflictionBleeding identifier="mycoolaffliction3"/>
    /// </Afflictions>
    /// </code>
    /// </example>
    class AfflictionPrefab : PrefabWithUintIdentifier
    {
        /// <summary>
        /// Effects are the primary way to add functionality to afflictions.
        /// </summary>
        /// <doc>
        /// <Ignore type="SubElement" identifier="AbilityFlag" />
        /// <SubElement identifier="abilityflag" type="AppliedAbilityFlag">
        ///     Enables the specified flag on the character as long as the effect is active.
        /// </SubElement>
        /// <Type identifier="AppliedAbilityFlag">
        /// <Summary>
        ///     Flag that will be enabled for the character as long as the effect is active.
        /// <example>
        /// <code language="xml">
        /// <Effect minstrength="0" maxstrength="100">
        ///     <!-- Grants pressure immunity to the character while the effect is active. -->
        ///     <AbilityFlag flagtype="ImmuneToPressure" />
        /// </Effect>
        /// </code>
        /// </example>
        /// </Summary>
        /// <Field identifier="FlagType" type="AbilityFlags" defaultValue="None">
        ///     Which ability flag to enable.
        /// </Field>
        /// </Type>
        /// </doc>
        public sealed class Effect
        {
            //this effect is applied when the strength is within this range
            [Serialize(0.0f, IsPropertySaveable.No, description: "Minimum affliction strength required for this effect to be active.")]
            public float MinStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Maximum affliction strength for which this effect will be active.")]
            public float MaxStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "The amount of vitality that is lost at this effect's lowest strength.")]
            public float MinVitalityDecrease { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "The amount of vitality that is lost at this effect's highest strength.")]
            public float MaxVitalityDecrease { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "How much the affliction's strength changes every second while this effect is active.")]
            public float StrengthChange { get; private set; }

            [Serialize(false, IsPropertySaveable.No, description:
                "If set to true, MinVitalityDecrease and MaxVitalityDecrease represent a fraction of the affected character's maximum " +
                "vitality, with 1 meaning 100%, instead of the same amount for all species.")]
            public bool MultiplyByMaxVitality { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Blur effect strength at this effect's lowest strength.")]
            public float MinScreenBlur { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Blur effect strength at this effect's highest strength.")]
            public float MaxScreenBlur { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Generic distortion effect strength at this effect's lowest strength.")]
            public float MinScreenDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Generic distortion effect strength at this effect's highest strength.")]
            public float MaxScreenDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Radial distortion effect strength at this effect's lowest strength.")]
            public float MinRadialDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Radial distortion effect strength at this effect's highest strength.")]
            public float MaxRadialDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Chromatic aberration effect strength at this effect's lowest strength.")]
            public float MinChromaticAberration { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Chromatic aberration effect strength at this effect's highest strength.")]
            public float MaxChromaticAberration { get; private set; }

            [Serialize("255,255,255,255", IsPropertySaveable.No, description: "Radiation grain effect color.")]
            public Color GrainColor { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Radiation grain effect strength at this effect's lowest strength.")]
            public float MinGrainStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description: "Radiation grain effect strength at this effect's highest strength.")]
            public float MaxGrainStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No, description:
                "The maximum rate of fluctuation to apply to visual effects caused by this affliction effect. " +
                "Effective fluctuation is proportional to the affliction's current strength.")]
            public float ScreenEffectFluctuationFrequency { get; private set; }
            
            [Serialize(1.0f, IsPropertySaveable.No, description:
                "Multiplier for the affliction overlay's opacity at this effect's lowest strength. " +
                "See the list of elements for more details.")]
            public float MinAfflictionOverlayAlphaMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description:
                "Multiplier for the affliction overlay's opacity at this effect's highest strength. " +
                "See the list of elements for more details.")]
            public float MaxAfflictionOverlayAlphaMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description:
                "Multiplier for every buff's decay rate at this effect's lowest strength. " +
                "Only applies to afflictions of class BuffDurationIncrease.")]
            public float MinBuffMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description:
                "Multiplier for every buff's decay rate at this effect's highest strength. " +
                "Only applies to afflictions of class BuffDurationIncrease.")]
            public float MaxBuffMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier to apply to the affected character's speed at this effect's lowest strength.")]
            public float MinSpeedMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier to apply to the affected character's speed at this effect's highest strength.")]
            public float MaxSpeedMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier to apply to all of the affected character's skill levels at this effect's lowest strength.")]
            public float MinSkillMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No, description: "Multiplier to apply to all of the affected character's skill levels at this effect's highest strength.")]
            public float MaxSkillMultiplier { get; private set; }
            
            /// <summary>
            /// A list of identifiers of afflictions that the affected character will be
            /// resistant to when this effect is active.
            /// </summary>
            public readonly ImmutableArray<Identifier> ResistanceFor;

            [Serialize(0.0f, IsPropertySaveable.No,
                description: "The amount of resistance to the afflictions specified by ResistanceFor to apply at this effect's lowest strength.")]
            public float MinResistance { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No,
                description: "The amount of resistance to the afflictions specified by ResistanceFor to apply at this effect's highest strength.")]
            public float MaxResistance { get; private set; }

            [Serialize("", IsPropertySaveable.No, description: "Identifier used by AI to determine conversation lines to say when this effect is active.")]
            public Identifier DialogFlag { get; private set; }

            [Serialize("", IsPropertySaveable.No, description: "Tag that enemy AI may use to target the affected character when this effect is active.")]
            public Identifier Tag { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No,
                description: "Color to tint the affected character's face with at this effect's lowest strength. The alpha channel is used to determine how much to tint the character's face.")]
            public Color MinFaceTint { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No,
                description: "Color to tint the affected character's face with at this effect's highest strength. The alpha channel is used to determine how much to tint the character's face.")]
            public Color MaxFaceTint { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No,
                description: "Color to tint the affected character's entire body with at this effect's lowest strength. The alpha channel is used to determine how much to tint the character.")]
            public Color MinBodyTint { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No,
                description: "Color to tint the affected character's entire body with at this effect's highest strength. The alpha channel is used to determine how much to tint the character.")]
            public Color MaxBodyTint { get; private set; }

            /// <summary>
            /// StatType that will be applied to the affected character when the effect is active that is proportional to the effect's strength.
            /// </summary>
            /// <example>
            /// <code language="xml">
            /// <Effect minstrength="0" maxstrength="100">
            ///     <!-- Walking speed will be increased by 10% at strength 0, 20% at 50 and 30% at 100 -->
            ///     <StatValue stattype="WalkingSpeed" minvalue="0.1" maxvalue="0.3" />
            ///     <!-- Maximum health will be increased by 20% regardless of the effect strength -->
            ///     <StatValue stattype="MaximumHealthMultiplier" value="0.2" />
            /// </Effect>
            /// </code>
            /// </example>
            public readonly struct AppliedStatValue
            {
                /// <summary>
                /// Which StatType to apply
                /// </summary>
                public readonly StatTypes StatType;

                /// <summary>
                /// Minimum value to apply
                /// </summary>
                public readonly float MinValue;

                /// <summary>
                /// Minimum value to apply
                /// </summary>
                public readonly float MaxValue;

                /// <summary>
                /// Constant value to apply, will be ignored if MinValue or MaxValue are set
                /// </summary>
                private readonly float Value;

                public AppliedStatValue(ContentXElement element)
                {
                    Value = element.GetAttributeFloat("value", 0.0f);
                    StatType = element.GetAttributeEnum("stattype", StatTypes.None);
                    MinValue = element.GetAttributeFloat("minvalue", Value);
                    MaxValue = element.GetAttributeFloat("maxvalue", Value);
                }
            }

            /// <summary>
            /// Prevents AfflictionHusks with the specified identifier(s) from transforming the character into an AI-controlled character.
            /// </summary>
            public readonly ImmutableArray<Identifier> BlockTransformation;

            /// <summary>
            /// StatType that will be applied to the affected character when the effect is active that is proportional to the effect's strength.
            /// </summary>
            public readonly ImmutableDictionary<StatTypes, AppliedStatValue> AfflictionStatValues;

            public readonly AbilityFlags AfflictionAbilityFlags;

            //statuseffects applied on the character when the affliction is active
            public readonly ImmutableArray<StatusEffect> StatusEffects;

            public Effect(ContentXElement element, string parentDebugName)
            {
                SerializableProperty.DeserializeProperties(this, element);

                ResistanceFor = element.GetAttributeIdentifierArray("resistancefor", Array.Empty<Identifier>())!.ToImmutableArray();
                BlockTransformation = element.GetAttributeIdentifierArray("blocktransformation", Array.Empty<Identifier>())!.ToImmutableArray();

                var afflictionStatValues = new Dictionary<StatTypes, AppliedStatValue>();
                var statusEffects = new List<StatusEffect>();
                foreach (var subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "statuseffect":
                            statusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
                            break;
                        case "statvalue":
                            var newStatValue = new AppliedStatValue(subElement);
                            afflictionStatValues.Add(newStatValue.StatType, newStatValue);
                            break;
                        case "abilityflag":
                            AbilityFlags flagType = subElement.GetAttributeEnum("flagtype", AbilityFlags.None);
                            if (flagType is AbilityFlags.None)
                            {
                                DebugConsole.ThrowError($"Error in affliction \"{parentDebugName}\" - invalid ability flag type \"{subElement.GetAttributeString("flagtype", "")}\".");
                                continue;
                            }
                            AfflictionAbilityFlags |= flagType;
                            break;
                        case "affliction":
                            DebugConsole.AddWarning($"Error in affliction \"{parentDebugName}\" - additional afflictions caused by the affliction should be configured inside status effects.");
                            break;
                    }
                }
                AfflictionStatValues = afflictionStatValues.ToImmutableDictionary();
                StatusEffects = statusEffects.ToImmutableArray();
            }

            /// <summary>
            /// Returns 0 if affliction.Strength is MinStrength,
            /// 1 if affliction.Strength is MaxStrength
            /// </summary>
            public float GetStrengthFactor(Affliction affliction) => GetStrengthFactor(affliction.Strength);

            /// <summary>
            /// Returns 0 if affliction.Strength is MinStrength,
            /// 1 if affliction.Strength is MaxStrength
            /// </summary>
            public float GetStrengthFactor(float strength)
                => MathUtils.InverseLerp(
                    MinStrength,
                    MaxStrength,
                    strength);
        }

        /// <summary>
        /// The description element can be used to define descriptions for the affliction which are shown under specific conditions;
        /// for example a description that only shows to other players or only at certain strength levels.
        /// </summary>
        /// <doc>
        /// <Field identifier="Text" type="string" defaultValue="&quot;&quot;">
        /// Raw text for the description.
        /// </Field>
        /// </doc>
        public sealed class Description
        {
            public enum TargetType
            {
                /// <summary>
                /// Everyone can see the description.
                /// </summary>
                Any,
                /// <summary>
                /// Only the affected character can see the description.
                /// </summary>
                Self,
                /// <summary>
                /// The affected character cannot see the description but others can.
                /// </summary>
                OtherCharacter
            }

            /// <summary>
            /// Raw text for the description.
            /// </summary>
            public readonly LocalizedString Text;

            /// <summary>
            /// Text tag used to set the text from the localization files.
            /// </summary>
            public readonly Identifier TextTag;

            /// <summary>
            /// Minimum strength required for the description to be shown.
            /// </summary>
            public readonly float MinStrength;

            /// <summary>
            /// Maximum strength required for the description to be shown.
            /// </summary>
            public readonly float MaxStrength;

            /// <summary>
            /// Who can see the description.
            /// </summary>
            public readonly TargetType Target;

            public Description(ContentXElement element, AfflictionPrefab affliction)
            {
                TextTag = element.GetAttributeIdentifier("textidentifier", Identifier.Empty);
                if (!TextTag.IsEmpty)
                {
                    Text = TextManager.Get(TextTag);
                }
                string text = element.GetAttributeString("text", string.Empty);
                if (!text.IsNullOrEmpty())
                {
                    Text = Text?.Fallback(text) ?? text;
                }
                else if (TextTag.IsEmpty)
                {
                    DebugConsole.ThrowError($"Error in affliction \"{affliction.Identifier}\" - no text defined for one of the descriptions.");
                }

                MinStrength = element.GetAttributeFloat(nameof(MinStrength), 0.0f);
                MaxStrength = element.GetAttributeFloat(nameof(MaxStrength), 100.0f);
                if (MinStrength >= MaxStrength)
                {
                    DebugConsole.ThrowError($"Error in affliction \"{affliction.Identifier}\" - max strength is not larger than min.");
                }
                Target = element.GetAttributeEnum(nameof(Target), TargetType.Any);
            }
        }

        /// <summary>
        /// PeriodicEffect applies StatusEffects to the character periodically.
        /// </summary>
        /// <doc>
        /// <SubElement identifier="StatusEffect" type="StatusEffect" />
        /// <Field identifier="Interval" type="float" defaultValue="1.0">
        ///     How often the status effect is applied in seconds.
        ///     Setting this attribute will set both the min and max interval to the specified value.
        /// </Field>
        /// <Field identifier="MinInterval" type="float" defaultValue="1.0">
        ///     Minimum interval between applying the status effect in seconds.
        /// </Field>
        /// <Field identifier="MaxInterval" type="float" defaultValue="1.0">
        ///     Maximum interval between applying the status effect in seconds.
        /// </Field>
        /// </doc>
        public sealed class PeriodicEffect
        {
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();
            public readonly float MinInterval, MaxInterval;
            public readonly float MinStrength, MaxStrength;

            public PeriodicEffect(ContentXElement element, string parentDebugName)
            {
                foreach (var subElement in element.Elements())
                {
                    StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
                }

                if (element.GetAttribute("interval") != null)
                {
                    MinInterval = MaxInterval = Math.Max(element.GetAttributeFloat("interval", 1.0f), 1.0f);
                }
                else
                {
                    MinInterval = Math.Max(element.GetAttributeFloat(nameof(MinInterval), 1.0f), 1.0f);
                    MaxInterval = Math.Max(element.GetAttributeFloat(nameof(MaxInterval), 1.0f), MinInterval);
                    MinStrength = Math.Max(element.GetAttributeFloat(nameof(MinStrength), 0f), 0f);
                    MaxStrength = Math.Max(element.GetAttributeFloat(nameof(MaxStrength), MinStrength), MinStrength);
                }
            }
        }

        public static readonly Identifier DamageType = "damage".ToIdentifier();
        public static readonly Identifier BurnType = "burn".ToIdentifier();
        public static readonly Identifier BleedingType = "bleeding".ToIdentifier();
        public static readonly Identifier ParalysisType = "paralysis".ToIdentifier();
        public static readonly Identifier PoisonType = "poison".ToIdentifier();
        public static readonly Identifier StunType = "stun".ToIdentifier();
        public static readonly Identifier EMPType = "emp".ToIdentifier();
        public static readonly Identifier SpaceHerpesType = "spaceherpes".ToIdentifier();
        public static readonly Identifier AlienInfectedType = "alieninfected".ToIdentifier();
        public static readonly Identifier InvertControlsType = "invertcontrols".ToIdentifier();
        public static readonly Identifier HuskInfectionType = "huskinfection".ToIdentifier();

        public static AfflictionPrefab InternalDamage => Prefabs["internaldamage"];
        public static AfflictionPrefab BiteWounds => Prefabs["bitewounds"];
        public static AfflictionPrefab ImpactDamage => Prefabs["blunttrauma"];
        public static AfflictionPrefab Bleeding => Prefabs[BleedingType];
        public static AfflictionPrefab Burn => Prefabs[BurnType];
        public static AfflictionPrefab OxygenLow => Prefabs["oxygenlow"];
        public static AfflictionPrefab Bloodloss => Prefabs["bloodloss"];
        public static AfflictionPrefab Pressure => Prefabs["pressure"];
        public static AfflictionPrefab Stun => Prefabs[StunType];
        public static AfflictionPrefab RadiationSickness => Prefabs["radiationsickness"];


        public static readonly PrefabCollection<AfflictionPrefab> Prefabs = new PrefabCollection<AfflictionPrefab>();

        public static IEnumerable<AfflictionPrefab> List => Prefabs;

        public override void Dispose() { }

        private readonly ContentXElement configElement;

        public readonly LocalizedString Name;
        
        public readonly LocalizedString CauseOfDeathDescription, SelfCauseOfDeathDescription;

        private readonly LocalizedString defaultDescription;
        public readonly ImmutableList<Description> Descriptions;

        /// <summary>
        /// Arbitrary string that is used to identify the type of the affliction.
        /// </summary>
        public readonly Identifier AfflictionType;
        
        /// <summary>
        /// If set to true, the affliction affects individual limbs. Otherwise, it affects the whole character.
        /// </summary>
        public readonly bool LimbSpecific;

        /// <summary>
        /// If the affliction doesn't affect individual limbs, this attribute determines
        /// where the game will render the affliction's indicator when viewed in the
        /// in-game health UI.
        ///
        /// For example, the psychosis indicator is rendered on the head, and low oxygen
        /// is rendered on the torso.
        /// </summary>
        public readonly LimbType IndicatorLimb;

        /// <summary>
        /// Can be set to the identifier of another affliction to make this affliction 
        /// reuse the same name and description.
        /// </summary>
        public readonly Identifier TranslationIdentifier;
        
        /// <summary>
        /// If set to true, the game will recognize this affliction as a buff.
        /// This means, among other things, that bots won't attempt to treat it,
        /// and the health UI will render the affected limb in green rather than red.
        /// </summary>
        public readonly bool IsBuff;
        
        /// <summary>
        /// If set to true, this affliction can affect characters that are marked as
        /// machines, such as the Fractal Guardian.
        /// </summary>
        public readonly bool AffectMachines;
        
        /// <summary>
        /// If set to true, this affliction can be healed at the medical clinic.
        /// </summary>
        /// <doc>
        /// <override type="DefaultValue">
        ///     false if the affliction is a buff or has the type "geneticmaterialbuff" or "geneticmaterialdebuff", true otherwise.
        /// </override>
        /// </doc>
        public readonly bool HealableInMedicalClinic;
        
        /// <summary>
        /// How much each unit of this affliction's strength will add
        /// to the cost of healing at the medical clinic.
        /// </summary>
        public readonly float HealCostMultiplier;
        
        /// <summary>
        /// The minimum cost of healing this affliction at the medical clinic.
        /// </summary>
        public readonly int BaseHealCost;
        
        /// <summary>
        /// If set to false, the health UI will not show the strength of the affliction
        /// as a bar under its indicator.
        /// </summary>
        public readonly bool ShowBarInHealthMenu;

        /// <summary>
        /// If set to true, this affliction's icon will be hidden from the HUD after 5 seconds.
        /// </summary>
        public readonly bool HideIconAfterDelay;

        /// <summary>
        /// How high the strength has to be for the affliction to take effect
        /// </summary>
        public readonly float ActivationThreshold = 0.0f;

        /// <summary>
        /// How high the strength has to be for the affliction icon to be shown in the UI
        /// </summary>
        public readonly float ShowIconThreshold = 0.05f;

        /// <summary>
        /// How high the strength has to be for the affliction icon to be shown to others with a health scanner or via the health interface
        /// </summary>
        public readonly float ShowIconToOthersThreshold = 0.05f;

        /// <summary>
        /// The maximum strength this affliction can have.
        /// </summary>
        public readonly float MaxStrength = 100.0f;

        /// <summary>
        /// The strength of the radiation grain effect to apply when the strength of this affliction increases.
        /// </summary>
        public readonly float GrainBurst;

        /// <summary>
        /// How high the strength has to be for the affliction icon to be shown with a health scanner
        /// </summary>
        public readonly float ShowInHealthScannerThreshold;

        /// <summary>
        /// How strong the affliction needs to be before bots attempt to treat it.
        /// Also effects when the affliction is shown in the suitable treatments list.
        /// </summary>
        public readonly float TreatmentThreshold;

        /// <summary>
        /// Bots will not try to treat the affliction if the character has any of these afflictions
        /// </summary>
        public ImmutableHashSet<Identifier> IgnoreTreatmentIfAfflictedBy;

        /// <summary>
        /// The duration of the affliction, in seconds. If set to 0, the affliction does not expire.
        /// </summary>
        public readonly float Duration;

        /// <summary>
        /// How much karma changes when a player applies this affliction to someone (per strength of the affliction)
        /// </summary>
        public float KarmaChangeOnApplied;

        /// <summary>
        /// Opacity of the burn effect (darker tint) on limbs affected by this affliction. 1 = full strength. 
        /// </summary>
        public readonly float BurnOverlayAlpha;

        /// <summary>
        /// Opacity of the bloody damage overlay on limbs affected by this affliction. 1 = full strength. 
        /// </summary>
        public readonly float DamageOverlayAlpha;

        /// Steam achievement given when the controlled character receives the affliction.
        /// </summary>
        public readonly Identifier AchievementOnReceived;

        /// <summary>
        /// Steam achievement given when the affliction is removed from the controlled character.
        /// </summary>
        public readonly Identifier AchievementOnRemoved;

        /// <summary>
        /// A gradient that defines which color to render this affliction's icon
        /// with, based on the affliction's current strength.
        /// </summary>
        public readonly Color[] IconColors;

        /// <summary>
        /// If set to true and the affliction has an AfflictionOverlay element, the overlay's opacity will be strictly proportional to its strength.
        /// Otherwise, the overlay's opacity will be determined based on its activation threshold and effects.
        /// </summary>
        public readonly bool AfflictionOverlayAlphaIsLinear;

        /// <summary>
        /// If set to true, this affliction will not persist between rounds.
        /// </summary>
        public readonly bool ResetBetweenRounds;

        /// <summary>
        /// Should damage particles be emitted when a character receives this affliction?
        /// Only relevant if the affliction is of the type "bleeding" or "damage".
        /// </summary>
        public readonly bool DamageParticles;

        /// <summary>
        /// An arbitrary modifier that affects how much medical skill is increased when you apply the affliction on a target. 
        /// If the affliction causes damage or is of the 'poison' or 'paralysis' type, the skill is increased only when the target is hostile. 
        /// If the affliction is of the 'buff' type, the skill is increased only when the target is friendly.
        /// </summary>
        public readonly float MedicalSkillGain;

        /// <summary>
        /// An arbitrary modifier that affects how much weapons skill is increased when you apply the affliction on a target. 
        /// The skill is increased only when the target is hostile. 
        /// </summary>
        public readonly float WeaponsSkillGain;

        /// <summary>
        /// A list of species this affliction is allowed to affect.
        /// </summary>
        public Identifier[] TargetSpecies { get; protected set; }

        /// <summary>
        /// Effects to apply at various strength levels.
        /// Only one effect can be applied at any given moment, so their ranges should be defined with no overlap.
        /// </summary>
        private readonly List<Effect> effects = new List<Effect>();
        
        /// <summary>
        /// PeriodicEffect applies StatusEffects to the character periodically.
        /// </summary>
        private readonly List<PeriodicEffect> periodicEffects = new List<PeriodicEffect>();

        public IEnumerable<Effect> Effects => effects;

        public IList<PeriodicEffect> PeriodicEffects => periodicEffects;

        private readonly ConstructorInfo constructor;

        /// <summary>
        /// An icon that’s used in the UI to represent this affliction.
        /// </summary>
        public readonly Sprite Icon;

        /// <summary>
        /// A sprite that covers the affected player's entire screen when this affliction is active.
        /// Its opacity is controlled by the active effect's MinAfflictionOverlayAlphaMultiplier and MaxAfflictionOverlayAlphaMultiplier
        /// </summary>
        public readonly Sprite AfflictionOverlay;

        public ImmutableDictionary<Identifier, float> TreatmentSuitabilities
        {
            get;
            private set;
        } = new Dictionary<Identifier, float>().ToImmutableDictionary();

        /// <summary>
        /// Can this affliction be treated with some item?
        /// </summary>
        public bool HasTreatments { get; private set; }

        public AfflictionPrefab(ContentXElement element, AfflictionsFile file, Type type) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            configElement = element;
            
            AfflictionType = element.GetAttributeIdentifier("type", "");
            TranslationIdentifier = element.GetAttributeIdentifier("translationoverride", Identifier);
            Name = TextManager.Get($"AfflictionName.{TranslationIdentifier}");
            string fallbackName = element.GetAttributeString("name", "");
            if (!string.IsNullOrEmpty(fallbackName))
            {
                Name = Name.Fallback(fallbackName);
            }
            defaultDescription = TextManager.Get($"AfflictionDescription.{TranslationIdentifier}");
            string fallbackDescription = element.GetAttributeString("description", "");
            if (!string.IsNullOrEmpty(fallbackDescription))
            {
                defaultDescription = defaultDescription.Fallback(fallbackDescription);
            }
            IsBuff = element.GetAttributeBool(nameof(IsBuff), false);
            AffectMachines = element.GetAttributeBool(nameof(AffectMachines), true);

            ShowBarInHealthMenu = element.GetAttributeBool("showbarinhealthmenu", true);

            HealableInMedicalClinic = element.GetAttributeBool("healableinmedicalclinic", 
                !IsBuff && 
                AfflictionType != "geneticmaterialbuff" && 
                AfflictionType != "geneticmaterialdebuff");
            HealCostMultiplier = element.GetAttributeFloat(nameof(HealCostMultiplier), 1f);
            BaseHealCost = element.GetAttributeInt(nameof(BaseHealCost), 0);

            IgnoreTreatmentIfAfflictedBy = element.GetAttributeIdentifierArray(nameof(IgnoreTreatmentIfAfflictedBy), Array.Empty<Identifier>()).ToImmutableHashSet();

            Duration = element.GetAttributeFloat(nameof(Duration), 0.0f);

            if (element.GetAttribute("nameidentifier") != null)
            {
                Name = TextManager.Get(element.GetAttributeString("nameidentifier", string.Empty)).Fallback(Name);
            }

            LimbSpecific = element.GetAttributeBool("limbspecific", false);
            if (!LimbSpecific)
            {
                string indicatorLimbName = element.GetAttributeString("indicatorlimb", "Torso");
                if (!Enum.TryParse(indicatorLimbName, out IndicatorLimb))
                {
                    DebugConsole.ThrowError("Error in affliction prefab " + Name + " - limb type \"" + indicatorLimbName + "\" not found.");
                }
            }

            HideIconAfterDelay = element.GetAttributeBool(nameof(HideIconAfterDelay), false);

            ActivationThreshold = element.GetAttributeFloat(nameof(ActivationThreshold), 0.0f);
            ShowIconThreshold   = element.GetAttributeFloat(nameof(ShowIconThreshold), Math.Max(ActivationThreshold, 0.05f));
            ShowIconToOthersThreshold   = element.GetAttributeFloat(nameof(ShowIconToOthersThreshold), ShowIconThreshold);
            MaxStrength         = element.GetAttributeFloat(nameof(MaxStrength), 100.0f);
            GrainBurst          = element.GetAttributeFloat(nameof(GrainBurst), 0.0f);

            ShowInHealthScannerThreshold = element.GetAttributeFloat(nameof(ShowInHealthScannerThreshold), 
                Math.Max(ActivationThreshold, AfflictionType == "talentbuff" ? float.MaxValue : ShowIconToOthersThreshold));
            TreatmentThreshold = element.GetAttributeFloat(nameof(TreatmentThreshold), Math.Max(ActivationThreshold, 10.0f));

            DamageOverlayAlpha  = element.GetAttributeFloat(nameof(DamageOverlayAlpha), 0.0f);
            BurnOverlayAlpha    = element.GetAttributeFloat(nameof(BurnOverlayAlpha), 0.0f);

            KarmaChangeOnApplied = element.GetAttributeFloat(nameof(KarmaChangeOnApplied), 0.0f);

            CauseOfDeathDescription     = 
                TextManager.Get($"AfflictionCauseOfDeath.{TranslationIdentifier}")
                .Fallback(TextManager.Get(element.GetAttributeString("causeofdeathdescription", "")))
                .Fallback(element.GetAttributeString("causeofdeathdescription", ""));
            SelfCauseOfDeathDescription = 
                TextManager.Get($"AfflictionCauseOfDeathSelf.{TranslationIdentifier}")
                .Fallback(TextManager.Get(element.GetAttributeString("selfcauseofdeathdescription", "")))
                .Fallback(element.GetAttributeString("selfcauseofdeathdescription", ""));

            IconColors = element.GetAttributeColorArray(nameof(IconColors), null);
            AfflictionOverlayAlphaIsLinear = element.GetAttributeBool(nameof(AfflictionOverlayAlphaIsLinear), false);
            AchievementOnReceived = element.GetAttributeIdentifier(nameof(AchievementOnReceived), "");
            AchievementOnRemoved = element.GetAttributeIdentifier(nameof(AchievementOnRemoved), "");

            TargetSpecies = element.GetAttributeIdentifierArray("targets", Array.Empty<Identifier>(), trim: true);

            ResetBetweenRounds = element.GetAttributeBool("resetbetweenrounds", false);

            DamageParticles = element.GetAttributeBool(nameof(DamageParticles), true);
            WeaponsSkillGain = element.GetAttributeFloat(nameof(WeaponsSkillGain), 0.0f);
            MedicalSkillGain = element.GetAttributeFloat(nameof(MedicalSkillGain), 0.0f);

            List<Description> descriptions = new List<Description>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        break;
                    case "afflictionoverlay":
                        AfflictionOverlay = new Sprite(subElement);
                        break;
                    case "statvalue":
                        DebugConsole.ThrowError($"Error in affliction \"{Identifier}\" - stat values should be configured inside the affliction's effects.");
                        break;
                    case "effect":
                    case "periodiceffect":
                        break;
                    case "description":
                        descriptions.Add(new Description(subElement, this));
                        break;
                    default:
                        DebugConsole.AddWarning($"Unrecognized element in affliction \"{Identifier}\" ({subElement.Name})");
                        break;
                }
            }
            Descriptions = descriptions.ToImmutableList();

            constructor = type.GetConstructor(new[] { typeof(AfflictionPrefab), typeof(float) });
        }

        private void RefreshTreatmentSuitabilities()
        {
            var newTreatmentSuitabilities = new Dictionary<Identifier, float>();

            foreach (var itemPrefab in ItemPrefab.Prefabs)
            {
                float suitability = itemPrefab.GetTreatmentSuitability(Identifier) + itemPrefab.GetTreatmentSuitability(AfflictionType);
                if (!MathUtils.NearlyEqual(suitability, 0.0f))
                {
                    newTreatmentSuitabilities.TryAdd(itemPrefab.Identifier, suitability);
                }
            }
            HasTreatments = newTreatmentSuitabilities.Any(kvp => kvp.Value > 0);
            TreatmentSuitabilities = newTreatmentSuitabilities.ToImmutableDictionary();
        }

        public LocalizedString GetDescription(float strength, Description.TargetType targetType)
        {
            foreach (var description in Descriptions)
            {
                if (strength < description.MinStrength || strength > description.MaxStrength) { continue; }
                switch (targetType)
                {
                    case Description.TargetType.Self:
                        if (description.Target == Description.TargetType.OtherCharacter) { continue; }
                        break;
                    case Description.TargetType.OtherCharacter:
                        if (description.Target == Description.TargetType.Self) { continue; }
                        break;
                }
                return description.Text;
            }
            return defaultDescription;
        }

        /// <summary>
        /// Should be called before each round: loads all StatusEffects and refreshes treatment suitabilities.
        /// </summary>
        public static void LoadAllEffectsAndTreatmentSuitabilities()
        {
            foreach (var prefab in Prefabs)
            {
                prefab.RefreshTreatmentSuitabilities();
                prefab.LoadEffects();
            }
        }

        public static void ClearAllEffects()
        {
            Prefabs.ForEach(p => p.ClearEffects());
        }
        
        private void LoadEffects()
        {
            ClearEffects();
            foreach (var subElement in configElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "effect":
                        effects.Add(new Effect(subElement, Name.Value));
                        break;
                    case "periodiceffect":
                        periodicEffects.Add(new PeriodicEffect(subElement, Name.Value));
                        break;
                }
            }
            for (int i = 0; i < effects.Count; i++)
            {
                for (int j = i + 1; j < effects.Count; j++)
                {
                    var a = effects[i];
                    var b = effects[j];
                    if (a.MinStrength < b.MaxStrength && b.MinStrength < a.MaxStrength)
                    {
                        DebugConsole.AddWarning($"Affliction \"{Identifier}\" contains effects with overlapping strength ranges. Only one effect can be active at a time, meaning one of the effects won't work.");
                    }
                }
            }
        }

        private void ClearEffects()
        {
            effects.Clear();
            periodicEffects.Clear();
        }

#if CLIENT
        public void ReloadSoundsIfNeeded()
        {
            foreach (var effect in effects)
            {
                foreach (var statusEffect in effect.StatusEffects)
                {
                    foreach (var sound in statusEffect.Sounds)
                    {
                        if (sound.Sound == null) { RoundSound.Reload(sound); }                       
                    }
                }
            }
            foreach (var periodicEffect in periodicEffects)
            {
                foreach (var statusEffect in periodicEffect.StatusEffects)
                {
                    foreach (var sound in statusEffect.Sounds)
                    {
                        if (sound.Sound == null) { RoundSound.Reload(sound); }
                    }
                }
            }
        }
#endif

        public override string ToString()
        {
            return $"AfflictionPrefab ({Name})";
        }

        public Affliction Instantiate(float strength, Character source = null)
        {
            object instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this, strength });
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
            }
            Affliction affliction = instance as Affliction;
            affliction.Source = source;
            return affliction;
        }

        public Effect GetActiveEffect(float currentStrength)
        {
            foreach (Effect effect in effects)
            {
                if (currentStrength > effect.MinStrength && currentStrength <= effect.MaxStrength)
                {
                    return effect;
                }
            }

            //if above the strength range of all effects, use the highest strength effect
            Effect strongestEffect = null;
            float largestStrength = currentStrength;
            foreach (Effect effect in effects)
            {
                if (currentStrength > effect.MaxStrength && 
                    (strongestEffect == null || effect.MaxStrength > largestStrength))
                {
                    strongestEffect = effect;
                    largestStrength = effect.MaxStrength;
                }
            }
            return strongestEffect;
        }

        public float GetTreatmentSuitability(Item item)
        {
            if (item == null)
            {
                return 0.0f;
            }
            return Math.Max(item.Prefab.GetTreatmentSuitability(Identifier), item.Prefab.GetTreatmentSuitability(AfflictionType));
        }
    }
}
