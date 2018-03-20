using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma
{
    class ScriptedEvent
    {        
        protected bool isFinished;

        private readonly ScriptedEventPrefab prefab;

        public string Name
        {
            get { return prefab.Name; }
        }

        /*public string Description
        {
            get { return prefab.Description; }
        }*/

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
            return "ScriptedEvent (" + prefab.Name + ")";
        }

        public virtual Vector2 DebugDrawPos
        {
            get
            {
                return Vector2.Zero;
            }
        }

        public virtual string DebugDrawText
        {
            get
            {
                return "";
            }
        }

        public ScriptedEvent(ScriptedEventPrefab prefab)
        {
            this.prefab = prefab;
        }

        public virtual void Init(bool affectSubImmediately)
        {
        }

        public virtual void Update(float deltaTime)
        {
        }

        public virtual void Finished()
        {
            isFinished = true;
        }
        
        public virtual bool CanAffectSubImmediately(Level level)
        {
            return true;
        }

        /*public static List<ScriptedEvent> GenerateInitialEvents(Random random, Level level)
        {
            if (ScriptedEventPrefab.List == null)
            {
                ScriptedEventPrefab.LoadPrefabs();
            }

            List<ScriptedEvent> events = new List<ScriptedEvent>();
            foreach (ScriptedEventPrefab scriptedEvent in ScriptedEventPrefab.List)
            {
                int minCount = scriptedEvent.MinEventCount.ContainsKey(level.GenerationParams.Name) ? 
                    scriptedEvent.MinEventCount[level.GenerationParams.Name] : scriptedEvent.MinEventCount[""];
                int maxCount = scriptedEvent.MaxEventCount.ContainsKey(level.GenerationParams.Name) ?
                    scriptedEvent.MaxEventCount[level.GenerationParams.Name] : scriptedEvent.MaxEventCount[""];

                minCount = Math.Min(minCount, maxCount);
                int count = random.Next(maxCount - minCount) + minCount;
                for (int i = 0; i < count; i++)
                {
                    ScriptedEvent eventInstance = scriptedEvent.CreateInstance();
                    events.Add(eventInstance);
                }
            }

            return events;
        }*/
    }
}
