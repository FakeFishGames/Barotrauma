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

        public readonly string Identifier;
        public readonly string Name;

        //How much the event threshold increases per second. 0.0005f = 0.03f per minute
        public readonly float EventThresholdIncrease = 0.0005f;

        //The threshold is reset to this value after an event has been triggered.
        public readonly float DefaultEventThreshold = 0.2f;
        
        public readonly float EventCooldown = 360.0f;
        public readonly float WanderCooldown = 60.0f;

        public readonly float WanderChallengeScale = 0.0f;
        public readonly float WanderChallengeScaleRate = 0.001f;

        public readonly float MinLevelDifficulty = 0.0f;
        public readonly float MaxLevelDifficulty = 100.0f;

        static EventManagerSettings()
        {
            foreach (ContentFile file in GameMain.Instance.GetFilesOfType(ContentType.EventManagerSettings))
            {
                Load(file);
            }
        }

        private static void Load(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);
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
                string identifier = element.Name.ToString();
                var duplicate = List.FirstOrDefault(e => e.Identifier.ToString().Equals(identifier, StringComparison.OrdinalIgnoreCase));
                if (duplicate != null)
                {
                    if (allowOverriding || subElement.IsOverride())
                    {
                        DebugConsole.NewMessage($"Overriding the existing preset '{identifier}' in the event manager settings using the file '{file.Path}'", Color.Yellow);
                        List.Remove(duplicate);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in '{file.Path}': Another element with the name '{identifier}' found! Each element must have a unique name. Use <override></override> tags if you want to override an existing preset.");
                        continue;
                    }
                }
                List.Add(new EventManagerSettings(element));
            }
            List.Sort((x, y) => { return Math.Sign((x.MinLevelDifficulty + x.MaxLevelDifficulty) / 2.0f - (y.MinLevelDifficulty + y.MaxLevelDifficulty) / 2.0f); });
        }

        public EventManagerSettings(XElement element)
        {
            Identifier = element.Name.ToString();
            Name = TextManager.Get("difficulty." + Identifier, returnNull: true) ?? Identifier;
            EventThresholdIncrease = element.GetAttributeFloat("EventThresholdIncrease", 0.0005f);
            DefaultEventThreshold = element.GetAttributeFloat("DefaultEventThreshold", 0.2f);
            EventCooldown = element.GetAttributeFloat("EventCooldown", 360.0f);
            WanderCooldown = element.GetAttributeFloat("WanderCooldown", 60.0f);
            WanderChallengeScale = element.GetAttributeFloat("ChallengeScale", 5000.0f);
            WanderChallengeScaleRate = element.GetAttributeFloat("ChallengeScaleRate", 100.0f);

            MinLevelDifficulty = element.GetAttributeFloat("MinLevelDifficulty", 0.0f);
            MaxLevelDifficulty = element.GetAttributeFloat("MaxLevelDifficulty", 100.0f);
        }
    }
}
