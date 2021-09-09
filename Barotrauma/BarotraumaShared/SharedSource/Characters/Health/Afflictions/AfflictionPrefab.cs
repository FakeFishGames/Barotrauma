using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using System.Linq;
using System.Security.Cryptography;
using Barotrauma.Abilities;

namespace Barotrauma
{
    static class CPRSettings
    {
        public static string FilePath { get; private set; }
        public static bool IsLoaded { get; private set; }
        public static float ReviveChancePerSkill { get; private set; }
        public static float ReviveChanceExponent { get; private set; }
        public static float ReviveChanceMin { get; private set; }
        public static float ReviveChanceMax { get; private set; }
        public static float StabilizationPerSkill { get; private set; }
        public static float StabilizationMin { get; private set; }
        public static float StabilizationMax { get; private set; }
        public static float DamageSkillThreshold { get; private set; }
        public static float DamageSkillMultiplier { get; private set; }

        private static string insufficientSkillAfflictionIdentifier { get; set; }
        public static AfflictionPrefab InsufficientSkillAffliction
        {
            get
            {
                return
                    AfflictionPrefab.Prefabs.ContainsKey(insufficientSkillAfflictionIdentifier) ?
                    AfflictionPrefab.Prefabs[insufficientSkillAfflictionIdentifier] :
                    AfflictionPrefab.InternalDamage;
            }
        }

        public static void Load(XElement element, string filePath)
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

            IsLoaded = true;
            FilePath = filePath;
        }

        public static void Unload()
        {
            IsLoaded = false;
            FilePath = null;
        }
    }

    class AfflictionPrefabHusk : AfflictionPrefab
    {
        public AfflictionPrefabHusk(XElement element, string filePath, Type type = null) : base(element, filePath, type)
        {
            HuskedSpeciesName = element.GetAttributeString("huskedspeciesname", null).ToLowerInvariant();
            if (HuskedSpeciesName == null)
            {
                DebugConsole.NewMessage($"No 'huskedspeciesname' defined for the husk affliction ({Identifier}) in {element}", Color.Orange);
                HuskedSpeciesName = "[speciesname]husk";
            }
            TargetSpecies = element.GetAttributeStringArray("targets", new string[0] { }, trim: true, convertToLowerInvariant: true);
            if (TargetSpecies.Length == 0)
            {
                DebugConsole.NewMessage($"No 'targets' defined for the husk affliction ({Identifier}) in {element}", Color.Orange);
                TargetSpecies = new string[] { "human" };
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

        public readonly string HuskedSpeciesName;
        public readonly string[] TargetSpecies;
        public const string Tag = "[speciesname]";

        public readonly bool TransferBuffs;
        public readonly bool SendMessages;
        public readonly bool CauseSpeechImpediment;
        public readonly bool NeedsAir;
        public readonly bool ControlHusk;
    }

    class AfflictionPrefab : IPrefab, IDisposable, IHasUintIdentifier
    {
        public class Effect
        {
            //this effect is applied when the strength is within this range
            [Serialize(0.0f, false)]
            public float MinStrength { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxStrength { get; private set; }

            [Serialize(0.0f, false)]
            public float MinVitalityDecrease { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxVitalityDecrease { get; private set; }

            //how much the strength of the affliction changes per second
            [Serialize(0.0f, false)]
            public float StrengthChange { get; private set; }

            [Serialize(false, false)]
            public bool MultiplyByMaxVitality { get; private set; }

            [Serialize(0.0f, false)]
            public float MinScreenBlur { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxScreenBlur { get; private set; }

            [Serialize(0.0f, false)]
            public float MinScreenDistort { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxScreenDistort { get; private set; }

            [Serialize(0.0f, false)]
            public float MinRadialDistort { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxRadialDistort { get; private set; }

            [Serialize(0.0f, false)]
            public float MinChromaticAberration { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxChromaticAberration { get; private set; }

            [Serialize("255,255,255,255", false)]
            public Color GrainColor { get; private set; }

            [Serialize(0.0f, false)]
            public float MinGrainStrength { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxGrainStrength { get; private set; }

            [Serialize(0.0f, false)]
            public float ScreenEffectFluctuationFrequency { get; private set; }

            [Serialize(1.0f, false)]
            public float MinAfflictionOverlayAlphaMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MaxAfflictionOverlayAlphaMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MinBuffMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MaxBuffMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MinSpeedMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MaxSpeedMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MinSkillMultiplier { get; private set; }

            [Serialize(1.0f, false)]
            public float MaxSkillMultiplier { get; private set; }

            private readonly string[] resistanceFor;
            public IEnumerable<string> ResistanceFor 
            {
                get { return resistanceFor; }
            }

            [Serialize(0.0f, false)]
            public float MinResistance { get; private set; }

            [Serialize(0.0f, false)]
            public float MaxResistance { get; private set; }

            [Serialize("", false)]
            public string DialogFlag { get; private set; }

            public readonly Dictionary<StatTypes, (float minValue, float maxValue)> AfflictionStatValues = new Dictionary<StatTypes, (float minValue, float maxValue)>();

            //statuseffects applied on the character when the affliction is active
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

            public Effect(XElement element, string parentDebugName)
            {
                SerializableProperty.DeserializeProperties(this, element);

                resistanceFor = element.GetAttributeStringArray("resistancefor", new string[0], convertToLowerInvariant: true);

                foreach (XElement subElement in element.Elements())
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
                    }
                }
            }
        }

        public class PeriodicEffect
        {
            public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();
            public readonly float MinInterval, MaxInterval;

            public PeriodicEffect(XElement element, string parentDebugName)
            {
                foreach (XElement subElement in element.Elements())
                {
                    StatusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
                }

                if (element.Attribute("interval") != null)
                {
                    MinInterval = MaxInterval = Math.Max(element.GetAttributeFloat("interval", 1.0f), 1.0f);
                }
                else
                {
                    MinInterval = Math.Max(element.GetAttributeFloat("mininterval", 1.0f), 1.0f);
                    MaxInterval = Math.Max(element.GetAttributeFloat("maxinterval", 1.0f), MinInterval);
                }
            }
        }

        public static AfflictionPrefab InternalDamage;
        public static AfflictionPrefab ImpactDamage;
        public static AfflictionPrefab Bleeding;
        public static AfflictionPrefab Burn;
        public static AfflictionPrefab OxygenLow;
        public static AfflictionPrefab Bloodloss;
        public static AfflictionPrefab Pressure;
        public static AfflictionPrefab Stun;
        public static AfflictionPrefab RadiationSickness;

        public static readonly PrefabCollection<AfflictionPrefab> Prefabs = new PrefabCollection<AfflictionPrefab>();

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        public static IEnumerable<AfflictionPrefab> List
        {
            get
            {
                foreach (var prefab in Prefabs)
                {
                    yield return prefab;
                }
            }
        }

        public string FilePath { get; private set; }

        /// <summary>
        /// Unique identifier that's generated by hashing the prefab's string identifier. 
        /// Used to reduce the amount of bytes needed to write affliction data into network messages in multiplayer.
        /// </summary>
        public uint UIntIdentifier { get; set; }

        // Arbitrary string that is used to identify the type of the affliction.
        public readonly string AfflictionType;

        //Does the affliction affect a specific limb or the whole character
        public readonly bool LimbSpecific;

        //If not a limb-specific affliction, which limb is the indicator shown on in the health menu
        //(e.g. mental health problems on head, lack of oxygen on torso...)
        public readonly LimbType IndicatorLimb;

        public string Identifier { get; private set; }
        public string OriginalName { get { return Identifier; } }
        public ContentPackage ContentPackage { get; private set; }

        public readonly string Name, Description;
        public readonly string TranslationOverride;
        public readonly bool IsBuff;

        public readonly string CauseOfDeathDescription, SelfCauseOfDeathDescription;

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

        //how much karma changes when a player applies this affliction to someone (per strength of the affliction)
        public float KarmaChangeOnApplied;

        public float BurnOverlayAlpha;
        public float DamageOverlayAlpha;

        //steam achievement given when the affliction is removed from the controlled character
        public readonly string AchievementOnRemoved;

        public readonly Sprite Icon;
        public readonly Color[] IconColors;

        public readonly Sprite AfflictionOverlay;
        public readonly bool AfflictionOverlayAlphaIsLinear;

        private readonly List<Effect> effects = new List<Effect>();
        private readonly List<PeriodicEffect> periodicEffects = new List<PeriodicEffect>();

        public IEnumerable<Effect> Effects => effects;

        public IList<PeriodicEffect> PeriodicEffects => periodicEffects;

        private readonly string typeName;

        private readonly ConstructorInfo constructor;

        public IEnumerable<KeyValuePair<string, float>> TreatmentSuitability
        {
            get
            {
                foreach (var itemPrefab in ItemPrefab.Prefabs)
                {
                    float suitability = Math.Max(itemPrefab.GetTreatmentSuitability(Identifier), itemPrefab.GetTreatmentSuitability(AfflictionType));
                    if (suitability > 0.0f)
                    {
                        yield return new KeyValuePair<string, float>(itemPrefab.Identifier, suitability);
                    }
                }
            }
        }

        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            CPRSettings.Unload();
            InternalDamage = null;
            ImpactDamage = null;
            Bleeding = null;
            Burn = null;
            OxygenLow = null;
            Bloodloss = null;
            Pressure = null;
            Stun = null;
            RadiationSickness = null;
#if CLIENT
            CharacterHealth.DamageOverlay?.Remove();
            CharacterHealth.DamageOverlay = null;
            CharacterHealth.DamageOverlayFile = string.Empty;
#endif
            var prevPrefabs = Prefabs.AllPrefabs.SelectMany(kvp => kvp.Value).ToList();
            foreach (var prefab in prevPrefabs)
            {
                prefab?.Dispose();
            }
            System.Diagnostics.Debug.Assert(Prefabs.Count() == 0, "All previous AfflictionPrefabs were not removed in AfflictionPrefab.LoadAll");

            foreach (ContentFile file in files)
            {
                LoadFromFile(file);
            }

            if (InternalDamage == null) { DebugConsole.ThrowError("Affliction \"Internal Damage\" not defined in the affliction prefabs."); }
            if (Bleeding == null) { DebugConsole.ThrowError("Affliction \"Bleeding\" not defined in the affliction prefabs."); }
            if (Burn == null) { DebugConsole.ThrowError("Affliction \"Burn\" not defined in the affliction prefabs."); }
            if (OxygenLow == null) { DebugConsole.ThrowError("Affliction \"OxygenLow\" not defined in the affliction prefabs."); }
            if (Bloodloss == null) { DebugConsole.ThrowError("Affliction \"Bloodloss\" not defined in the affliction prefabs."); }
            if (Pressure == null) { DebugConsole.ThrowError("Affliction \"Pressure\" not defined in the affliction prefabs."); }
            if (Stun == null) { DebugConsole.ThrowError("Affliction \"Stun\" not defined in the affliction prefabs."); }
            if (RadiationSickness == null) { DebugConsole.ThrowError("Affliction \"RadiationSickness\" not defined in the affliction prefabs."); }
        }

        public static void LoadFromFile(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
            if (doc == null) { return; }
            var mainElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
            if (doc.Root.IsOverride())
            {
                DebugConsole.ThrowError("Cannot override all afflictions, because many of them are required by the main game! Please try overriding them one by one.");
            }

            List<(AfflictionPrefab prefab, XElement element)> loadedAfflictions = new List<(AfflictionPrefab prefab, XElement element)>();

            foreach (XElement element in mainElement.Elements())
            {
                bool isOverride = element.IsOverride();
                XElement sourceElement = isOverride ? element.FirstElement() : element;
                string elementName = sourceElement.Name.ToString().ToLowerInvariant();
                string identifier = sourceElement.GetAttributeString("identifier", null);
                if (!elementName.Equals("cprsettings", StringComparison.OrdinalIgnoreCase) &&
                    !elementName.Equals("damageoverlay", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        DebugConsole.ThrowError($"No identifier defined for the affliction '{elementName}' in file '{file.Path}'");
                        continue;
                    }
                    if (Prefabs.ContainsKey(identifier))
                    {
                        if (isOverride)
                        {
                            DebugConsole.NewMessage($"Overriding an affliction or a buff with the identifier '{identifier}' using the file '{file.Path}'", Color.Yellow);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Duplicate affliction: '{identifier}' defined in {elementName} of '{file.Path}'");
                            continue;
                        }
                    }
                }
                string type = sourceElement.GetAttributeString("type", "");
                switch (sourceElement.Name.ToString().ToLowerInvariant())
                {
                    case "cprsettings":
                        type = "cprsettings";
                        break;
                    case "damageoverlay":
                        type = "damageoverlay";
                        break;
                }

                AfflictionPrefab prefab = null;
                switch (type)
                {
                    case "damageoverlay":
#if CLIENT
                        if (CharacterHealth.DamageOverlay != null)
                        {
                            if (isOverride)
                            {
                                DebugConsole.NewMessage($"Overriding damage overlay with '{file.Path}'", Color.Yellow);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Error in '{file.Path}': damage overlay already loaded. Add <override></override> tags as the parent of the custom damage overlay sprite to allow overriding the vanilla one.");
                                break;
                            }
                        }
                        CharacterHealth.DamageOverlay?.Remove();
                        CharacterHealth.DamageOverlay = new Sprite(element);
                        CharacterHealth.DamageOverlayFile = file.Path;
#endif
                        break;
                    case "bleeding":
                        prefab = new AfflictionPrefab(sourceElement, file.Path, typeof(AfflictionBleeding));
                        break;
                    case "huskinfection":
                    case "alieninfection":
                        prefab = new AfflictionPrefabHusk(sourceElement, file.Path, typeof(AfflictionHusk));
                        break;
                    case "cprsettings":
                        if (CPRSettings.IsLoaded)
                        {
                            if (isOverride)
                            {
                                DebugConsole.NewMessage($"Overriding the CPR settings with '{file.Path}'", Color.Yellow);
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Error in '{file.Path}': CPR settings already loaded. Add <override></override> tags as the parent of the custom CPRSettings to allow overriding the vanilla values.");
                                break;
                            }
                        }
                        CPRSettings.Load(sourceElement, file.Path);
                        break;
                    case "damage":
                    case "burn":
                    case "oxygenlow":
                    case "bloodloss":
                    case "stun":
                    case "pressure":
                    case "internaldamage":
                        prefab = new AfflictionPrefab(sourceElement, file.Path, typeof(Affliction))
                        {
                            ContentPackage = file.ContentPackage
                        };
                        break;
                    default:
                        prefab = new AfflictionPrefab(sourceElement, file.Path)
                        {
                            ContentPackage = file.ContentPackage
                        };
                        break;
                }
                switch (identifier)
                {
                    case "internaldamage":
                        InternalDamage = prefab;
                        break;
                    case "blunttrauma":
                        ImpactDamage = prefab;
                        break;
                    case "bleeding":
                        Bleeding = prefab;
                        break;
                    case "burn":
                        Burn = prefab;
                        break;
                    case "oxygenlow":
                        OxygenLow = prefab;
                        break;
                    case "bloodloss":
                        Bloodloss = prefab;
                        break;
                    case "pressure":
                        Pressure = prefab;
                        break;
                    case "stun":
                        Stun = prefab;
                        break;
                    case "radiationsickness":
                        RadiationSickness = prefab;
                        break;
                }
                if (ImpactDamage == null) { ImpactDamage = InternalDamage; }

                if (prefab != null)
                {
                    loadedAfflictions.Add((prefab, sourceElement));
                    Prefabs.Add(prefab, isOverride);
                    prefab.CalculatePrefabUIntIdentifier(Prefabs);
                }
            }

            //load the effects after all the afflictions in the file have been instantiated
            //otherwise afflictions can't inflict other afflictions that are defined at a later point in the file
            foreach ((AfflictionPrefab prefab, XElement element) in loadedAfflictions)
            {
                prefab.LoadEffects(element);
            }
        }

        public static void RemoveByFile(string filePath)
        {
            if (CPRSettings.FilePath == filePath) { CPRSettings.Unload(); }
#if CLIENT
            if (CharacterHealth.DamageOverlayFile == filePath)
            {
                CharacterHealth.DamageOverlay?.Remove();
                CharacterHealth.DamageOverlay = null;
            }
#endif

            Prefabs.RemoveByFile(filePath);
        }

        public AfflictionPrefab(XElement element, string filePath, Type type = null)
        {
            FilePath = filePath;

            typeName = type == null ? element.Name.ToString() : type.Name;
            if (typeName == "InternalDamage" && type == null)
            {
                type = typeof(Affliction);
            }

            Identifier = element.GetAttributeString("identifier", "");

            AfflictionType = element.GetAttributeString("type", "");
            TranslationOverride = element.GetAttributeString("translationoverride", null);
            string translationId = TranslationOverride ?? Identifier;
            Name = TextManager.Get("AfflictionName." + translationId, true) ?? element.GetAttributeString("name", "");
            Description = TextManager.Get("AfflictionDescription." + translationId, true) ?? element.GetAttributeString("description", "");
            IsBuff = element.GetAttributeBool("isbuff", false);

            LimbSpecific = element.GetAttributeBool("limbspecific", false);
            if (!LimbSpecific)
            {
                string indicatorLimbName = element.GetAttributeString("indicatorlimb", "Torso");
                if (!Enum.TryParse(indicatorLimbName, out IndicatorLimb))
                {
                    DebugConsole.ThrowError("Error in affliction prefab " + Name + " - limb type \"" + indicatorLimbName + "\" not found.");
                }
            }

            ActivationThreshold = element.GetAttributeFloat("activationthreshold", 0.0f);
            ShowIconThreshold   = element.GetAttributeFloat("showiconthreshold", Math.Max(ActivationThreshold, 0.05f));
            ShowIconToOthersThreshold   = element.GetAttributeFloat("showicontoothersthreshold", ShowIconThreshold);
            MaxStrength         = element.GetAttributeFloat("maxstrength", 100.0f);
            GrainBurst          = element.GetAttributeFloat(nameof(GrainBurst).ToLowerInvariant(), 0.0f);

            ShowInHealthScannerThreshold = element.GetAttributeFloat("showinhealthscannerthreshold", Math.Max(ActivationThreshold, 0.05f));
            TreatmentThreshold = element.GetAttributeFloat("treatmentthreshold", Math.Max(ActivationThreshold, 5.0f));

            DamageOverlayAlpha  = element.GetAttributeFloat("damageoverlayalpha", 0.0f);
            BurnOverlayAlpha    = element.GetAttributeFloat("burnoverlayalpha", 0.0f);

            KarmaChangeOnApplied = element.GetAttributeFloat("karmachangeonapplied", 0.0f);

            CauseOfDeathDescription     = TextManager.Get("AfflictionCauseOfDeath." + translationId, true) ?? element.GetAttributeString("causeofdeathdescription", "");
            SelfCauseOfDeathDescription = TextManager.Get("AfflictionCauseOfDeathSelf." + translationId, true) ?? element.GetAttributeString("selfcauseofdeathdescription", "");

            IconColors = element.GetAttributeColorArray("iconcolors", null);
            AfflictionOverlayAlphaIsLinear = element.GetAttributeBool("afflictionoverlayalphaislinear", false);
            AchievementOnRemoved = element.GetAttributeString("achievementonremoved", "");

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        break;
                    case "afflictionoverlay":
                        AfflictionOverlay = new Sprite(subElement);
                        break;
                }
            }

            try
            {
                if (type == null)
                {
                    type = Type.GetType("Barotrauma." + typeName, true, true);
                    if (type == null)
                    {
                        DebugConsole.ThrowError("Could not find an affliction class of the type \"" + typeName + "\".");
                        return;
                    }
                }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an affliction class of the type \"" + typeName + "\".");
                type = typeof(Affliction);
            }

            constructor = type.GetConstructor(new[] { typeof(AfflictionPrefab), typeof(float) });
        }

        private void LoadEffects(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "effect":
                        effects.Add(new Effect(subElement, Name));
                        break;
                    case "periodiceffect":
                        periodicEffects.Add(new PeriodicEffect(subElement, Name));
                        break;
                }
            }
        }

        public override string ToString()
        {
            return "AfflictionPrefab (" + Name + ")";
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
