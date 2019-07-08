using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        public static readonly string ConfigFile = "Data" + Path.DirectorySeparatorChar + "karmasettings.xml";

        public string Name => "KarmaManager";

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        [Serialize(0.1f, false)]
        public float KarmaDecay { get; set; }

        [Serialize(50.0f, false)]
        public float KarmaDecayThreshold { get; set; }

        [Serialize(0.15f, false)]
        public float KarmaIncrease { get; set; }

        [Serialize(50.0f, false)]
        public float KarmaIncreaseThreshold { get; set; }

        [Serialize(0.05f, false)]
        public float StructureRepairKarmaIncrease { get; set; }
        [Serialize(0.1f, false)]
        public float StructureDamageKarmaDecrease { get; set; }

        [Serialize(0.05f, false)]
        public float ItemRepairKarmaIncrease { get; set; }

        [Serialize(0.5f, false)]
        public float ReactorOverheatKarmaDecrease { get; set; }
        [Serialize(30.0f, false)]
        public float ReactorMeltdownKarmaDecrease { get; set; }

        [Serialize(0.1f, false)]
        public float DamageEnemyKarmaIncrease { get; set; }
        [Serialize(0.25f, false)]
        public float DamageFriendlyKarmaDecrease { get; set; }

        [Serialize(1.0f, false)]
        public float ExtinguishFireKarmaIncrease { get; set; }


        private float allowedWireDisconnectionsPerMinute;
        [Serialize(5.0f, false)]
        public float AllowedWireDisconnectionsPerMinute
        {
            get { return allowedWireDisconnectionsPerMinute; }
            set { allowedWireDisconnectionsPerMinute = Math.Max(0.0f, value); }
        }

        [Serialize(4.0f, false)]
        public float WireDisconnectionKarmaDecrease { get; set; }

        [Serialize(0.15f, false)]
        public float SteerSubKarmaIncrease { get; set; }

        [Serialize(15.0f, false)]
        public float SpamFilterKarmaDecrease { get; set; }

        [Serialize(40.0f, false)]
        public float HerpesThreshold { get; set; }

        [Serialize(1.0f, false)]
        public float KickBanThreshold { get; set; }

        [Serialize(10.0f, false)]
        public float KarmaNotificationInterval { get; set; }

        private readonly AfflictionPrefab herpesAffliction;

        public Dictionary<string, XElement> Presets = new Dictionary<string, XElement>();
        
        public KarmaManager()
        {
            XDocument doc = XMLExtensions.TryLoadXml(ConfigFile);
            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc?.Root);
            if (doc?.Root != null)
            {
                Presets["custom"] = doc.Root;
                foreach (XElement subElement in doc.Root.Elements())
                {
                    string presetName = subElement.GetAttributeString("name", "");
                    Presets[presetName.ToLowerInvariant()] = subElement;
                }
                SelectPreset("default");
            }
            herpesAffliction = AfflictionPrefab.List.Find(ap => ap.Identifier == "spaceherpes");
        }

        public void SelectPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) { return; }
            presetName = presetName.ToLowerInvariant();
            
            if (presetName != "custom" && Presets.ContainsKey(presetName))
            {
                SerializableProperty.DeserializeProperties(this, Presets[presetName]);
            }
        }
    }
}
