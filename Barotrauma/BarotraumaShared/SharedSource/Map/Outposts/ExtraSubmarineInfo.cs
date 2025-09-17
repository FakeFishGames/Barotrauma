using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class ExtraSubmarineInfo : ISerializableEntity
    {
        public string Name { get; protected set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; protected set; }

        public HashSet<Identifier> MissionTags { get; } = [];

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable]
        public float MinLevelDifficulty { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes), Editable]
        public float MaxLevelDifficulty { get; set; }

        public ExtraSubmarineInfo(SubmarineInfo submarineInfo, XElement element)
        {
            Name = $"{nameof(ExtraSubmarineInfo)} ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            foreach (var missionTag in element.GetAttributeIdentifierArray(nameof(MissionTags), []))
            {
                MissionTags.Add(missionTag);
            }
        }

        public ExtraSubmarineInfo(SubmarineInfo submarineInfo)
        {
            Name = $"{nameof(ExtraSubmarineInfo)} ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this);
        }

        public ExtraSubmarineInfo(ExtraSubmarineInfo original)
        {
            Name = original.Name;
            SerializableProperties = new Dictionary<Identifier, SerializableProperty>();
            foreach (KeyValuePair<Identifier, SerializableProperty> kvp in original.SerializableProperties)
            {
                SerializableProperties.Add(kvp.Key, kvp.Value);
                if (SerializableProperty.GetSupportedTypeName(kvp.Value.PropertyType) != null)
                {
                    kvp.Value.TrySetValue(this, kvp.Value.GetValue(original));
                }
            }
            foreach (var missionTag in original.MissionTags)
            {
                MissionTags.Add(missionTag);
            }
        }

        public virtual void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
            // MissionTags is not automatically serialized because HashSet<Identifier> is not a supported type
            // We need to manually serialize it as a comma-separated string
            element.SetAttributeValue(nameof(MissionTags), string.Join(',', MissionTags));
        }
    }

    class BeaconStationInfo : ExtraSubmarineInfo
    {
        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool AllowDamagedWalls { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool AllowDamagedDevices { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool AllowDisconnectedWires { get; set; }

        [Serialize(Level.PlacementType.Bottom, IsPropertySaveable.Yes), Editable]
        public Level.PlacementType Placement { get; set; }

        public BeaconStationInfo(SubmarineInfo submarineInfo, XElement element) : base(submarineInfo, element)
        {
            Name = $"{nameof(BeaconStationInfo)} ({submarineInfo.Name})";
        }

        public BeaconStationInfo(SubmarineInfo submarineInfo) : base(submarineInfo)
        {
            Name = $"{nameof(BeaconStationInfo)} ({submarineInfo.Name})";
        }

        public BeaconStationInfo(BeaconStationInfo original) : base(original) { }
    }

    class WreckInfo : ExtraSubmarineInfo
    {
        // Unknown -> older submarines before this property was added
        public enum HasThalamus { Unknown, Yes, No }

        [Serialize(HasThalamus.Unknown, IsPropertySaveable.Yes)]
        public HasThalamus WreckContainsThalamus { get; private set; }

        public WreckInfo(SubmarineInfo submarineInfo, XElement element) : base(submarineInfo, element)
        {
            Name = $"{nameof(WreckInfo)} ({submarineInfo.Name})";
            TryDetermineThalamusIfUnknown(element);
        }

        public WreckInfo(SubmarineInfo submarineInfo) : base(submarineInfo)
        {
            Name = $"{nameof(WreckInfo)} ({submarineInfo.Name})";
            TryDetermineThalamusIfUnknown(submarineInfo.SubmarineElement);
        }

        public WreckInfo(WreckInfo original) : base(original) { }

        // Attempts to determine if the wreck contains a thalamus item
        private void TryDetermineThalamusIfUnknown(XElement element)
        {
            if (WreckContainsThalamus != HasThalamus.Unknown) { return; }

            if (element == null)
            {
                // nothing we can do, oh well
                WreckContainsThalamus = HasThalamus.Unknown;
                return;
            }

            foreach (var subElement in element.Elements())
            {
                if (!string.Equals(subElement.Name.ToString(), nameof(Item), StringComparison.InvariantCultureIgnoreCase)) { continue; }

                var tags = subElement.GetAttributeIdentifierImmutableHashSet(nameof(ItemPrefab.Tags), ImmutableHashSet<Identifier>.Empty);

                if (tags.Contains(Tags.Thalamus))
                {
                    WreckContainsThalamus = HasThalamus.Yes;
                    return;
                }
            }

            WreckContainsThalamus = HasThalamus.No;
        }
    }

    class EnemySubmarineInfo : ExtraSubmarineInfo
    {
        [Serialize(4000.0f, IsPropertySaveable.Yes), Editable]
        public float Reward { get; set; }

        [Serialize(50.0f, IsPropertySaveable.Yes), Editable]
        public float PreferredDifficulty { get; set; }

        public EnemySubmarineInfo(SubmarineInfo submarineInfo, XElement element) : base(submarineInfo, element)
        {
            Name = $"{nameof(EnemySubmarineInfo)} ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public EnemySubmarineInfo(SubmarineInfo submarineInfo) : base(submarineInfo)
        {
            Name = $"{nameof(EnemySubmarineInfo)} ({submarineInfo.Name})";
        }

        public EnemySubmarineInfo(EnemySubmarineInfo original) : base(original)
        {
        }


    }
}
