#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    internal sealed partial class CircuitBox
    {
        public CircuitBoxUI? UI;
        public readonly Dictionary<Character, CircuitBoxCursor> ActiveCursors = new Dictionary<Character, CircuitBoxCursor>();
        public Option<ItemPrefab> HeldComponent = Option.None;

        private const float CursorUpdateInterval = 1f;
        private float cursorUpdateTimer;

        private readonly Vector2[] recordedCursorPositions = new Vector2[10];
        private Option<Vector2> recordedDragStart = Option.None;
        private Option<ItemPrefab> recordedHeldPrefab = Option.None;

        /// <summary>
        /// If the circuit box was initialized by the server instead of from the save file.
        /// Used to ensure the wires the server sends are properly connected up when we load in.
        /// </summary>
        private bool wasInitializedByServer;

        public Sprite? WireSprite { get; private set; }
        public Sprite? ConnectionSprite { get; private set; }
        public Sprite? WireConnectorSprite { get; private set; }
        public Sprite? ConnectionScrewSprite { get; private set; }
        public UISprite? NodeFrameSprite { get; private set; }
        public UISprite? NodeTopSprite { get; private set; }

        protected override void CreateGUI()
        {
            base.CreateGUI();
            GuiFrame.ClearChildren();
            UI?.CreateGUI(GuiFrame);
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            UI = new CircuitBoxUI(this);
            IsActive = true;
            CreateGUI();

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "wiresprite":
                        WireSprite = new Sprite(subElement);
                        break;
                    case "connectionsprite":
                        ConnectionSprite = new Sprite(subElement);
                        break;
                    case "wireconnectorsprite":
                        WireConnectorSprite = new Sprite(subElement);
                        break;
                    case "connectionscrewsprite":
                        ConnectionScrewSprite = new Sprite(subElement);
                        break;
                }
            }

            if (GUIStyle.GetComponentStyle("CircuitBoxTop") is {  } topStyle)
            {
                NodeTopSprite = topStyle.Sprites[GUIComponent.ComponentState.None][0];
            }

            if (GUIStyle.GetComponentStyle("CircuitBoxFrame") is { } compStyle)
            {
                NodeFrameSprite = compStyle.Sprites[GUIComponent.ComponentState.None][0];
            }
        }

        public override bool ShouldDrawHUD(Character character)
            => character == Character.Controlled && (character.SelectedItem == item || character.SelectedSecondaryItem == item);

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (UI is null) { return; }

            UI.Update(deltaTime);

            if (GameMain.NetworkMember is null) { return; }

            foreach (var (cursorChar, cursor) in ActiveCursors)
            {
                if (!cursor.IsActive) { continue; }

                ActiveCursors[cursorChar].Update(deltaTime);
            }

            Vector2 cursorPos = UI.GetCursorPosition();
            int lastCursorPosIndex = recordedCursorPositions.Length - 1;

            if (cursorUpdateTimer < CursorUpdateInterval)
            {
                cursorUpdateTimer += deltaTime;
                int cursorIndex = (int)MathF.Floor(cursorUpdateTimer * lastCursorPosIndex);
                RecordCursorPosition(cursorIndex);
            }
            else
            {
                RecordCursorPosition(lastCursorPosIndex);
                SendCursorState(recordedCursorPositions, recordedDragStart, recordedHeldPrefab.Select(static c => c.Identifier));

                recordedDragStart = Option.None;
                recordedHeldPrefab = Option.None;
                cursorUpdateTimer = 0f;
            }

            void RecordCursorPosition(int index)
            {
                var dragStart = UI.GetDragStart();
                if (dragStart.IsSome()) { recordedDragStart = dragStart; }

                var heldComponent = HeldComponent;
                if (heldComponent.IsSome()) { recordedHeldPrefab = heldComponent; }

                if (index >= 0 && index < recordedCursorPositions.Length) { recordedCursorPositions[index] = cursorPos; }
            }
        }

        public void RemoveComponents(IReadOnlyCollection<CircuitBoxComponent> node)
        {
            if (Locked) { return; }
            var ids = node.Select(static n => n.ID).ToImmutableArray();

            if (GameMain.NetworkMember is null)
            {
                CreateRefundItemsForUsedResources(ids, Character.Controlled);
                RemoveComponentInternal(ids);
                return;
            }

            if (!node.Any()) { return; }

            CreateClientEvent(new CircuitBoxRemoveComponentEvent(ids));
        }

        public void AddWire(CircuitBoxConnection one, CircuitBoxConnection two)
        {
            if (Locked) { return; }
            if (GameMain.NetworkMember is null)
            {
                Connect(one, two, static delegate { }, CircuitBoxWire.SelectedWirePrefab);
                return;
            }

            if (!VerifyConnection(one, two)) { return; }

            CreateClientEvent(new CircuitBoxClientAddWireEvent(Color.White, CircuitBoxConnectorIdentifier.FromConnection(one), CircuitBoxConnectorIdentifier.FromConnection(two), CircuitBoxWire.SelectedWirePrefab.UintIdentifier));
        }

        public void RemoveWires(IReadOnlyCollection<CircuitBoxWire> wires)
        {
            if (Locked) { return; }
            var ids = wires.Select(static w => w.ID).ToImmutableArray();
            if (GameMain.NetworkMember is null)
            {
                RemoveWireInternal(ids);
                return;
            }

            if (!ids.Any()) { return; }
            CreateClientEvent(new CircuitBoxRemoveWireEvent(ids));
        }

        public void SelectComponents(IReadOnlyCollection<CircuitBoxNode> moveables, bool overwrite)
        {
            if (Character.Controlled is not { ID: var controlledId }) { return; }

            var ids = ImmutableArray.CreateBuilder<ushort>();
            var ios = ImmutableArray.CreateBuilder<CircuitBoxInputOutputNode.Type>();
            var labelIds = ImmutableArray.CreateBuilder<ushort>();

            foreach (var moveable in moveables)
            {
                if (moveable is { IsSelected: true, IsSelectedByMe: false }) { continue; }

                switch (moveable)
                {
                    case CircuitBoxComponent node:
                        ids.Add(node.ID);
                        break;
                    case CircuitBoxInputOutputNode io:
                        ios.Add(io.NodeType);
                        break;
                    case CircuitBoxLabelNode label:
                        labelIds.Add(label.ID);
                        break;
                }
            }

            if (GameMain.NetworkMember is null)
            {
                SelectComponentsInternal(ids, controlledId, overwrite);
                SelectInputOutputInternal(ios, controlledId, overwrite);
                SelectLabelsInternal(labelIds, controlledId, overwrite);
                return;
            }

            if (!ids.Any() && !ios.Any() && !labelIds.Any() && !overwrite) { return; }

            CreateClientEvent(new CircuitBoxSelectNodesEvent(ids.ToImmutable(), ios.ToImmutable(), labelIds.ToImmutable(), overwrite, controlledId));
        }

        public void SelectWires(IReadOnlyCollection<CircuitBoxWire> wires, bool overwrite)
        {
            if (Character.Controlled is not { ID: var controlledId }) { return; }

            var ids = (from wire in wires where !wire.IsSelected || wire.IsSelectedByMe select wire.ID).ToImmutableArray();

            if (GameMain.NetworkMember is null)
            {
                SelectWiresInternal(ids, controlledId, overwrite);
                return;
            }

            if (!ids.Any() && !overwrite) { return; }

            CreateClientEvent(new CircuitBoxSelectWiresEvent(ids, overwrite, Character.Controlled.ID));
        }

        public void MoveComponent(Vector2 moveAmount, IReadOnlyCollection<CircuitBoxNode> moveables)
        {
            if (Locked) { return; }
            var ids = ImmutableArray.CreateBuilder<ushort>();
            var ios = ImmutableArray.CreateBuilder<CircuitBoxInputOutputNode.Type>();
            var labelIds = ImmutableArray.CreateBuilder<ushort>();

            foreach (CircuitBoxNode move in moveables)
            {
                switch (move)
                {
                    case CircuitBoxComponent node:
                        ids.Add(node.ID);
                        break;
                    case CircuitBoxInputOutputNode io:
                        ios.Add(io.NodeType);
                        break;
                    case CircuitBoxLabelNode label:
                        labelIds.Add(label.ID);
                        break;
                }
            }

            if (GameMain.NetworkMember is null)
            {
                MoveNodesInternal(ids, ios, labelIds, moveAmount);
                return;
            }

            if (!ids.Any() && !ios.Any() && !labelIds.Any()) { return; }


            CreateClientEvent(new CircuitBoxMoveComponentEvent(ids.ToImmutable(), ios.ToImmutable(), labelIds.ToImmutable(), moveAmount));
        }

        public void AddComponent(ItemPrefab prefab, Vector2 pos)
        {
            if (Locked) { return; }
            if (GameMain.NetworkMember is null)
            {
                ItemPrefab resource;

                if (IsFull) { return; }

                if (IsInGame())
                {
                    if (!GetApplicableResourcePlayerHas(prefab, Character.Controlled).TryUnwrap(out var r)) { return; }
                    resource = r.Prefab;
                    RemoveItem(r);
                }
                else
                {
                    resource = ItemPrefab.Prefabs[Tags.FPGACircuit];
                }

                AddComponentInternal(ICircuitBoxIdentifiable.FindFreeID(Components), prefab, resource, pos, Character.Controlled, onItemSpawned: null);
                return;
            }

            CreateClientEvent(new CircuitBoxAddComponentEvent(prefab.UintIdentifier, pos));
        }

        public void RenameLabel(CircuitBoxLabelNode label, Color color, NetLimitedString header, NetLimitedString body)
        {
            if (Locked) { return; }
            if (GameMain.NetworkMember is null)
            {
                label.EditText(header, body);
                label.Color = color;
                return;
            }

            CreateClientEvent(new CircuitBoxRenameLabelEvent(label.ID, color, header, body));
        }

        public void SetConnectionLabelOverrides(CircuitBoxInputOutputNode node, Dictionary<string, string> newOverrides)
        {
            if (GameMain.NetworkMember is null)
            {
                node.ReplaceAllConnectionLabelOverrides(newOverrides);
                return;
            }

            CreateClientEvent(new CircuitBoxRenameConnectionLabelsEvent(node.NodeType, newOverrides.ToNetDictionary()));
        }

        public void ResizeNode(CircuitBoxNode node, CircuitBoxResizeDirection dir, Vector2 amount)
        {
            if (Locked) { return; }
            var resize = node.ResizeBy(dir, amount);
            if (GameMain.NetworkMember is null)
            {
                node.ApplyResize(resize.Size, resize.Pos);
                return;
            }

            // TODO this needs to be refactored at some point, probably not now
            // the problem here is that the circuit  box supports resizing all nodes
            // but we limit the resizing to only labels on the client
            // and on the server we only have a network message that targets labels
            // so if we ever want the ability to resize other nodes (could be useful) the network message
            // needs to know what type of ID it's targeting
            if (node is not ICircuitBoxIdentifiable identifiable)
            {
                DebugConsole.ThrowError("Tried to resize a node that doesn't have an ID.");
                return;
            }

            CreateClientEvent(new CircuitBoxResizeLabelEvent(identifiable.ID, resize.Pos, resize.Size));
        }

        public void AddLabel(Vector2 pos)
        {
            if (Locked) { return; }
            if (GameMain.NetworkMember is null)
            {
                AddLabelInternal(ICircuitBoxIdentifiable.FindFreeID(Labels), GUIStyle.Blue, pos, CircuitBoxLabelNode.DefaultHeaderText, NetLimitedString.Empty);
                return;
            }

            CreateClientEvent(new CircuitBoxAddLabelEvent(pos, GUIStyle.Blue, CircuitBoxLabelNode.DefaultHeaderText, NetLimitedString.Empty));
        }

        public void RemoveLabel(IReadOnlyCollection<CircuitBoxLabelNode> labels)
        {
            if (Locked) { return; }
            if (!labels.Any()) { return; }

            var ids = labels.Select(static n => n.ID).ToImmutableArray();

            if (GameMain.NetworkMember is null)
            {
                RemoveLabelInternal(ids);
                return;
            }

            CreateClientEvent(new CircuitBoxRemoveLabelEvent(ids));
        }

        public partial void OnViewUpdateProjSpecific()
        {
            UI?.MouseSnapshotHandler.UpdateConnections();
            UI?.UpdateComponentList();
        }

        protected override void OnResolutionChanged()
        {
            base.OnResolutionChanged();
            CreateGUI();
        }

        // Remove selection when the circuit box is deselected
        public partial void OnDeselected(Character c)
        {
            cursorUpdateTimer = 0f;

            // Server will broadcast the deselection, we don't need to do it ourselves
            if (GameMain.NetworkMember is not null) { return; }
            ClearAllSelectionsInternal(c.ID);
        }

        public void ClientRead(INetSerializableStruct data)
        {
            switch (data)
            {
                case NetCircuitBoxCursorInfo cursorInfo:
                {
                    ClientReadCursor(cursorInfo);
                    break;
                }
                case CircuitBoxErrorEvent errorData:
                {
                    DebugConsole.ThrowError($"The server responded with an error: {errorData.Message}");
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(data), data, "This data cannot be handled using direct network messages.");
            }
        }

        public void SendMessage(CircuitBoxOpcode opcode, INetSerializableStruct data)
        {
            IWriteMessage msg = new WriteOnlyMessage().WithHeader(ClientPacketHeader.CIRCUITBOX);

            msg.WriteNetSerializableStruct(new NetCircuitBoxHeader(
                Opcode: opcode,
                ItemID: item.ID,
                ComponentIndex: (byte)item.GetComponentIndex(this)));

            msg.WriteNetSerializableStruct(data);

            DeliveryMethod deliveryMethod =
                UnrealiableOpcodes.Contains(opcode)
                    ? DeliveryMethod.Unreliable
                    : DeliveryMethod.Reliable;

            GameMain.Client?.ClientPeer?.Send(msg, deliveryMethod);
        }

        private void SendCursorState(Vector2[] cursorPositions, Option<Vector2> dragStart, Option<Identifier> heldComponent)
        {
            if (!IsRoundRunning()) { return; }

            var msg = new NetCircuitBoxCursorInfo(
                RecordedPositions: cursorPositions,
                DragStart: dragStart,
                HeldItem: heldComponent);

            SendMessage(CircuitBoxOpcode.Cursor, msg);
        }

        public void ClientReadCursor(NetCircuitBoxCursorInfo info)
        {
            if (Entity.FindEntityByID(info.CharacterID) is not Character character) { return; }

            if (!ActiveCursors.ContainsKey(character))
            {
                var newCursor = new CircuitBoxCursor(info);
                ActiveCursors.Add(character, newCursor);
                return;
            }

            var activeCursor = ActiveCursors[character];
            activeCursor.UpdateInfo(info);
            activeCursor.ResetTimers();
        }

        public void CreateClientEvent(INetSerializableStruct data)
            => item.CreateClientEvent(this, new CircuitBoxEventData(data));

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
        {
            if (extraData is null) { return; }
            var eventData = ExtractEventData<CircuitBoxEventData>(extraData);
            msg.WriteByte((byte)eventData.Opcode);
            msg.WriteNetSerializableStruct(eventData.Data);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            var header = (CircuitBoxOpcode)msg.ReadByte();

            switch (header)
            {
                case CircuitBoxOpcode.AddComponent:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxServerCreateComponentEvent>(msg);
                    AddComponentFromData(data);
                    break;
                }
                case CircuitBoxOpcode.DeleteComponent:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRemoveComponentEvent>(msg);
                    RemoveComponentInternal(data.TargetIDs);
                    break;
                }
                case CircuitBoxOpcode.MoveComponent:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxMoveComponentEvent>(msg);
                    MoveNodesInternal(data.TargetIDs, data.IOs, data.LabelIDs, data.MoveAmount);
                    break;
                }
                case CircuitBoxOpcode.UpdateSelection:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxServerUpdateSelection>(msg);

                    var nodeDict = data.ComponentIds.ToImmutableDictionary(static s => s.ID, static s => s.SelectedBy);
                    var wireDict = data.WireIds.ToImmutableDictionary(static s => s.ID, static s => s.SelectedBy);
                    var ioDict = data.InputOutputs.ToImmutableDictionary(static s => s.Type, static s => s.SelectedBy);
                    var labelDict = data.LabelIds.ToImmutableDictionary(static s => s.ID, static s => s.SelectedBy);

                    UpdateSelections(nodeDict, wireDict, ioDict, labelDict);
                    break;
                }
                case CircuitBoxOpcode.AddWire:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxServerCreateWireEvent>(msg);
                    AddWireFromData(data);
                    break;
                }
                case CircuitBoxOpcode.RemoveWire:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRemoveWireEvent>(msg);
                    RemoveWireInternal(data.TargetIDs);
                    break;
                }
                case CircuitBoxOpcode.ServerInitialize:
                {
                    Components.Clear();
                    Wires.Clear();
                    Labels.Clear();

                    var data = INetSerializableStruct.Read<CircuitBoxInitializeStateFromServerEvent>(msg);
                    foreach (var compData in data.Components) { AddComponentFromData(compData); }
                    foreach (var wireData in data.Wires) { AddWireFromData(wireData); }

                    foreach (var labelData in data.Labels)
                    {
                        AddLabelInternal(labelData.ID, labelData.Color, labelData.Position, labelData.Header, labelData.Body);
                        ResizeLabelInternal(labelData.ID, labelData.Position, labelData.Size);
                    }

                    foreach (var node in InputOutputNodes)
                    {
                        node.Position = node.NodeType switch
                        {
                            CircuitBoxInputOutputNode.Type.Input => data.InputPos,
                            CircuitBoxInputOutputNode.Type.Output => data.OutputPos,
                            _ => node.Position
                        };
                    }

                    foreach (var labelOverride in data.LabelOverrides)
                    {
                        RenameConnectionLabelsInternal(labelOverride.Type, labelOverride.Override.ToDictionary());
                    }

                    wasInitializedByServer = true;
                    break;
                }
                case CircuitBoxOpcode.RenameLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRenameLabelEvent>(msg);
                    RenameLabelInternal(data.LabelId, data.Color, data.NewHeader, data.NewBody);
                    break;
                }
                case CircuitBoxOpcode.AddLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxServerAddLabelEvent>(msg);
                    AddLabelInternal(data.ID, data.Color, data.Position, data.Header, data.Body);
                    ResizeLabelInternal(data.ID, data.Position, data.Size);
                    break;
                }
                case CircuitBoxOpcode.RemoveLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRemoveLabelEvent>(msg);
                    RemoveLabelInternal(data.TargetIDs);
                    break;
                }
                case CircuitBoxOpcode.ResizeLabel:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxResizeLabelEvent>(msg);
                    ResizeLabelInternal(data.ID, data.Position, data.Size);
                    break;
                }
                case CircuitBoxOpcode.RenameConnections:
                {
                    var data = INetSerializableStruct.Read<CircuitBoxRenameConnectionLabelsEvent>(msg);
                    RenameConnectionLabelsInternal(data.Type, data.Override.ToDictionary());
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(header), header, "This opcode cannot be handled using entity events");
            }
        }

        public void AddComponentFromData(CircuitBoxServerCreateComponentEvent data)
        {
            if (ItemPrefab.Prefabs.Find(p => p.UintIdentifier == data.UsedResource) is not { } prefab)
            {
                throw new Exception($"No item prefab found for \"{data.UsedResource}\"");
            }

            AddComponentInternalUnsafe(data.ComponentId, FindItemByID(data.BackingItemId), prefab, data.Position);
        }

        public void AddWireFromData(CircuitBoxServerCreateWireEvent data)
        {
            var (req, wireId, possibleItemId) = data;
            var prefab = ItemPrefab.Prefabs.Find(p => p.UintIdentifier == req.SelectedWirePrefabIdentifier);
            if (prefab is null)
            {
                throw new Exception($"No prefab found for \"{req.SelectedWirePrefabIdentifier}\"");
            }

            if (!req.Start.FindConnection(this).TryUnwrap(out var start))
            {
                throw new Exception($"No connection found for ({req.Start})");
            }

            if (!req.End.FindConnection(this).TryUnwrap(out var end))
            {
                throw new Exception($"No connection found for ({req.Start})");
            }

            if (possibleItemId.TryUnwrap(out var backingItem))
            {
                CreateWireWithItem(start, end, wireId, FindItemByID(backingItem));
            }
            else
            {
                CreateWireWithoutItem(start, end, wireId, prefab);
            }
        }

        public static Item FindItemByID(ushort id)
            => Entity.FindEntityByID(id) as Item ?? throw new Exception($"No item with ID {id} exists.");

        public override void AddToGUIUpdateList(int order = 0)
        {
            base.AddToGUIUpdateList(order);
            UI?.AddToGUIUpdateList();
        }
    }
}