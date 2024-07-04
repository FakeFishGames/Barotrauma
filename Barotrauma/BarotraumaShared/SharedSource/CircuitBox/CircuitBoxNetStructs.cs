#nullable enable

using System;
using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    [Flags]
    internal enum CircuitBoxResizeDirection
    {
        None = 0,
        Down = 1,
        Right = 2,
        Left = 4
    }

    // TODO this needs to be refactored at some point for reasons:
    // 1. We need to send 4 different ImmutableArray<short> for some network packets
    // 2. We have 3 identical remove events that are identical in signature
    // 3. We have 3 different events for selecting. nodes, wires, and server broadcast
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
        RenameLabel,
        AddLabel,
        RemoveLabel,
        ResizeLabel,
        RenameConnections,
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
    internal readonly record struct CircuitBoxAddLabelEvent(Vector2 Position, Color Color, NetLimitedString Header, NetLimitedString Body) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxServerAddLabelEvent(ushort ID, Vector2 Position, Vector2 Size, Color Color, NetLimitedString Header, NetLimitedString Body) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxResizeLabelEvent(ushort ID, Vector2 Position, Vector2 Size) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxRemoveLabelEvent(ImmutableArray<ushort> TargetIDs) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxAddComponentEvent(UInt32 PrefabIdentifier, Vector2 Position) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxServerCreateComponentEvent(ushort BackingItemId, UInt32 UsedResource, ushort ComponentId, Vector2 Position) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxRemoveComponentEvent(ImmutableArray<ushort> TargetIDs) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxMoveComponentEvent(ImmutableArray<ushort> TargetIDs, ImmutableArray<CircuitBoxInputOutputNode.Type> IOs, ImmutableArray<ushort> LabelIDs, Vector2 MoveAmount) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxSelectNodesEvent(ImmutableArray<ushort> TargetIDs, ImmutableArray<CircuitBoxInputOutputNode.Type> IOs, ImmutableArray<ushort> LabelIDs, bool Overwrite, ushort CharacterID) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxServerUpdateSelection(ImmutableArray<CircuitBoxIdSelectionPair> ComponentIds, ImmutableArray<CircuitBoxIdSelectionPair> WireIds, ImmutableArray<CircuitBoxTypeSelectionPair> InputOutputs, ImmutableArray<CircuitBoxIdSelectionPair> LabelIds) : INetSerializableStruct;

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
    internal readonly record struct CircuitBoxRenameLabelEvent(ushort LabelId, Color Color, NetLimitedString NewHeader, NetLimitedString NewBody) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxRenameConnectionLabelsEvent(CircuitBoxInputOutputNode.Type Type, NetDictionary<string, string> Override) : INetSerializableStruct;


    [NetworkSerialize]
    internal readonly record struct CircuitBoxErrorEvent(string Message) : INetSerializableStruct;

    [NetworkSerialize]
    internal readonly record struct CircuitBoxInitializeStateFromServerEvent(
        ImmutableArray<CircuitBoxServerCreateComponentEvent> Components,
        ImmutableArray<CircuitBoxServerCreateWireEvent> Wires,
        ImmutableArray<CircuitBoxServerAddLabelEvent> Labels,
        ImmutableArray<CircuitBoxRenameConnectionLabelsEvent> LabelOverrides,
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
                CircuitBoxRenameLabelEvent
                    => CircuitBoxOpcode.RenameLabel,
                (CircuitBoxAddLabelEvent or CircuitBoxServerAddLabelEvent)
                    => CircuitBoxOpcode.AddLabel,
                CircuitBoxRemoveLabelEvent
                    => CircuitBoxOpcode.RemoveLabel,
                CircuitBoxResizeLabelEvent
                    => CircuitBoxOpcode.ResizeLabel,
                CircuitBoxRenameConnectionLabelsEvent
                    => CircuitBoxOpcode.RenameConnections,
                _ => throw new ArgumentOutOfRangeException(nameof(Data))
            };
    }
}