#nullable enable
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
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

    public readonly struct SetHighlightEventData : IEventData
    {
        public EventType EventType => EventType.SetHighlight;
        public readonly bool Highlighted;
        public readonly Color Color;

        public readonly ImmutableArray<Client> TargetClients;

        public SetHighlightEventData(bool highlighted, Color color, IEnumerable<Client>? targetClients)
        {
            Highlighted = highlighted;
            Color = color;
            TargetClients = (targetClients ?? Enumerable.Empty<Client>()).ToImmutableArray();
        }
    }
}
