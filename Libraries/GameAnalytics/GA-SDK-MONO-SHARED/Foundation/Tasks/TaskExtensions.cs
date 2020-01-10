#if UNITY
using System;
using System.Collections;
using UnityEngine;

namespace Foundation.Tasks
{
    public static class TaskExtensions
    {
        /// <summary>
        /// will throw if faulted
        /// </summary>
        /// <returns></returns>
        public static T ThrowIfFaulted<T>(this T self) where T : AsyncTask
        {
            if (self.IsFaulted)
                throw self.Exception;
            return self;
        }

        /// <summary>
        /// Waits for the task to complete
        /// </summary>
        public static T ContinueWith<T>(this T self, Action<T> continuation) where T : AsyncTask
        {
            if (self.IsCompleted)
            {
                continuation(self);
            }
            else
            {
                self.AddContinue(continuation);
            }
            return self;
        }

        /// <summary>
        /// Adds a timeout to the task. Will raise an exception if still running
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="self"></param>
        /// <param name="seconds"></param>
        /// <param name="onTimeout"></param>
        /// <returns></returns>
        public static T AddTimeout<T>(this T self, int seconds, Action<AsyncTask> onTimeout = null) where T : AsyncTask
        {
            TaskManager.StartRoutine(TimeOutAsync(self, seconds, onTimeout));

            return self;
        }

        static IEnumerator TimeOutAsync(AsyncTask task, int seconds, Action<AsyncTask> onTimeout = null)
        {
            yield return new WaitForSeconds(seconds);

            if (task.IsRunning)
            {
                if (onTimeout != null)
                {
                    onTimeout(task);
                }
                
                task.Complete(new Exception("Timeout"));
            }
        }
    }
}
#endif