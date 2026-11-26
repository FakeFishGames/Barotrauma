using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Voronoi2;

namespace Barotrauma
{
    public static class Rand
    {
        public enum RandSync
        {
            /// <summary>
            /// Not synced, used for unimportant details like minor particle properties.
            /// </summary>
            Unsynced,
            /// <summary>
            /// An RNG instance shared by the client and the server is used to generate the values. 
            /// Please note that just using RandSync.ServerAndClient DOES NOT guarantee that the server and the client will generate the same values when doing some arbitrary calls to the methods in this class. 
            /// The values are only guaranteed to be the same if the same seed (<see cref="SetSyncedSeed(int)"/>) is set on both sides, and then the same sequence of calls is made.
            /// </summary>
            ServerAndClient,
#if CLIENT
            /// <summary>
            /// An RNG instance shared by different clients. Used for things that the server doesn't track, but clients want to match anyway (e.g. certain level visuals).
            /// Please note that just using RandSync.ClientOnly DOES NOT guarantee that the clients will generate the same values when doing some arbitrary calls to the methods in this class. 
            /// The values are only guaranteed to be the same if the same seed (<see cref="SetSyncedSeed(int)"/>) is set on both sides, and then the same sequence of calls is made.
            /// </summary>
            ClientOnly //set to match between clients (used for misc elements that the server doesn't track, but clients want to match anyway)
#endif
        }

        private static Random localRandom = new Random();
        private static readonly Dictionary<RandSync, Random> syncedRandom = new Dictionary<RandSync, Random> {
            { RandSync.ServerAndClient, new MTRandom() },
#if CLIENT
            { RandSync.ClientOnly, new MTRandom() }
#endif
        };

        public static Random GetRNG(RandSync randSync)
        {
            CheckRandThreadSafety(randSync);
            return randSync == RandSync.Unsynced ? localRandom : syncedRandom[randSync];
        }

        public static void SetLocalRandom(int seed)
        {
            localRandom = new Random(seed);
        }

        public static void SetSyncedSeed(int seed)
        {
            syncedRandom[RandSync.ServerAndClient] = new MTRandom(seed);
#if CLIENT
            syncedRandom[RandSync.ClientOnly] = new MTRandom(seed);
#endif
        }

        public static int ThreadId = 0;
        private static void CheckRandThreadSafety(RandSync sync)
        {
            if (ThreadId != 0 && sync == RandSync.ServerAndClient)
            {
                if (Environment.CurrentManagedThreadId != ThreadId)
                {
#if DEBUG
                    throw new Exception("Unauthorized multithreaded access to RandSync.ServerAndClient");
#else
                    DebugConsole.ThrowError("Unauthorized multithreaded access to RandSync.ServerAndClient\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                }
            }
        }

        public static float Range(float minimum, float maximum, RandSync sync = RandSync.Unsynced)
            => GetRNG(sync).Range(minimum, maximum);

        public static double Range(double minimum, double maximum, RandSync sync = RandSync.Unsynced)
            => GetRNG(sync).Range(minimum, maximum);

        /// <summary>
        /// Min inclusive, Max exclusive!
        /// </summary>
        public static int Range(int minimum, int maximum, RandSync sync = RandSync.Unsynced)
        {
            CheckRandThreadSafety(sync);
            return (sync == RandSync.Unsynced ? localRandom : (syncedRandom[sync])).Next(maximum - minimum) + minimum;
        }

        public static int Int(int max, RandSync sync = RandSync.Unsynced)
        {
            CheckRandThreadSafety(sync);
            return (sync == RandSync.Unsynced ? localRandom : (syncedRandom[sync])).Next(max);
        }

        /// <summary>
        /// Returns a uniformly distributed random direction vector of the specified length.
        /// </summary>
        public static Vector2 Vector(float length, RandSync sync = RandSync.Unsynced)
        {
            float angle = Range(0f, MathHelper.TwoPi, sync);
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
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

        /// <summary>
        /// Returns a uniformly distributed random direction vector of the specified length (double precision).
        /// </summary>
        public static DoubleVector2 Vector(double length, RandSync sync = RandSync.Unsynced)
        {
            double angle = Range(0.0, Math.Tau, sync); // Tau is just 2pi that happens to already be double
            return new DoubleVector2(Math.Cos(angle) * length, Math.Sin(angle) * length);
        }
    }
}
