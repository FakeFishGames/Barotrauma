#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EosInterfacePrivate;

internal sealed partial class ImplementationPrivate : Barotrauma.EosInterface.Implementation
{
    /// <summary>
    /// Custom TaskScheduler to force every EOS-related task to run on the main thread, because
    /// the docs say the SDK is not thread-safe even though it's worked fine without this :/
    ///
    /// See https://dev.epicgames.com/docs/epic-online-services/eos-get-started/eossdkc-sharp-getting-started#threading
    /// </summary>
    internal sealed class CustomTaskScheduler : TaskScheduler
    {
        private readonly ConcurrentQueue<Task> taskQueue = new ConcurrentQueue<Task>();

        internal Task<T> Schedule<T>(Func<Task<T>> action)
        {
            return
                Task.Factory.StartNew(
                    function: action,
                    cancellationToken: CancellationToken.None,
                    creationOptions: TaskCreationOptions.None,
                    scheduler: this).Unwrap();
        }

        internal Task Schedule(Func<Task> action)
        {
            return
                Task.Factory.StartNew(
                    function: action,
                    cancellationToken: CancellationToken.None,
                    creationOptions: TaskCreationOptions.None,
                    scheduler: this).Unwrap();
        }

        internal void RunOnCurrentThread()
        {
            while (taskQueue.TryDequeue(out var task))
            {
                TryExecuteTask(task);
            }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
            => Enumerable.Empty<Task>();

        protected override void QueueTask(Task task)
        {
            taskQueue.Enqueue(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // Never allow executing inline because that means it's not the main thread
            return false;
        }
    }

    internal readonly CustomTaskScheduler TaskScheduler = new CustomTaskScheduler();
}
