#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Barotrauma
{
    public static class TaskExtensionsCore
    {
        public static async Task<Option<T>> ToOptionTask<T>(this Task<T?> nullableTask) where T : struct
        {
            var nullableResult = await nullableTask;
            return nullableResult is { } result
                ? Option.Some(result)
                : Option.None;
        }

        public static bool TryGetResult<T>(this Task task, [NotNullWhen(returnValue: true)]out T? result) where T : notnull
        {
            if (task is Task<T> { IsCompletedSuccessfully: true, Result: not null } castTask)
            {
                result = castTask.Result;
                return true;
            }
#if DEBUG
            if (task.Exception != null)
            {
                var ex = task.Exception.GetInnermost();
                throw new InvalidOperationException($"Failed to get result from task: task failed with exception {ex.Message} ({ex.GetType()}) {ex.StackTrace}");
            }
            if (task is not Task<T>)
            {
                throw new InvalidOperationException($"Failed to get result from task: expected Task<{typeof(T).NameWithGenerics()}>, got {task.GetType().NameWithGenerics()}");
            }
#endif
            result = default;
            return false;
        }
    }
}
