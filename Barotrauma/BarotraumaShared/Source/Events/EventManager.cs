using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    class EventManager
    {
        const float CriticalPriority = 50.0f;

        private List<ScriptedEvent> events;

        public List<ScriptedEvent> Events
        {
            get { return events; }
        }
        
        public EventManager(GameSession session)
        {
            events = new List<ScriptedEvent>();        
        }
        
        public void StartRound(Level level)
        {
            CreateScriptedEvents(level);
            foreach (ScriptedEvent ev in events)
            {
                ev.Init();
            }
        }

        public void EndRound()
        {
            events.Clear();
        }

        private void CreateScriptedEvents(Level level)
        {
            System.Diagnostics.Debug.Assert(events.Count == 0);

            MTRandom rand = new MTRandom(ToolBox.StringToInt(level.Seed));

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Generating events (seed: " + level.Seed + ")", Color.White);
            }

            events.AddRange(ScriptedEvent.GenerateLevelEvents(rand, level));
        }
        
        public void Update(float deltaTime)
        {
            events.RemoveAll(t => t.IsFinished);
            foreach (ScriptedEvent ev in events)
            {
                if (!ev.IsFinished)
                {
                    ev.Update(deltaTime);
                }
            }
        }
    }
}
