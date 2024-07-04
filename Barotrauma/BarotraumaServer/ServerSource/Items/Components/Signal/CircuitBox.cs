#nullable enable

using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Items.Components
{
    internal sealed partial class CircuitBox
    {
        /// <summary>
        /// If the server needs to initialize the circuit box to the clients
        /// instead of the clients loading it from the save file.
        /// </summary>
        private bool needsServerInitialization;

        /// <summary>
        /// When in multiplayer and the circuit box are loaded from the player inventory,
        /// We only load the components from XML on the server side
        /// since only the server has access to CharacterCampaignData
        /// and then send a network event syncing the loaded properties.
        /// But circuit box properties are too complex to
        /// sync using the existing syncing logic,
        /// so we instead send the state using <see cref="CircuitBoxInitializeStateFromServerEvent"/>.
        /// </summary>
        public void MarkServerRequiredInitialization()
            => needsServerInitialization = true;

        public partial void OnDeselected(Character c)
        {
            ClearAllSelectionsInternal(c.ID);
            BroadcastSelectionStatus();
        }

        public void ServerRead(INetSerializableStruct data, Client c)
        {
            switch (data)
            {
                case NetCircuitBoxCursorInfo { RecordedPositions.Length: 10 } cursorInfo:
                {
                    RelayCursorState(cursorInfo, c);
                    break;
                }
            }
        }

        private void RelayCursorState(NetCircuitBoxCursorInfo data, Client sender)
        {
            if (GameMain.Server is null || !IsRoundRunning()) { return; }

            SendToAll(CircuitBoxOpcode.Cursor, data with { CharacterID = sender.CharacterID }, FilterClients);

            bool FilterClients(Client client)
            {
                // ReSharper disable once RedundantAssignment
                bool isSender = client == sender;
#if DEBUG
                // Shown own cursor in debug builds
                isSender = false;
#endif
                return !isSender && client.Character is not null && client.Character.SelectedItem == item;
            }
        }

        public void SendToClient(CircuitBoxOpcode opcode, INetSerializableStruct data, Client targetClient)
        {
            var (msg, deliveryMethod) = PrepareToSend(opcode, data);

            GameMain.Server?.ServerPeer?.Send(msg, targetClient.Connection, deliveryMethod);
        }

        public void SendToAll(CircuitBoxOpcode opcode, INetSerializableStruct data, Func<Client, bool>? predicate = null)
        {
            var (msg, deliveryMethod) = PrepareToSend(opcode, data);

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (predicate is not null && !predicate(client)) { continue; }

                GameMain.Server?.ServerPeer?.Send(msg, client.Connection, deliveryMethod);
            }
        }

        private (IWriteMessage Message, DeliveryMethod DeliveryMethod) PrepareToSend(CircuitBoxOpcode opcode, INetSerializableStruct data)
        {
            IWriteMessage msg = new WriteOnlyMessage().WithHeader(ServerPacketHeader.CIRCUITBOX);

            msg.WriteNetSerializableStruct(new NetCircuitBoxHeader(
                Opcode: opcode,
                ItemID: item.ID,
                ComponentIndex: (byte)item.GetComponentIndex(this)));

            msg.WriteNetSerializableStruct(data);

            DeliveryMethod deliveryMethod =
                UnrealiableOpcodes.Contains(opcode)
                    ? DeliveryMethod.Unreliable
                    : DeliveryMethod.Reliable;

            return (msg, deliveryMethod);
        }

        public void CreateServerEvent(INetSerializableStruct data)
            => item.CreateServerEvent(this, new CircuitBoxEventData(data));

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = null)
        {
            if (extraData is null) { return; }

            var eventData = ExtractEventData<CircuitBoxEventData>(extraData);
            msg.WriteByte((byte)eventData.Opcode);
            msg.WriteNetSerializableStruct(eventData.Data);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            var header = (CircuitBoxOpcode)msg.ReadByte();
            switch (header)
            {
                case CircuitBoxOpcode.AddComponent:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxAddComponentEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    var prefab = ItemPrefab.Prefabs.Find(p => p.UintIdentifier == data.PrefabIdentifier);
                    if (prefab is null)
                    {
                        ThrowError("Unable to add component because the prefab was not found.", c);
                        return;
                    }

                    if (IsFull || !GetApplicableResourcePlayerHas(prefab, c.Character).TryUnwrap(out var resource)) { return; }

                    ushort id = ICircuitBoxIdentifiable.FindFreeID(Components);
                    if (id is ICircuitBoxIdentifiable.NullComponentID)
                    {
                        ThrowError("Unable to add component because there are no available IDs left.", c);
                        return;
                    }

                    bool result = AddComponentInternal(id, prefab, resource.Prefab, data.Position, c.Character, it =>
                    {
                        CreateServerEvent(new CircuitBoxServerCreateComponentEvent(it.ID, resource.Prefab.UintIdentifier, id, data.Position));
                    });

                    if (!result)
                    {
                        ThrowError("Unable to add component because the component could not be created.", c);
                        return;
                    }

                    GameServer.Log($"{NetworkMember.ClientLogName(c)} added a {prefab.Name} into a circuit box.", ServerLog.MessageType.Wiring);
                    RemoveItem(resource);
                    break;
                }
                case CircuitBoxOpcode.MoveComponent:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxMoveComponentEvent>(msg);
                    if (!item.CanClientAccess(c)) { break; }

                    MoveNodesInternal(data.TargetIDs, data.IOs, data.LabelIDs, data.MoveAmount);
                    CreateServerEvent(data);
                    break;
                }
                case CircuitBoxOpcode.DeleteComponent:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRemoveComponentEvent>(msg);
                    if (!data.TargetIDs.Any() || !CanAccessAndUnlocked(c)) { break; }

                    CreateRefundItemsForUsedResources(data.TargetIDs, c.Character);
                    GameServer.Log($"{NetworkMember.ClientLogName(c)} removed {GetLogComponentName(data.TargetIDs)} from circuit box.", ServerLog.MessageType.Wiring);
                    RemoveComponentInternal(data.TargetIDs);
                    CreateServerEvent(data);
                    break;
                }
                case CircuitBoxOpcode.SelectComponents:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxSelectNodesEvent>(msg);
                    if (!item.CanClientAccess(c)) { break; }

                    SelectComponentsInternal(data.TargetIDs, c.CharacterID, data.Overwrite);
                    SelectInputOutputInternal(data.IOs, c.CharacterID, data.Overwrite);
                    SelectLabelsInternal(data.LabelIDs, c.CharacterID, data.Overwrite);
                    BroadcastSelectionStatus();
                    break;
                }
                case CircuitBoxOpcode.SelectWires:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxSelectWiresEvent>(msg);
                    if (!item.CanClientAccess(c)) { break; }

                    SelectWiresInternal(data.TargetIDs, c.CharacterID, data.Overwrite);
                    BroadcastSelectionStatus();
                    break;
                }
                case CircuitBoxOpcode.AddWire:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxClientAddWireEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    var prefab = ItemPrefab.Prefabs.Find(p => p.UintIdentifier == data.SelectedWirePrefabIdentifier);
                    if (prefab is null)
                    {
                        ThrowError($"Unable to connect wire because wire by identifier \"{data.SelectedWirePrefabIdentifier}\" was not found.", c);
                        break;
                    }

                    if (data.Start.FindConnection(this).TryUnwrap(out var start) &&
                        data.End.FindConnection(this).TryUnwrap(out var end))
                    {
                        bool result = Connect(start, end, wire =>
                        {
                            CreateServerEvent(new CircuitBoxServerCreateWireEvent(data with { Start = wire.Start, End = wire.End }, wire.ID, wire.Item.Select(static i => i.ID)));
                        }, prefab);

                        if (!result)
                        {
                            ThrowError("Unable to connect wire because the circuit box rejected it.", c);
                        }

                        GameServer.Log($"{NetworkMember.ClientLogName(c)} connected a wire from {start.Name} to {end.Name} in a circuit box.", ServerLog.MessageType.Wiring);
                    }
                    else
                    {
                        ThrowError($"Unable to connect wire because the start or end connection was not found. (start: {data.Start}, end: {data.End})", c);
                    }

                    break;
                }
                case CircuitBoxOpcode.RemoveWire:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRemoveWireEvent>(msg);
                    if (!data.TargetIDs.Any() || !CanAccessAndUnlocked(c)) { break; }

                    GameServer.Log($"{NetworkMember.ClientLogName(c)} removed {GetLogWireName(data.TargetIDs)} from circuit box.", ServerLog.MessageType.Wiring);
                    RemoveWireInternal(data.TargetIDs);
                    CreateServerEvent(data);
                    break;
                }
                case CircuitBoxOpcode.RenameLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRenameLabelEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    RenameLabelInternal(data.LabelId, data.Color, data.NewHeader, data.NewBody);
                    CreateServerEvent(data);
                    break;
                }
                case CircuitBoxOpcode.AddLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxAddLabelEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    ushort id = ICircuitBoxIdentifiable.FindFreeID(Labels);
                    if (id is ICircuitBoxIdentifiable.NullComponentID)
                    {
                        ThrowError("Unable to add label because there are no available IDs left.", c);
                        return;
                    }

                    AddLabelInternal(id, data.Color, data.Position, data.Header, data.Body);
                    CreateServerEvent(new CircuitBoxServerAddLabelEvent(id, data.Position, new Vector2(256), data.Color, data.Header, data.Body));
                    break;
                }
                case CircuitBoxOpcode.RemoveLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRemoveLabelEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    RemoveLabelInternal(data.TargetIDs);
                    CreateServerEvent(data);
                    break;
                }
                case CircuitBoxOpcode.ResizeLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxResizeLabelEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    ResizeLabelInternal(data.ID, data.Position, data.Size);
                    CreateServerEvent(data with { Size = Vector2.Max(data.Size, CircuitBoxLabelNode.MinSize) });
                    break;
                }
                case CircuitBoxOpcode.RenameConnections:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRenameConnectionLabelsEvent>(msg);
                    if (!CanAccessAndUnlocked(c)) { break; }

                    RenameConnectionLabelsInternal(data.Type, data.Override.ToDictionary());
                    CreateServerEvent(data);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(header), header, "This opcode cannot be handled using entity events");
            }

            string GetLogComponentName(IReadOnlyList<ushort> ids)
            {
                if (ids.Count > 1) { return $"{ids.Count} components"; }
                return Components.FirstOrDefault(comp => ids.Contains(comp.ID))?.Item.Name ?? "[UNKNOWN]";
            }

            string GetLogWireName(IReadOnlyList<ushort> ids)
            {
                if (ids.Count > 1) { return $"{ids.Count} wires"; }
                if (Wires.FirstOrDefault(w => ids.Contains(w.ID)) is not { } wire) { return "[UNKNOWN]"; }

                return wire.BackingWire.TryUnwrap(out var backingWire) ? backingWire.Name : "a wire";
            }

            bool CanAccessAndUnlocked(Client client) => item.CanClientAccess(client) && !Locked;
        }

        /// <summary>
        /// Creates an event that overrides the state of the circuit box for all clients.
        /// This is only required if the circuit box is loaded from the players inventory in multiplayer.
        /// </summary>
        public void CreateInitializationEvent()
        {
            Vector2 inputPos = Vector2.Zero,
                    outputPos = Vector2.Zero;

            foreach (var ioNode in InputOutputNodes)
            {
                switch (ioNode.NodeType)
                {
                    case CircuitBoxInputOutputNode.Type.Input:
                        inputPos = ioNode.Position;
                        break;
                    case CircuitBoxInputOutputNode.Type.Output:
                        outputPos = ioNode.Position;
                        break;
                }
            }

            CircuitBoxInitializeStateFromServerEvent data = new(
                Components: Components.Select(EventFromComponent).ToImmutableArray(),
                Wires: Wires.Select(EventFromWire).ToImmutableArray(),
                Labels: Labels.Select(EventFromLabel).ToImmutableArray(),
                LabelOverrides: InputOutputNodes.Select(EventFromLabelOverride).ToImmutableArray(),
                InputPos: inputPos,
                OutputPos: outputPos);

            CreateServerEvent(data);

            static CircuitBoxServerCreateComponentEvent EventFromComponent(CircuitBoxComponent component)
                => new(component.Item.ID, component.UsedResource.UintIdentifier, component.ID, component.Position);

            static CircuitBoxServerCreateWireEvent EventFromWire(CircuitBoxWire wire)
            {
                var backingWire = wire.BackingWire.Select(static i => i.ID);
                var from = CircuitBoxConnectorIdentifier.FromConnection(wire.From);
                var to = CircuitBoxConnectorIdentifier.FromConnection(wire.To);

                var request = new CircuitBoxClientAddWireEvent(wire.Color, from, to, wire.UsedItemPrefab.UintIdentifier);
                return new CircuitBoxServerCreateWireEvent(request, wire.ID, backingWire);
            }

            static CircuitBoxServerAddLabelEvent EventFromLabel(CircuitBoxLabelNode label)
                => new(label.ID, label.Position, label.Size, label.Color, label.HeaderText, label.BodyText);

            static CircuitBoxRenameConnectionLabelsEvent EventFromLabelOverride(CircuitBoxInputOutputNode node)
                => new(node.NodeType, node.ConnectionLabelOverrides.ToNetDictionary());
        }

        // we don't care about updating the view on server
        public partial void OnViewUpdateProjSpecific() { }

        private void ThrowError(string message, Client c)
        {
            DebugConsole.ThrowError(message,
                contentPackage: item.Prefab.ContentPackage);
            SendToClient(CircuitBoxOpcode.Error, new CircuitBoxErrorEvent(message), c);
        }

        private void BroadcastSelectionStatus()
        {
            var nodes = Components.Select(static c => new CircuitBoxIdSelectionPair(c.ID, c.IsSelected ? Option.Some(c.SelectedBy) : Option.None)).ToImmutableArray();
            var wires = Wires.Select(static w => new CircuitBoxIdSelectionPair(w.ID, w.IsSelected ? Option.Some(w.SelectedBy) : Option.None)).ToImmutableArray();
            var ios = InputOutputNodes.Select(static n => new CircuitBoxTypeSelectionPair(n.NodeType, n.IsSelected ? Option.Some(n.SelectedBy) : Option.None)).ToImmutableArray();
            var labels = Labels.Select(static n => new CircuitBoxIdSelectionPair(n.ID, n.IsSelected ? Option.Some(n.SelectedBy) : Option.None)).ToImmutableArray();

            CreateServerEvent(new CircuitBoxServerUpdateSelection(nodes, wires, ios, labels));
        }
    }
}