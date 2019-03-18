using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class EventManagerSettings
    {
        public static readonly List<EventManagerSettings> List = new List<EventManagerSettings>();

        public readonly string Name;

        //How much the event threshold increases per second. 0.0005f = 0.03f per minute
        public readonly float EventThresholdIncrease = 0.0005f;

        //The threshold is reset to this value after an event has been triggered.
        public readonly float DefaultEventThreshold = 0.2f;
        
        public readonly float EventCooldown = 360.0f;
        
        public readonly float MinEventDifficulty = 0.0f;
        public readonly float MaxEventDifficulty = 100.0f;

        public readonly float MinLevelDifficulty = 0.0f;
        public readonly float MaxLevelDifficulty = 100.0f;

        static EventManagerSettings()
        {
            Load(Path.Combine("Content", "EventManagerSettings.xml"));
        }

        private static void Load(string file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null || doc.Root == null) return;

            foreach (XElement subElement in doc.Root.Elements())
            {
                List.Add(new EventManagerSettings(subElement));
            }
        }

        public EventManagerSettings(XElement element)
        {
            Name = element.Name.ToString();
            EventThresholdIncrease = element.GetAttributeFloat("EventThresholdIncrease", 0.0005f);
            DefaultEventThreshold = element.GetAttributeFloat("DefaultEventThreshold", 0.2f);
            EventCooldown = element.GetAttributeFloat("EventCooldown", 360.0f);

            MinEventDifficulty = element.GetAttributeFloat("MinEventDifficulty", 0.0f);
            MaxEventDifficulty = element.GetAttributeFloat("MaxEventDifficulty", 100.0f);

            MinLevelDifficulty = element.GetAttributeFloat("MinLevelDifficulty", 0.0f);
            MaxLevelDifficulty = element.GetAttributeFloat("MaxLevelDifficulty", 100.0f);
        }
    }
}
