using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class KarmaManager : ISerializableEntity
    {
        public static readonly string ConfigFile = "Data" + Path.DirectorySeparatorChar + "karmasettings.xml";

        public string Name => "KarmaManager";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool ResetKarmaBetweenRounds { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes)]
        public float KarmaDecay { get; set; }

        [Serialize(50.0f, IsPropertySaveable.Yes)]
        public float KarmaDecayThreshold { get; set; }

        [Serialize(0.15f, IsPropertySaveable.Yes)]
        public float KarmaIncrease { get; set; }

        [Serialize(50.0f, IsPropertySaveable.Yes)]
        public float KarmaIncreaseThreshold { get; set; }

        [Serialize(0.05f, IsPropertySaveable.Yes)]
        public float StructureRepairKarmaIncrease { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes)]
        public float StructureDamageKarmaDecrease { get; set; }

        [Serialize(15.0f, IsPropertySaveable.Yes)]
        public float MaxStructureDamageKarmaDecreasePerSecond { get; set; }

        [Serialize(0.03f, IsPropertySaveable.Yes)]
        public float ItemRepairKarmaIncrease { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes)]
        public float ReactorOverheatKarmaDecrease { get; set; }
        [Serialize(30.0f, IsPropertySaveable.Yes)]
        public float ReactorMeltdownKarmaDecrease { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes)]
        public float DamageEnemyKarmaIncrease { get; set; }
        [Serialize(0.2f, IsPropertySaveable.Yes)]
        public float HealFriendlyKarmaIncrease { get; set; }
        [Serialize(0.25f, IsPropertySaveable.Yes)]
        public float DamageFriendlyKarmaDecrease { get; set; }

        [Serialize(0.25f, IsPropertySaveable.Yes)]
        public float StunFriendlyKarmaDecrease { get; set; }

        [Serialize(0.3f, IsPropertySaveable.Yes)]
        public float StunFriendlyKarmaDecreaseThreshold { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float ExtinguishFireKarmaIncrease { get; set; }
        
        [Serialize(defaultValue: 15.0f, IsPropertySaveable.Yes)]
        public float DangerousItemStealKarmaDecrease { get; set; }
        
        [Serialize(defaultValue: false, IsPropertySaveable.Yes)]
        public bool DangerousItemStealBots { get; set; }

        [Serialize(defaultValue: 0.05f, IsPropertySaveable.Yes)]
        public float BallastFloraKarmaIncrease { get; set; }


        private int allowedWireDisconnectionsPerMinute;
        [Serialize(5, IsPropertySaveable.Yes)]
        public int AllowedWireDisconnectionsPerMinute
        {
            get { return allowedWireDisconnectionsPerMinute; }
            set { allowedWireDisconnectionsPerMinute = Math.Max(0, value); }
        }

        [Serialize(6.0f, IsPropertySaveable.Yes)]
        public float WireDisconnectionKarmaDecrease { get; set; }

        [Serialize(0.15f, IsPropertySaveable.Yes)]
        public float SteerSubKarmaIncrease { get; set; }

        [Serialize(15.0f, IsPropertySaveable.Yes)]
        public float SpamFilterKarmaDecrease { get; set; }

        [Serialize(40.0f, IsPropertySaveable.Yes)]
        public float HerpesThreshold { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes)]
        public float KickBanThreshold { get; set; }
        
        [Serialize(0, IsPropertySaveable.Yes)]
        public int KicksBeforeBan { get; set; }
        
        [Serialize(10.0f, IsPropertySaveable.Yes)]
        public float KarmaNotificationInterval { get; set; }

        [Serialize(120.0f, IsPropertySaveable.Yes)]
        public float AllowedRetaliationTime { get; set; }

        [Serialize(5.0f, IsPropertySaveable.Yes)]
        public float DangerousItemContainKarmaDecrease { get; set; }

        [Serialize(defaultValue: true, IsPropertySaveable.Yes)]
        public bool IsDangerousItemContainKarmaDecreaseIncremental { get; set; }

        [Serialize(30.0f, IsPropertySaveable.Yes)]
        public float MaxDangerousItemContainKarmaDecrease { get; set; }

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
                catch (System.IO.IOException)
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
                foreach (var subElement in doc.Root.Elements())
                {
                    string presetName = subElement.GetAttributeString("name", "");
                    Presets[presetName.ToLowerInvariant()] = subElement;
                }
                SelectPreset(GameMain.NetworkMember?.ServerSettings?.KarmaPreset ?? "default");
            }
            herpesAffliction = AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier == "spaceherpes");
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

            System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings
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
                        doc.SaveSafe(writer);
                    }
                    break;
                }
                catch (System.IO.IOException)
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
