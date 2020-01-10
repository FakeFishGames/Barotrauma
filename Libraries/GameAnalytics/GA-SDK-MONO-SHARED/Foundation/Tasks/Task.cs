
using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY
using UnityEngine;
#endif
#if WINDOWS_WSA || WINDOWS_UWP
using Windows.System.Threading;
#else
using System.Threading;
#endif

namespace Foundation.Tasks
{


    /// <summary>
    /// Describes the Tasks State
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// Working
        /// </summary>
        Pending,
        /// <summary>
        /// Exception as thrown or otherwise stopped early
        /// </summary>
        Faulted,
        /// <summary>
        /// Complete without error
        /// </summary>
        Success,
    }

    /// <summary>
    /// Execution strategy for the Task
    /// </summary>
    public enum TaskStrategy
    {
        /// <summary>
        /// Dispatches the task to a background thread
        /// </summary>
        BackgroundThread,
        /// <summary>
        /// Dispatches the task to the main thread
        /// </summary>
        MainThread,
        /// <summary>
        /// Dispatches the task to the current thread
        /// </summary>
        CurrentThread,
        /// <summary>
        /// Runs the task as a coroutine
        /// </summary>
        Coroutine,
        /// <summary>
        /// Does nothing. For custom tasks.
        /// </summary>
        Custom,
    }

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
    public partial class AsyncTask :
#if UNITY_5
	CustomYieldInstruction,
#endif
	IDisposable
    {
        #region options
        /// <summary>
        /// Forces use of a single thread for debugging
        /// </summary>
        public static bool DisableMultiThread = false;

        /// <summary>
        /// Logs Exceptions
        /// </summary>
        public static bool LogErrors = false;
        #endregion
        
        #region properties

        /// <summary>
        /// Run execution path
        /// </summary>
        public TaskStrategy Strategy;

        /// <summary>
        /// Error
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Run State
        /// </summary>
        public TaskStatus Status { get; set; }

#if UNITY_5
        /// <summary>
        /// Custom Yield
        /// </summary>
        public override bool keepWaiting
        {
            get { return !IsCompleted; }
        }
#endif

		public bool IsRunning
        {
            get { return Status == TaskStatus.Pending; }
        }

        public bool IsCompleted
        {
            get { return (Status == TaskStatus.Success || Status == TaskStatus.Faulted) && !HasContinuations; }
        }

        public bool IsFaulted
        {
            get { return Status == TaskStatus.Faulted; }
        }

        public bool IsSuccess
        {
            get { return Status == TaskStatus.Success; }
        }

        public bool HasContinuations { get; protected set; }
#endregion

#region private

        protected TaskStatus _status;
        protected Action _action;
        protected IEnumerator _routine;
        List<Delegate> _completeList;

#endregion

#region constructor

        static AsyncTask()
        {
#if UNITY
            TaskManager.ConfirmInit();
#endif
        }

        /// <summary>
        /// Creates a new task
        /// </summary>
        public AsyncTask()
        {
        }

        /// <summary>
        /// Creates a new task
        /// </summary>
        public AsyncTask(TaskStrategy mode)
        {
            Strategy = mode;
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
        /// Creates a new background task
        /// </summary>
        /// <param name="action"></param>
        public AsyncTask(Action action)
        {
            _action = action;
            Strategy = TaskStrategy.BackgroundThread;
        }

        /// <summary>
        /// Creates a new Task 
        /// </summary>
        /// <param name="action"></param>
        /// <param name="mode"></param>
        public AsyncTask(Action action, TaskStrategy mode)
            : this()
        {
            if (mode == TaskStrategy.Coroutine)
                throw new ArgumentException("Action tasks may not be coroutines");

            _action = action;
            Strategy = mode;
        }

        /// <summary>
        /// Creates a new Coroutine Task
        /// </summary>
        /// <param name="action"></param>
        public AsyncTask(IEnumerator action)
            : this()
        {
            if (action == null)
                throw new ArgumentNullException("action");

            _routine = action;
            Strategy = TaskStrategy.Coroutine;
        }

#endregion

#region Private

        protected virtual void Execute()
        {
            try
            {
                if (_action != null)
                {
                    _action();
                }
                Status = TaskStatus.Success;
                OnTaskComplete();
            }
            catch (Exception ex)
            {
                Exception = ex;
                Status = TaskStatus.Faulted;

#if UNITY
                if (LogErrors)
                    Debug.LogException(ex);
#endif
            }
        }



#if WINDOWS_WSA || WINDOWS_UWP
        protected async void RunOnBackgroundThread()
        {
            Status = TaskStatus.Pending;
            await ThreadPool.RunAsync(o => Execute());
#else
        protected void RunOnBackgroundThread()
        {
            Status = TaskStatus.Pending;
            ThreadPool.QueueUserWorkItem(state => Execute());
#endif
        }

        protected void RunOnCurrentThread()
        {
            Status = TaskStatus.Pending;
            Execute();
        }

#if UNITY
        protected void RunOnMainThread()
        {
            Status = TaskStatus.Pending;
            TaskManager.RunOnMainThread(Execute);
        }

        protected void RunAsCoroutine()
        {
            Status = TaskStatus.Pending;

            TaskManager.StartRoutine(new TaskManager.CoroutineCommand
            {
                Coroutine = _routine,
                OnComplete = OnRoutineComplete
            });
        }
#endif

        protected virtual void OnTaskComplete()
        {
            if (_completeList != null)
            {
                foreach (var d in _completeList)
                {
                    if (d != null)
                        d.DynamicInvoke(this);
                }
                _completeList = null;
            }
            HasContinuations = false;
        }

        protected void OnRoutineComplete()
        {
            if (Status == TaskStatus.Pending)
            {
                Status = TaskStatus.Success;
                OnTaskComplete();
            }
        }

#endregion

#region public methods

        /// <summary>
        /// Runs complete logic, for custom tasks
        /// </summary>
        public virtual void Complete(Exception ex = null)
        {
            if (ex == null)
            {
                Exception = null;
                Status = TaskStatus.Success;
                OnTaskComplete();
            }
            else
            {
                Exception = ex;
                Status = TaskStatus.Faulted;
                OnTaskComplete();
            }
        }

        /// <summary>
        /// Executes the task
        /// </summary>
        public virtual void Start()
        {
            Status = TaskStatus.Pending;

            switch (Strategy)
            {

                case TaskStrategy.Custom:
                    break;
#if UNITY
                case TaskStrategy.Coroutine:
                    RunAsCoroutine();
                    break;
#endif
                case TaskStrategy.BackgroundThread:
                    if (DisableMultiThread)
                        RunOnCurrentThread();
                    else
                        RunOnBackgroundThread();
                    break;
                case TaskStrategy.CurrentThread:
                    RunOnCurrentThread();
                    break;
#if UNITY
                case TaskStrategy.MainThread:
                    RunOnMainThread();
                    break;
#endif
            }
        }

        public virtual void Dispose()
        {
            Status = TaskStatus.Pending;
            Exception = null;
            _action = null;
            _routine = null;
            _completeList = null;
            HasContinuations = false;
        }

        public void AddContinue(Delegate action)
        {
            HasContinuations = true;
            if (_completeList == null)
            {
                _completeList = new List<Delegate>();
            }

            _completeList.Add(action);
        }
#endregion
    }
}
