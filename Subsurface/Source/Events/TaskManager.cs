using System.Collections.Generic;
using System;
using System.Linq;

namespace Barotrauma
{
    class TaskManager
    {
        const float CriticalPriority = 50.0f;

        private List<Task> tasks;

        //private GUIListBox taskListBox;

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

            //taskListBox = new GUIListBox(new Rectangle(Game1.GraphicsWidth - 250, 50, 250, 500), Color.Transparent);
            //taskListBox.ScrollBarEnabled = false;
            //taskListBox.Padding = GUI.style.smallPadding;           
        }

        public void AddTask(Task newTask)
        {
            if (tasks.Contains(newTask)) return;
            
            tasks.Add(newTask);
        }

        public void StartShift(Level level)
        {
            CreateScriptedEvents(level);

            //taskListBox.ClearChildren();
        }


        public void EndShift()
        {
            //taskListBox.ClearChildren();
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

                AddTask(new ScriptedTask(scriptedEvent));
                totalDifficulty -= scriptedEvent.Difficulty;
                tries = 0;
            } 

        }

        public void TaskStarted(Task task)
        {
            //Color color = Color.Red;
            //int width = 250;
            //if (task.Priority < 30.0f)
            //{
            //    width = 200;
            //    color = Color.Yellow;
            //}
            //else if (task.Priority < 60)
            //{
            //    width = 220;
            //    color = Color.Orange;
            //}

            //GUIFrame frame = new GUIFrame(new Rectangle(0,0,width,40), Color.Transparent, Alignment.Right, null, taskListBox);
            //frame.UserData = task;
            //frame.Padding = new Vector4(0.0f, 5.0f, 5.0f, 5.0f);

            //GUIFrame colorFrame = new GUIFrame(new Rectangle(0, 0, 0, 0), color * 0.5f, Alignment.Right, null, frame);
            //GUITextBlock textBlock = new GUITextBlock(new Rectangle(5, 5, 0, 20), task.Name, Color.Transparent, Color.Black, Alignment.Right, null, colorFrame);
            //textBlock.Padding = new Vector4(10.0f, 10.0f, 0.0f, 0.0f);

            //colorFrame.AddChild(textBlock);

            //taskListBox.children.Sort((x, y) => ((Task)y.UserData).Priority.CompareTo(((Task)x.UserData).Priority));      
        }

        public void TaskFinished(Task task)
        {

        }



        public void Update(float deltaTime)
        {
            Task removeTask = null;
            foreach (Task task in tasks)
            {
                if (task.IsFinished)
                {                    
                    //foreach (GUIComponent comp in taskListBox.children)
                    //{
                    //    if (comp.UserData as Task != task) continue;
                    //    comp.Rect = new Rectangle(comp.Rect.X, comp.Rect.Y, comp.Rect.Width, comp.Rect.Height - 1);
                    //    comp.children[0].ClearChildren();
                    //    if (comp.Rect.Height < 1)
                    //    {
                    //        removeComponent = comp;
                            removeTask = task;
                    //    }
                    //    break;
                    //}

                }
                else
                {
                    task.Update(deltaTime);
                }
            }

            if (removeTask!= null)
            {
                //taskListBox.RemoveChild(removeComponent);
                tasks.Remove(removeTask);
            }

            //endShiftButton.Enabled = finished || Game1.server!=null;
        }

    }
}
