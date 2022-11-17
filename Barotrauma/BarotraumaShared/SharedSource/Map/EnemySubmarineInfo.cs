using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class EnemySubmarineInfo : ISerializableEntity
    {
        [Serialize(4000.0f, IsPropertySaveable.Yes), Editable]
        public float Reward { get; set; }

        [Serialize(50.0f, IsPropertySaveable.Yes), Editable]
        public float PreferredDifficulty { get; set; }

        [Serialize("default", IsPropertySaveable.Yes), Editable]
        public string MissionTags { get; set; }

        public string Name { get; private set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        public EnemySubmarineInfo(SubmarineInfo submarineInfo, XElement element)
        {
            Name = $"EnemySubmarineInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public EnemySubmarineInfo(SubmarineInfo submarineInfo)
        {
            Name = $"EnemySubmarineInfo ({submarineInfo.Name})";
            SerializableProperties = SerializableProperty.DeserializeProperties(this);
        }

        public EnemySubmarineInfo(EnemySubmarineInfo original)
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
