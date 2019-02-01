using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Barotrauma.Threading
{
    public static class CrossThread
    {
        private class RequestedTask
		{
			Action action;
			ManualResetEventSlim mre;

			public RequestedTask(Action act)
            {
				action = act;
				mre = new ManualResetEventSlim(false);            
			}

			public void Wait()
            {
                mre.Wait();
			}

			public void Call()
            {
				action();
				mre.Set();
			}
		};
      
		private static List<RequestedTask> requestedTasks;

        static CrossThread()
        {
			requestedTasks = new List<RequestedTask>();
        }

		public static void RequestExecuteOnMainThread(Action act)
        {
            if (Thread.CurrentThread.ManagedThreadId == GameMain.MainThreadID)
            {
				act();
				return;
			}
			RequestedTask newTask = new RequestedTask(act);
			lock (requestedTasks)
            {
				requestedTasks.Add(newTask);
			}
			newTask.Wait();
		}

		public static void ProcessRequestedTasks()
        {
			lock (requestedTasks)
            {
				foreach (RequestedTask task in requestedTasks)
                {
					task.Call();
				}
                requestedTasks.Clear();
			}
		}
    }
}
