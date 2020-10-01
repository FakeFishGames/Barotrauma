using Microsoft.Xna.Framework;
using System;
using Voronoi2;

namespace Barotrauma
{
    public static class Rand
    {
        public enum RandSync
        {
            Unsynced = -1, //not synced, used for unimportant details like minor particle properties
            Server = 0, //synced with the server (used for gameplay elements that the players can interact with)
            ClientOnly = 1 //set to match between clients (used for misc elements that the server doesn't track, but clients want to match anyway)
        }

        private static Random localRandom = new Random();
        private static readonly Random[] syncedRandom = new MTRandom[] {
            new MTRandom(), new MTRandom()
        };

        public static Random GetRNG(RandSync randSync)
        {
            return randSync == RandSync.Unsynced ? localRandom : syncedRandom[(int)randSync];
        }

        public static void SetLocalRandom(int seed)
        {
            localRandom = new Random(seed);
        }

        public static void SetSyncedSeed(int seed)
        {
            syncedRandom[(int)RandSync.Server] = new MTRandom(seed);
            syncedRandom[(int)RandSync.ClientOnly] = new MTRandom(seed);
        }

        public static int ThreadId = 0;
        private static void CheckRandThreadSafety(RandSync sync)
        {
            if (ThreadId != 0 && sync == RandSync.Server)
            {
                if (System.Threading.Thread.CurrentThread.ManagedThreadId != ThreadId)
                {
#if DEBUG
                    throw new Exception("Unauthorized multithreaded access to RandSync.Server");
#else
                    DebugConsole.ThrowError("Unauthorized multithreaded access to RandSync.Server\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                }
            }
        }

        public static float Range(float minimum, float maximum, RandSync sync=RandSync.Unsynced)
        {
            CheckRandThreadSafety(sync);
            return (float)(sync == RandSync.Unsynced ? localRandom : (syncedRandom[(int)sync])).NextDouble() * (maximum - minimum) + minimum;
        }

        public static double Range(double minimum, double maximum, RandSync sync = RandSync.Unsynced)
        {
            CheckRandThreadSafety(sync);
            return (sync == RandSync.Unsynced ? localRandom : (syncedRandom[(int)sync])).NextDouble() * (maximum - minimum) + minimum;
        }

        public static int Range(int minimum, int maximum, RandSync sync = RandSync.Unsynced)
        {
            CheckRandThreadSafety(sync);
            return (sync == RandSync.Unsynced ? localRandom : (syncedRandom[(int)sync])).Next(maximum - minimum) + minimum;
        }

        public static int Int(int max, RandSync sync = RandSync.Unsynced)
        {
            CheckRandThreadSafety(sync);
            return (sync == RandSync.Unsynced ? localRandom : (syncedRandom[(int)sync])).Next(max);
        }

        public static Vector2 Vector(float length, RandSync sync = RandSync.Unsynced)
        {
            Vector2 randomVector = new Vector2(Range(-1.0f, 1.0f, sync), Range(-1.0f, 1.0f, sync));

            if (randomVector.LengthSquared() < 0.001f) return new Vector2(0.0f, length);

            return Vector2.Normalize(randomVector) * length;
        }
        
        /// <summary>
        /// Random float between 0 and 1.
        /// </summary>
        public static float Value(RandSync sync = RandSync.Unsynced)
        {
            return Range(0f, 1f, sync);
        }

        public static Color Color(bool randomAlpha = false, RandSync sync = RandSync.Unsynced)
        {
            if (randomAlpha)
            {
                return new Color(Value(sync), Value(sync), Value(sync), Value(sync));
            }
            else
            {
                return new Color(Value(sync), Value(sync), Value(sync));
            }
        }

        public static DoubleVector2 Vector(double length, RandSync sync = RandSync.Unsynced)
        {
            double x = Range(-1.0, 1.0, sync);
            double y = Range(-1.0, 1.0, sync);

            double len = Math.Sqrt(x * x + y * y);
            if (len < 0.00001) return new DoubleVector2(0.0, length);

            return new DoubleVector2(x / len * length, y / len * length);
        }
    }
}
