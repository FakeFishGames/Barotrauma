using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        public static readonly string ConfigFile = "Data" + Path.DirectorySeparatorChar + "karmasettings.xml";

        public string Name => "KarmaManager";

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        [Serialize(true, true)]
        public bool ResetKarmaBetweenRounds { get; set; }

        [Serialize(0.1f, true)]
        public float KarmaDecay { get; set; }

        [Serialize(50.0f, true)]
        public float KarmaDecayThreshold { get; set; }

        [Serialize(0.15f, true)]
        public float KarmaIncrease { get; set; }

        [Serialize(50.0f, true)]
        public float KarmaIncreaseThreshold { get; set; }

        [Serialize(0.05f, true)]
        public float StructureRepairKarmaIncrease { get; set; }
        [Serialize(0.1f, true)]
        public float StructureDamageKarmaDecrease { get; set; }
        [Serialize(30.0f, true)]
        public float MaxStructureDamageKarmaDecreasePerSecond { get; set; }

        [Serialize(0.03f, true)]
        public float ItemRepairKarmaIncrease { get; set; }

        [Serialize(0.5f, true)]
        public float ReactorOverheatKarmaDecrease { get; set; }
        [Serialize(30.0f, true)]
        public float ReactorMeltdownKarmaDecrease { get; set; }

        [Serialize(0.1f, true)]
        public float DamageEnemyKarmaIncrease { get; set; }
        [Serialize(0.2f, true)]
        public float HealFriendlyKarmaIncrease { get; set; }
        [Serialize(0.25f, true)]
        public float DamageFriendlyKarmaDecrease { get; set; }

        [Serialize(1.0f, true)]
        public float ExtinguishFireKarmaIncrease { get; set; }


        private int allowedWireDisconnectionsPerMinute;
        [Serialize(5, true)]
        public int AllowedWireDisconnectionsPerMinute
        {
            get { return allowedWireDisconnectionsPerMinute; }
            set { allowedWireDisconnectionsPerMinute = Math.Max(0, value); }
        }

        [Serialize(6.0f, true)]
        public float WireDisconnectionKarmaDecrease { get; set; }

        [Serialize(0.15f, true)]
        public float SteerSubKarmaIncrease { get; set; }

        [Serialize(15.0f, true)]
        public float SpamFilterKarmaDecrease { get; set; }

        [Serialize(40.0f, true)]
        public float HerpesThreshold { get; set; }

        [Serialize(1.0f, true)]
        public float KickBanThreshold { get; set; }
        
        [Serialize(0, true)]
        public int KicksBeforeBan { get; set; }
        
        [Serialize(10.0f, true)]
        public float KarmaNotificationInterval { get; set; }

        [Serialize(120.0f, true)]
        public float AllowedRetaliationTime { get; set; }

        private readonly AfflictionPrefab herpesAffliction;

        public Dictionary<string, XElement> Presets = new Dictionary<string, XElement>();
        
        public KarmaManager()
        {
            XDocument doc = null;
            int maxLoadRetries = 4;
            for (int i = 0; i <= maxLoadRetries; i++)
            {
                try
                {
                    doc = XMLExtensions.TryLoadXml(ConfigFile);
                    break;
                }
                catch (IOException)
                {
                    if (i == maxLoadRetries) { break; }
                    DebugConsole.NewMessage("Opening karma settings file \"" + ConfigFile + "\" failed, retrying in 250 ms...");
                    System.Threading.Thread.Sleep(250);
                }
            }

            SerializableProperties = SerializableProperty.DeserializeProperties(this, doc?.Root);
            if (doc?.Root != null)
            {
                Presets["custom"] = doc.Root;
                foreach (XElement subElement in doc.Root.Elements())
                {
                    string presetName = subElement.GetAttributeString("name", "");
                    Presets[presetName.ToLowerInvariant()] = subElement;
                }
                SelectPreset(GameMain.NetworkMember?.ServerSettings?.KarmaPreset ?? "default");
            }
            herpesAffliction = AfflictionPrefab.List.Find(ap => ap.Identifier == "spaceherpes");
        }

        public void SelectPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) { return; }
            presetName = presetName.ToLowerInvariant();
            
            if (Presets.ContainsKey(presetName))
            {
                SerializableProperty.DeserializeProperties(this, Presets[presetName]);
            }
            else if (Presets.ContainsKey("custom"))
            {
                SerializableProperty.DeserializeProperties(this, Presets["custom"]);

            }
        }

        public void SaveCustomPreset()
        {
            if (Presets.ContainsKey("custom"))
            {
                SerializableProperty.SerializeProperties(this, Presets["custom"], saveIfDefault: true);
            }
        }

        public void Save()
        {
            XDocument doc = new XDocument(new XElement(Name));

            foreach (KeyValuePair<string, XElement> preset in Presets)
            {
                doc.Root.Add(preset.Value);
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            int maxLoadRetries = 4;
            for (int i = 0; i <= maxLoadRetries; i++)
            {
                try
                {
                    using (var writer = XmlWriter.Create(ConfigFile, settings))
                    {
                        doc.Save(writer);
                    }
                    break;
                }
                catch (IOException)
                {
                    if (i == maxLoadRetries) { throw; }

                    DebugConsole.NewMessage("Saving karma settings file file \"" + ConfigFile + "\" failed, retrying in 250 ms...");
                    System.Threading.Thread.Sleep(250);
                    continue;
                }
            }
        }
    }
}
