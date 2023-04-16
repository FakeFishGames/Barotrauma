using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class BeaconStationInfo : ISerializableEntity
    {
        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool AllowDamagedWalls { get; set; }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool AllowDisconnectedWires { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable]
        public float MinLevelDifficulty { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes), Editable]
        public float MaxLevelDifficulty { get; set; }

        [Serialize(Level.PlacementType.Bottom, IsPropertySaveable.Yes), Editable]
        public Level.PlacementType Placement { get; set; }

        public string Name { get; private set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        public BeaconStationInfo(SubmarineInfo submarineInfo, XElement element)
        {
            Name = $"BeaconStationInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public BeaconStationInfo(SubmarineInfo submarineInfo)
        {
            Name = $"BeaconStationInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this);
        }

        public BeaconStationInfo(BeaconStationInfo original)
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
}
