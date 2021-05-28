using System;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class EventPrefab
    {
        public readonly XElement ConfigElement;    
        public readonly Type EventType;
        public readonly float Probability;
        public readonly bool TriggerEventCooldown;
        public float Commonness;
        public string Identifier;
        public string BiomeIdentifier;

        public bool UnlockPathEvent;
        public string UnlockPathTooltip;
        public int UnlockPathReputation;
        public string UnlockPathFaction;

        public EventPrefab(XElement element)
        {
            ConfigElement = element;
         
            try
            {
                EventType = Type.GetType("Barotrauma." + ConfigElement.Name, true, true);
                if (EventType == null)
                {
                    DebugConsole.ThrowError("Could not find an event class of the type \"" + ConfigElement.Name + "\".");
                }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an event class of the type \"" + ConfigElement.Name + "\".");
            }

            Identifier = ConfigElement.GetAttributeString("identifier", string.Empty);
            BiomeIdentifier = ConfigElement.GetAttributeString("biome", string.Empty);
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            Probability = Math.Clamp(element.GetAttributeFloat(1.0f, "probability", "spawnprobability"), 0, 1);
            TriggerEventCooldown = element.GetAttributeBool("triggereventcooldown", true);

            UnlockPathEvent = element.GetAttributeBool("unlockpathevent", false);
            UnlockPathTooltip = element.GetAttributeString("unlockpathtooltip", "lockedpathtooltip");
            UnlockPathReputation = element.GetAttributeInt("unlockpathreputation", 0);
            UnlockPathFaction = element.GetAttributeString("unlockpathfaction", "");
        }

        public Event CreateInstance()
        {
            ConstructorInfo constructor = EventType.GetConstructor(new[] { typeof(EventPrefab) });
            object instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this });
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
            }

            Event ev = (Event)instance;
            if (!ev.LevelMeetsRequirements()) { return null; }

            return (Event)instance;
        }

        public override string ToString()
        {
            return $"EventPrefab ({Identifier})";
        }
    }
}
