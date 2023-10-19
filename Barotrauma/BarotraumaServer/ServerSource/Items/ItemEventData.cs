using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma;

partial class Item
{
    private readonly struct DroppedStackEventData : IEventData
    {
        public EventType EventType => EventType.DroppedStack;
        public readonly ImmutableArray<Item> Items;

        public DroppedStackEventData(IEnumerable<Item> items)
        {
            Items = items.Distinct().ToImmutableArray();
        }
    }
}
