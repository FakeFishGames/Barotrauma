using System;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    class EventPrefab : Prefab
    {
        public static readonly PrefabCollection<EventPrefab> Prefabs = new PrefabCollection<EventPrefab>();

        public readonly ContentXElement ConfigElement;
        public readonly Type EventType;
        public readonly float Probability;
        public readonly bool TriggerEventCooldown;
        public readonly float Commonness;
        public readonly Identifier BiomeIdentifier;
        public readonly Identifier Faction;
        public readonly float SpawnDistance;

        public readonly bool UnlockPathEvent;
        public readonly string UnlockPathTooltip;
        public readonly int UnlockPathReputation;

        public EventPrefab(ContentXElement element, RandomEventsFile file, Identifier fallbackIdentifier = default)
            : base(file, element.GetAttributeIdentifier("identifier", fallbackIdentifier))
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

            BiomeIdentifier = ConfigElement.GetAttributeIdentifier("biome", Identifier.Empty);
            Faction = ConfigElement.GetAttributeIdentifier("faction", Identifier.Empty);
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            Probability = Math.Clamp(element.GetAttributeFloat(1.0f, "probability", "spawnprobability"), 0, 1);
            TriggerEventCooldown = element.GetAttributeBool("triggereventcooldown", EventType != typeof(ScriptedEvent));

            UnlockPathEvent = element.GetAttributeBool("unlockpathevent", false);
            UnlockPathTooltip = element.GetAttributeString("unlockpathtooltip", "lockedpathtooltip");
            UnlockPathReputation = element.GetAttributeInt("unlockpathreputation", 0);

            SpawnDistance = element.GetAttributeFloat("spawndistance", 0);
        }

        public bool TryCreateInstance<T>(out T instance) where T : Event
        {
            instance = CreateInstance() as T;
            return instance is T;
        }

        public Event CreateInstance()
        {
            ConstructorInfo constructor = EventType.GetConstructor(new[] { typeof(EventPrefab) });
            Event instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this }) as Event;
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
            }
            if (instance != null && !instance.LevelMeetsRequirements()) { return null; }
            return instance;
        }

        public override void Dispose() { }

        public override string ToString()
        {
            return $"EventPrefab ({Identifier})";
        }

        public static EventPrefab GetUnlockPathEvent(Identifier biomeIdentifier, Faction faction)
        {
            var unlockPathEvents = Prefabs.OrderBy(p => p.Identifier).Where(e => e.UnlockPathEvent);
            if (faction != null && unlockPathEvents.Any(e => e.Faction == faction.Prefab.Identifier))
            {
                unlockPathEvents = unlockPathEvents.Where(e => e.Faction == faction.Prefab.Identifier);
            }
            return
                unlockPathEvents.FirstOrDefault(ep => ep.BiomeIdentifier == biomeIdentifier) ??
                unlockPathEvents.FirstOrDefault(ep => ep.BiomeIdentifier == Identifier.Empty);
        }
    }
}
