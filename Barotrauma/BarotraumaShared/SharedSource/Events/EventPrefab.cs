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

        /// <summary>
        /// The probability for the event to do something if it gets selected. For example, the probability for a MonsterEvent to spawn the monster(s).
        /// </summary>
        public readonly float Probability;

        /// <summary>
        /// When this event occurs, should it trigger the event cooldown during which no new events are triggered?
        /// </summary>
        public readonly bool TriggerEventCooldown;

        /// <summary>
        /// The commonness of the event (i.e. how likely it is for this specific event to be chosen from the event set it's configured in). 
        /// Only valid if the event set is configured to choose a random event (as opposed to just executing all the events in the set).
        /// </summary>
        public readonly float Commonness;

        /// <summary>
        /// If set, the event set can only be chosen in this biome.
        /// </summary>
        public readonly Identifier BiomeIdentifier;

        /// <summary>
        /// If set, this layer must be present somewhere in the level.
        /// </summary>
        public readonly Identifier RequiredLayer;

        /// <summary>
        /// If set, the event set can only be chosen in locations that belong to this faction.
        /// </summary>
        public readonly Identifier Faction;

        public readonly LocalizedString Name;

        /// <summary>
        /// If set, this event is used as an event that can unlock a path to the next biome.
        /// </summary>
        public readonly bool UnlockPathEvent;

        /// <summary>
        /// Only valid if UnlockPathEvent is set to true. The tooltip displayed on the pathway this event is blocking.
        /// </summary>
        public readonly string UnlockPathTooltip;

        /// <summary>
        /// Only valid if UnlockPathEvent is set to true. The reputation requirement displayed on the pathway this event is blocking.
        /// </summary>
        public readonly int UnlockPathReputation;

        public static EventPrefab Create(ContentXElement element, RandomEventsFile file, Identifier fallbackIdentifier = default)
        {
            if (element.NameAsIdentifier() == nameof(TraitorEvent))
            {
                return new TraitorEventPrefab(element, file, fallbackIdentifier);
            }
            else
            {
                return new EventPrefab(element, file, fallbackIdentifier);
            }
        }

        public EventPrefab(ContentXElement element, RandomEventsFile file, Identifier fallbackIdentifier = default)
            : base(file, element.GetAttributeIdentifier("identifier", fallbackIdentifier))
        {
            ConfigElement = element;
         
            try
            {
                EventType = Type.GetType("Barotrauma." + ConfigElement.Name, true, true);
                if (EventType == null)
                {
                    DebugConsole.ThrowError("Could not find an event class of the type \"" + ConfigElement.Name + "\".",
                        contentPackage: element.ContentPackage);
                }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an event class of the type \"" + ConfigElement.Name + "\".",
                    contentPackage: element.ContentPackage);
            }

            Name = TextManager.Get($"eventname.{Identifier}").Fallback(Identifier.ToString());

            BiomeIdentifier = ConfigElement.GetAttributeIdentifier("biome", Identifier.Empty);
            Faction = ConfigElement.GetAttributeIdentifier("faction", Identifier.Empty);
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            Probability = Math.Clamp(element.GetAttributeFloat(1.0f, "probability", "spawnprobability"), 0, 1);
            TriggerEventCooldown = element.GetAttributeBool("triggereventcooldown", EventType != typeof(ScriptedEvent));

            RequiredLayer = element.GetAttributeIdentifier(nameof(RequiredLayer), Identifier.Empty);

            UnlockPathEvent = element.GetAttributeBool("unlockpathevent", false);
            UnlockPathTooltip = element.GetAttributeString("unlockpathtooltip", "lockedpathtooltip");
            UnlockPathReputation = element.GetAttributeInt("unlockpathreputation", 0);
        }

        public bool TryCreateInstance<T>(int seed, out T instance) where T : Event
        {
            instance = CreateInstance(seed) as T;
            return instance is not null;
        }

        public Event CreateInstance(int seed)
        {
            ConstructorInfo constructor = EventType.GetConstructor(new[] { GetType(), typeof(int) });
            Event instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this, seed }) as Event;
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
            return $"{nameof(EventPrefab)} ({Identifier})";
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
