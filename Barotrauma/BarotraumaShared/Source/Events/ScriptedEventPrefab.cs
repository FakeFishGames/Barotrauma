using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class ScriptedEventPrefab
    {
        /*public static List<ScriptedEventPrefab> List
        {
            get;
            private set;
        }*/

        public readonly XElement ConfigElement;

        public readonly string Name;

        private readonly Type eventType;
        
        //key = level type, "" means any level type
        /*public readonly Dictionary<string, int> MinEventCount;
        public readonly Dictionary<string, int> MaxEventCount;*/
        
        //used when randomizing which event to create mid-round
        //public readonly Dictionary<string, float> MidRoundCommonness;
        
        public readonly string MusicType;

        public ScriptedEventPrefab(XElement element)
        {
            ConfigElement = element;

            Name = element.GetAttributeString("name", "");
            
            MusicType = element.GetAttributeString("musictype", "default");

            /*MidRoundCommonness = new Dictionary<string, float>();
            MidRoundCommonness.Add("", 0);
            MinEventCount = new Dictionary<string, int>();
            MinEventCount.Add("", 0);
            MaxEventCount = new Dictionary<string, int>();
            MaxEventCount.Add("", 0);
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "initialcount":
                        MinEventCount[""] = subElement.GetAttributeInt("min", 0);
                        MaxEventCount[""] = subElement.GetAttributeInt("max", 0);

                        foreach (XElement overrideElement in subElement.Elements())
                        {
                            if (overrideElement.Name.ToString().ToLowerInvariant() == "override")
                            {
                                string levelType = overrideElement.GetAttributeString("leveltype", "");
                                if (!MinEventCount.ContainsKey(levelType))
                                {
                                    MinEventCount.Add(levelType, overrideElement.GetAttributeInt("min", 0));
                                    MaxEventCount.Add(levelType, overrideElement.GetAttributeInt("max", 0));
                                }
                            }
                        }
                        break;
                    case "midround":
                        MidRoundCommonness[""] = subElement.GetAttributeFloat("commonness", 0.0f);
                        foreach (XElement overrideElement in subElement.Elements())
                        {
                            if (overrideElement.Name.ToString().ToLowerInvariant() == "override")
                            {
                                string levelType = overrideElement.GetAttributeString("leveltype", "");
                                if (!MidRoundCommonness.ContainsKey(levelType))
                                {
                                    MidRoundCommonness.Add(levelType, overrideElement.GetAttributeFloat("commonness", 0.0f));
                                }
                            }
                        }
                        break;
                }
            }*/
            
            try
            {
                eventType = Type.GetType("Barotrauma." + ConfigElement.Name, true, true);
                if (eventType == null)
                {
                    DebugConsole.ThrowError("Could not find an event class of the type \"" + ConfigElement.Name + "\".");
                }
            }
            catch
            {
                DebugConsole.ThrowError("Could not find an event class of the type \"" + ConfigElement.Name + "\".");
            }
        }

        public ScriptedEvent CreateInstance()
        {
            ConstructorInfo constructor = eventType.GetConstructor(new[] { typeof(ScriptedEventPrefab) });
            object instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this });
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
            }

            return (ScriptedEvent)instance;
        }

        /*public static void LoadPrefabs()
        {
            List = new List<ScriptedEventPrefab>();
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
                    List.Add(new ScriptedEventPrefab(element));
                }
            }
        }*/
        
        /*public float GetMidRoundCommonness(Level level)
        {
            return MidRoundCommonness.ContainsKey(level.GenerationParams.Name) ?
                    MidRoundCommonness[level.GenerationParams.Name] : MidRoundCommonness[""];
        }*/
    }
}
