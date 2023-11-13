#nullable enable

using System;
using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public enum CircuitBoxOpcode
    {
        Error,
        Cursor,
        AddComponent,
        MoveComponent,
        AddWire,
        RemoveWire,
        SelectComponents,
        SelectWires,
        UpdateSelection,
        DeleteComponent,
        ServerInitialize
    }

    [NetworkSerialize]
    internal readonly record struct NetCircuitBoxHeader(CircuitBoxOpcode Opcode, ushort ItemID, byte ComponentIndex) : INetSerializableStruct
    {
        public Option<CircuitBox> FindTarget() => CircuitBox.FindCircuitBox(ItemID, ComponentIndex);
    }

    [NetworkSerialize]
    internal readonly record struct CircuitBoxConnectorIdentifier(Identifier SignalConnection, Option<ushort> TargetId) : INetSerializableStruct
    {
        public static CircuitBoxConnectorIdentifier FromConnection(CircuitBoxConnection connection) =>
            connection switch
            {
                (CircuitBoxInputConnection or CircuitBoxOutputConnection)
                    => new CircuitBoxConnectorIdentifier(connection.Name.ToIdentifier(), Option.None),

                CircuitBoxNodeConnection nodeConnection
                    => new CircuitBoxConnectorIdentifier(connection.Name.ToIdentifier(), Option.Some(nodeConnection.Component.ID)),

                _ => throw new ArgumentOutOfRangeException(nameof(connection))
            };

        public Option<CircuitBoxConnection> FindConnection(CircuitBox circuitBox)
        {
            if (!TargetId.TryUnwrap(out var id))
            {
                return circuitBox.FindInputOutputConnection(SignalConnection);
            }

            foreach (CircuitBoxComponent boxNode in circuitBox.Components)
            {
                if (boxNode.ID != id) { continue; }

                foreach (var conn in boxNode.Connectors)
                {
                    if (conn.Name != SignalConnection) { continue; }

                    return Option.Some(conn);
                }
            }

            return Option.None;
        }

        public XElement Save(string name) => new XElement(name,
            new XAttribute("name", SignalConnection),
            new XAttribute("target", TargetId.TryUnwrap(out var value) ? value.ToString() : string.Empty));

        public static CircuitBoxConnectorIdentifier Load(ContentXElement element)
        {
            string? name = element.GetAttributeString("name", string.Empty);
            string? target = element.GetAttributeString("target", string.Empty);

            Option<ushort> targetId = Option.None;
            if (!string.IsNullOrWhiteSpace(target))
            {
                targetId = ushort.TryParse(target, out var value) ? Option.Some(value) : Option.None;
            }

            return new CircuitBoxConnectorIdentifier(name.ToIdentifier(), targetId);
        }

        public override string ToString()
            => $"{{Name: {SignalConnection}, ID: {(TargetId.TryUnwrap(out var value) ? value.ToString() : "N/A")}}}";
    }

    [NetworkSerialize]
    internal readonly record struct CircuitBoxAddComponentEvent(UInt32 PrefabIdentifier, Vector2 Position) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxServerCreateComponentEvent(ushort BackingItemId, UInt32 UsedResource, ushort ComponentId, Vector2 Position) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxRemoveComponentEvent(ImmutableArray<ushort> TargetIDs) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxMoveComponentEvent(ImmutableArray<ushort> TargetIDs, ImmutableArray<CircuitBoxInputOutputNode.Type> IOs, Vector2 MoveAmount) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxSelectNodesEvent(ImmutableArray<ushort> TargetIDs, ImmutableArray<CircuitBoxInputOutputNode.Type> IOs, bool Overwrite, ushort CharacterID) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxServerUpdateSelection(ImmutableArray<CircuitBoxIdSelectionPair> ComponentIds, ImmutableArray<CircuitBoxIdSelectionPair> WireIds, ImmutableArray<CircuitBoxTypeSelectionPair> InputOutputs) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxIdSelectionPair(ushort ID, Option<ushort> SelectedBy) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxTypeSelectionPair(CircuitBoxInputOutputNode.Type Type, Option<ushort> SelectedBy) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxSelectWiresEvent(ImmutableArray<ushort> TargetIDs, bool Overwrite, ushort CharacterID) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxClientAddWireEvent(Color Color, CircuitBoxConnectorIdentifier Start, CircuitBoxConnectorIdentifier End, UInt32 SelectedWirePrefabIdentifier) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxServerCreateWireEvent(CircuitBoxClientAddWireEvent Request, ushort WireId, Option<ushort> BackingItemId) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxRemoveWireEvent(ImmutableArray<ushort> TargetIDs) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxErrorEvent(string Message) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxInitializeStateFromServerEvent(
        ImmutableArray<CircuitBoxServerCreateComponentEvent> Components,
        ImmutableArray<CircuitBoxServerCreateWireEvent> Wires,
        Vector2 InputPos,
        Vector2 OutputPos) : INetSerializableStruct;

    internal readonly record struct CircuitBoxEventData(INetSerializableStruct Data) : ItemComponent.IEventData
    {
        public CircuitBoxOpcode Opcode =>
            Data switch
            {
                (CircuitBoxAddComponentEvent or CircuitBoxServerCreateComponentEvent)
                    => CircuitBoxOpcode.AddComponent,
                CircuitBoxRemoveComponentEvent
                    => CircuitBoxOpcode.DeleteComponent,
                CircuitBoxMoveComponentEvent
                    => CircuitBoxOpcode.MoveComponent,
                CircuitBoxSelectNodesEvent
                    => CircuitBoxOpcode.SelectComponents,
                CircuitBoxSelectWiresEvent
                    => CircuitBoxOpcode.SelectWires,
                CircuitBoxServerUpdateSelection
                    => CircuitBoxOpcode.UpdateSelection,
                (CircuitBoxClientAddWireEvent or CircuitBoxServerCreateWireEvent)
                    => CircuitBoxOpcode.AddWire,
                CircuitBoxRemoveWireEvent
                    => CircuitBoxOpcode.RemoveWire,
                CircuitBoxInitializeStateFromServerEvent
                    => CircuitBoxOpcode.ServerInitialize,
                _ => throw new ArgumentOutOfRangeException(nameof(Data))
            };
    }
}