using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Barotrauma
{
    enum CoroutineStatus
    {
        Running, Success, Failure
    }

    class CoroutineHandle
    {
        public readonly IEnumerator<object> Coroutine;
        public readonly string Name;

        public Exception Exception;

        public Thread Thread;

        public CoroutineHandle(IEnumerator<object> coroutine, string name = "", bool useSeparateThread = false)
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

        public static CoroutineHandle StartCoroutine(IEnumerable<object> func, string name = "", bool useSeparateThread = false)
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

        public static CoroutineHandle InvokeAfter(Action action, float delay)
        {
            return StartCoroutine(DoInvokeAfter(action, delay));
        }

        private static IEnumerable<object> DoInvokeAfter(Action action, float delay)
        {
            if (action == null)
            {
                yield return CoroutineStatus.Failure;
            }

            yield return new WaitForSeconds(delay);

            action();

            yield return CoroutineStatus.Success;
        }


        public static bool IsCoroutineRunning(string name)
        {
            return Coroutines.Any(c => c.Name == name);
        }

        public static bool IsCoroutineRunning(CoroutineHandle handle)
        {
            return Coroutines.Contains(handle);
        }
        
        public static void StopCoroutines(string name)
        {
            Coroutines.ForEach(c =>
            {
                if (c.Name == name)
                {
                    c.Thread?.Abort();
                    c.Thread?.Join();
                }
            });
            Coroutines.RemoveAll(c => c.Name == name);
        }

        public static void StopCoroutines(CoroutineHandle handle)
        {
            Coroutines.RemoveAll(c => c == handle);
        }

        public static void ExecuteCoroutineThread(CoroutineHandle handle)
        {
            try
            {
                while (true)
                {
                    if (handle.Coroutine.Current != null)
                    {
                        WaitForSeconds wfs = handle.Coroutine.Current as WaitForSeconds;
                        if (wfs != null)
                        {
                            Thread.Sleep((int)(wfs.TotalTime * 1000));
                        }
                        else
                        {
                            switch ((CoroutineStatus)handle.Coroutine.Current)
                            {
                                case CoroutineStatus.Success:
                                    return;

                                case CoroutineStatus.Failure:
                                    DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
                                    return;
                            }
                        }
                    }

                    Thread.Yield();
                    if (!handle.Coroutine.MoveNext()) return;
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
                    if (handle.Coroutine.Current != null)
                    {
                        WaitForSeconds wfs = handle.Coroutine.Current as WaitForSeconds;
                        if (wfs != null)
                        {
                            if (!wfs.CheckFinished(UnscaledDeltaTime)) return false;
                        }
                        else
                        {
                            switch ((CoroutineStatus)handle.Coroutine.Current)
                            {
                                case CoroutineStatus.Success:
                                    return true;

                                case CoroutineStatus.Failure:
                                    DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
                                    return true;
                            }
                        }
                    }

                    handle.Coroutine.MoveNext();
                    return false;
                }
                else
                {
                    if (handle.Thread.ThreadState.HasFlag(ThreadState.Stopped))
                    {
                        if (handle.Exception!=null || (CoroutineStatus)handle.Coroutine.Current == CoroutineStatus.Failure)
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
                DebugConsole.ThrowError("Coroutine " + handle.Name + " threw an exception: " + e.Message + "\n" + e.StackTrace.ToString());
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

            foreach (var x in Coroutines.ToList())
                if(IsDone(x))
                    Coroutines.Remove(x);
        }
    }
  
    class WaitForSeconds
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

        public bool CheckFinished(float deltaTime) 
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
    }
}
