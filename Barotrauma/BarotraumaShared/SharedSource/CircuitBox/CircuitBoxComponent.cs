#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal partial class CircuitBoxComponent : CircuitBoxNode, ICircuitBoxIdentifiable
    {
        public readonly Item Item;
        public ushort ID { get; }

        public readonly ItemPrefab UsedResource;

        public CircuitBoxComponent(ushort id, Item item, Vector2 position, CircuitBox circuitBox, ItemPrefab usedResource): base(circuitBox)
        {
            if (item.Connections is null)
            {
                throw new ArgumentNullException(nameof(item.Connections), $"Tried to load a CircuitBoxNode with an item \"{item.Prefab.Name}\" that has no connections.");
            }

            var conns = item.Connections.Select(connection => new CircuitBoxNodeConnection(Vector2.Zero, this, connection, circuitBox)).ToList();

            Vector2 size = CalculateSize(conns);

            ID = id;
            Item = item;
            Size = size;
            Connectors = conns.Cast<CircuitBoxConnection>().ToImmutableArray();
            Position = position;
            UsedResource = usedResource;
            UpdatePositions();
        }

        public static Option<CircuitBoxComponent> TryLoadFromXML(ContentXElement element, CircuitBox circuitBox)
        {
            ushort id = element.GetAttributeUInt16("id", ICircuitBoxIdentifiable.NullComponentID);
            Vector2 position = element.GetAttributeVector2("position", Vector2.Zero);
            var itemIdOption = ItemSlotIndexPair.TryDeserializeFromXML(element, "backingitemid");
            Identifier usedResourceIdentifier = element.GetAttributeIdentifier("usedresource", Identifier.Empty);

            if (!itemIdOption.TryUnwrap(out var itemId) || itemId.FindItemInContainer(circuitBox.ComponentContainer) is not { } backingItem)
            {
                DebugConsole.ThrowErrorAndLogToGA("CircuitBoxComponent.TryLoadFromXML:IdNotFound",
                    $"Failed to find item with ID {itemId} for CircuitBoxNode with ID {id}");
                return Option.None;
            }

            if (!ItemPrefab.Prefabs.TryGet(usedResourceIdentifier, out var usedResource))
            {
                DebugConsole.ThrowErrorAndLogToGA("CircuitBoxComponent.TryLoadXML:UsedResourceNotFound",
                    $"Failed to find item prefab with identifier {usedResourceIdentifier} for CircuitBoxNode with ID {id}");
                return Option.None;
            }

            return Option.Some(new CircuitBoxComponent(id, backingItem, position, circuitBox, usedResource));
        }

        public XElement Save()
        {
            return new XElement("Component",
                new XAttribute("id", ID),
                new XAttribute("position", XMLExtensions.Vector2ToString(Position)),
                new XAttribute("backingitemid", ItemSlotIndexPair.Serialize(Item)),
                new XAttribute("usedresource", UsedResource.Identifier));
        }

        public void Remove()
        {
            if (Entity.Spawner is { } spawner && Screen.Selected is not { IsEditor: true })
            {
                spawner.AddEntityToRemoveQueue(Item);
                return;
            }

            // if EntitySpawner is not available
            Item.Remove();
        }
    }
}