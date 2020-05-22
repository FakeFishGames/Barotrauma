using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{ 

    class ScriptedEventSet
    {
        internal class EventDebugStats
        {
            public readonly ScriptedEventSet RootSet;
            public readonly Dictionary<string, int> MonsterCounts = new Dictionary<string, int>();

            public EventDebugStats(ScriptedEventSet rootSet)
            {
                RootSet = rootSet;
            }
        }

        public static List<ScriptedEventSet> List
        {
            get;
            private set;
        }

        //0-100
        public readonly float MinLevelDifficulty, MaxLevelDifficulty;
        
        public readonly bool ChooseRandom;

        public readonly float MinDistanceTraveled;
        public readonly float MinMissionTime;

        //the events in this set are delayed if the current EventManager intensity is not between these values
        public readonly float MinIntensity, MaxIntensity;

        public readonly bool AllowAtStart;

        public readonly bool PerRuin;
        public readonly bool PerWreck;

        public readonly Dictionary<string, float> Commonness;

        public readonly List<ScriptedEventPrefab> EventPrefabs;

        public readonly List<ScriptedEventSet> ChildSets;

        public string DebugIdentifier
        {
            get;
            private set;
        } = "";

        private ScriptedEventSet(XElement element, string debugIdentifier)
        {
            DebugIdentifier = element.GetAttributeString("identifier", null) ?? debugIdentifier;
            Commonness = new Dictionary<string, float>();
            EventPrefabs = new List<ScriptedEventPrefab>();
            ChildSets = new List<ScriptedEventSet>();

            MinLevelDifficulty = element.GetAttributeFloat("minleveldifficulty", 0);
            MaxLevelDifficulty = Math.Max(element.GetAttributeFloat("maxleveldifficulty", 100), MinLevelDifficulty);

            MinIntensity = element.GetAttributeFloat("minintensity", 0.0f);
            MaxIntensity = Math.Max(element.GetAttributeFloat("maxintensity", 100.0f), MinIntensity);

            ChooseRandom = element.GetAttributeBool("chooserandom", false);
            MinDistanceTraveled = element.GetAttributeFloat("mindistancetraveled", 0.0f);
            MinMissionTime = element.GetAttributeFloat("minmissiontime", 0.0f);

            AllowAtStart = element.GetAttributeBool("allowatstart", false);
            PerRuin = element.GetAttributeBool("perruin", false);
            PerWreck = element.GetAttributeBool("perwreck", false);

            Commonness[""] = 1.0f;
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
                        ChildSets.Add(new ScriptedEventSet(subElement, this.DebugIdentifier + "-" + ChildSets.Count));
                        break;
                    default:
                        EventPrefabs.Add(new ScriptedEventPrefab(subElement));
                        break;
                }
            }
        }

        public float GetCommonness(Level level)
        {
            string key = level.GenerationParams?.Name ?? "";
            return Commonness.ContainsKey(key) ?
                    Commonness[key] : Commonness[""];
        }

        public static void LoadPrefabs()
        {
            List = new List<ScriptedEventSet>();
            var configFiles = GameMain.Instance.GetFilesOfType(ContentType.RandomEvents);

            if (!configFiles.Any())
            {
                DebugConsole.ThrowError("No config files for random events found in the selected content package");
                return;
            }

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

                int i = 0;
                foreach (XElement element in doc.Root.Elements())
                {
                    if (!element.Name.ToString().Equals("eventset", StringComparison.OrdinalIgnoreCase)) { continue; }
                    List.Add(new ScriptedEventSet(element, i.ToString()));
                    i++;
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
                    ScriptedEventSet selectedSet = List.Where(s => difficulty >= s.MinLevelDifficulty && difficulty <= s.MaxLevelDifficulty).GetRandom();
                    if (selectedSet == null) { continue; }
                    var newStats = new EventDebugStats(selectedSet);
                    CheckEventSet(newStats, selectedSet);
                    stats.Add(newStats);
                }
                LogEventStats(stats, debugLines);
            }

            return debugLines;

            static void CheckEventSet(EventDebugStats stats, ScriptedEventSet thisSet)
            {
                if (thisSet.ChooseRandom)
                {
                    var eventPrefab = ToolBox.SelectWeightedRandom(thisSet.EventPrefabs, thisSet.EventPrefabs.Select(e => e.Commonness).ToList(), Rand.RandSync.Unsynced);
                    if (eventPrefab != null)
                    {
                        AddEvent(stats, eventPrefab);
                    }
                }
                else
                {
                    foreach (var eventPrefab in thisSet.EventPrefabs)
                    {
                        AddEvent(stats, eventPrefab);
                    }
                }
                foreach (var childSet in thisSet.ChildSets)
                {
                    CheckEventSet(stats, childSet);
                }
            }

            static void AddEvent(EventDebugStats stats, ScriptedEventPrefab eventPrefab)
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
