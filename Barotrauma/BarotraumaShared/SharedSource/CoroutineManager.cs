#nullable enable

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

    sealed class EnumCoroutineStatus : CoroutineStatus
    {
        private enum StatusValue
        {
            Running, Success, Failure
        }

        private readonly StatusValue value;

        private EnumCoroutineStatus(StatusValue value) { this.value = value; }

        public new static readonly EnumCoroutineStatus Running = new EnumCoroutineStatus(StatusValue.Running);
        public new static readonly EnumCoroutineStatus Success = new EnumCoroutineStatus(StatusValue.Success);
        public new static readonly EnumCoroutineStatus Failure = new EnumCoroutineStatus(StatusValue.Failure);

        public override bool CheckFinished(float deltaTime)
        {
            return true;
        }

        public override bool EndsCoroutine(CoroutineHandle handle)
        {
            if (value == StatusValue.Failure)
            {
                DebugConsole.ThrowError("Coroutine \"" + handle.Name + "\" has failed");
            }
            return value != StatusValue.Running;
        }

        public override bool Equals(object? obj)
        {
            return obj is EnumCoroutineStatus other && value == other.value;
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value.ToString();
        }
    }
    
    sealed class WaitForSeconds : CoroutineStatus
    {
        public readonly float TotalTime;

        private float timer;
        private readonly bool ignorePause;

        public WaitForSeconds(float time, bool ignorePause = true)
        {
            timer = time;
            TotalTime = time;
            this.ignorePause = ignorePause;
        }

        public override bool CheckFinished(float deltaTime) 
        {
#if !SERVER
            if (ignorePause || !CoroutineManager.Paused)
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

    sealed class CoroutineHandle
    {
        public readonly IEnumerator<CoroutineStatus> Coroutine;
        public readonly string Name;

        public Exception? Exception;
        public bool AbortRequested;

        public CoroutineHandle(IEnumerator<CoroutineStatus> coroutine, string name = "")
        {
            Coroutine = coroutine;
            Name = string.IsNullOrWhiteSpace(name) ? (coroutine.ToString() ?? "") : name;
            Exception = null;
        }

    }

    // Keeps track of all running coroutines, and runs them till the end.
    static class CoroutineManager
    {
        static readonly List<CoroutineHandle> Coroutines = new List<CoroutineHandle>();

        public static float DeltaTime { get; private set; }

        public static bool Paused { get; private set; }

        public static CoroutineHandle StartCoroutine(IEnumerable<CoroutineStatus> func, string name = "")
        {
            var handle = new CoroutineHandle(func.GetEnumerator(), name);
            lock (Coroutines)
            {
                Coroutines.Add(handle);
            }

            return handle;
        }

        public static CoroutineHandle Invoke(Action action, float delay = 0f)
        {
            return StartCoroutine(DoInvokeAfter(action, delay));
        }

        private static IEnumerable<CoroutineStatus> DoInvokeAfter(Action? action, float delay)
        {
            if (action == null)
            {
                yield return CoroutineStatus.Failure;
                yield break;
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
            // No lock here because all callers lock for us
            foreach (CoroutineHandle coroutine in Coroutines)
            {
                if (filter(coroutine))
                {
                    coroutine.AbortRequested = true;
                }
            }
        }

        private static bool PerformCoroutineStep(CoroutineHandle handle)
        {
            var current = handle.Coroutine.Current;
            if (current != null)
            {
                if (current.EndsCoroutine(handle) || handle.AbortRequested) { return true; }
                if (!current.CheckFinished(DeltaTime)) { return false; }
            }
            if (!handle.Coroutine.MoveNext()) { return true; }
            return false;
        }

        private static bool IsDone(CoroutineHandle handle)
        {
#if !DEBUG
            try
            {
#endif
                return PerformCoroutineStep(handle);
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
        private static readonly List<CoroutineHandle> coroutinePass = new List<CoroutineHandle>();
        public static void Update(bool paused, float deltaTime)
        {
            Paused = paused;
            DeltaTime = deltaTime;

            // Do not optimize this as a for loop directly over the Coroutines list!
            // Coroutines are able to spawn new coroutines!
            lock (Coroutines)
            {
                coroutinePass.AddRange(Coroutines);
            }
            foreach (var coroutine in coroutinePass)
            {
                if (!IsDone(coroutine)) { continue; }
                lock (Coroutines)
                {
                    Coroutines.Remove(coroutine);
                }
            }
            coroutinePass.Clear();
        }

        public static void ListCoroutines()
        {
            lock (Coroutines)
            {
                DebugConsole.NewMessage("***********");
                DebugConsole.NewMessage($"{Coroutines.Count} coroutine(s)");
                foreach (var c in Coroutines)
                {
                    DebugConsole.NewMessage($"- {c.Name}");
                }
            }
        }
    }
}
