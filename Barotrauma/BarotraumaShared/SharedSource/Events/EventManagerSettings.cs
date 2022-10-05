using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using System;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class EventManagerSettings : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<EventManagerSettings> Prefabs = new PrefabCollection<EventManagerSettings>();
        public static IOrderedEnumerable<EventManagerSettings> OrderedByDifficulty
        {
            get
            {
                return Prefabs.OrderBy(p => (p.MinLevelDifficulty + p.MaxLevelDifficulty) * 0.5f)
                    .ThenBy(p => p.UintIdentifier);
            }
        }

        public static EventManagerSettings GetByDifficultyPercentile(float p)
        {
            EventManagerSettings[] settings = OrderedByDifficulty.ToArray();
            return settings[Math.Clamp((int)(settings.Length * p), 0, settings.Length - 1)];
        }

        public readonly LocalizedString Name;

        //How much the event threshold increases per second. 0.0005f = 0.03f per minute
        public readonly float EventThresholdIncrease = 0.0005f;

        //The threshold is reset to this value after an event has been triggered.
        public readonly float DefaultEventThreshold = 0.2f;
        
        public readonly float EventCooldown = 360.0f;
        
        public readonly float MinLevelDifficulty = 0.0f;
        public readonly float MaxLevelDifficulty = 100.0f;

        public readonly float FreezeDurationWhenCrewAway = 60.0f * 10.0f;

        public override void Dispose() { }

        public EventManagerSettings(XElement element, EventManagerSettingsFile file) : base(file, element.NameAsIdentifier())
        {
            Name = TextManager.Get("difficulty." + Identifier).Fallback(Identifier.Value);
            EventThresholdIncrease = element.GetAttributeFloat("EventThresholdIncrease", EventThresholdIncrease);
            DefaultEventThreshold = element.GetAttributeFloat("DefaultEventThreshold", DefaultEventThreshold);
            EventCooldown = element.GetAttributeFloat("EventCooldown", EventCooldown);

            MinLevelDifficulty = element.GetAttributeFloat("MinLevelDifficulty", MinLevelDifficulty);
            MaxLevelDifficulty = element.GetAttributeFloat("MaxLevelDifficulty", MaxLevelDifficulty);

            FreezeDurationWhenCrewAway = element.GetAttributeFloat("FreezeDurationWhenCrewAway", FreezeDurationWhenCrewAway);
        }
    }
}
