using System;
using System.Diagnostics;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Helper class for dealing with 16-bit IDs that wrap around ushort.MaxValue
    /// </summary>
    static class NetIdUtils
    {
        /// <summary>
        /// Is newID more recent than oldID, i.e. newId > oldId accounting for ushort rollover
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

        /// <summary>
        /// newId >= oldId accounting for ushort rollover (newer or equals)
        /// </summary>
        public static bool IdMoreRecentOrMatches(ushort newId, ushort oldId)
            => !IdMoreRecent(oldId, newId);

        /// <summary>
        /// Returns some ID that is older than the input ID. There are no guarantees
        /// regarding its relation to values other than the input.
        /// </summary>
        public static ushort GetIdOlderThan(ushort id)
#if DEBUG
            // Debug implementation has some RNG to discourage bad assumptions about the return value
            => unchecked((ushort)(id - 1 - Rand.Int(500, sync: Rand.RandSync.Unsynced)));
#else
            // Release implementation favors performance
            => unchecked((ushort)(id - 1));
#endif

        public static ushort Difference(ushort id1, ushort id2)
        {
            int diff = id2 > id1 ? id2 - id1 : id1 - id2;
            return (ushort)(diff > ushort.MaxValue / 2 ? ushort.MaxValue - diff : diff);           
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

        /// <summary>
        /// Is the current ID valid given the previous ID and latest possible ID (not smaller than the previous ID or larger than the latest ID)
        /// </summary>
        public static bool IsValidId(ushort currentId, ushort previousId, ushort latestPossibleId)
        {
            //cannot be valid if more recent than the latest Id
            if (IdMoreRecent(currentId, latestPossibleId)) { return false; }

            //normally the id needs to be more recent than the previous id,
            //but there's a special case when the previous id was 0:
            //  if a client reconnects mid-round and tries to jump from the unitialized state (0) back to some previous high id (> ushort.MaxValue / 2),
            //  this would normally get interpreted as trying to jump backwards, but in this case we'll allow it
            return IdMoreRecent(currentId, previousId) || (previousId == 0 && currentId > ushort.MaxValue / 2);
        }

#if DEBUG
        public static void Test()
        {
            Debug.Assert(IdMoreRecent((ushort)2, (ushort)1));
            Debug.Assert(IdMoreRecent((ushort)2, (ushort)(ushort.MaxValue - 5)));
            Debug.Assert(!IdMoreRecent((ushort)ushort.MaxValue, (ushort)5));

            Debug.Assert(Clamp((ushort)5, (ushort)1, (ushort)10) == 5);
            Debug.Assert(Clamp((ushort)(ushort.MaxValue - 5), (ushort)(ushort.MaxValue - 2), (ushort)3) == (ushort)(ushort.MaxValue - 2));

            Debug.Assert(IsValidId((ushort)10, (ushort)1, (ushort)10));
            Debug.Assert(!IsValidId((ushort)11, (ushort)1, (ushort)10));

            Debug.Assert(IsValidId((ushort)1, (ushort)(ushort.MaxValue - 5), (ushort)10));
            Debug.Assert(!IsValidId((ushort)(ushort.MaxValue - 6), (ushort)(ushort.MaxValue - 5), (ushort)10));

            Debug.Assert(IsValidId((ushort)0, (ushort)(ushort.MaxValue - 100), (ushort)5));
            Debug.Assert(!IsValidId((ushort)(ushort.MaxValue - 101), (ushort)(ushort.MaxValue - 100), (ushort)ushort.MaxValue));

            Debug.Assert(Difference((ushort)0, (ushort)56) == 56);
            Debug.Assert(Difference((ushort)56, (ushort)0) == 56);
            Debug.Assert(Difference((ushort)5, (ushort)(ushort.MaxValue - 101)) == 106);
            Debug.Assert(Difference((ushort)(ushort.MaxValue - 101), (ushort)5) == 106);
        }
#endif
    }
}
