using System.Collections.Generic;
using System.Threading;

namespace Barotrauma
{
    public static class CrossThread
    {
        public delegate void TaskDelegate();

        private class Task
        {
            public TaskDelegate Deleg;
            public ManualResetEvent Mre;
            public bool Done;

            public Task(TaskDelegate d)
            {
                Deleg = d;
                Mre = new ManualResetEvent(false);
                Done = false;
            }

            public void PerformWait()
            {
                if (!Done) { Mre.WaitOne(); }
            }
        }
        private static List<Task> enqueuedTasks;

        static CrossThread() { enqueuedTasks = new List<Task>(); }

        public static void ProcessTasks()
        {
            lock (enqueuedTasks)
            {
                foreach (var task in enqueuedTasks)
                {
                    task.Deleg();
                    task.Mre.Set();
                    task.Done = true;
                }
                enqueuedTasks.Clear();
            }
        }

        public static void RequestExecutionOnMainThread(TaskDelegate deleg)
        {
            if (Thread.CurrentThread == GameMain.MainThread)
            {
                deleg();
            }
            else
            {
                Task newTask = new Task(deleg);
                lock (enqueuedTasks)
                {
                    enqueuedTasks.Add(newTask);
                }
                newTask.PerformWait();
            }
        }
    }
}
