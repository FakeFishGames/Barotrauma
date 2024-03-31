#nullable enable
using System.Threading.Tasks;

namespace Barotrauma
{
    public static class TaskExtensions
    {
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