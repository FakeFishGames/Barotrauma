using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    abstract class ExtraSubmarineInfo : ISerializableEntity
    {
        public string Name { get; protected set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; protected set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable]
        public float MinLevelDifficulty { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes), Editable]
        public float MaxLevelDifficulty { get; set; }

        public ExtraSubmarineInfo(SubmarineInfo submarineInfo, XElement element)
        {
            Name = $"{nameof(ExtraSubmarineInfo)} ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
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
        }

        public void Save(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
        }
    }

    class BeaconStationInfo : ExtraSubmarineInfo
    {
        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool AllowDamagedWalls { get; set; }

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
        public WreckInfo(SubmarineInfo submarineInfo, XElement element) : base(submarineInfo, element)
        {
            Name = $"{nameof(WreckInfo)} ({submarineInfo.Name})";
        }

        public WreckInfo(SubmarineInfo submarineInfo) : base(submarineInfo)
        {
            Name = $"{nameof(WreckInfo)} ({submarineInfo.Name})";
        }

        public WreckInfo(WreckInfo original) : base(original) { }
    }
}
