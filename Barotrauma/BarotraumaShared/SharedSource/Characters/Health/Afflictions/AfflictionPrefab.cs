using Barotrauma.Abilities;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Extensions;
using System.Collections.Immutable;

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

    class AfflictionPrefabHusk : AfflictionPrefab
    {
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
                AttachLimbType = Enum.TryParse(attachElement.GetAttributeString("type", "none"), true, out LimbType limbType) ? limbType : LimbType.None;
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
            TransformThresholdOnDeath = element.GetAttributeFloat("transformthresholdondeath", ActiveThreshold);
        }

        // Use any of these to define which limb the appendage is attached to.
        // If multiple are defined, the order of preference is: id, name, type.
        public readonly int AttachLimbId;
        public readonly string AttachLimbName;
        public readonly LimbType AttachLimbType;

        public float ActiveThreshold, DormantThreshold, TransitionThreshold;
        public float TransformThresholdOnDeath;

        public readonly Identifier HuskedSpeciesName;

        public readonly bool TransferBuffs;
        public readonly bool SendMessages;
        public readonly bool CauseSpeechImpediment;
        public readonly bool NeedsAir;
        public readonly bool ControlHusk;
    }

    class AfflictionPrefab : PrefabWithUintIdentifier
    {
        public class Effect
        {
            //this effect is applied when the strength is within this range
            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinVitalityDecrease { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxVitalityDecrease { get; private set; }

            //how much the strength of the affliction changes per second
            [Serialize(0.0f, IsPropertySaveable.No)]
            public float StrengthChange { get; private set; }

            [Serialize(false, IsPropertySaveable.No)]
            public bool MultiplyByMaxVitality { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinScreenBlur { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxScreenBlur { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinScreenDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxScreenDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinRadialDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxRadialDistort { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinChromaticAberration { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxChromaticAberration { get; private set; }

            [Serialize("255,255,255,255", IsPropertySaveable.No)]
            public Color GrainColor { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinGrainStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxGrainStrength { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float ScreenEffectFluctuationFrequency { get; private set; }
            
            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MinAfflictionOverlayAlphaMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MaxAfflictionOverlayAlphaMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MinBuffMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MaxBuffMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MinSpeedMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MaxSpeedMultiplier { get; private set; }
            
            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MinSkillMultiplier { get; private set; }

            [Serialize(1.0f, IsPropertySaveable.No)]
            public float MaxSkillMultiplier { get; private set; }
            
            private readonly Identifier[] resistanceFor;
            public IReadOnlyList<Identifier> ResistanceFor => resistanceFor;

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MinResistance { get; private set; }

            [Serialize(0.0f, IsPropertySaveable.No)]
            public float MaxResistance { get; private set; }

            [Serialize("", IsPropertySaveable.No)]
            public Identifier DialogFlag { get; private set; }

            [Serialize("", IsPropertySaveable.No)]
            public Identifier Tag { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No)]
            public Color MinFaceTint { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No)]
            public Color MaxFaceTint { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No)]
            public Color MinBodyTint { get; private set; }

            [Serialize("0,0,0,0", IsPropertySaveable.No)]
            public Color MaxBodyTint { get; private set; }

            /// <summary>
            /// Prevents AfflictionHusks with the specified identifier(s) from transforming the character into an AI-controlled character
            /// </summary>
            public Identifier[] BlockTransformation { get; private set; }

            public readonly Dictionary<StatTypes, (float minValue, float maxValue)> AfflictionStatValues = new Dictionary<StatTypes, (float minValue, float maxValue)>();
            public AbilityFlags AfflictionAbilityFlags;

            //statuseffects applied on the character when the affliction is active
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

            public Effect(ContentXElement element, string parentDebugName)
            {
                SerializableProperty.DeserializeProperties(this, element);

                resistanceFor = element.GetAttributeIdentifierArray("resistancefor", Array.Empty<Identifier>());
                BlockTransformation = element.GetAttributeIdentifierArray("blocktransformation", Array.Empty<Identifier>());

                foreach (var subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "statuseffect":
                            StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
                            break;
                        case "statvalue":
                            var statType = CharacterAbilityGroup.ParseStatType(subElement.GetAttributeString("stattype", ""), parentDebugName);

                            float defaultValue = subElement.GetAttributeFloat("value", 0f);
                            float minValue = subElement.GetAttributeFloat("minvalue", defaultValue);
                            float maxValue = subElement.GetAttributeFloat("maxvalue", defaultValue);

                            AfflictionStatValues.TryAdd(statType, (minValue, maxValue));
                            break;
                        case "abilityflag":
                            var flagType = CharacterAbilityGroup.ParseFlagType(subElement.GetAttributeString("flagtype", ""), parentDebugName);
                            AfflictionAbilityFlags |= flagType;
                            break;
                        case "affliction":
                            DebugConsole.AddWarning($"Error in affliction \"{parentDebugName}\" - additional afflictions caused by the affliction should be configured inside status effects.");
                            break;
                    }
                }
            }
        }

        public class Description
        {
            public enum TargetType
            {
                Any,
                Self,
                OtherCharacter
            }

            public readonly LocalizedString Text;
            public readonly Identifier TextTag;
            public readonly float MinStrength, MaxStrength;
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

        public class PeriodicEffect
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

        public override void Dispose() { }

        public static IEnumerable<AfflictionPrefab> List => Prefabs;

        // Arbitrary string that is used to identify the type of the affliction.
        public readonly Identifier AfflictionType;

        private readonly ContentXElement configElement;
        
        //Does the affliction affect a specific limb or the whole character
        public readonly bool LimbSpecific;

        //If not a limb-specific affliction, which limb is the indicator shown on in the health menu
        //(e.g. mental health problems on head, lack of oxygen on torso...)
        public readonly LimbType IndicatorLimb;

        public readonly LocalizedString Name;
        public readonly Identifier TranslationIdentifier;
        public readonly bool IsBuff;
        public readonly bool AffectMachines;
        public readonly bool HealableInMedicalClinic;
        public readonly float HealCostMultiplier;
        public readonly int BaseHealCost;
        public readonly bool ShowBarInHealthMenu;

        public readonly LocalizedString CauseOfDeathDescription, SelfCauseOfDeathDescription;

        private readonly LocalizedString defaultDescription;
        public readonly ImmutableList<Description> Descriptions;

        public readonly bool HideIconAfterDelay;

        //how high the strength has to be for the affliction to take affect
        public readonly float ActivationThreshold = 0.0f;
        //how high the strength has to be for the affliction icon to be shown in the UI
        public readonly float ShowIconThreshold = 0.05f;
        //how high the strength has to be for the affliction icon to be shown to others with a health scanner or via the health interface
        public readonly float ShowIconToOthersThreshold = 0.05f;
        public readonly float MaxStrength = 100.0f;

        public readonly float GrainBurst;

        //how high the strength has to be for the affliction icon to be shown with a health scanner
        public readonly float ShowInHealthScannerThreshold = 0.05f;

        //how strong the affliction needs to be before bots attempt to treat it
        public readonly float TreatmentThreshold = 5.0f;

        /// <summary>
        /// Bots will not try to treat the affliction if the character has any of these afflictions
        /// </summary>
        public ImmutableHashSet<Identifier> IgnoreTreatmentIfAfflictedBy;

        /// <summary>
        /// The affliction is automatically removed after this time. 0 = unlimited
        /// </summary>
        public readonly float Duration;

        //how much karma changes when a player applies this affliction to someone (per strength of the affliction)
        public float KarmaChangeOnApplied;

        public readonly float BurnOverlayAlpha;
        public readonly float DamageOverlayAlpha;

        //steam achievement given when the controlled character receives the affliction
        public readonly Identifier AchievementOnReceived;

        //steam achievement given when the affliction is removed from the controlled character
        public readonly Identifier AchievementOnRemoved;

        public readonly Sprite Icon;
        public readonly Color[] IconColors;

        public readonly Sprite AfflictionOverlay;
        public readonly bool AfflictionOverlayAlphaIsLinear;

        public readonly bool DamageParticles;

        /// <summary>
        /// An arbitrary modifier that affects how much medical skill is increased when you apply the affliction on a target. 
        /// If the affliction causes damage or is of type poison or paralysis, the skill is increased only when the target is hostile. 
        /// If the affliction is of type buff, the skill is increased only when the target is friendly.
        /// </summary>
        public readonly float MedicalSkillGain;
        /// <summary>
        /// An arbitrary modifier that affects how much weapons skill is increased when you apply the affliction on a target. 
        /// The skill is increased only when the target is hostile. 
        /// </summary>
        public readonly float WeaponsSkillGain;

        private readonly List<Effect> effects = new List<Effect>();
        private readonly List<PeriodicEffect> periodicEffects = new List<PeriodicEffect>();

        public IEnumerable<Effect> Effects => effects;

        public IList<PeriodicEffect> PeriodicEffects => periodicEffects;

        private readonly ConstructorInfo constructor;

        public Identifier[] TargetSpecies { get; protected set; }

        public readonly bool ResetBetweenRounds;

        public IEnumerable<KeyValuePair<Identifier, float>> TreatmentSuitability
        {
            get
            {
                foreach (var itemPrefab in ItemPrefab.Prefabs)
                {
                    float suitability = Math.Max(itemPrefab.GetTreatmentSuitability(Identifier), itemPrefab.GetTreatmentSuitability(AfflictionType));
                    if (!MathUtils.NearlyEqual(suitability, 0.0f))
                    {
                        yield return new KeyValuePair<Identifier, float>(itemPrefab.Identifier, suitability);
                    }
                }
            }
        }

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
            TreatmentThreshold = element.GetAttributeFloat(nameof(TreatmentThreshold), Math.Max(ActivationThreshold, 5.0f));

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

        public static void LoadAllEffects()
        {
            Prefabs.ForEach(p => p.LoadEffects());
        }

        public static void ClearAllEffects()
        {
            Prefabs.ForEach(p => p.ClearEffects());
        }
        
        public void LoadEffects()
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

        public void ClearEffects()
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
