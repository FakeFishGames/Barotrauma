#nullable enable

using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    internal class CampaignSettings : INetSerializableStruct, ISerializableEntity
    {
        public static CampaignSettings Empty => new CampaignSettings(element: null);

#if CLIENT
        public static CampaignSettings CurrentSettings = new CampaignSettings(GameSettings.CurrentConfig.SavedCampaignSettings);
#endif
        public string Name => "CampaignSettings";

        public const string LowerCaseSaveElementName = "campaignsettings";

        [Serialize("Normal", IsPropertySaveable.Yes)]
        public string PresetName { get; set; } = string.Empty;

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool TutorialEnabled { get; set; }

        [Serialize(false, IsPropertySaveable.Yes), NetworkSerialize]
        public bool RadiationEnabled { get; set; }

        public const int DefaultMaxMissionCount = 2;
        public const int MaxMissionCountLimit = 10;
        public const int MinMissionCountLimit = 1;

        private int maxMissionCount;

        [Serialize(DefaultMaxMissionCount, IsPropertySaveable.Yes), NetworkSerialize(MinValueInt = MinMissionCountLimit, MaxValueInt = MaxMissionCountLimit)]
        public int MaxMissionCount
        {
            get => maxMissionCount;
            set => maxMissionCount = MathHelper.Clamp(value, MinMissionCountLimit, MaxMissionCountLimit);
        }

        public int TotalMaxMissionCount => MaxMissionCount + GetAddedMissionCount();

        [Serialize(WorldHostilityOption.Medium, IsPropertySaveable.Yes), NetworkSerialize]
        public WorldHostilityOption WorldHostility { get; set; }

        [Serialize("normal", IsPropertySaveable.Yes), NetworkSerialize]
        public Identifier StartItemSet { get; set; }

        [Serialize(StartingBalanceAmountOption.Medium, IsPropertySaveable.Yes), NetworkSerialize]
        public StartingBalanceAmountOption StartingBalanceAmount { get; set; }

        private int? _initialMoney;
        public const int DefaultInitialMoney = 8000;

        public int InitialMoney
        {
            get
            {
                if (_initialMoney is int alreadyCachedValue)
                {
                    return alreadyCachedValue;
                }
                else
                {
                    _initialMoney = DefaultInitialMoney;
                    Identifier settingDefinitionIdentifier = nameof(StartingBalanceAmount).ToIdentifier();
                    Identifier attributeIdentifier = StartingBalanceAmount.ToIdentifier();
                    if (CampaignModePresets.TryGetAttribute(settingDefinitionIdentifier, attributeIdentifier, out XAttribute? attribute))
                    {
                        _initialMoney = attribute.GetAttributeInt(DefaultInitialMoney);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"CampaignSettings: Can't find value for {attributeIdentifier} in {settingDefinitionIdentifier}");
                    }
                    return _initialMoney ?? DefaultInitialMoney;
                }
            }
        }

        private float? _extraEventManagerDifficulty;
        private const float defaultExtraEventManagerDifficulty = 0;

        public float ExtraEventManagerDifficulty
        {
            get
            {
                if (_extraEventManagerDifficulty is float alreadyCachedValue)
                {
                    return alreadyCachedValue;
                }
                else
                {
                    _extraEventManagerDifficulty = defaultExtraEventManagerDifficulty;
                    Identifier settingDefinitionIdentifier = nameof(ExtraEventManagerDifficulty).ToIdentifier();
                    Identifier attributeIdentifier = WorldHostility.ToIdentifier();
                    if (CampaignModePresets.TryGetAttribute(settingDefinitionIdentifier, attributeIdentifier, out XAttribute? attribute))
                    {
                        _extraEventManagerDifficulty = attribute.GetAttributeFloat(defaultExtraEventManagerDifficulty);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"CampaignSettings: Can't find value for {attributeIdentifier} in {settingDefinitionIdentifier}");
                    }
                    return _extraEventManagerDifficulty ?? defaultExtraEventManagerDifficulty;
                }
            }
        }

        private float? _levelDifficultyMultiplier;
        private const float defaultLevelDifficultyMultiplier = 1.0f;

        public float LevelDifficultyMultiplier
        {
            get
            {
                if (_levelDifficultyMultiplier is float alreadyCachedValue)
                {
                    return alreadyCachedValue;
                }
                else
                {
                    _levelDifficultyMultiplier = defaultLevelDifficultyMultiplier;
                    Identifier settingDefinitionIdentifier = nameof(LevelDifficultyMultiplier).ToIdentifier();
                    Identifier attributeIdentifier = WorldHostility.ToIdentifier();
                    if (CampaignModePresets.TryGetAttribute(settingDefinitionIdentifier, attributeIdentifier, out XAttribute? attribute))
                    {
                        _levelDifficultyMultiplier = attribute.GetAttributeFloat(defaultLevelDifficultyMultiplier);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"CampaignSettings: Can't find value for {attributeIdentifier} in {settingDefinitionIdentifier}");
                    }
                    return _levelDifficultyMultiplier ?? defaultLevelDifficultyMultiplier;
                }
            }
        }

        private static readonly Dictionary<string, MultiplierSettings> _multiplierSettings = new Dictionary<string, MultiplierSettings>
        {
            { "default",                        new MultiplierSettings { Min = 0.2f, Max = 2.0f, Step = 0.1f } },
            { nameof(CrewVitalityMultiplier),   new MultiplierSettings { Min = 0.5f, Max = 2.0f, Step = 0.1f } },
            { nameof(NonCrewVitalityMultiplier),new MultiplierSettings { Min = 0.5f, Max = 3.0f, Step = 0.1f } },
            { nameof(MissionRewardMultiplier),  new MultiplierSettings { Min = 0.5f, Max = 2.0f, Step = 0.1f } },
            { nameof(RepairFailMultiplier),     new MultiplierSettings { Min = 0.5f, Max = 5.0f, Step = 0.5f } },
            { nameof(ShopPriceMultiplier),      new MultiplierSettings { Min = 0.1f, Max = 3.0f, Step = 0.1f } },
            { nameof(ShipyardPriceMultiplier),  new MultiplierSettings { Min = 0.1f, Max = 3.0f, Step = 0.1f } }
            // Add overrides for default values here
        };

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float CrewVitalityMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float NonCrewVitalityMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float OxygenMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float FuelMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float MissionRewardMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float ShopPriceMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float ShipyardPriceMultiplier { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes), NetworkSerialize]
        public float RepairFailMultiplier { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), NetworkSerialize]
        public bool ShowHuskWarning { get; set; }

        [Serialize(PatdownProbabilityOption.Medium, IsPropertySaveable.Yes), NetworkSerialize]
        public PatdownProbabilityOption PatdownProbability { get; set; }

        private float? _minPatdownProbability;
        private float? _maxPatdownProbability;
        public const float DefaultMinPatdownProbability = 0.2f;
        public const float DefaultMaxPatdownProbability = 0.9f;

        public float PatdownProbabilityMin
        {
            get
            {
                if (_minPatdownProbability is float alreadyCachedValue)
                {
                    return alreadyCachedValue;
                }
                else
                {
                    _minPatdownProbability = DefaultMinPatdownProbability;
                    Identifier settingDefinitionIdentifier = nameof(PatdownProbabilityMin).ToIdentifier();
                    Identifier attributeIdentifier = PatdownProbability.ToIdentifier();
                    if (CampaignModePresets.TryGetAttribute(settingDefinitionIdentifier, attributeIdentifier, out XAttribute? attribute))
                    {
                        _minPatdownProbability = attribute.GetAttributeFloat(DefaultMinPatdownProbability);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"CampaignSettings: Can't find value for {attributeIdentifier} in {settingDefinitionIdentifier}");
                    }
                    return _minPatdownProbability ?? DefaultMinPatdownProbability;
                }
            }
        }

        public float PatdownProbabilityMax
        {
            get
            {
                if (_maxPatdownProbability is float alreadyCachedValue)
                {
                    return alreadyCachedValue;
                }
                else
                {
                    _maxPatdownProbability = DefaultMaxPatdownProbability;
                    Identifier settingDefinitionIdentifier = nameof(PatdownProbabilityMax).ToIdentifier();
                    Identifier attributeIdentifier = PatdownProbability.ToIdentifier();
                    if (CampaignModePresets.TryGetAttribute(settingDefinitionIdentifier, attributeIdentifier, out XAttribute? attribute))
                    {
                        _maxPatdownProbability = attribute.GetAttributeFloat(DefaultMaxPatdownProbability);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"CampaignSettings: Can't find value for {attributeIdentifier} in {settingDefinitionIdentifier}");
                    }
                    return _maxPatdownProbability ?? DefaultMaxPatdownProbability;
                }
            }
        }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set;  }

        // required for INetSerializableStruct
        public CampaignSettings()
        {
            SerializableProperties = SerializableProperty.GetProperties(this);
        }

        public CampaignSettings(XElement? element = null)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public XElement Save()
        {
            XElement saveElement = new XElement(LowerCaseSaveElementName);
            SerializableProperty.SerializeProperties(this, saveElement, saveIfDefault: true);
            return saveElement;
        }

        private static int GetAddedMissionCount()
        {
            var characters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
            if (!characters.Any()) { return 0; }
            return characters.Max(static character => (int)character.GetStatValue(StatTypes.ExtraMissionCount));
        }

        public struct MultiplierSettings
        {
            public float Min { get; set; }
            public float Max { get; set; }
            public float Step { get; set; }
        }

        public static MultiplierSettings GetMultiplierSettings(string multiplierName)
        {
            if (_multiplierSettings.TryGetValue(multiplierName, out MultiplierSettings value))
            {
                return value;
            }

            return _multiplierSettings["default"];
        }
    }
}