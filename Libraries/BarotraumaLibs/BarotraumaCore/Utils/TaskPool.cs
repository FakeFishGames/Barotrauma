using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma
{
    public static class TaskPool
    {
        /// <summary>
        /// Empty callback that can be used when we do not care about the completion status of a task.
        /// </summary>
        public static void IgnoredCallback(Task task) { }

        const int MaxTasks = 5000;

        private struct TaskAction
        {
            public string Name;
            public Task Task;
            public Action<Task, object?> OnCompletion;
            public object? UserData;
        }

        private static readonly List<TaskAction> taskActions = new List<TaskAction>();

        public static void ListTasks(Action<string> log)
        {
            lock (taskActions)
            {
                log($"Task count: {taskActions.Count}");
                for (int i = 0; i < taskActions.Count; i++)
                {
                    log($" -{i}: {taskActions[i].Name}, {taskActions[i].Task.Status}");
                }
            }
        }

        public static bool IsTaskRunning(string name)
        {
            lock (taskActions)
            {
                return taskActions.Any(t => t.Name == name);
            }
        }

        private static void AddInternal(string name, Task task, Action<Task, object?> onCompletion, object? userdata, bool addIfFound = true)
        {
            lock (taskActions)
            {
                if (!addIfFound)
                {
                    if (taskActions.Any(t => t.Name == name)) { return; }
                }
                if (taskActions.Count >= MaxTasks)
                {
                    throw new Exception(
                        "Too many tasks in the TaskPool:\n" + string.Join('\n', taskActions.Select(ta => ta.Name))
                    );
                }
                taskActions.Add(new TaskAction() { Name = name, Task = task, OnCompletion = onCompletion, UserData = userdata });
            }
        }

        public static Unit Add(string name, Task task, Action<Task>? onCompletion)
        {
            AddInternal(name, task, (t, _) => { onCompletion?.Invoke(t); }, null);
            return Unit.Value;
        }

        public static Unit AddWithResult<T>(string name, Task<T> task, Action<T>? onCompletion) where T : notnull
        {
            AddInternal(name, task, (t, _) =>
            {
                if (t.TryGetResult(out T? result)) { onCompletion?.Invoke(result); }
            }, null);
            return Unit.Value;
        }
        public static Unit AddIfNotFound(string name, Task task, Action<Task> onCompletion)
        {
            AddInternal(name, task, (t, _) => { onCompletion?.Invoke(t); }, null, addIfFound: false);
            return Unit.Value;
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
                        taskActions[i].Task.Dispose();
                        taskActions.RemoveAt(i);
                        i--;
                    }
                }
            }
        }

        public static void PrintTaskExceptions(Task task, string msg, Action<string> throwError)
        {
            throwError(msg);
            foreach (Exception e in task.Exception?.InnerExceptions ?? Enumerable.Empty<Exception>())
            {
                throwError($"{e.Message}\n{e.StackTrace}");
            }
        }
    }
}
