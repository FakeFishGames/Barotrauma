using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    static class Rand
    {
        private static Random localRandom = new Random();
        private static Random syncedRandom = new Random();

        public static void SetSyncedSeed(int seed)
        {
            syncedRandom = new Random(seed);
        }

        public static float Range(float minimum, float maximum, bool local = true)
        {
            return (float)(local ? localRandom : syncedRandom).NextDouble() * (maximum - minimum) + minimum;
       
        }

        public static int Range(int minimum, int maximum, bool local = true)
        {
            return (local ? localRandom : syncedRandom).Next(maximum - minimum) + minimum;
            
        }

        public static int Int(int max = int.MaxValue, bool local = true)
        {
            return (local ? localRandom : syncedRandom).Next(max);            
        }

        public static Vector2 Vector(float length = 1.0f, bool local = true)
        {
            Vector2 randomVector = new Vector2(Range(-1.0f, 1.0f, local), Range(-1.0f, 1.0f, local));

            if (randomVector == Vector2.Zero) return new Vector2(0.0f, length);

            return Vector2.Normalize(randomVector) * length;
        }       

    }
}
