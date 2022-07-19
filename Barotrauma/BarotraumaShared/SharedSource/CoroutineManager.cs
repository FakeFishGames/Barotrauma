using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Barotrauma
{
    abstract class CoroutineStatus
    {
        public static CoroutineStatus Running => EnumCoroutineStatus.Running;
        public static CoroutineStatus Success => EnumCoroutineStatus.Success;
        public static CoroutineStatus Failure => EnumCoroutineStatus.Failure;

        public abstract bool CheckFinished(float deltaTime);
        public abstract bool EndsCoroutine(CoroutineHandle handle);
    }

    class EnumCoroutineStatus : CoroutineStatus
    {
        private enum StatusValue
        {
            Running, Success, Failure
        }

        private readonly StatusValue Value;

        private EnumCoroutineStatus(StatusValue value) { Value = value; }

        public new readonly static EnumCoroutineStatus Running = new EnumCoroutineStatus(StatusValue.Running);
        public new readonly static EnumCoroutineStatus Success = new EnumCoroutineStatus(StatusValue.Success);
        public new readonly static EnumCoroutineStatus Failure = new EnumCoroutineStatus(StatusValue.Failure);

        public override bool CheckFinished(float deltaTime)
        {
            return true;
        }

        public override bool EndsCoroutine(CoroutineHandle handle)
        {
            if (Value == StatusValue.Failure)
            {
                DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
            }
            return Value != StatusValue.Running;
        }

        public override bool Equals(object obj)
        {
            return obj is EnumCoroutineStatus other && Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }

    class CoroutineHandle
    {
        public readonly IEnumerator<CoroutineStatus> Coroutine;
        public readonly string Name;

        public Exception Exception;
        public volatile bool AbortRequested;

        public Thread Thread;

        public CoroutineHandle(IEnumerator<CoroutineStatus> coroutine, string name = "", bool useSeparateThread = false)
        {
            Coroutine = coroutine;
            Name = string.IsNullOrWhiteSpace(name) ? coroutine.ToString() : name;
            Exception = null;
        }

    }

    // Keeps track of all running coroutines, and runs them till the end.
    static class CoroutineManager
    {
        static readonly List<CoroutineHandle> Coroutines = new List<CoroutineHandle>();

        public static float UnscaledDeltaTime, DeltaTime;

        public static CoroutineHandle StartCoroutine(IEnumerable<CoroutineStatus> func, string name = "", bool useSeparateThread = false)
        {
            var handle = new CoroutineHandle(func.GetEnumerator(), name);
            lock (Coroutines)
            {
                Coroutines.Add(handle);
            }

            handle.Thread = null;
            if (useSeparateThread)
            {
                handle.Thread = new Thread(() => { ExecuteCoroutineThread(handle); })
                {
                    Name = "Coroutine Thread (" + handle.Name + ")",
                    IsBackground = true
                };
                handle.Thread.Start();
            }

            return handle;
        }

        public static CoroutineHandle Invoke(Action action, float delay = 0f)
        {
            return StartCoroutine(DoInvokeAfter(action, delay));
        }

        private static IEnumerable<CoroutineStatus> DoInvokeAfter(Action action, float delay)
        {
            if (action == null)
            {
                yield return CoroutineStatus.Failure;
            }

            if (delay > 0.0f)
            {
                yield return new WaitForSeconds(delay);
            }

            action();

            yield return CoroutineStatus.Success;
        }


        public static bool IsCoroutineRunning(string name)
        {
            lock (Coroutines)
            {
                return Coroutines.Any(c => c.Name == name);
            }
        }

        public static bool IsCoroutineRunning(CoroutineHandle handle)
        {
            lock (Coroutines)
            {
                return Coroutines.Contains(handle);
            }
        }

        public static void StopCoroutines(string name)
        {
            lock (Coroutines)
            {
                HandleCoroutineStopping(c => c.Name == name);
                Coroutines.RemoveAll(c => c.Name == name);
            }
        }

        public static void StopCoroutines(CoroutineHandle handle)
        {
            lock (Coroutines)
            {
                HandleCoroutineStopping(c => c == handle);
                Coroutines.RemoveAll(c => c == handle);
            }
        }

        private static void HandleCoroutineStopping(Func<CoroutineHandle, bool> filter)
        {
            foreach (CoroutineHandle coroutine in Coroutines)
            {
                if (filter(coroutine))
                {
                    coroutine.AbortRequested = true;
                    if (coroutine.Thread != null)
                    {
                        bool joined = false;
                        while (!joined)
                        {
                            CrossThread.ProcessTasks();
                            joined = coroutine.Thread.Join(TimeSpan.FromMilliseconds(500));
                        }
                    }
                }
            }
        }

        private static bool PerformCoroutineStep(CoroutineHandle handle)
        {
            var current = handle.Coroutine.Current;
            if (current != null)
            {
                if (current.EndsCoroutine(handle) || handle.AbortRequested) { return true; }
                if (!current.CheckFinished(UnscaledDeltaTime)) { return false; }
            }
            if (!handle.Coroutine.MoveNext()) { return true; }
            return false;
        }

        public static void ExecuteCoroutineThread(CoroutineHandle handle)
        {
            try
            {
                while (!handle.AbortRequested)
                {
                    if (PerformCoroutineStep(handle)) { return; }
                    Thread.Sleep((int)(UnscaledDeltaTime * 1000));
                }
            }
            catch (ThreadAbortException)
            {
                //not an error, don't worry about it
            }
            catch (Exception e)
            {
                handle.Exception = e;
                DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has thrown an exception", e);
            }
        }

        private static bool IsDone(CoroutineHandle handle)
        {
#if !DEBUG
            try
            {
#endif
                if (handle.Thread == null)
                {
                    return PerformCoroutineStep(handle);
                }
                else
                {
                    if (handle.Thread.ThreadState.HasFlag(ThreadState.Stopped))
                    {
                        if (handle.Exception!=null || handle.Coroutine.Current == CoroutineStatus.Failure)
                        {
                            DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
                        }
                        return true;
                    }
                    return false;
                }
#if !DEBUG
            }
            catch (Exception e)
            {
#if CLIENT && WINDOWS
                if (e is SharpDX.SharpDXException) { throw; }
#endif
                DebugConsole.ThrowError("Coroutine " + handle.Name + " threw an exception: " + e.Message + "\n" + e.StackTrace.CleanupStackTrace());
                handle.Exception = e;
                return true;
            }
#endif
        }
        // Updating just means stepping through all the coroutines
        public static void Update(float unscaledDeltaTime, float deltaTime)
        {
            UnscaledDeltaTime = unscaledDeltaTime;
            DeltaTime = deltaTime;

            List<CoroutineHandle> coroutineList;
            lock (Coroutines)
            {
                coroutineList = Coroutines.ToList();
            }

            foreach (var coroutine in coroutineList)
            {
                if (IsDone(coroutine))
                {
                    lock (Coroutines)
                    {
                        Coroutines.Remove(coroutine);
                    }
                }
            }
        }
    }
  
    class WaitForSeconds : CoroutineStatus
    {
        public readonly float TotalTime;

        float timer;
        bool ignorePause;

        public WaitForSeconds(float time, bool ignorePause = true)
        {
            timer = time;
            TotalTime = time;
            this.ignorePause = ignorePause;
        }

        public override bool CheckFinished(float deltaTime) 
        {
#if !SERVER
            if (ignorePause || !GUI.PauseMenuOpen)
            {
                timer -= deltaTime;
            }
#else
            timer -= deltaTime;
#endif
            return timer <= 0.0f;
        }

        public override bool EndsCoroutine(CoroutineHandle handle)
        {
            return false;
        }
    }
}
