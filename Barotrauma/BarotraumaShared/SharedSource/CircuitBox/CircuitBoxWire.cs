#nullable enable

using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal partial class CircuitBoxWire : CircuitBoxSelectable, ICircuitBoxIdentifiable
    {
        public CircuitBoxConnection From, To;
        public readonly Option<Item> BackingWire;

        public readonly Color Color;
        public readonly ItemPrefab UsedItemPrefab;

        public ushort ID { get; }

        public CircuitBoxWire(CircuitBox circuitBox, ushort Id, Option<Item> backingItem, CircuitBoxConnection from, CircuitBoxConnection to, ItemPrefab prefab)
        {
            ID = Id;
            From = from;
            To = to;
            BackingWire = backingItem;
            Color = prefab.SpriteColor;
            UsedItemPrefab = prefab;
#if CLIENT
            Renderer = new CircuitBoxWireRenderer(Option.Some(this), to.AnchorPoint, from.AnchorPoint, Color, circuitBox.WireSprite);
#endif
            EnsureWireConnected();
        }

        public XElement Save()
        {
            XElement element = new XElement("Wire",
                new XAttribute("id", ID),
                new XAttribute("backingitemid", BackingWire.TryUnwrap(out var item) ? ItemSlotIndexPair.Serialize(item) : string.Empty),
                new XAttribute("prefab", UsedItemPrefab.Identifier));

            XElement fromElement = CircuitBoxConnectorIdentifier.FromConnection(From).Save("From"),
                     toElement = CircuitBoxConnectorIdentifier.FromConnection(To).Save("To");

            element.Add(fromElement);
            element.Add(toElement);

            return element;
        }

        public static Option<CircuitBoxWire> TryLoadFromXML(ContentXElement element, CircuitBox circuitBox)
        {
            ushort id = element.GetAttributeUInt16("id", ICircuitBoxIdentifiable.NullComponentID);
            var backingItemIdOption = ItemSlotIndexPair.TryDeserializeFromXML(element, "backingitemid");
            Identifier usedPrefabIdentifier = element.GetAttributeIdentifier("prefab", Identifier.Empty);

            if (!ItemPrefab.Prefabs.TryGet(usedPrefabIdentifier, out var itemPrefab))
            {
                DebugConsole.ThrowErrorAndLogToGA("CircuitBoxWire.TryLoadFromXML:PrefabNotFound",
                    $"Failed to find prefab used to create wire with identifier {usedPrefabIdentifier} for CircuitBoxWire with ID {id}");
                return Option.None;
            }

            Option<Item> backingItem = Option.None;
            if (backingItemIdOption.TryUnwrap(out var backingItemIdPair))
            {
                if (backingItemIdPair.FindItemInContainer(circuitBox.WireContainer) is { } item)
                {
                    backingItem = Option.Some(item);
                }
                else
                {
                    DebugConsole.ThrowErrorAndLogToGA("CircuitBoxWire.TryLoadFromXML:IdNotFound",
                        $"Failed to find item with ID {backingItemIdPair} for CircuitBoxWire with ID {id}");
                    return Option.None;
                }
            }

            Option<CircuitBoxConnection> From = Option.None,
                                         To = Option.None;

            foreach (ContentXElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "from":
                        var fromIdentifier = CircuitBoxConnectorIdentifier.Load(subElement);
                        if (fromIdentifier.FindConnection(circuitBox).TryUnwrap(out var fromConnection))
                        {
                            From = Option.Some(fromConnection);
                        }
                        break;
                    case "to":
                        var toIdentifier = CircuitBoxConnectorIdentifier.Load(subElement);
                        if (toIdentifier.FindConnection(circuitBox).TryUnwrap(out var toConnection))
                        {
                            To = Option.Some(toConnection);
                        }
                        break;
                }
            }

            if (From.TryUnwrap(out var from) && To.TryUnwrap(out var to))
            {
                return Option.Some(new CircuitBoxWire(circuitBox, id, backingItem, from, to, itemPrefab));
            }

            DebugConsole.ThrowErrorAndLogToGA("CircuitBoxWire.TryLoadFromXML:MissingFromOrTo",
                $"Failed to load CircuitBoxWire with ID {id}, missing \"From\" or \"To\" connection.");

            return Option.None;
        }

        public void EnsureWireConnected()
        {
            EnsureExternalConnection(From, To);
            EnsureExternalConnection(To, From);

            if (!BackingWire.TryUnwrap(out var item) || item.GetComponent<Wire>() is not { } wire) { return; }

            wire.DropOnConnect = false;

            From.Connection.ConnectWire(wire);
            To.Connection.ConnectWire(wire);

            wire.Connect(From.Connection, 0, addNode: false, sendNetworkEvent: false);
            wire.Connect(To.Connection, 1, addNode: false, sendNetworkEvent: false);

            static void EnsureExternalConnection(CircuitBoxConnection one, CircuitBoxConnection two)
            {
                switch (one)
                {
                    case CircuitBoxInputConnection input:
                    {
                        if (input.ExternallyConnectedTo.Contains(two)) { break; }
                        input.ExternallyConnectedTo.Add(two);
                        break;
                    }
                    case CircuitBoxOutputConnection output:
                    {
                        if (output.ExternallyConnectedFrom.Contains(two)) { break; }
                        output.ExternallyConnectedFrom.Add(two);
                        break;
                    }
                    case CircuitBoxNodeConnection node when two is CircuitBoxOutputConnection output:
                    {
                        if (node.Connection.CircuitBoxConnections.Contains(output)) { break; }
                        node.Connection.CircuitBoxConnections.Add(output);
                        break;
                    }
                    case CircuitBoxNodeConnection node when two is CircuitBoxInputConnection input:
                    {
                        if (!node.Connection.CircuitBoxConnections.Contains(input))
                        {
                            node.Connection.CircuitBoxConnections.Add(input);
                        }
                        if (!node.ExternallyConnectedFrom.Contains(input))
                        {
                            node.ExternallyConnectedFrom.Add(input);
                        }
                        break;
                    }
                }
            }
        }

        public void Remove()
        {
            // client should not remove wires
            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            if (!BackingWire.TryUnwrap(out var wireItem)) { return; }

            if (Entity.Spawner is { } spawner && Screen.Selected is not { IsEditor: true })
            {
                spawner.AddEntityToRemoveQueue(wireItem);
                return;
            }

            Wire? wire = wireItem.GetComponent<Wire>();
            if (wire is not null)
            {
                From.Connection.DisconnectWire(wire);
                To.Connection.DisconnectWire(wire);
            }
            // if EntitySpawner is not available
            wireItem.Remove();
        }

        public static ItemPrefab DefaultWirePrefab => ItemPrefab.Prefabs[Tags.RedWire];
        public static ItemPrefab SelectedWirePrefab = DefaultWirePrefab;
        public static readonly Color DefaultWireColor = DefaultWirePrefab.SpriteColor;
    }
}