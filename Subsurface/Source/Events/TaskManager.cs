using System.Collections.Generic;
using System;
using System.Linq;

namespace Barotrauma
{
    class TaskManager
    {
        const float CriticalPriority = 50.0f;

        private List<Task> tasks;

        public List<Task> Tasks
        {
            get { return tasks; }
        }

        public bool CriticalTasks
        {
            get
            {
                return tasks.Any(task => task.Priority >= CriticalPriority);
            }
        }

        public TaskManager(GameSession session)
        {
            tasks = new List<Task>();        
        }

        public void AddTask(Task newTask)
        {
            if (tasks.Contains(newTask)) return;
            
            tasks.Add(newTask);
        }

        public void StartShift(Level level)
        {
            CreateScriptedEvents(level);
        }


        public void EndShift()
        {
            tasks.Clear();
        }

        private void CreateScriptedEvents(Level level)
        {
            MTRandom rand = new MTRandom(ToolBox.StringToInt(level.Seed));

            float totalDifficulty = level.Difficulty;

            int tries = 0;
            while (tries < 5)
            {
                ScriptedEvent scriptedEvent = ScriptedEvent.LoadRandom(rand);
                if (scriptedEvent==null || scriptedEvent.Difficulty > totalDifficulty)
                {
                    tries++;
                    continue;
                }
                DebugConsole.Log("Created scripted event " + scriptedEvent.ToString());

                AddTask(new ScriptedTask(scriptedEvent));
                totalDifficulty -= scriptedEvent.Difficulty;
                tries = 0;
            } 

        }
        
        public void Update(float deltaTime)
        {
            foreach (Task task in tasks)
            {
                if (!task.IsFinished)
                {             
                    task.Update(deltaTime);
                }
            }

            tasks.RemoveAll(t => t.IsFinished);
        }

    }
}
