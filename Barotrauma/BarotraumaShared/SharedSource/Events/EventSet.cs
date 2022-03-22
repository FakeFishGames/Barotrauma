using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{ 
    class EventSet
    {
        internal class EventDebugStats
        {
            public readonly EventSet RootSet;
            public readonly Dictionary<string, int> MonsterCounts = new Dictionary<string, int>();
            public float MonsterStrength;

            public EventDebugStats(EventSet rootSet)
            {
                RootSet = rootSet;
            }
        }

        public static List<EventSet> List
        {
            get;
            private set;
        }
        
        public static readonly List<EventPrefab> PrefabList = new List<EventPrefab>();
#if CLIENT
        private static readonly Dictionary<string, Sprite> EventSprites = new Dictionary<string, Sprite>();

        public static Sprite GetEventSprite(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) { return null; }

            foreach (var (key, value) in EventSprites)
            {
                if (key.Equals(identifier, StringComparison.OrdinalIgnoreCase)) { return value; }
            }

            return null;
        }
#endif

        public static List<EventPrefab> GetAllEventPrefabs()
        {
            List<EventPrefab> eventPrefabs = new List<EventPrefab>(PrefabList);
            foreach (var eventSet in List)
            {
                eventPrefabs.AddRange(eventSet.EventPrefabs.SelectMany(ep => ep.Prefabs));
                foreach (var childSet in eventSet.ChildSets)
                {
                    eventPrefabs.AddRange(childSet.EventPrefabs.SelectMany(ep => ep.Prefabs));
                }
            }
            return eventPrefabs;
        }

        public static EventPrefab GetEventPrefab(string identifer)
        {
            return GetAllEventPrefabs().Find(prefab => string.Equals(prefab.Identifier, identifer, StringComparison.Ordinal));
        }

        public readonly bool IsCampaignSet;

        //0-100
        public readonly float MinLevelDifficulty, MaxLevelDifficulty;

        public readonly string BiomeIdentifier;

        public readonly LevelData.LevelType LevelType;

        public readonly string[] LocationTypeIdentifiers;
        
        public readonly bool ChooseRandom;

        public readonly int EventCount = 1;

        public readonly float MinDistanceTraveled;
        public readonly float MinMissionTime;

        //the events in this set are delayed if the current EventManager intensity is not between these values
        public readonly float MinIntensity, MaxIntensity;

        public readonly bool AllowAtStart;

        public readonly bool IgnoreCoolDown;

        public readonly bool PerRuin, PerCave, PerWreck;
        public readonly bool DisableInHuntingGrounds;

        public readonly bool OncePerOutpost;

        public readonly bool DelayWhenCrewAway;

        public readonly bool TriggerEventCooldown;

        public readonly bool Additive;

        public readonly Dictionary<string, float> Commonness;

        public struct SubEventPrefab
        {
            public SubEventPrefab(string debugIdentifier, string[] prefabIdentifiers, float? commonness, float? probability)
            {
                EventPrefab tryFindPrefab(string id)
                {
                    var prefab = PrefabList.Find(p => p.Identifier.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (prefab is null)
                    {
                        DebugConsole.ThrowError($"Error in event set \"{debugIdentifier}\" - could not find the event prefab \"{id}\".");
                    }
                    return prefab;
                }

                this.Prefabs = prefabIdentifiers
                    .Select(tryFindPrefab)
                    .Where(p => p != null)
                    .ToImmutableArray();
                this.Commonness = commonness ?? this.Prefabs.Select(p => p.Commonness).MaxOrNull() ?? 0.0f;
                this.Probability = probability ?? this.Prefabs.Select(p => p.Probability).MaxOrNull() ?? 0.0f;
            }

            public SubEventPrefab(EventPrefab prefab, float commonness, float probability)
            {
                Prefabs = prefab.ToEnumerable().ToImmutableArray();
                Commonness = commonness;
                Probability = probability;
            }

            public readonly ImmutableArray<EventPrefab> Prefabs;
            public readonly float Commonness;
            public readonly float Probability;

            public void Deconstruct(out IEnumerable<EventPrefab> prefabs, out float commonness, out float probability)
            {
                prefabs = Prefabs;
                commonness = Commonness;
                probability = Probability;
            }
        }
        
        public readonly List<SubEventPrefab> EventPrefabs;

        public readonly List<EventSet> ChildSets;

        public string DebugIdentifier
        {
            get;
            private set;
        } = "";

        private EventSet(XElement element, string debugIdentifier, EventSet parentSet = null)
        {
            DebugIdentifier = element.GetAttributeString("identifier", null) ?? debugIdentifier;
            Commonness = new Dictionary<string, float>();
            EventPrefabs =  new List<SubEventPrefab>();
            ChildSets = new List<EventSet>();

            BiomeIdentifier = element.GetAttributeString("biome", string.Empty);
            MinLevelDifficulty = element.GetAttributeFloat("minleveldifficulty", 0);
            MaxLevelDifficulty = Math.Max(element.GetAttributeFloat("maxleveldifficulty", 100), MinLevelDifficulty);

            Additive = element.GetAttributeBool("additive", false);

            string levelTypeStr = element.GetAttributeString("leveltype", "LocationConnection");
            if (!Enum.TryParse(levelTypeStr, true, out LevelType))
            {
                DebugConsole.ThrowError($"Error in event set \"{debugIdentifier}\". \"{levelTypeStr}\" is not a valid level type.");
            }

            string[] locationTypeStr = element.GetAttributeStringArray("locationtype", null);
            if (locationTypeStr != null)
            {
                LocationTypeIdentifiers = locationTypeStr;
                if (LocationType.List.Any()) { CheckLocationTypeErrors(); }
            }

            MinIntensity = element.GetAttributeFloat("minintensity", 0.0f);
            MaxIntensity = Math.Max(element.GetAttributeFloat("maxintensity", 100.0f), MinIntensity);

            ChooseRandom = element.GetAttributeBool("chooserandom", false);
            EventCount = element.GetAttributeInt("eventcount", 1);
            MinDistanceTraveled = element.GetAttributeFloat("mindistancetraveled", 0.0f);
            MinMissionTime = element.GetAttributeFloat("minmissiontime", 0.0f);

            AllowAtStart = element.GetAttributeBool("allowatstart", false);
            PerRuin = element.GetAttributeBool("perruin", false);
            PerCave = element.GetAttributeBool("percave", false);
            PerWreck = element.GetAttributeBool("perwreck", false);
            DisableInHuntingGrounds = element.GetAttributeBool("disableinhuntinggrounds", false);
            IgnoreCoolDown = element.GetAttributeBool("ignorecooldown", parentSet?.IgnoreCoolDown ?? (PerRuin || PerCave || PerWreck));
            DelayWhenCrewAway = element.GetAttributeBool("delaywhencrewaway", !PerRuin && !PerCave && !PerWreck);
            OncePerOutpost = element.GetAttributeBool("onceperoutpost", false);
            TriggerEventCooldown = element.GetAttributeBool("triggereventcooldown", true);
            IsCampaignSet = element.GetAttributeBool("campaign", LevelType == LevelData.LevelType.Outpost || (parentSet?.IsCampaignSet ?? false));

            Commonness[""] = element.GetAttributeFloat("commonness", 1.0f);
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "commonness":
                        Commonness[""] = subElement.GetAttributeFloat("commonness", 0.0f);
                        foreach (XElement overrideElement in subElement.Elements())
                        {
                            if (overrideElement.Name.ToString().Equals("override", StringComparison.OrdinalIgnoreCase))
                            {
                                string levelType = overrideElement.GetAttributeString("leveltype", "").ToLowerInvariant();
                                if (!Commonness.ContainsKey(levelType))
                                {
                                    Commonness.Add(levelType, overrideElement.GetAttributeFloat("commonness", 0.0f));
                                }
                            }
                        }
                        break;
                    case "eventset":
                        ChildSets.Add(new EventSet(subElement, this.DebugIdentifier + "-" + ChildSets.Count, this));
                        break;
                    default:
                        //an element with just an identifier = reference to an event prefab
                        if (!subElement.HasElements && subElement.Attributes().First().Name.ToString().Equals("identifier", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] identifiers = subElement.GetAttributeStringArray("identifier", Array.Empty<string>());
                        
                            float commonness = subElement.GetAttributeFloat("commonness", -1f);
                            float probability = subElement.GetAttributeFloat("probability", -1f);
                            EventPrefabs.Add(new SubEventPrefab(
                                debugIdentifier,
                                identifiers,
                                commonness >= 0f ? commonness : (float?)null,
                                probability >= 0f ? probability : (float?)null));
                        }
                        else
                        {
                            var prefab = new EventPrefab(subElement);
                            EventPrefabs.Add(new SubEventPrefab(prefab, prefab.Commonness, prefab.Probability));
                        }
                        break;
                }
            }
        }

        public void CheckLocationTypeErrors()
        {
            if (LocationTypeIdentifiers == null) { return; }
            foreach (string locationTypeId in LocationTypeIdentifiers)
            {
                if (!LocationType.List.Any(lt => lt.Identifier.Equals(locationTypeId, StringComparison.OrdinalIgnoreCase)))
                {
                    DebugConsole.ThrowError($"Error in event set \"{DebugIdentifier}\". Location type \"{locationTypeId}\" not found.");
                }
            }
        }

        public float GetCommonness(Level level)
        {
            string key = level.GenerationParams?.Identifier ?? "";
            return Commonness.ContainsKey(key) ? Commonness[key] : Commonness[""];
        }

        public static void LoadPrefabs()
        {
#if CLIENT
            EventSprites.ForEach(pair => pair.Value?.Remove());
            EventSprites.Clear();
#endif
            List = new List<EventSet>();
            var configFiles = GameMain.Instance.GetFilesOfType(ContentType.RandomEvents);

            if (!configFiles.Any())
            {
                DebugConsole.ThrowError("No config files for random events found in the selected content package");
                return;
            }

            List<XElement> configElements = new List<XElement>();
            Dictionary<XElement, string> filePaths = new Dictionary<XElement, string>();

            foreach (ContentFile configFile in configFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile.Path);
                if (doc == null) { continue; }

                var mainElement = doc.Root.IsOverride() ? doc.Root.FirstElement() : doc.Root;
                if (doc.Root.IsOverride())
                {
                    DebugConsole.NewMessage($"Overriding all random events using the file {configFile.Path}", Color.Yellow);
                    List.Clear();
                }

                foreach (XElement element in doc.Root.Elements())
                {
                    configElements.Add(element);
                    filePaths[element] = configFile.Path;
                }
            }

            //load event prefabs first so we can link to them when loading event sets
            foreach (XElement element in configElements)
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "eventprefabs":                        
                        foreach (var subElement in element.Elements())
                        {
                            // Warn if an event prefab has no identifier as this would make it impossible to refer to
                            if (!element.GetAttributeBool("suppresswarnings", false) && string.IsNullOrWhiteSpace(subElement.GetAttributeString("identifier", string.Empty)))
                            {
                                DebugConsole.AddWarning($"An event prefab {subElement.Name} in {filePaths[element]} is missing an identifier.");
                            }

                            PrefabList.Add(new EventPrefab(subElement));
                        }
                        break;
                    case "eventsprites":
#if CLIENT
                        foreach (var subElement in element.Elements())
                        {
                            string identifier = subElement.GetAttributeString("identifier", string.Empty);

                            if (EventSprites.ContainsKey(identifier))
                            {
                                EventSprites[identifier]?.Remove();
                                EventSprites[identifier] = new Sprite(subElement);
                                continue;
                            }
                            else
                            {
                                EventSprites.Add(identifier, new Sprite(subElement));
                            }
                        }
#endif
                        break;                        
                }
            }

            int i = 0;
            foreach (XElement element in configElements)
            {
                switch (element.Name.ToString().ToLowerInvariant())
                {
                    case "eventset":
                        List.Add(new EventSet(element, i.ToString()));
                        i++;
                        break;
                }
            }
        }

        public static List<string> GetDebugStatistics(int simulatedRoundCount = 100, Func<MonsterEvent, bool> filter = null, bool fullLog = false)
        {
            List<string> debugLines = new List<string>();

            foreach (var eventSet in List)
            {
                List<EventDebugStats> stats = new List<EventDebugStats>();
                for (int i = 0; i < simulatedRoundCount; i++)
                {
                    var newStats = new EventDebugStats(eventSet);
                    CheckEventSet(newStats, eventSet, filter);
                    stats.Add(newStats);
                }
                debugLines.Add($"Event stats ({eventSet.DebugIdentifier}): ");
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
                        for (int i = 0; i < thisSet.EventCount; i++)
                        {
                            var eventPrefab = ToolBox.SelectWeightedRandom(unusedEvents, unusedEvents.Select(e => e.Commonness).ToList());
                            if (eventPrefab.Prefabs.Any(p => p != null))
                            {
                                AddEvents(stats, eventPrefab.Prefabs, filter);
                                unusedEvents.Remove(eventPrefab);
                            }
                        }
                    }
                    List<float> values = thisSet.ChildSets.SelectMany(s => s.Commonness.Values).ToList();
                    EventSet childSet = ToolBox.SelectWeightedRandom(thisSet.ChildSets, values);
                    if (childSet != null)
                    {
                        CheckEventSet(stats, childSet, filter);
                    }
                }
                else
                {
                    foreach (var eventPrefab in thisSet.EventPrefabs)
                    {
                        AddEvents(stats, eventPrefab.Prefabs, filter);
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
                if (eventPrefab.EventType == typeof(MonsterEvent) && eventPrefab.TryCreateInstance(out MonsterEvent monsterEvent))
                {
                    if (filter != null && !filter(monsterEvent)) { return; }
                    float spawnProbability = monsterEvent.Prefab.Probability;
                    if (Rand.Value() > spawnProbability) { return; }
                    int count = Rand.Range(monsterEvent.MinAmount, monsterEvent.MaxAmount + 1);
                    if (count <= 0) { return; }
                    string character = monsterEvent.speciesName;
                    if (stats.MonsterCounts.TryGetValue(character, out int currentCount))
                    {
                        if (currentCount >= monsterEvent.MaxAmountPerLevel) { return; }
                    }
                    else
                    {
                        stats.MonsterCounts[character] = 0;
                    }
                    stats.MonsterCounts[character] += count;
                    
                    var aiElement = CharacterPrefab.FindBySpeciesName(character)?.XDocument?.Root?.GetChildElement("ai");
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
                    var allMonsters = new Dictionary<string, int>();
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

            static string LogMonsterCounts(Dictionary<string, int> stats, float divider = 0)
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
    }
}
