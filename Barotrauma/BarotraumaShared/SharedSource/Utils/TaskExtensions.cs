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

        public static async Task<T> WaitForLoadingScreen<T>(this Task<T> task)
        {
            var result = await task;
#if CLIENT
            while (GameMain.Instance.LoadingScreenOpen)
            {
                await Task.Delay((int)(1000 * Timing.Step));
            }
#endif
            return result;
        }
    }
}
