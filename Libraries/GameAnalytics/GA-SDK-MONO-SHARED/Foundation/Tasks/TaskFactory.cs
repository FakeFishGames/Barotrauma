using System;
using System.Collections;

namespace Foundation.Tasks
{
    /// <summary>
    /// A task encapsulates future work that may be waited on.
    /// - Support running actions in background threads 
    /// - Supports running coroutines with return results
    /// - Use the WaitForRoutine method to wait for the task in a coroutine
    /// </summary>
    /// <example>
    /// <code>
    ///     var task = Task.Run(() =>
    ///     {
    ///        //Debug.Log does not work in
    ///        Debug.Log("Sleeping...");
    ///        Task.Delay(2000);
    ///        Debug.Log("Slept");
    ///    });
    ///    // wait for it
    ///    yield return task;
    ///
    ///    // check exceptions
    ///    if(task.IsFaulted)
    ///        Debug.LogException(task.Exception)
    ///</code>
    ///</example>
    public partial class AsyncTask
    {
        #region Task
        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask Run(Action action)
        {
            var task = new AsyncTask(action);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask RunOnMain(Action action)
        {
            var task = new AsyncTask(action, TaskStrategy.MainThread);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask RunOnCurrent(Action action)
        {
            var task = new AsyncTask(action, TaskStrategy.CurrentThread);
            task.Start();
            return task;
        }
        #endregion
        
        #region Coroutine

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask RunCoroutine(IEnumerator function)
        {
            var task = new AsyncTask(function);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask RunCoroutine(Func<IEnumerator> function)
        {
            var task = new AsyncTask(function());
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask RunCoroutine(Func<AsyncTask, IEnumerator> function)
        {
            var task = new AsyncTask();
            task.Strategy = TaskStrategy.Coroutine;
            task._routine = function(task);
            task.Start();
            return task;
        }
        #endregion

#if UNITY
#region Task With Result
        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask<TResult> Run<TResult>(Func<TResult> function)
        {
            var task = new AsyncTask<TResult>(function);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask<TResult> RunOnMain<TResult>(Func<TResult> function)
        {
            var task = new AsyncTask<TResult>(function, TaskStrategy.MainThread);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask<TResult> RunOnCurrent<TResult>(Func<TResult> function)
        {
            var task = new AsyncTask<TResult>(function, TaskStrategy.CurrentThread);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a new running task
        /// </summary>
        public static AsyncTask<TResult> RunCoroutine<TResult>(IEnumerator function)
        {
            var task = new AsyncTask<TResult>(function);
            task.Start();
            return task;
        }

        /// <summary>
        /// Creates a task which passes the task as a parameter
        /// </summary>
        public static AsyncTask<TResult> RunCoroutine<TResult>(Func<AsyncTask<TResult>, IEnumerator> function)
        {
            var task = new AsyncTask<TResult>();
            task.Strategy = TaskStrategy.Coroutine;
            task._routine = function(task);
            task.Start();
            return task;
        }
#endregion

#region success / fails

        /// <summary>
        /// A default task in the success state
        /// </summary>
        static AsyncTask _successTask = new AsyncTask(TaskStrategy.Custom) { Status = TaskStatus.Success };
        
        /// <summary>
        /// A default task in the success state
        /// </summary>
        public static AsyncTask<T> SuccessTask<T>(T result)
        {
            return new AsyncTask<T>(TaskStrategy.Custom) { Status = TaskStatus.Success, Result = result };
        }

        /// <summary>
        /// A default task in the faulted state
        /// </summary>
        public static AsyncTask SuccessTask()
        {
            return _successTask;
        }


        /// <summary>
        /// A default task in the faulted state
        /// </summary>
        public static AsyncTask FailedTask(string exception)
        {
            return FailedTask(new Exception(exception));
        }

        /// <summary>
        /// A default task in the faulted state
        /// </summary>
        public static AsyncTask FailedTask(Exception ex)
        {
            return new AsyncTask(TaskStrategy.Custom) { Status = TaskStatus.Faulted, Exception = ex };
        }

        /// <summary>
        /// A default task in the faulted state
        /// </summary>
        public static AsyncTask<T> FailedTask<T>(string exception)
        {
            return FailedTask<T>(new Exception(exception));
        }

        /// <summary>
        /// A default task in the faulted state
        /// </summary>
        public static AsyncTask<T> FailedTask<T>(Exception ex) 
        {
            return new AsyncTask<T>(TaskStrategy.Custom) { Status = TaskStatus.Faulted, Exception = ex };
        }
#endregion
#endif
    }
}
