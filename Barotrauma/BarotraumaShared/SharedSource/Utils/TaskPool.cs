using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    public static class TaskPool
    {
        private struct TaskAction
        {
            public Task Task;
            public Action<Task, object> OnCompletion;
            public object UserData;
        }

        private static List<TaskAction> taskActions = new List<TaskAction>();

        private static void AddInternal(Task task, Action<Task, object> onCompletion, object userdata)
        {
            lock (taskActions)
            {
                taskActions.Add(new TaskAction() { Task = task, OnCompletion = onCompletion, UserData = userdata });
            }
        }

        public static void Add(Task task, Action<Task> onCompletion)
        {
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke(t); }, null);
        }

        public static void Add<U>(Task task, U userdata, Action<Task, U> onCompletion) where U : class
        {
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke(t, (U)obj); }, userdata);
        }

        public static void Add<T>(Task<T> task, Action<Task<T>> onCompletion)
        {
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke((Task<T>)t); }, null);
        }

        public static void Add<T,U>(Task<T> task, U userdata, Action<Task<T>, U> onCompletion) where U : class
        {
            AddInternal(task, (Task t, object obj) => { onCompletion?.Invoke((Task<T>)t, (U)obj); }, userdata);
        }

        public static void Update()
        {
            lock (taskActions)
            {
                for (int i = 0; i < taskActions.Count; i++)
                {
                    if (taskActions[i].Task.IsCompleted)
                    {
                        taskActions[i].OnCompletion?.Invoke(taskActions[i].Task, taskActions[i].UserData);
                        taskActions.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        public static void PrintTaskExceptions(Task task, string msg)
        {
            DebugConsole.ThrowError(msg);
            foreach (Exception e in task.Exception.InnerExceptions)
            {
                DebugConsole.ThrowError(e.Message + "\n" + e.StackTrace);
            }
        }
    }
}
