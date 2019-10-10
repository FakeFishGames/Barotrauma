using System;
using System.Diagnostics;

namespace Barotrauma.Networking
{
    static class NetIdUtils
    {
        /// <summary>
        /// Is newID more recent than oldID
        /// </summary>
        public static bool IdMoreRecent(ushort newID, ushort oldID)
        {
            uint id1 = newID;
            uint id2 = oldID;

            return
                (id1 > id2) && (id1 - id2 <= ushort.MaxValue / 2)
                   ||
                (id2 > id1) && (id2 - id1 > ushort.MaxValue / 2);
        }

        public static ushort Clamp(ushort id, ushort min, ushort max)
        {
            if (IdMoreRecent(min, max))
            {
                throw new ArgumentException($"Min cannot be larger than max ({min}, {max})");
            }

            if (!IdMoreRecent(id, min))
            {
                return min;
            }
            else if (IdMoreRecent(id, max))
            {
                return max;
            }

            return id;
        }

#if DEBUG
        public static void Test()
        {
            Debug.Assert(NetIdUtils.IdMoreRecent((ushort)2, (ushort)1));
            Debug.Assert(NetIdUtils.IdMoreRecent((ushort)2, (ushort)(ushort.MaxValue - 5)));
            Debug.Assert(!NetIdUtils.IdMoreRecent((ushort)ushort.MaxValue, (ushort)5));

            Debug.Assert(Clamp((ushort)5, (ushort)1, (ushort)10) == 5);
            Debug.Assert(Clamp((ushort)(ushort.MaxValue - 5), (ushort)(ushort.MaxValue - 2), (ushort)3) == (ushort)(ushort.MaxValue - 2));
        }
#endif
    }
}
