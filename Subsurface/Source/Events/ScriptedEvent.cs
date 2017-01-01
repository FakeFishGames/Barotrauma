using System;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class ScriptedEvent
    {
        protected string name;
        protected string description;

        protected int commonness;
        protected int difficulty;

        protected bool isFinished;
        
        public string Name
        {
            get { return name; }
        }

        public string Description
        {
            get { return description; }
        }

        public int Commonness
        {
            get { return commonness; }
        }

        public string MusicType
        {
            get;
            set;
        }
        
        public bool IsFinished
        {
            get { return isFinished; }
        }

        public int Difficulty
        {
            get { return difficulty; }
        }

        public override string ToString()
        {
            return "ScriptedEvent ("+name+")";
        }

        public ScriptedEvent(XElement element)
        {
            name = ToolBox.GetAttributeString(element, "name", "");
            description = ToolBox.GetAttributeString(element, "description", "");

            difficulty = ToolBox.GetAttributeInt(element, "difficulty", 1);
            commonness = ToolBox.GetAttributeInt(element, "commonness", 1);

            MusicType = ToolBox.GetAttributeString(element, "musictype", "default");
        }


        public static ScriptedEvent LoadRandom(Random rand)
        {
            var configFiles = GameMain.Config.SelectedContentPackage.GetFilesOfType(ContentType.RandomEvents);

            if (!configFiles.Any())
            {
                DebugConsole.ThrowError("No config files for random events found in the selected content package");
                return null;
            }

            string configFile = configFiles[0];

            XDocument doc = ToolBox.TryLoadXml(configFile);
            if (doc == null) return null;
            
            int eventCount = doc.Root.Elements().Count();
            //int[] commonness = new int[eventCount];
            float[] eventProbability = new float[eventCount];

            float probabilitySum = 0.0f;

            int i = 0;
            foreach (XElement element in doc.Root.Elements())
            {
                eventProbability[i] = ToolBox.GetAttributeInt(element, "commonness", 1);

                //if the event has been previously selected, it's less likely to be selected now
                //int previousEventIndex = previousEvents.FindIndex(x => x == i);
                //if (previousEventIndex >= 0)
                //{
                //    //how many shifts ago was the event last selected
                //    int eventDist = eventCount - previousEventIndex;

                //    float weighting = (1.0f / eventDist) * PreviouslyUsedWeight;

                //    eventProbability[i] *= weighting;
                //}

                probabilitySum += eventProbability[i];

                i++;
            }

            float randomNumber = (float)rand.NextDouble() * probabilitySum;

            i = 0;
            foreach (XElement element in doc.Root.Elements())
            {
                if (randomNumber <= eventProbability[i])
                {
                    Type t;
                    string type = element.Name.ToString();

                    try
                    {
                        t = Type.GetType("Barotrauma." + type, true, true);
                        if (t == null)
                        {
                            DebugConsole.ThrowError("Error in " + configFile + "! Could not find an event class of the type \"" + type + "\".");
                            continue;
                        }
                    }
                    catch
                    {
                        DebugConsole.ThrowError("Error in " + configFile + "! Could not find an event class of the type \"" + type + "\".");
                        continue;
                    }

                    ConstructorInfo constructor = t.GetConstructor(new[] { typeof(XElement) });
                    object instance = constructor.Invoke(new object[] { element });

                    //previousEvents.Add(i);

                    return (ScriptedEvent)instance;
                }

                randomNumber -= eventProbability[i];
                i++;
            }

            return null;
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
            //EventManager.EventFinished(this);
        }


    }
}
