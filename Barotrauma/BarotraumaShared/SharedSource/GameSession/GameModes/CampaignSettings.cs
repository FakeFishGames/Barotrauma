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

        public string Name => "CampaignSettings";

        public const string LowerCaseSaveElementName = "campaignsettings";

        [Serialize("", IsPropertySaveable.Yes)]
        public string PresetName { get; set; } = string.Empty;

        [Serialize(false, IsPropertySaveable.Yes), NetworkSerialize]
        public bool RadiationEnabled { get; set; }

        private int maxMissionCount;

        [Serialize(DefaultMaxMissionCount, IsPropertySaveable.Yes), NetworkSerialize(MinValueInt = MinMissionCountLimit, MaxValueInt = MaxMissionCountLimit)]
        public int MaxMissionCount
        {
            get => maxMissionCount;
            set => maxMissionCount = MathHelper.Clamp(value, MinMissionCountLimit, MaxMissionCountLimit);
        }

        public int TotalMaxMissionCount => MaxMissionCount + GetAddedMissionCount();

        [Serialize(StartingBalanceAmount.Medium, IsPropertySaveable.Yes), NetworkSerialize]
        public StartingBalanceAmount StartingBalanceAmount { get; set; }

        [Serialize(GameDifficulty.Medium, IsPropertySaveable.Yes), NetworkSerialize]
        public GameDifficulty Difficulty { get; set; }

        [Serialize("normal", IsPropertySaveable.Yes), NetworkSerialize]
        public Identifier StartItemSet { get; set; }

        public int InitialMoney
        {
            get
            {
                if (CampaignModePresets.Definitions.TryGetValue(nameof(StartingBalanceAmount).ToIdentifier(), out var definition))
                {
                    return definition.GetInt(StartingBalanceAmount.ToIdentifier());
                }
                return 8000;
                
            }
        }

        public float ExtraEventManagerDifficulty
        {
            get
            {
                if (CampaignModePresets.Definitions.TryGetValue(nameof(ExtraEventManagerDifficulty).ToIdentifier(), out var definition))
                {
                    return definition.GetFloat(Difficulty.ToIdentifier());
                }
                return 0;                
            }
        }

        public float LevelDifficultyMultiplier
        {
            get
            {
                if (CampaignModePresets.Definitions.TryGetValue(nameof(LevelDifficultyMultiplier).ToIdentifier(), out var definition))
                {
                    return definition.GetFloat(Difficulty.ToIdentifier());
                }
                return 1.0f;
            }
        }

        public const int DefaultMaxMissionCount = 2;
        public const int MaxMissionCountLimit = 10;
        public const int MinMissionCountLimit = 1;

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
            return GameSession.GetSessionCrewCharacters(CharacterType.Both).Max(static character => (int)character.GetStatValue(StatTypes.ExtraMissionCount));
        }
    }
}