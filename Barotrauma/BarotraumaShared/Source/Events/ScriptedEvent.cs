using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class ScriptedEvent
    {
        private static List<ScriptedEvent> prefabs;

        protected readonly string name;
        protected readonly string description;

        private readonly int minEventCount, maxEventCount;

        protected bool isFinished;

        private readonly XElement configElement;

        private readonly Dictionary<string, int> overrideMinEventCount;
        private readonly Dictionary<string, int> overrideMaxEventCount;

        public string Name
        {
            get { return name; }
        }

        public string Description
        {
            get { return description; }
        }
        
        public string MusicType
        {
            get;
            set;
        }

        public virtual bool IsActive
        {
            get { return true; }
        }
        
        public bool IsFinished
        {
            get { return isFinished; }
        }
        
        public override string ToString()
        {
            return "ScriptedEvent (" + name + ")";
        }

        protected ScriptedEvent(XElement element)
        {
            configElement = element;

            name = element.GetAttributeString("name", "");
            description = element.GetAttributeString("description", "");
            
            minEventCount = element.GetAttributeInt("mineventcount", 0);
            maxEventCount = element.GetAttributeInt("maxeventcount", 0);

            MusicType = element.GetAttributeString("musictype", "default");

            overrideMinEventCount = new Dictionary<string, int>();
            overrideMaxEventCount = new Dictionary<string, int>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "overrideeventcount":
                        string levelType = subElement.GetAttributeString("leveltype", "");
                        if (!overrideMinEventCount.ContainsKey(levelType))
                        {
                            overrideMinEventCount.Add(levelType, subElement.GetAttributeInt("min", 0));
                            overrideMaxEventCount.Add(levelType, subElement.GetAttributeInt("max", 0));
                        }
                        break;
                }
            }
        }

        public virtual void Init()
        {
            isFinished = false;
        }

        public virtual void Update(float deltaTime)
        {
        }

        public virtual void Finished()
        {
            isFinished = true;
        }


        private static void LoadPrefabs()
        {
            prefabs = new List<ScriptedEvent>();
            var configFiles = GameMain.Config.SelectedContentPackage.GetFilesOfType(ContentType.RandomEvents);

            if (configFiles.Count == 0)
            {
                DebugConsole.ThrowError("No config files for random events found in the selected content package");
                return;
            }

            foreach (string configFile in configFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(configFile);
                if (doc == null) continue;

                foreach (XElement element in doc.Root.Elements())
                {
                    prefabs.Add(new ScriptedEvent(element));
                }
            }
        }

        public static List<ScriptedEvent> GenerateLevelEvents(Random random, Level level)
        {
            if (prefabs == null)
            {
                LoadPrefabs();
            }

            List<ScriptedEvent> events = new List<ScriptedEvent>();
            foreach (ScriptedEvent scriptedEvent in prefabs)
            {
                int minCount = scriptedEvent.overrideMinEventCount.ContainsKey(level.GenerationParams.Name) ? 
                    scriptedEvent.overrideMinEventCount[level.GenerationParams.Name] : scriptedEvent.minEventCount;
                int maxCount = scriptedEvent.overrideMaxEventCount.ContainsKey(level.GenerationParams.Name) ?
                    scriptedEvent.overrideMaxEventCount[level.GenerationParams.Name] : scriptedEvent.maxEventCount;

                minCount = Math.Min(minCount, maxCount);

                int count = random.Next(maxCount - minCount) + minCount;

                for (int i = 0; i<count; i++)
                {
                    Type t;

                    try
                    {
                        t = Type.GetType("Barotrauma." + scriptedEvent.configElement.Name, true, true);
                        if (t == null)
                        {
                            DebugConsole.ThrowError("Could not find an event class of the type \"" + scriptedEvent.configElement.Name + "\".");
                            continue;
                        }
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Could not find an event class of the type \"" + scriptedEvent.configElement.Name + "\".");
                        continue;
                    }

                    ConstructorInfo constructor = t.GetConstructor(new[] { typeof(XElement) });
                    object instance = null;
                    try
                    {
                        instance = constructor.Invoke(new object[] { scriptedEvent.configElement });
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
                    }

                    events.Add((ScriptedEvent)instance);
                }
            }

            return events;
        }


        /*public static ScriptedEvent Load(ScriptedEvent scriptedEvent)
        {
            if (prefabs == null)
            {
                LoadPrefabs();
            }

            if (prefabs.Count == 0) return null;

            int eventCount = prefabs.Count;
            float[] eventProbability = new float[eventCount];
            float probabilitySum = 0.0f;

            int i = 0;
            foreach (ScriptedEvent scriptedEvent in prefabs)
            {
                eventProbability[i] = scriptedEvent.commonness;
                if (level != null)
                {
                    scriptedEvent.OverrideCommonness.TryGetValue(level.GenerationParams.Name, out eventProbability[i]);
                }
                probabilitySum += eventProbability[i];
                i++;
            }

            float randomNumber = (float)rand.NextDouble() * probabilitySum;

            i = 0;
            foreach (ScriptedEvent scriptedEvent in prefabs)
            {
                if (randomNumber <= eventProbability[i])
                {
                    Type t;

                    try
                    {
                        t = Type.GetType("Barotrauma." + scriptedEvent.configElement.Name, true, true);
                        if (t == null)
                        {
                            DebugConsole.ThrowError("Could not find an event class of the type \"" + scriptedEvent.configElement.Name + "\".");
                            continue;
                        }
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Could not find an event class of the type \"" + scriptedEvent.configElement.Name + "\".");
                        continue;
                    }

                    ConstructorInfo constructor = t.GetConstructor(new[] { typeof(XElement) });
                    object instance = null;
                    try
                    {
                        instance = constructor.Invoke(new object[] { scriptedEvent.configElement });
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
                    }

                    return (ScriptedEvent)instance;
                }

                randomNumber -= eventProbability[i];
                i++;
            }

            return null;
        }*/
    }
}
