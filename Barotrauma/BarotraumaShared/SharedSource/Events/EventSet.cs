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
                                string levelType = overrideElement.GetAttributeString("leveltype", "");
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
                                commonness>=0f ? commonness : (float?)null,
                                probability>=0f ? probability : (float?)null));
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

        public static List<string> GetDebugStatistics(int simulatedRoundCount = 100)
        {
            List<string> debugLines = new List<string>();

            foreach (var eventSet in List)
            {
                List<EventDebugStats> stats = new List<EventDebugStats>();
                for (int i = 0; i < simulatedRoundCount; i++)
                {
                    var newStats = new EventDebugStats(eventSet);
                    CheckEventSet(newStats, eventSet);
                    stats.Add(newStats);
                }
                debugLines.Add($"Event stats ({eventSet.DebugIdentifier}): ");
                LogEventStats(stats, debugLines);
            }

            for (int difficulty = 0; difficulty <= 100; difficulty += 10)
            {
                debugLines.Add($"Event stats on difficulty level {difficulty}: ");
                List<EventDebugStats> stats = new List<EventDebugStats>();
                for (int i = 0; i < simulatedRoundCount; i++)
                {
                    EventSet selectedSet = List.Where(s => difficulty >= s.MinLevelDifficulty && difficulty <= s.MaxLevelDifficulty).GetRandom();
                    if (selectedSet == null) { continue; }
                    var newStats = new EventDebugStats(selectedSet);
                    CheckEventSet(newStats, selectedSet);
                    stats.Add(newStats);
                }
                LogEventStats(stats, debugLines);
            }

            return debugLines;

            static void CheckEventSet(EventDebugStats stats, EventSet thisSet)
            {
                if (thisSet.ChooseRandom)
                {
                    var unusedEvents = thisSet.EventPrefabs.ToList();
                    for (int i = 0; i < thisSet.EventCount; i++)
                    {
                        var eventPrefab = ToolBox.SelectWeightedRandom(unusedEvents, unusedEvents.Select(e => e.Commonness).ToList(), Rand.RandSync.Unsynced);
                        if (eventPrefab.Prefabs.Any(p => p != null))
                        {
                            AddEvents(stats, eventPrefab.Prefabs);
                            unusedEvents.Remove(eventPrefab);
                        }
                    }
                }
                else
                {
                    foreach (var eventPrefab in thisSet.EventPrefabs)
                    {
                        AddEvents(stats, eventPrefab.Prefabs);
                    }
                }
                foreach (var childSet in thisSet.ChildSets)
                {
                    CheckEventSet(stats, childSet);
                }
            }

            static void AddEvents(EventDebugStats stats, IEnumerable<EventPrefab> eventPrefabs)
                => eventPrefabs.ForEach(p => AddEvent(stats, p));
            
            static void AddEvent(EventDebugStats stats, EventPrefab eventPrefab)
            {
                if (eventPrefab.EventType == typeof(MonsterEvent))
                {
                    float spawnProbability = eventPrefab.ConfigElement.GetAttributeFloat("spawnprobability", 1.0f);
                    if (Rand.Value(Rand.RandSync.Server) > spawnProbability)
                    {
                        return;
                    }

                    string character = eventPrefab.ConfigElement.GetAttributeString("characterfile", "");
                    System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(character));
                    int amount = eventPrefab.ConfigElement.GetAttributeInt("amount", 0);
                    int minAmount = eventPrefab.ConfigElement.GetAttributeInt("minamount", amount);
                    int maxAmount = eventPrefab.ConfigElement.GetAttributeInt("maxamount", amount);

                    int count = Rand.Range(minAmount, maxAmount + 1);
                    if (count <= 0) { return; }

                    if (!stats.MonsterCounts.ContainsKey(character)) { stats.MonsterCounts[character] = 0; }
                    stats.MonsterCounts[character] += count;
                }
            }

            static void LogEventStats(List<EventDebugStats> stats, List<string> debugLines)
            {
                if (stats.Count == 0 || stats.All(s => s.MonsterCounts.Values.Sum() == 0))
                {
                    debugLines.Add("  No monster spawns");
                    debugLines.Add($" ");
                }
                else
                {
                    stats.Sort((s1, s2) => { return s1.MonsterCounts.Values.Sum().CompareTo(s2.MonsterCounts.Values.Sum()); });

                    EventDebugStats minStats = stats.First();
                    EventDebugStats maxStats = stats.First();
                    debugLines.Add($"  Minimum monster spawns: {stats.First().MonsterCounts.Values.Sum()}");
                    debugLines.Add($"     {LogMonsterCounts(stats.First())}");
                    debugLines.Add($"  Median monster spawns: {stats[stats.Count / 2].MonsterCounts.Values.Sum()}");
                    debugLines.Add($"     {LogMonsterCounts(stats[stats.Count / 2])}");
                    debugLines.Add($"  Maximum monster spawns: {stats.Last().MonsterCounts.Values.Sum()}");
                    debugLines.Add($"     {LogMonsterCounts(stats.Last())}");
                    debugLines.Add($" ");
                }
            }

            static string LogMonsterCounts(EventDebugStats stats)
            {
                return string.Join(", ", stats.MonsterCounts.Select(mc => mc.Key + " x " + mc.Value));
            }
        }
    }
}
