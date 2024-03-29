#nullable enable

using System;
using System.Threading.Tasks;
using Barotrauma;

namespace EosInterfacePrivate;

/// <summary>
/// Creates a task that returns the result of a callback.
/// This is meant to be used with EOS' asynchronous methods,
/// which are all callback-based because this is a C library.
/// </summary>
internal class CallbackWaiter<T> where T : notnull
{
    private readonly object mutex = new object();
    private Option<T> result = Option.None;
    private readonly DateTime timeout;

    public readonly Task<Option<T>> Task;
    
    public CallbackWaiter(TimeSpan timeout = default)
    {
        this.timeout = DateTime.Now + (timeout == default
            ? TimeSpan.FromSeconds(60)
            : timeout);
        this.Task = System.Threading.Tasks.Task.Run(RunTask);
    }

    public void OnCompletion(ref T result)
    {
        lock (mutex)
        {
            this.result = Option<T>.Some(result);
        }
    }

    private async Task<Option<T>> RunTask()
    {
        while (DateTime.Now < timeout)
        {
            lock (mutex)
            {
                if (result.IsSome()) { return result; }
            }
            await System.Threading.Tasks.Task.Delay(32);
        }
        return Option.None;
    }
}
