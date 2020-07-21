using System;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class EventPrefab
    {
        public readonly XElement ConfigElement;    
        public readonly Type EventType;      
        public readonly string MusicType;
        public readonly float SpawnProbability;
        public float Commonness;
        public string Identifier;

        public EventPrefab(XElement element)
        {
            ConfigElement = element;
         
            MusicType = element.GetAttributeString("musictype", "default");

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

            Identifier = ConfigElement.GetAttributeString("identifier", string.Empty);
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            SpawnProbability = Math.Clamp(element.GetAttributeFloat("spawnprobability", 1.0f), 0, 1);
        }

        public Event CreateInstance()
        {
            ConstructorInfo constructor = EventType.GetConstructor(new[] { typeof(EventPrefab) });
            object instance = null;
            try
            {
                instance = constructor.Invoke(new object[] { this });
            }
            catch (Exception ex)
            {
                DebugConsole.ThrowError(ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString());
            }

            return (Event)instance;
        }
    }
}
