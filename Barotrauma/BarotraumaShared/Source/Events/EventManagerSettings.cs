using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System;
using Microsoft.Xna.Framework;

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
            foreach (string file in GameMain.Instance.GetFilesOfType(ContentType.EventManagerSettings))
            {
                Load(file);
            }
        }

        private static void Load(string file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file);
            if (doc == null) { return; }
            var mainElement = doc.Root;
            bool allowOverriding = false;
            if (doc.Root.IsOverride())
            {
                mainElement = doc.Root.FirstElement();
                allowOverriding = true;
            }
            foreach (XElement subElement in mainElement.Elements())
            {
                var element = subElement.IsOverride() ? subElement.FirstElement() : subElement;
                string name = element.Name.ToString();
                var duplicate = List.FirstOrDefault(e => e.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
                if (duplicate != null)
                {
                    if (allowOverriding || subElement.IsOverride())
                    {
                        DebugConsole.NewMessage($"Overriding the existing preset '{name}' in the event manager settings using the file '{file}'", Color.Yellow);
                        List.Remove(duplicate);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in '{file}': Another element with the name '{name}' found! Each element must have a unique name. Use <override></override> tags if you want to override an existing preset.");
                        continue;
                    }
                }
                List.Add(new EventManagerSettings(element));
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
