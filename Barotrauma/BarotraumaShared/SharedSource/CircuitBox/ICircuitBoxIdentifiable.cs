using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public interface ICircuitBoxIdentifiable
    {
        public const ushort NullComponentID = ushort.MaxValue;

        public ushort ID { get; }

        public static ushort FindFreeID<T>(IReadOnlyCollection<T> ids) where T : ICircuitBoxIdentifiable
        {
            var sortedIds = ids.Select(static i => i.ID).ToImmutableHashSet();

            for (ushort i = 0; i < NullComponentID - 1; i++)
            {
                if (!sortedIds.Contains(i))
                {
                    return i;
                }
            }

            return NullComponentID;
        }
    }
}