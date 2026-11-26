using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Barotrauma
{
    public class EventPrefab : Prefab
    {
        public static readonly PrefabCollection<EventPrefab> Prefabs = new PrefabCollection<EventPrefab>();

        public readonly ContentXElement ConfigElement;
        public readonly Type EventType;

        private readonly ImmutableHashSet<Identifier> tags;
        public ImmutableHashSet<Identifier> Tags => tags;

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
        /// If set, this spawn point tag must be present somewhere in the level.
        /// </summary>
        public readonly Identifier RequiredSpawnPointTag;

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

        /// <summary>
        /// Only used in the context of <see cref="EventLogAction"/>, determines the amount of skulls to show in the mission event log
        /// </summary>
        public readonly int DifficultyIconCount;

        /// <summary>
        /// Only used in the context of <see cref="EventLogAction"/>, determines the icon in the mission event log
        /// </summary>
        public readonly Sprite Icon;

        /// <summary>
        /// Only used in the context of <see cref="EventLogAction"/>, determines the color of the icon in the mission event log
        /// </summary>
        public readonly Color IconColor;

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

            string name = element.GetAttributeString(nameof(Name), "");
            Name = TextManager.Get(name).Fallback(TextManager.Get($"eventname.{Identifier}").Fallback(Identifier.ToString()));

            tags = ConfigElement.GetAttributeIdentifierImmutableHashSet(nameof(tags), ImmutableHashSet<Identifier>.Empty);
            BiomeIdentifier = ConfigElement.GetAttributeIdentifier("biome", Identifier.Empty);
            Faction = ConfigElement.GetAttributeIdentifier("faction", Identifier.Empty);
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            Probability = Math.Clamp(element.GetAttributeFloat(1.0f, "probability", "spawnprobability"), 0, 1);
            TriggerEventCooldown = element.GetAttributeBool("triggereventcooldown", EventType != typeof(ScriptedEvent));

            RequiredLayer = element.GetAttributeIdentifier(nameof(RequiredLayer), Identifier.Empty);
            RequiredSpawnPointTag = element.GetAttributeIdentifier(nameof(RequiredSpawnPointTag), Identifier.Empty);

            UnlockPathEvent = element.GetAttributeBool("unlockpathevent", false);
            UnlockPathTooltip = element.GetAttributeString("unlockpathtooltip", "lockedpathtooltip");
            UnlockPathReputation = element.GetAttributeInt("unlockpathreputation", 0);

            DifficultyIconCount = element.GetAttributeInt(nameof(DifficultyIconCount), -1);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "icon":
                        Icon = new Sprite(subElement);
                        IconColor = subElement.GetAttributeColor("color", new Color(245, 105, 105, 255));
                        break;
                }
            }
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

        public override void Dispose()
        {
            Icon?.Remove();
        }

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

        /// <summary>
        /// Finds an event prefab with the specified identifier, or if it isn't defined or a matching event isn't found, a random event prefab with the specified tag.
        /// </summary>
        /// <param name="callingContentPackage">Which content package is trying to find the event (if any)? Only used for logging error messages.</param>
        public static EventPrefab FindEventPrefab(Identifier identifier, Identifier tag, ContentPackage callingContentPackage = null)
        {
            EventPrefab eventPrefab = null;
            if (!identifier.IsEmpty)
            {
                eventPrefab = EventSet.GetEventPrefab(identifier);
                if (eventPrefab == null)
                {
                    DebugConsole.ThrowError($"Failed to find an event prefab with the identifier {identifier}.",
                        contentPackage: callingContentPackage);
                }
            }
            else if (!tag.IsEmpty)
            {
                var matchingEvents = EventSet.GetAllEventPrefabs().Where(e => e.Tags.Contains(tag));
                eventPrefab = ToolBox.SelectWeightedRandom(matchingEvents, e => e.Commonness, Rand.RandSync.Unsynced);
                if (eventPrefab == null)
                {
                    DebugConsole.ThrowError($"Failed to find an event prefab with the tag {tag}.",
                        contentPackage: callingContentPackage);
                }
            }
            else
            {
                DebugConsole.ThrowError($"Failed to find an event prefab: neither an identifier or tag were defined.",
                    contentPackage: callingContentPackage);
            }
            return eventPrefab;
        }
    }
}
