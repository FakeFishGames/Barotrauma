using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
#if CLIENT
    class EventSprite : Prefab
    {
        public readonly static PrefabCollection<EventSprite> Prefabs = new PrefabCollection<EventSprite>();

        public readonly Sprite Sprite;

        public EventSprite(ContentXElement element, RandomEventsFile file) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            Sprite = new Sprite(element);
        }

        public override void Dispose() { Sprite?.Remove(); }
    }
#endif

    /// <summary>
    /// Event sets are sets of random events that occur within a level (most commonly, monster spawns and scripted events).
    /// Event sets can also be nested: a "parent set" can choose from several "subsets", either randomly or by some kind of criteria.
    /// </summary>
    sealed class EventSet : Prefab
    {
        internal class EventDebugStats
        {
            public readonly EventSet RootSet;
            public readonly Dictionary<Identifier, int> MonsterCounts = new Dictionary<Identifier, int>();
            public float MonsterStrength;

            public EventDebugStats(EventSet rootSet)
            {
                RootSet = rootSet;
            }
        }

        public readonly static PrefabCollection<EventSet> Prefabs = new PrefabCollection<EventSet>();
#if CLIENT
        public static Sprite GetEventSprite(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) { return null; }

            if (EventSprite.Prefabs.TryGet(identifier.ToIdentifier(), out EventSprite sprite))
            {
                return sprite.Sprite;
            }

#if DEBUG || UNSTABLE
            DebugConsole.ThrowError($"Could not find the event sprite \"{identifier}\"");
#else
            DebugConsole.AddWarning($"Could not find the event sprite \"{identifier}\"");
#endif
            return null;
        }
#endif

        public static List<EventPrefab> GetAllEventPrefabs()
        {
            List<EventPrefab> eventPrefabs = EventPrefab.Prefabs.ToList();
            foreach (var eventSet in Prefabs)
            {
                AddSetEventPrefabsToList(eventPrefabs, eventSet);
            }
            return eventPrefabs;
        }

        public static void AddSetEventPrefabsToList(List<EventPrefab> list, EventSet set)
        {
            list.AddRange(set.EventPrefabs.SelectMany(ep => ep.EventPrefabs));
            foreach (var childSet in set.ChildSets) { AddSetEventPrefabsToList(list, childSet); }
        }

        public static EventPrefab GetEventPrefab(Identifier identifier)
        {
            return GetAllEventPrefabs().Find(prefab => prefab.Identifier == identifier);
        }

        /// <summary>
        /// If enabled, this set can only be chosen in the campaign mode.
        /// </summary>
        public readonly bool IsCampaignSet;

        /// <summary>
        /// The difficulty of the current level must be equal to or higher than this for this set to be chosen. 
        /// </summary>
        public readonly float MinLevelDifficulty;
        /// <summary>
        /// The difficulty of the current level must be equal to or less than this for this set to be chosen. 
        /// </summary>
        public readonly float MaxLevelDifficulty;

        /// <summary>
        /// If set, the event set can only be chosen in this biome.
        /// </summary>
        public readonly Identifier BiomeIdentifier;

        /// <summary>
        /// If set, the event set can only be chosen in this type of level (outpost level or a connection between outpost levels).
        /// </summary>
        public readonly LevelData.LevelType LevelType;

        /// <summary>
        /// If set, this layer must be present somewhere in the level.
        /// </summary>
        public readonly Identifier RequiredLayer;

        /// <summary>
        /// If set, the event set can only be chosen in locations of this type.
        /// </summary>
        public readonly ImmutableArray<Identifier> LocationTypeIdentifiers;

        /// <summary>
        /// If set, the event set can only be chosen in locations that belong to this faction.
        /// </summary>
        public readonly Identifier Faction;

        /// <summary>
        /// If set, one event, or a sub event set, is chosen randomly from this set.
        /// </summary>
        public readonly bool ChooseRandom;

        /// <summary>
        /// Only valid if ChooseRandom is enabled. How many random events to choose from the set?
        /// </summary>
        private readonly int eventCount = 1;
        public readonly int SubSetCount = 1;
        private readonly Dictionary<Identifier, int> overrideEventCount = new Dictionary<Identifier, int>();

        /// <summary>
        /// 'Exhaustible' sets won't appear in the same level until after one world step (~10 min, see Map.ProgressWorld) has passed.
        /// </summary>
        public readonly bool Exhaustible;

        /// <summary>
        /// The event set won't become active until the submarine has travelled at least this far. A value between 0-1, where 0 is the beginning of the level and 1 the end of the level (e.g. 0.5 would mean the sub needs to be half-way through the level).
        /// </summary>
        public readonly float MinDistanceTraveled;

        /// <summary>
        /// The event set won't become active until the round has lasted at least this many seconds.
        /// </summary>
        public readonly float MinMissionTime;

        //the events in this set are delayed if the current EventManager intensity is not between these values
        public readonly float MinIntensity, MaxIntensity;

        /// <summary>
        /// If the event is not allowed at start, it won't become active until the submarine has moved at least 50 meters away from the beginning of the level. Only valid in LocationConnections (levels between locations).
        /// </summary>
        public readonly bool AllowAtStart;

        /// <summary>
        /// Normally an event (such as a monster spawn) triggers a cooldown during which no new events are created. This can be used to ignore the cooldown.
        /// </summary>
        public readonly bool IgnoreCoolDown;

        /// <summary>
        /// Should this event set trigger the event cooldown (during which no new events are created) when it becomes active?
        /// </summary>
        public readonly bool TriggerEventCooldown;

        /// <summary>
        /// Normally events can only trigger if the intensity of the situation is low enough (e.g. you won't get new monster spawns if the submarine is already facing a disaster). This can be used to ignore the intensity.
        /// </summary>
        public readonly bool IgnoreIntensity;

        /// <summary>
        /// The set is applied once per each ruin in the level. Can be used to ensure there's a consistent amount of monster spawns in the ruins in the level regardless of how many there are (and that no ruin monsters spawn if there are no ruins).
        /// </summary>
        public readonly bool PerRuin;

        /// <summary>
        /// The set is applied once per each cave in the level. Can be used to ensure there's a consistent amount of monster spawns in the cave in the level regardless of how many there are (and that no cave monsters spawn if there are no caves).
        /// </summary>
        public readonly bool PerCave;

        /// <summary>
        /// The set is applied once per each wreck in the level. Can be used to ensure there's a consistent amount of monster spawns in the wreck in the level regardless of how many there are (and that no wreck monsters spawn if there are no wreck).
        /// </summary>
        public readonly bool PerWreck;

        /// <summary>
        /// If enabled, this event will not be applied if the level contains hunting grounds.
        /// </summary>
        public readonly bool DisableInHuntingGrounds;

        /// <summary>
        /// If enabled, events from this set can only occur once in the level.
        /// </summary>
        public readonly bool OncePerLevel;

        /// <summary>
        /// Should the event set be delayed if at least half of the crew is away from the submarine? The maximum amount of time the events can get delayed is defined in event manager settings (<see cref="EventManagerSettings.FreezeDurationWhenCrewAway"/>)
        /// </summary>
        public readonly bool DelayWhenCrewAway;

        /// <summary>
        /// Additive sets are important to be aware of when creating custom event sets! If an additive set gets chosen for a level, the game will also select a non-additive one.
        /// This means you can for example configure an additive set that spawns custom monsters (and make it very common if you want the monsters to spawn frequently), which will spawn those custom
        /// monsters in addition to the vanilla monsters spawned by vanilla sets, without you having to add your custom monsters to every single vanilla set.
        /// </summary>
        public readonly bool Additive;
            
        /// <summary>
        /// The commonness of the event set (i.e. how likely it is for this specific set to be chosen).
        /// </summary>
        public readonly float DefaultCommonness;
        public readonly ImmutableDictionary<Identifier, float> OverrideCommonness;

        /// <summary>
        /// If set, the event set can trigger again after this amount of seconds has passed since it last triggered.
        /// </summary>
        public readonly float ResetTime;

        /// <summary>
        /// Used to force an event set based on how many other locations have been discovered before this (used for campaign tutorial event sets).
        /// </summary>
        public readonly int ForceAtDiscoveredNr;

        /// <summary>
        /// Used to force an event set based on how many other outposts have been visited before this (used for campaign tutorial event sets).
        /// </summary>
        public readonly int ForceAtVisitedNr;

        /// <summary>
        /// If enabled, this set can only occur when the campaign tutorial is enabled (generally used for the tutorial events).
        /// </summary>
        public readonly bool CampaignTutorialOnly;

        public readonly struct SubEventPrefab
        {
            public SubEventPrefab(Either<Identifier[], EventPrefab> prefabOrIdentifiers, float? commonness, float? probability, Identifier factionId)
            {
                PrefabOrIdentifier = prefabOrIdentifiers;
                SelfCommonness = commonness;
                SelfProbability = probability;
                Faction = factionId;
            }

            public readonly Either<Identifier[], EventPrefab> PrefabOrIdentifier;
            public IEnumerable<EventPrefab> EventPrefabs
            {
                get
                {
                    if (PrefabOrIdentifier.TryGet(out EventPrefab p))
                    {
                        yield return p;
                    }
                    else
                    {
                        foreach (var id in (Identifier[])PrefabOrIdentifier)
                        {
                            if (EventPrefab.Prefabs.TryGet(id, out EventPrefab prefab))
                            {
                                yield return prefab;
                            }
                        }
                    }
                }
            }

            public readonly float? SelfCommonness;
            public float Commonness => SelfCommonness ?? EventPrefabs.MaxOrNull(p => p.Commonness) ?? 0.0f;

            public readonly float? SelfProbability;
            public float Probability => SelfProbability ?? EventPrefabs.MaxOrNull(p => p.Probability) ?? 0.0f;

            public readonly Identifier Faction;

            public void Deconstruct(out IEnumerable<EventPrefab> eventPrefabs, out float commonness, out float probability)
            {
                eventPrefabs = EventPrefabs;
                commonness = Commonness;
                probability = Probability;
            }

            public IEnumerable<Identifier> GetMissingIdentifiers()
            {
                if (PrefabOrIdentifier.TryCast<Identifier[]>(out var ids))
                {
                    foreach (var id in ids)
                    {
                        if (!EventPrefab.Prefabs.ContainsKey(id))
                        {
                            yield return id;
                        }
                    }
                }
            }        
        }
        public readonly ImmutableArray<SubEventPrefab> EventPrefabs;

        public readonly ImmutableArray<EventSet> ChildSets;

        private static Identifier DetermineIdentifier(EventSet parent, XElement element, RandomEventsFile file)
        {
            Identifier retVal = element.GetAttributeIdentifier("identifier", Identifier.Empty);

            if (retVal.IsEmpty)
            {
                if (parent is null)
                {
                    if (file.ContentPackage is CorePackage)
                    {
                        throw new Exception($"Error in {file.Path}: All root EventSets in a core package must have identifiers");
                    }
                    else
                    {
                        DebugConsole.AddWarning($"{file.Path}: All root EventSets should have an identifier",
                            file.ContentPackage);
                    }
                }

                XElement currElement = element;
                string siblingIndices = "";
                while (currElement.Parent != null)
                {
                    int siblingIndex = currElement.ElementsBeforeSelf().Count();
                    siblingIndices = $"-{siblingIndex}{siblingIndices}";
                    if (parent != null) { break; }
                    currElement = currElement.Parent;
                }

                retVal =
                    ((parent != null
                        ? parent.Identifier.Value
                        : $"{file.ContentPackage.Name}-{file.Path}")
                    + siblingIndices)
                        .ToIdentifier();
            }
            return retVal;
        }

        public EventSet(ContentXElement element, RandomEventsFile file, EventSet parentSet = null)
            : base(file, DetermineIdentifier(parentSet, element, file))
        {
            var eventPrefabs = new List<SubEventPrefab>();
            var childSets = new List<EventSet>();
            var overrideCommonness = new Dictionary<Identifier, float>();

            BiomeIdentifier = element.GetAttributeIdentifier("biome", Barotrauma.Identifier.Empty);
            MinLevelDifficulty = element.GetAttributeFloat("minleveldifficulty", 0);
            MaxLevelDifficulty = Math.Max(element.GetAttributeFloat("maxleveldifficulty", 100), MinLevelDifficulty);

            Additive = element.GetAttributeBool("additive", false);

            string levelTypeStr = element.GetAttributeString("leveltype", parentSet?.LevelType.ToString() ?? "LocationConnection");
            if (!Enum.TryParse(levelTypeStr, true, out LevelType))
            {
                DebugConsole.ThrowError($"Error in event set \"{Identifier}\". \"{levelTypeStr}\" is not a valid level type.",
                    contentPackage: element.ContentPackage);
            }

            Faction = element.GetAttributeIdentifier(nameof(Faction), Identifier.Empty);

            Identifier[] locationTypeStr = element.GetAttributeIdentifierArray("locationtype", null);
            if (locationTypeStr != null)
            {
                LocationTypeIdentifiers = locationTypeStr.ToImmutableArray();
                //if (LocationType.List.Any()) { CheckLocationTypeErrors(); } //TODO: perform validation elsewhere
            }

            MinIntensity = element.GetAttributeFloat("minintensity", 0.0f);
            MaxIntensity = Math.Max(element.GetAttributeFloat("maxintensity", 100.0f), MinIntensity);

            ChooseRandom = element.GetAttributeBool("chooserandom", false);
            eventCount = element.GetAttributeInt("eventcount", 1);
            SubSetCount = element.GetAttributeInt("setcount", 1);
            Exhaustible = element.GetAttributeBool("exhaustible", parentSet?.Exhaustible ?? false);
            MinDistanceTraveled = element.GetAttributeFloat("mindistancetraveled", 0.0f);
            MinMissionTime = element.GetAttributeFloat("minmissiontime", 0.0f);

            AllowAtStart = element.GetAttributeBool("allowatstart", parentSet?.AllowAtStart ?? false);
            PerRuin = element.GetAttributeBool("perruin", false);
            PerCave = element.GetAttributeBool("percave", false);
            PerWreck = element.GetAttributeBool("perwreck", false);
            DisableInHuntingGrounds = element.GetAttributeBool("disableinhuntinggrounds", parentSet?.DisableInHuntingGrounds ?? false);
            IgnoreCoolDown = element.GetAttributeBool("ignorecooldown", parentSet?.IgnoreCoolDown ?? (PerRuin || PerCave || PerWreck));
            IgnoreIntensity = element.GetAttributeBool("ignoreintensity", parentSet?.IgnoreIntensity ?? false);
            DelayWhenCrewAway = element.GetAttributeBool("delaywhencrewaway", parentSet?.DelayWhenCrewAway ?? (!PerRuin && !PerCave && !PerWreck));
            OncePerLevel = element.GetAttributeBool("onceperlevel", element.GetAttributeBool("onceperoutpost", parentSet?.OncePerLevel ?? false));
            TriggerEventCooldown = element.GetAttributeBool("triggereventcooldown", parentSet?.TriggerEventCooldown ?? true);
            IsCampaignSet = element.GetAttributeBool("campaign", LevelType == LevelData.LevelType.Outpost || (parentSet?.IsCampaignSet ?? false));
            ResetTime = element.GetAttributeFloat(nameof(ResetTime), parentSet?.ResetTime ?? 0);
            CampaignTutorialOnly = element.GetAttributeBool(nameof(CampaignTutorialOnly), parentSet?.CampaignTutorialOnly ?? false);

            RequiredLayer = element.GetAttributeIdentifier(nameof(RequiredLayer), Identifier.Empty);

            ForceAtDiscoveredNr = element.GetAttributeInt(nameof(ForceAtDiscoveredNr), -1);
            ForceAtVisitedNr = element.GetAttributeInt(nameof(ForceAtVisitedNr), -1);
            if (ForceAtDiscoveredNr >= 0 && ForceAtVisitedNr >= 0)
            {
                DebugConsole.ThrowError($"Error with event set \"{Identifier}\" - both ForceAtDiscoveredNr and ForceAtVisitedNr are defined, this could lead to unexpected behavior",
                        contentPackage: element.ContentPackage);
            }

            DefaultCommonness = element.GetAttributeFloat("commonness", 1.0f);
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "commonness":
                        DefaultCommonness = subElement.GetAttributeFloat("commonness", DefaultCommonness);
                        foreach (XElement overrideElement in subElement.Elements())
                        {
                            if (overrideElement.NameAsIdentifier() == "override")
                            {
                                Identifier levelType = overrideElement.GetAttributeIdentifier("leveltype", "");
                                if (!overrideCommonness.ContainsKey(levelType))
                                {
                                    overrideCommonness.Add(levelType, overrideElement.GetAttributeFloat("commonness", 0.0f));
                                }
                            }
                        }
                        break;
                    case "eventset":
                        childSets.Add(new EventSet(subElement, file, this));
                        break;
                    case "overrideeventcount":
                        Identifier locationType = subElement.GetAttributeIdentifier("locationtype", "");
                        if (!overrideEventCount.ContainsKey(locationType))
                        {
                            overrideEventCount.Add(locationType, subElement.GetAttributeInt("eventcount", eventCount));
                        }
                        break;
                    default:
                        //an element with just an identifier = reference to an event prefab
                        if (!subElement.HasElements && subElement.Attributes().First().Name.ToString().Equals("identifier", StringComparison.OrdinalIgnoreCase))
                        {
                            Identifier[] identifiers = subElement.GetAttributeIdentifierArray("identifier", Array.Empty<Identifier>());
                            float commonness = subElement.GetAttributeFloat("commonness", -1f);
                            float probability = subElement.GetAttributeFloat("probability", -1f);
                            Identifier factionId = subElement.GetAttributeIdentifier(nameof(Faction), Identifier.Empty);
                            eventPrefabs.Add(new SubEventPrefab(
                                identifiers,
                                commonness >= 0f ? commonness : (float?)null,
                                probability >= 0f ? probability : (float?)null,
                                factionId));
                        }
                        else
                        {
                            var prefab = new EventPrefab(subElement, file, $"{Identifier}-{subElement.ElementsBeforeSelf().Count()}".ToIdentifier());
                            eventPrefabs.Add(new SubEventPrefab(prefab, prefab.Commonness, prefab.Probability, prefab.Faction));
                        }
                        break;
                }
            }

            EventPrefabs = eventPrefabs.ToImmutableArray();
            ChildSets = childSets.ToImmutableArray();
            OverrideCommonness = overrideCommonness.ToImmutableDictionary();

            if ((PerRuin && PerCave) || (PerWreck && PerCave) || (PerRuin && PerWreck))
            {
                DebugConsole.AddWarning($"Error in event set \"{Identifier}\". Only one of the settings {nameof(PerRuin)}, {nameof(PerCave)} or {nameof(PerWreck)} can be enabled at the time.");
            }
        }

        public void CheckLocationTypeErrors()
        {
            if (LocationTypeIdentifiers == null) { return; }
            foreach (Identifier locationTypeId in LocationTypeIdentifiers)
            {
                if (!LocationType.Prefabs.ContainsKey(locationTypeId))
                {
                    DebugConsole.ThrowError($"Error in event set \"{Identifier}\". Location type \"{locationTypeId}\" not found.");
                }
            }
        }

        public float GetCommonness(Level level)
        {
            if (level.GenerationParams?.Identifier is { IsEmpty: false } && 
                OverrideCommonness.TryGetValue(level.GenerationParams.Identifier, out float generationParamsCommonness))
            {
                return generationParamsCommonness;
            }
            else if (level.StartOutpost?.Info.OutpostGenerationParams?.Identifier is { IsEmpty: false } && 
                OverrideCommonness.TryGetValue(level.StartOutpost.Info.OutpostGenerationParams.Identifier, out float startOutpostParamsCommonness))
            {
                return startOutpostParamsCommonness;
            }
            else if (level.EndOutpost?.Info.OutpostGenerationParams?.Identifier is { IsEmpty: false } &&
                OverrideCommonness.TryGetValue(level.EndOutpost.Info.OutpostGenerationParams.Identifier, out float endOutpostParamsCommonness))
            {
                return endOutpostParamsCommonness;
            }
            return DefaultCommonness;
        }

        public int GetEventCount(Level level)
        {
            int finishedEventCount = 0;
            if (level is not null)
            {
                level.LevelData.FinishedEvents.TryGetValue(this, out finishedEventCount);
            }
            if (level.StartLocation == null || !overrideEventCount.TryGetValue(level.StartLocation.Type.Identifier, out int count))
            {
                return eventCount - finishedEventCount;
            }
            return count - finishedEventCount;
        }

        public static List<string> GetDebugStatistics(int simulatedRoundCount = 100, Func<MonsterEvent, bool> filter = null, bool fullLog = false)
        {
            List<string> debugLines = new List<string>();

            foreach (var eventSet in Prefabs)
            {
                List<EventDebugStats> stats = new List<EventDebugStats>();
                for (int i = 0; i < simulatedRoundCount; i++)
                {
                    var newStats = new EventDebugStats(eventSet);
                    CheckEventSet(newStats, eventSet, filter);
                    stats.Add(newStats);
                }
                debugLines.Add($"Event stats ({eventSet.Identifier}): ");
                LogEventStats(stats, debugLines, fullLog);
            }

            return debugLines;

            static void CheckEventSet(EventDebugStats stats, EventSet thisSet, Func<MonsterEvent, bool> filter = null)
            {
                if (thisSet.ChooseRandom)
                {
                    var unusedEvents = thisSet.EventPrefabs.ToList();
                    if (unusedEvents.Any())
                    {
                        for (int i = 0; i < thisSet.eventCount; i++)
                        {
                            var eventPrefab = ToolBox.SelectWeightedRandom(unusedEvents, unusedEvents.Select(e => e.Commonness).ToList(), Rand.RandSync.Unsynced);
                            if (eventPrefab.EventPrefabs.Any(p => p != null))
                            {
                                AddEvents(stats, eventPrefab.EventPrefabs, filter);
                                unusedEvents.Remove(eventPrefab);
                            }
                        }
                    }
                    List<float> values = thisSet.ChildSets
                        .SelectMany(s => s.DefaultCommonness.ToEnumerable().Concat(s.OverrideCommonness.Values))
                        .ToList();
                    EventSet childSet = ToolBox.SelectWeightedRandom(thisSet.ChildSets, values, Rand.RandSync.Unsynced);
                    if (childSet != null)
                    {
                        CheckEventSet(stats, childSet, filter);
                    }
                }
                else
                {
                    foreach (var eventPrefab in thisSet.EventPrefabs)
                    {
                        AddEvents(stats, eventPrefab.EventPrefabs, filter);
                    }
                    foreach (var childSet in thisSet.ChildSets)
                    {
                        CheckEventSet(stats, childSet, filter);
                    }
                }
            }

            static void AddEvents(EventDebugStats stats, IEnumerable<EventPrefab> eventPrefabs, Func<MonsterEvent, bool> filter = null)
                => eventPrefabs.ForEach(p => AddEvent(stats, p, filter));
            
            static void AddEvent(EventDebugStats stats, EventPrefab eventPrefab, Func<MonsterEvent, bool> filter = null)
            {
                if (eventPrefab.EventType == typeof(MonsterEvent) && 
                    eventPrefab.TryCreateInstance(GameMain.GameSession?.EventManager?.RandomSeed ?? 0, out MonsterEvent monsterEvent))
                {
                    if (filter != null && !filter(monsterEvent)) { return; }
                    float spawnProbability = monsterEvent.Prefab?.Probability ?? 0.0f;
                    if (Rand.Value() > spawnProbability) { return; }
                    int count = Rand.Range(monsterEvent.MinAmount, monsterEvent.MaxAmount + 1);
                    if (count <= 0) { return; }
                    Identifier character = monsterEvent.SpeciesName;
                    if (stats.MonsterCounts.TryGetValue(character, out int currentCount))
                    {
                        if (currentCount >= monsterEvent.MaxAmountPerLevel) { return; }
                    }
                    else
                    {
                        stats.MonsterCounts[character] = 0;
                    }
                    stats.MonsterCounts[character] += count;
                    
                    var aiElement = CharacterPrefab.FindBySpeciesName(character)?.ConfigElement?.GetChildElement("ai");
                    if (aiElement != null)
                    {
                        stats.MonsterStrength += aiElement.GetAttributeFloat("combatstrength", 0) * count;
                    }
                }
            }

            static void LogEventStats(List<EventDebugStats> stats, List<string> debugLines, bool fullLog)
            {
                if (stats.Count == 0 || stats.All(s => s.MonsterCounts.Values.Sum() == 0))
                {
                    debugLines.Add("  No monster spawns");
                    debugLines.Add($" ");
                }
                else
                {
                    var allMonsters = new Dictionary<Identifier, int>();
                    foreach (var stat in stats)
                    {
                        foreach (var monster in stat.MonsterCounts)
                        {
                            if (!allMonsters.TryAdd(monster.Key, monster.Value))
                            {
                                allMonsters[monster.Key] += monster.Value;
                            }
                        }
                    }
                    allMonsters = allMonsters.OrderBy(m => m.Key).ToDictionary(m => m.Key, m => m.Value);
                    stats.Sort((s1, s2) => s1.MonsterCounts.Values.Sum().CompareTo(s2.MonsterCounts.Values.Sum()));
                    debugLines.Add($"  Average monster count: {StringFormatter.FormatZeroDecimal((float)stats.Average(s => s.MonsterCounts.Values.Sum()))} (Min: {stats.First().MonsterCounts.Values.Sum()}, Max: {stats.Last().MonsterCounts.Values.Sum()})");
                    debugLines.Add($"     {LogMonsterCounts(allMonsters, divider: stats.Count)}");
                    if (fullLog)
                    {
                        debugLines.Add($"  All samples:");
                        stats.ForEach(s => debugLines.Add($"     {LogMonsterCounts(s.MonsterCounts)}"));
                    }
                    stats.Sort((s1, s2) => s1.MonsterStrength.CompareTo(s2.MonsterStrength));
                    debugLines.Add($"  Average monster strength: {StringFormatter.FormatZeroDecimal(stats.Average(s => s.MonsterStrength))} (Min: {StringFormatter.FormatZeroDecimal(stats.First().MonsterStrength)}, Max: {StringFormatter.FormatZeroDecimal(stats.Last().MonsterStrength)})");
                    debugLines.Add($" ");
                }
            }

            static string LogMonsterCounts(Dictionary<Identifier, int> stats, float divider = 0)
            {
                if (divider > 0)
                {
                    return string.Join("\n     ", stats.Select(mc => mc.Key + " x " + (mc.Value / divider).FormatSingleDecimal()));
                }
                else
                {
                    return string.Join(", ", stats.Select(mc => mc.Key + " x " + mc.Value));
                }      
            }
        }

        public override string ToString()
        {
            return $"{base.ToString()} ({Identifier.Value})";
        }

        public override void Dispose() { }
    }
}
