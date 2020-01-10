using System;
using System.Collections.Generic;
#if WINDOWS_WSA || WINDOWS_UWP
using Windows.System.Threading;
using Windows.Foundation;
using System.Threading.Tasks;
#else
using System.Threading;
#endif
using GameAnalyticsSDK.Net.Logging;

namespace GameAnalyticsSDK.Net.Threading
{
	public class GAThreading
	{
        private static bool endThread = false;
        private static DateTime threadDeadline;
        private static readonly GAThreading _instance = new GAThreading ();
		private const int ThreadWaitTimeInMs = 1000;
		private readonly PriorityQueue<long, TimedBlock> blocks = new PriorityQueue<long, TimedBlock>();
		private readonly object threadLock = new object();
        private TimedBlock scheduledBlock;
        private bool hasScheduledBlockRun;
#if WINDOWS_WSA || WINDOWS_UWP
        private IAsyncAction thread;
#else
        private Thread thread;
#endif

        private GAThreading()
		{
            threadDeadline = DateTime.Now;
            hasScheduledBlockRun = true;
        }

        ~GAThreading()
        {
            StopThread();
        }

		private static GAThreading Instance
		{
			get 
			{
				return _instance;
			}
		}

        private static void RunBlocks()
        {
            TimedBlock timedBlock;

            while ((timedBlock = GetNextBlock()) != null)
            {
                timedBlock.block();
            }

            if ((timedBlock = GetScheduledBlock()) != null)
            {
                timedBlock.block();
            }
        }

        public static void Run()
		{
			GALogger.D("Starting GA thread");

			try
			{
				while(!endThread && threadDeadline.CompareTo(DateTime.Now) > 0)
				{
                    RunBlocks();

#if WINDOWS_WSA || WINDOWS_UWP
                    Task.Delay(ThreadWaitTimeInMs).Wait();
#else
                    Thread.Sleep(ThreadWaitTimeInMs);
#endif
                }

                // run any last blocks added
                RunBlocks();

                if (!endThread)
                {
                    GALogger.D("Ending GA thread");
                }
            }
			catch(Exception)
			{
				//GALogger.E("Error on GA thread");
				//GALogger.E(e.ToString());
			}
		}

        public static void PerformTaskOnGAThread(string blockName, Action taskBlock)
		{
			PerformTaskOnGAThread(blockName, taskBlock, 0);
		}

		public static void PerformTaskOnGAThread(string blockName, Action taskBlock, long delayInSeconds)
		{
            if(endThread)
            {
                return;
            }

			lock(Instance.threadLock)
			{
				DateTime time = DateTime.Now;
				time = time.AddSeconds(delayInSeconds);

				TimedBlock timedBlock = new TimedBlock(time, taskBlock, blockName);
				Instance.AddTimedBlock(timedBlock);
                threadDeadline = time.AddSeconds(10);
#if WINDOWS_WSA || WINDOWS_UWP
                if (IsThreadFinished())
                {
                    StartThread();
                }
#else
                if (IsThreadFinished())
                {
                    if(Instance.thread != null)
                    {
                        Instance.thread.Join();
                    }
                    StartThread();
                }
#endif

            }
        }

		public static void ScheduleTimer(double interval, string blockName, Action callback)
		{
            if (endThread)
            {
                return;
            }

            lock (Instance.threadLock)
			{
                if(Instance.hasScheduledBlockRun)
                {
                    DateTime time = DateTime.Now;
                    time = time.AddSeconds(interval);
                    Instance.scheduledBlock = new TimedBlock(time, callback, blockName);
                    Instance.hasScheduledBlockRun = false;
                    threadDeadline = time.AddSeconds(2);
#if WINDOWS_WSA || WINDOWS_UWP
                    if (IsThreadFinished())
                    {
                        StartThread();
                    }
#else
                    if (IsThreadFinished())
                    {
                        if(Instance.thread != null)
                        {
                            Instance.thread.Join();
                        }
                        StartThread();
                    }
#endif
                }
            }
		}

		private void AddTimedBlock(TimedBlock timedBlock)
		{
			this.blocks.Enqueue(timedBlock.deadline.Ticks, timedBlock);
		}

		private static TimedBlock GetNextBlock()
		{
			lock(Instance.threadLock)
			{
				DateTime now = DateTime.Now;

				if(Instance.blocks.HasItems && Instance.blocks.Peek().deadline.CompareTo(now) <= 0)
				{
					return Instance.blocks.Dequeue();
				}

				return null;
			}
		}

        private static TimedBlock GetScheduledBlock()
        {
            lock (Instance.threadLock)
            {
                DateTime now = DateTime.Now;

                if (!Instance.hasScheduledBlockRun && Instance.scheduledBlock != null && Instance.scheduledBlock.deadline.CompareTo(now) <= 0)
                {
                    Instance.hasScheduledBlockRun = true;
                    return Instance.scheduledBlock;
                }

                return null;
            }
        }

        public static void StartThread()
        {
#if WINDOWS_WSA || WINDOWS_UWP
            Instance.thread = ThreadPool.RunAsync(o => Run());
#else
            Instance.thread = new Thread(new ThreadStart(Run));
            Instance.thread.Priority = ThreadPriority.Lowest;
            Instance.thread.Start();
#endif
        }

		public static void StopThread()
		{
            endThread = true;
		}

        public static bool IsThreadFinished()
        {
#if WINDOWS_WSA || WINDOWS_UWP
            return Instance.thread == null || Instance.thread.Status != AsyncStatus.Started;
#else
            return Instance.thread == null || !Instance.thread.IsAlive;
#endif
        }
    }
}

