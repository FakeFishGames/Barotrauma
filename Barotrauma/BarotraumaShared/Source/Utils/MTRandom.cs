using System;

namespace Barotrauma
{
    /// <summary>
    /// Mersenne Twister based random
    /// </summary>
    public sealed class MTRandom : Random
    {
        private const int N = 624;
        private const int M = 397;
        private const uint MATRIX_A = 0x9908b0dfU;
        private const uint UPPER_MASK = 0x80000000U;
        private const uint LOWER_MASK = 0x7fffffffU;
        private const uint TEMPER1 = 0x9d2c5680U;
        private const uint TEMPER2 = 0xefc60000U;
        private const int TEMPER3 = 11;
        private const int TEMPER4 = 7;
        private const int TEMPER5 = 15;
        private const int TEMPER6 = 18;

        private UInt32[] mt;
        private int mti;
        private UInt32[] mag01;

        private const double c_realUnitInt = 1.0 / ((double)int.MaxValue + 1.0);

        /// <summary>
        /// Constructor with randomized seed
        /// </summary>
        public MTRandom()
        {
            Initialize((uint)Environment.TickCount);
        }

        /// <summary>
        /// Constructor with provided 32 bit seed
        /// </summary>
        public MTRandom(int seed)
        {
            Initialize((uint)Math.Abs(seed));
        }

        /// <summary>
        /// (Re)initialize this instance with provided 32 bit seed
        /// </summary>
        private void Initialize(uint seed)
        {
            mt = new UInt32[N];
            mti = N + 1;
            mag01 = new UInt32[] { 0x0U, MATRIX_A };
            mt[0] = seed;
            for (int i = 1; i < N; i++)
                mt[i] = (UInt32)(1812433253 * (mt[i - 1] ^ (mt[i - 1] >> 30)) + i);
        }

        /// <summary>
        /// Generates a random value from UInt32.MinValue to UInt32.MaxValue, inclusively
        /// </summary>
        private uint NextUInt32()
        {
            UInt32 y;
            if (mti >= N)
            {
                GenRandAll();
                mti = 0;
            }
            y = mt[mti++];
            y ^= (y >> TEMPER3);
            y ^= (y << TEMPER4) & TEMPER1;
            y ^= (y << TEMPER5) & TEMPER2;
            y ^= (y >> TEMPER6);
            return y;
        }

        /// <summary>
        /// Generates a random value that is greater or equal than 0 and less than Int32.MaxValue
        /// </summary>
        public override int Next()
        {
            var retval = (int)(0x7FFFFFFF & NextUInt32());
            if (retval == 0x7FFFFFFF)
                return NextInt32();
            return retval;
        }

        /// <summary>
        /// Returns a random value is greater or equal than 0 and less than maxValue
        /// </summary>
        public override int Next(int maxValue)
        {
            return (int)(NextDouble() * maxValue);
        }

        /// <summary>
        /// Generates a random value greater or equal than 0 and less or equal than Int32.MaxValue (inclusively)
        /// </summary>
        public int NextInt32()
        {
            return (int)(0x7FFFFFFF & NextUInt32());
        }

        /// <summary>
        /// Returns random value larger or equal to 0.0 and less than 1.0
        /// </summary>
        public override double NextDouble()
        {
            return c_realUnitInt * NextInt32();
        }

        private void GenRandAll()
        {
            int kk = 1;
            UInt32 y;
            UInt32 p;
            y = mt[0] & UPPER_MASK;
            do
            {
                p = mt[kk];
                mt[kk - 1] = mt[kk + (M - 1)] ^ ((y | (p & LOWER_MASK)) >> 1) ^ mag01[p & 1];
                y = p & UPPER_MASK;
            } while (++kk < N - M + 1);
            do
            {
                p = mt[kk];
                mt[kk - 1] = mt[kk + (M - N - 1)] ^ ((y | (p & LOWER_MASK)) >> 1) ^ mag01[p & 1];
                y = p & UPPER_MASK;
            } while (++kk < N);
            p = mt[0];
            mt[N - 1] = mt[M - 1] ^ ((y | (p & LOWER_MASK)) >> 1) ^ mag01[p & 1];
        }
    }
}
