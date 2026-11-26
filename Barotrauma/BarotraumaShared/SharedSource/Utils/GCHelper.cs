#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime;
using System.Text;
using System.Threading;

namespace Barotrauma
{
    public enum WatchPriority
    {
        Low,
        Normal
    }

    public partial class GCHelper : IDisposable
    {
        private class TrackedObject(object obj, string notes, WatchPriority priority, StackTrace stackTrace)
        {
            public readonly WeakReference WeakRef = new WeakReference(obj);
            public readonly string ObjectName = obj.GetType().FullName ?? "UnknownType";
            public readonly string Notes = notes;
            public readonly DateTimeOffset CreatedAt = DateTimeOffset.Now;

            // Store as string to avoid holding references to methods in stack trace
            public readonly string StackTrace = stackTrace.ToString();

            public readonly WatchPriority Priority = priority;

            public ulong Address
            {
                get
                {
                    if (!WeakRef.IsAlive)
                    {
                        return 0; // Object has been collected, return address 0
                    }

                    if (WeakRef.Target is { } target)
                    {
                        return GetManagedObjectAddress(target);
                    }

                    return 0;
                }
            }

            public bool IsAlive
                => WeakRef.IsAlive;
        }

        private delegate ulong GetObjectAddressDelegate(object obj);

        /// <inheritdoc cref="CreateGetManagedObjectAddressMethod"/>
        private static readonly GetObjectAddressDelegate GetManagedObjectAddress = CreateGetManagedObjectAddressMethod();

        private static Option<GCHelper> active = Option.None;
        public static bool IsActive => active.IsSome();

        private readonly Thread gcTrackerThread;

        private const int MaxTrackedObjects = short.MaxValue;

        private readonly List<TrackedObject> trackedObjects = [];
        private readonly object lockObj = new object();
        private bool running = true;

        private GCHelper()
        {
            gcTrackerThread = new Thread(GCTrackerThread)
            {
                IsBackground = true,
                Name = "GCHelper Tracker"
            };
            gcTrackerThread.Start();
        }

        /// <summary>
        /// Thread to keep track of collected objects to remove them from the tracking list.
        /// </summary>
        private void GCTrackerThread()
        {
            int lastGen0 = GC.CollectionCount(0),
                lastGen1 = GC.CollectionCount(1),
                lastGen2 = GC.CollectionCount(2);

            while (running)
            {
                try
                {
                    lock (lockObj)
                    {
                        foreach (TrackedObject tracked in trackedObjects.ToArray())
                        {
                            if (!tracked.IsAlive)
                            {
                                trackedObjects.Remove(tracked);
                            }
                        }
                    }

                    Thread.Sleep(1000);
#if CLIENT
                    int newGen0 = GC.CollectionCount(0),
                        newGen1 = GC.CollectionCount(1),
                        newGen2 = GC.CollectionCount(2);

                    gcGraph.Update(0, newGen0 - lastGen0);
                    gcGraph.Update(1, newGen1 - lastGen1);
                    gcGraph.Update(2, newGen2 - lastGen2);

                    lastGen0 = newGen0;
                    lastGen1 = newGen1;
                    lastGen2 = newGen2;
#endif
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        private string PrintInfo(bool fullInfo)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("GCHelper - Garbage Collection Helper");

            GCMemoryInfo gcMemoryInfo = GC.GetGCMemoryInfo();
            sb.AppendLine();
            sb.AppendLine($"Memory Usage: {ToMegaBytes(GC.GetTotalMemory(forceFullCollection: false))} MB");
            sb.AppendLine();
            sb.AppendLine($"GC Latency Mode: {GCSettings.LatencyMode}");
            sb.AppendLine($"Generation: {gcMemoryInfo.Generation}/{GC.MaxGeneration}");
            sb.AppendLine($"Heap Size: {ToMegaBytes(gcMemoryInfo.HeapSizeBytes)} MB");
            sb.AppendLine($"Fragmentation: {ToMegaBytes(gcMemoryInfo.FragmentedBytes)} MB");
            sb.AppendLine($"Pending Finalizers: {gcMemoryInfo.FinalizationPendingCount}");
            sb.AppendLine();

            lock (lockObj)
            {
                sb.AppendLine($"Tracked objects: {trackedObjects.Count}");
                foreach (TrackedObject tracked in trackedObjects)
                {
                    if (!fullInfo && tracked.Priority is WatchPriority.Low) { continue; }
                    sb.AppendLine($"- {tracked.ObjectName} (Address: {tracked.Address:X})");
                    sb.AppendLine($"  Notes: {tracked.Notes}");
                    sb.AppendLine($"  Created at: {tracked.CreatedAt}");
                }
            }

            return sb.ToString();
        }

        private static int ToMegaBytes(long bytes)
            => (int)(bytes / 1024 / 1024);

        private bool IsRegisteredAlready(object obj)
        {
            lock (lockObj)
            {
                foreach (TrackedObject otherObj in trackedObjects)
                {
                    if (otherObj.WeakRef.Target == obj)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void Register(object obj, string notes, StackTrace stackTrace, WatchPriority priority)
        {
            lock (lockObj)
            {
                TrackedObject trackedObject = new TrackedObject(obj, notes, priority, stackTrace);
                trackedObjects.Add(trackedObject);

                if (trackedObjects.Count > MaxTrackedObjects)
                {
                    bool hasLowPriority = false;
                    // find the first low-priority object and remove it
                    for (int i = 0; i < trackedObjects.Count; i++)
                    {
                        if (trackedObjects[i].Priority == WatchPriority.Low)
                        {
                            trackedObjects.RemoveAt(i);
                            hasLowPriority = true;
                            break;
                        }
                    }

                    // if no low-priority object was found, remove the first object
                    if (!hasLowPriority)
                    {
                        trackedObjects.RemoveAt(0);
                    }
                }
            }
        }

        /// <summary>
        /// Start tracking garbage collection.
        /// This method should only be called once per application lifetime.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if GCHelper is already active.</exception>
        public static void Start()
        {
            if (active.IsSome())
            {
                throw new InvalidOperationException("GCHelper is already active.");
            }

            GCHelper helper = new GCHelper();
            active = Option.Some(helper);
        }

        public static string PrintActiveInfo(bool fullInfo)
        {
            if (!active.TryUnwrap(out var helper))
            {
                return "GCHelper is not active.";
            }

            return helper.PrintInfo(fullInfo);
        }

        /// <summary>
        /// Registers an object for garbage collection tracking with a note.
        /// </summary>
        /// <param name="obj">The object to track.</param>
        /// <param name="notes">Additional info that can be viewed on the overlay by hovering over the object.</param>
        /// <param name="priority">The priority of the object. Low-priority objects only show in the helper if a key is being held down.</param>
        /// <remarks>
        /// This only gets called in debug builds, so it is safe to use this method without worrying about performance.
        /// </remarks>
        [Conditional("DEBUG")]
        public static void RegisterForWatch(object obj,
                                            string? notes = null,
                                            WatchPriority priority = WatchPriority.Normal)
        {
            if (!active.TryUnwrap(out var helper)) { return; }
            if (helper.IsRegisteredAlready(obj)) { return; }

            StackTrace stackTrace = new StackTrace(1, fNeedFileInfo: false); // Skip the current method call

            helper.Register(obj, notes ?? string.Empty, stackTrace, priority);
        }

        /// <summary>
        /// Method for retrieving the memory address of a managed object.
        /// I believe in code it would look something like this:
        /// <code>
        /// public ulong GetManagedObjectAddress(object obj)
        ///     => (ulong)*((UIntPtr*)&amp;obj);
        /// </code>
        /// </summary>
        /// <remarks>
        /// No API exists to do this directly with C# code, and even with the unsafe keyword, the compiler will
        /// <a href="https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0208">reject any attempt to do so</a>
        /// because 99.99% of the time it is not what you want because the address of a
        /// managed object can change at any time due to garbage collection.
        /// This is why this has to be performed with IL generation, and it's actually quite simple.
        /// I've tried to explain how it works to the best of my ability by commenting each step in the code below,
        /// but I don't blame you if it flies over your head, most C# developers will never need to understand this.<br/>
        /// I'm not 100% sure about how it works either, especially why the `Ldind_I` instruction is required or
        /// what it does, probably something to do with how addresses are represented.<br/>
        /// <br/>
        /// Well, what is this 0.01% use case you might ask? It's for tracking objects with a memory debugger,
        /// especially useful if you use something like WinDbg with SOS extensions since it allows you to
        /// use the `!gcroot %address%` command to find out what is keeping the object alive instead of having to
        /// find the object first using the !dumpheap command and then using the address from there.<br/>
        /// For more advanced tools like dotMemory that aren't as cumbersome to use, this is still useful
        /// to differentiate the objects if you are, for example, tracking a specific object and want to
        /// find it in the sea of other objects in the memory debugger.
        /// </remarks>
        private static GetObjectAddressDelegate CreateGetManagedObjectAddressMethod()
        {
            DynamicMethod method = new(name: "GetManagedObjectAddress",
                                       returnType: typeof(ulong),
                                       parameterTypes: [typeof(object)]);

            ILGenerator il = method.GetILGenerator();

            // NOTE: Ldarg to load the object, Ldarga to load the address of it,
            // _S stands for short index, which are indices 0-255.
            // (we aren't dealing with functions with 255+ arguments here)
            il.Emit(OpCodes.Ldarga_S, 0); // Load the method's first argument's address into the evaluation stack
            il.Emit(OpCodes.Conv_U);      // Convert the address to an unsigned pointer type (System.UIntPtr/nuint)
            il.Emit(OpCodes.Ldind_I);     // Loads the value of the pointer into the evaluation stack
            il.Emit(OpCodes.Conv_U8);     // Convert the value to ulong
            il.Emit(OpCodes.Ret);         // Return the address

            return method.CreateDelegate<GetObjectAddressDelegate>();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            running = false;
            gcTrackerThread.Join();
        }
    }
}