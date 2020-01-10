#if UNITY
using System;
using System.Collections;
using UnityEngine;

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
    public class AsyncTask<TResult> : AsyncTask
    {
#region public fields

        /// <summary>
        /// get the result of the task. Blocking. It is recommended you yield on the wait before accessing this value
        /// </summary>
        public TResult Result;
#endregion

#region ctor

        Func<TResult> _function;

        public AsyncTask()
        {

        }

        /// <summary>
        /// Returns the task in the Success state.
        /// </summary>
        /// <param name="result"></param>
        public AsyncTask(TResult result)
            : this()
        {
            Status = TaskStatus.Success;
            Strategy = TaskStrategy.Custom;
            Result = result;
        }

        /// <summary>
        /// Creates a new background Task strategy
        /// </summary>
        /// <param name="function"></param>
        public AsyncTask(Func<TResult> function)
            : this()
        {
            if (function == null)
                throw new ArgumentNullException("function");

            _function = function;
        }

        /// <summary>
        /// Creates a new task with a specific strategy
        /// </summary>
        public AsyncTask(Func<TResult> function, TaskStrategy mode)
            : this()
        {
            if (function == null)
                throw new ArgumentNullException("function");

            if (mode == TaskStrategy.Coroutine)
                throw new ArgumentException("Mode can not be coroutine");

            _function = function;
            Strategy = mode;
        }

        /// <summary>
        /// Creates a new Coroutine  task
        /// </summary>
        public AsyncTask(IEnumerator routine)
        {
            if (routine == null)
                throw new ArgumentNullException("routine");


            _routine = routine;
            Strategy = TaskStrategy.Coroutine;
        }

        /// <summary>
        /// Creates a new Task in a Faulted state
        /// </summary>
        /// <param name="ex"></param>
        public AsyncTask(Exception ex)
        {
            Exception = ex;
            Strategy = TaskStrategy.Custom;
            Status = TaskStatus.Faulted;
        }

        /// <summary>
        /// Creates a new task
        /// </summary>
        public AsyncTask(TaskStrategy mode)
            : this()
        {
            Strategy = mode;
        }
#endregion

#region protected methods

        /// <summary>
        /// Runs complete logic, for custom tasks
        /// </summary>
        public override void Complete(Exception ex = null)
        {
            Result = default(TResult);
            base.Complete(ex);
        }

        /// <summary>
        /// Runs complete logic, for custom tasks
        /// </summary>
        public void Complete(TResult result)
        {
            Result = result;
            base.Complete();
        }

        public override void Start()
        {
            Result = default(TResult);
            base.Start();
        }

        protected override void Execute()
        {
            try
            {
                if (_function != null)
                {
                    Result = _function();
                }
                Status = TaskStatus.Success;
                OnTaskComplete();
            }
            catch (Exception ex)
            {
                Exception = ex;
                Status = TaskStatus.Faulted;
                if (LogErrors)
                    Debug.LogException(ex);
            }
        }
#endregion
    }
}
#endif
