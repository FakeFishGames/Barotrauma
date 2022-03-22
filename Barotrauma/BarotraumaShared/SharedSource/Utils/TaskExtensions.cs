using System.Threading.Tasks;

namespace Barotrauma
{
    static class TaskExtensions
    {
        public static bool TryGetResult<T>(this Task task, out T result)
        {
            if (task is Task<T> { IsCompletedSuccessfully: true } castTask)
            {
                result = castTask.Result;
                return true;
            }
            result = default;
            return false;
        }
    }
}
