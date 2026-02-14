using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal readonly struct InventorySlotItem
    {
        public readonly int Slot;
        public readonly Item Item;

        public InventorySlotItem(int slot, Item item)
        {
            Slot = slot;
            Item = item;
        }

        public void Deconstruct(out int slot, out Item item)
        {
            slot = Slot;
            item = Item;
        }
    }

    internal abstract partial class Command
    {
        public abstract LocalizedString GetDescription();

        public string AuthorSessionId { get; set; }

        public virtual IEnumerable<ushort> GetAffectedEntityIds()
        {
            return Enumerable.Empty<ushort>();
        }
    }

    internal class TransformCommand : Command
    {
        private readonly List<MapEntity> Receivers;
        private readonly List<Rectangle> NewData;
        private readonly List<Rectangle> OldData;
        private readonly bool Resized;

        public IReadOnlyList<MapEntity> AffectedEntities => Receivers;

        public TransformCommand(List<MapEntity> receivers, List<Rectangle> newData, List<Rectangle> oldData, bool resized)
        {
            Receivers = receivers;
            NewData = newData;
            OldData = oldData;
            Resized = resized;
        }

        public override void Execute() => SetRects(NewData);
        public override void UnExecute() => SetRects(OldData);

        public override void Cleanup()
        {
            NewData.Clear();
            OldData.Clear();
            Receivers.Clear();
        }

        private void SetRects(IReadOnlyList<Rectangle> rects)
        {
            if (Receivers.Count != rects.Count)
            {
                DebugConsole.ThrowError($"Receivers.Count did not match Rects.Count ({Receivers.Count} vs {rects.Count}).");
                return;
            }

            for (int i = 0; i < rects.Count; i++)
            {
                MapEntity entity = Receivers[i].GetReplacementOrThis();
                Rectangle Rect = rects[i];
                Vector2 diff = Rect.Location.ToVector2() - entity.Rect.Location.ToVector2();
                entity.Move(diff);
                entity.Rect = Rect;
            }
        }

        public override LocalizedString GetDescription()
        {
            if (Resized)
            {
                return TextManager.GetWithVariable("Undo.ResizedItem", "[item]", Receivers.FirstOrDefault()?.Name);
            }

            return Receivers.Count > 1
                ? TextManager.GetWithVariable("Undo.MovedItemsMultiple", "[count]", Receivers.Count.ToString())
                : TextManager.GetWithVariable("Undo.MovedItem", "[item]", Receivers.FirstOrDefault()?.Name);
        }

        public override IEnumerable<ushort> GetAffectedEntityIds()
            => Receivers.Select(e => e.ID);
    }

    internal class AddOrDeleteCommand : Command
    {
        private readonly Dictionary<InventorySlotItem, Inventory> PreviousInventories = new Dictionary<InventorySlotItem, Inventory>();
        public readonly List<MapEntity> Receivers;
        private readonly List<MapEntity> CloneList;
        private readonly bool WasDeleted;
        public bool IsDeleteOperation => WasDeleted;
        private readonly List<AddOrDeleteCommand> ContainedItemsCommand = new List<AddOrDeleteCommand>();

        // Snapshot circuit box state as XML
        private readonly List<XElement> CircuitBoxData = new List<XElement>();

        public AddOrDeleteCommand(List<MapEntity> receivers, bool wasDeleted, bool handleInventoryBehavior = true)
        {
            Debug.Assert(receivers.Count > 0, "Command has 0 receivers");
            WasDeleted = wasDeleted;
            Receivers = new List<MapEntity>(receivers);

            try
            {
                foreach (MapEntity receiver in receivers)
                {
                    if (receiver is Item { ParentInventory: not null } it)
                    {
                        PreviousInventories.Add(new InventorySlotItem(it.ParentInventory.FindIndex(it), it), it.ParentInventory);
                    }
                }

                List<MapEntity> clonedTargets = MapEntity.Clone(receivers);

                List<MapEntity> itemsToDelete = new List<MapEntity>();
                foreach (MapEntity receiver in Receivers)
                {
                    if (receiver is not Item it) { continue; }

                    foreach (var cb in it.GetComponents<CircuitBox>())
                    {
                        CircuitBoxData.Add(cb.Save(new XElement("root")));
                    }

                    foreach (ItemContainer component in it.GetComponents<ItemContainer>())
                    {
                        if (component.Inventory == null) { continue; }
                        itemsToDelete.AddRange(component.Inventory.AllItems.Where(static item => !item.Removed));
                    }
                }

                if (itemsToDelete.Any() && handleInventoryBehavior)
                {
                    ContainedItemsCommand.Add(new AddOrDeleteCommand(itemsToDelete, wasDeleted));
                    if (wasDeleted)
                    {
                        foreach (MapEntity item in itemsToDelete)
                        {
                            if (item != null && !item.Removed)
                            {
                                item.Remove();
                            }
                        }
                    }
                }

                foreach (MapEntity clone in clonedTargets)
                {
                    clone.ShallowRemove();
                    if (clone is Item it)
                    {
                        foreach (ItemContainer container in it.GetComponents<ItemContainer>())
                        {
                            container.Inventory?.DeleteAllItems();
                        }
                    }
                }

                CloneList = clonedTargets;
            }
            // This should never happen except if we decide to make a new type of MapEntity that isn't finished yet
            catch (Exception e)
            {
                Receivers = new List<MapEntity>();
                CloneList = new List<MapEntity>();
                DebugConsole.ThrowError("Could not store object", e);
            }
        }

        public override void Execute()
            => Process(true);

        public override void UnExecute()
            => Process(false);

        private void Process(bool redo)
        {
            var items = DeleteUndelete(redo);
            foreach (var cmd in ContainedItemsCommand)
            {
                cmd.Process(redo);
            }
            ApplyCircuitBoxDataIfAny(items);
        }

        // Undo system recursively deletes inventory items, so circuit box loses its internal
        // inventory when cloned/restored. Save XML here and reload after items are restored.
        private void ApplyCircuitBoxDataIfAny(ImmutableArray<Item> items)
        {
            int cbIndex = 0;
            foreach (var newItem in items)
            {
                foreach (ItemComponent component in newItem.Components)
                {
                    if (component is not CircuitBox cb) { continue; }

                    if (cbIndex < 0 || cbIndex >= CircuitBoxData.Count)
                    {
                        DebugConsole.ThrowError("Unable to restore wiring in circuit box, index out of range.");
                        continue;
                    }

                    var cbData = CircuitBoxData[cbIndex];
                    cbIndex++;

                    cb.LoadFromXML(new ContentXElement(null, cbData));
                }
            }
        }

        public override void Cleanup()
        {
            foreach (MapEntity entity in CloneList)
            {
                if (!entity.Removed)
                {
                    entity.Remove();
                }
            }

            CloneList?.Clear();
            Receivers.Clear();
            PreviousInventories?.Clear();
            ContainedItemsCommand?.ForEach(static cmd => cmd.Cleanup());
            CircuitBoxData.Clear();
        }

        private ImmutableArray<Item> DeleteUndelete(bool redo)
        {
            bool wasDeleted = WasDeleted;

            if (redo) { wasDeleted = !wasDeleted; }

            var builder = ImmutableArray.CreateBuilder<Item>();

            if (wasDeleted)
            {
                Debug.Assert(Receivers.All(static entity => entity.GetReplacementOrThis().Removed), "Tried to redo a deletion but some items were not deleted");

                List<MapEntity> clones = MapEntity.Clone(CloneList);
                int length = Math.Min(Receivers.Count, clones.Count);
                for (int i = 0; i < length; i++)
                {
                    MapEntity clone = clones[i],
                              receiver = Receivers[i];

                    if (receiver.GetReplacementOrThis() is Item item && clone is Item cloneItem)
                    {
                        builder.Add(cloneItem);
                        foreach (ItemComponent ic in item.Components)
                        {
                            int index = item.GetComponentIndex(ic);
                            ItemComponent component = cloneItem.Components.ElementAtOrDefault(index);
                            switch (component)
                            {
                                case null:
                                    continue;
                                case ItemContainer { Inventory: not null } newContainer when ic is ItemContainer { Inventory: not null } itemContainer:
                                    itemContainer.Inventory.GetReplacementOrThis().ReplacedBy = newContainer.Inventory;
                                    goto default;
                                default:
                                    ic.GetReplacementOrThis().ReplacedBy = component;
                                    break;
                            }
                        }
                    }

                    receiver.GetReplacementOrThis().ReplacedBy = clone;
                }

                for (int i = 0; i < length; i++)
                {
                    MapEntity clone = clones[i],
                              receiver = Receivers[i];

                    if (clone is Item it)
                    {
                        foreach (var (slotRef, inventory) in PreviousInventories)
                        {
                            if (slotRef.Item == receiver)
                            {
                                inventory.GetReplacementOrThis().TryPutItem(it, slotRef.Slot, false, false, null, createNetworkEvent: false);
                            }
                        }
                    }
                }

                foreach (MapEntity clone in clones)
                {
                    clone.Submarine = Submarine.MainSub;
                }

                return builder.ToImmutable();
            }
            else
            {
                foreach (MapEntity t in Receivers)
                {
                    MapEntity receiver = t.GetReplacementOrThis();
                    if (!receiver.Removed)
                    {
                        receiver.Remove();
                    }
                }

                return builder.ToImmutable();
            }
        }

        public void MergeInto(AddOrDeleteCommand master)
        {
            master.Receivers.AddRange(Receivers);
            master.CloneList.AddRange(CloneList);
            master.ContainedItemsCommand.AddRange(ContainedItemsCommand);
            foreach (var (slot, item) in PreviousInventories)
            {
                master.PreviousInventories.Add(slot, item);
            }
        }

        public override LocalizedString GetDescription()
        {
            if (WasDeleted)
            {
                return Receivers.Count > 1
                    ? TextManager.GetWithVariable("Undo.RemovedItemsMultiple", "[count]", Receivers.Count.ToString())
                    : TextManager.GetWithVariable("Undo.RemovedItem", "[item]", Receivers.FirstOrDefault()?.Name ?? "null");
            }

            return Receivers.Count > 1
                ? TextManager.GetWithVariable("Undo.AddedItemsMultiple", "[count]", Receivers.Count.ToString())
                : TextManager.GetWithVariable("Undo.AddedItem", "[item]", Receivers.FirstOrDefault()?.Name ?? "null");
        }

        public override IEnumerable<ushort> GetAffectedEntityIds()
            => Receivers.Select(e => e.ID);
    }

    internal class InventoryPlaceCommand : Command
    {
        private readonly Inventory Inventory;
        private readonly List<InventorySlotItem> Receivers;
        private readonly bool wasDropped;

        public InventoryPlaceCommand(Inventory inventory, List<Item> items, bool dropped)
        {
            Inventory = inventory;
            Receivers = items.Select(item => new InventorySlotItem(inventory.FindIndex(item), item)).ToList();
            wasDropped = dropped;
        }

        public override void Execute() => ContainUncontain(false);
        public override void UnExecute() => ContainUncontain(true);

        public override void Cleanup()
        {
            Receivers.Clear();
        }

        private void ContainUncontain(bool drop)
        {
            if (wasDropped) { drop = !drop; }

            foreach (var (slot, receiver) in Receivers)
            {
                Item item = (Item) receiver.GetReplacementOrThis();

                if (drop)
                {
                    item.Drop(null, createNetworkEvent: false);
                }
                else
                {
                    Inventory.GetReplacementOrThis().TryPutItem(item, slot, false, false, null, createNetworkEvent: false);
                }
            }
        }

        public override LocalizedString GetDescription()
        {
            if (wasDropped)
            {
                return TextManager.GetWithVariable("Undo.DroppedItem", "[item]", Receivers.FirstOrDefault().Item.Name);
            }

            string container = "[ERROR]";

            if (Inventory.Owner is Item item)
            {
                container = item.Name;
            }

            return Receivers.Count > 1
                ? TextManager.GetWithVariables("Undo.ContainedItemsMultiple", ("[count]", Receivers.Count.ToString()), ("[container]", container))
                : TextManager.GetWithVariables("Undo.ContainedItem", ("[item]", Receivers.FirstOrDefault().Item.Name), ("[container]", container));
        }
    }

    internal class PropertyCommand : Command
    {
        private Dictionary<object, List<ISerializableEntity>> OldProperties;
        private readonly List<ISerializableEntity> Receivers;
        private readonly Identifier PropertyName;
        private readonly object NewProperties;
        private string sanitizedProperty;

        public readonly int PropertyCount;

        public IReadOnlyList<ISerializableEntity> AffectedEntities => Receivers;

        public Identifier ChangedPropertyName => PropertyName;

        public string SanitizedPropertyValue => sanitizedProperty;

        public PropertyCommand(List<ISerializableEntity> receivers, Identifier propertyName, object newData, Dictionary<object, List<ISerializableEntity>> oldData)
        {
            Receivers = receivers;
            PropertyName = propertyName;
            OldProperties = oldData;
            NewProperties = newData;
            PropertyCount = receivers.Count;
            SanitizeProperty();
        }

        public PropertyCommand(ISerializableEntity receiver, Identifier propertyName, object newData, object oldData)
        {
            Receivers = new List<ISerializableEntity> { receiver };
            PropertyName = propertyName;
            OldProperties = new Dictionary<object, List<ISerializableEntity>> { { oldData, Receivers } };
            NewProperties = newData;
            PropertyCount = 1;
            SanitizeProperty();
        }

        public bool MergeInto(PropertyCommand master)
        {
            if (!master.Receivers.SequenceEqual(Receivers)) { return false; }
            master.OldProperties = OldProperties;
            return true;
        }

        private void SanitizeProperty()
        {
            sanitizedProperty = NewProperties switch
            {
                float f => f.FormatSingleDecimal(),
                Point point => XMLExtensions.PointToString(point),
                Vector2 vector2 => vector2.FormatZeroDecimal(),
                Vector3 vector3 => vector3.FormatSingleDecimal(),
                Vector4 vector4 => vector4.FormatSingleDecimal(),
                Color color => XMLExtensions.ColorToString(color),
                Rectangle rectangle => XMLExtensions.RectToString(rectangle),
                _ => NewProperties.ToString()
            };
        }

        public override void Execute() => SetProperties(false);
        public override void UnExecute() => SetProperties(true);

        public override void Cleanup()
        {
            Receivers.Clear();
            OldProperties.Clear();
        }

        private void SetProperties(bool undo)
        {
            foreach (ISerializableEntity t in Receivers)
            {
                ISerializableEntity receiver;
                switch (t)
                {
                    case MapEntity me when me.GetReplacementOrThis() is ISerializableEntity sEntity:
                        receiver = sEntity;
                        break;
                    case ItemComponent ic when ic.GetReplacementOrThis() is ISerializableEntity sItemComponent:
                        receiver = sItemComponent;
                        break;
                    default:
                        receiver = t;
                        break;
                }

                object data = NewProperties;

                if (undo)
                {
                    foreach (var (key, value) in OldProperties)
                    {
                        if (value.Contains(t)) { data = key; }
                    }
                }

                if (receiver.SerializableProperties != null)
                {
                    Dictionary<Identifier, SerializableProperty> props = receiver.SerializableProperties;

                    if (props.TryGetValue(PropertyName, out SerializableProperty prop))
                    {
                        prop.TrySetValue(receiver, data);
                        if (MapEntity.EditingHUD == null || (MapEntity.EditingHUD.UserData != receiver && (receiver is ItemComponent ic && MapEntity.EditingHUD.UserData != ic.Item))) { continue; }

                        GUIListBox list = MapEntity.EditingHUD.GetChild<GUIListBox>();
                        if (list == null) { continue; }

                        IEnumerable<SerializableEntityEditor> editors = list.Content.FindChildren(comp => comp is SerializableEntityEditor).Cast<SerializableEntityEditor>();
                        SerializableEntityEditor.LockEditing = true;
                        foreach (SerializableEntityEditor editor in editors)
                        {
                            if (editor.UserData == receiver && editor.Fields.TryGetValue(PropertyName, out GUIComponent[] _))
                            {
                                editor.UpdateValue(prop, data);
                            }
                        }

                        SerializableEntityEditor.LockEditing = false;
                    }
                }
            }
        }

        public override LocalizedString GetDescription()
        {
            return Receivers.Count > 1
                ? TextManager.GetWithVariables("Undo.ChangedPropertyMultiple",
                    ("[property]", PropertyName.Value),
                    ("[count]", Receivers.Count.ToString()),
                    ("[value]", sanitizedProperty))
                : TextManager.GetWithVariables("Undo.ChangedProperty",
                    ("[property]", PropertyName.Value),
                    ("[item]", Receivers.FirstOrDefault()?.Name),
                    ("[value]", sanitizedProperty));
        }

        public override IEnumerable<ushort> GetAffectedEntityIds()
        {
            foreach (var receiver in Receivers)
            {
                if (receiver is MapEntity me) yield return me.ID;
                else if (receiver is ItemComponent ic) yield return ic.Item.ID;
            }
        }
    }

    internal class InventoryMoveCommand : Command
    {
        private readonly Inventory oldInventory;
        private readonly Inventory newInventory;
        private readonly int oldSlot;
        private readonly int newSlot;
        private readonly Item targetItem;

        public InventoryMoveCommand(Inventory oldInventory, Inventory newInventory, Item item, int oldSlot, int newSlot)
        {
            this.newInventory = newInventory;
            this.oldInventory = oldInventory;
            this.oldSlot = oldSlot;
            this.newSlot = newSlot;
            targetItem = item;
        }

        public override void Execute()
        {
            if (targetItem.GetReplacementOrThis() is Item item)
            {
                newInventory?.GetReplacementOrThis().TryPutItem(item, newSlot, true, false, null, createNetworkEvent: false);
            }
        }

        public override void UnExecute()
        {
            if (targetItem.GetReplacementOrThis() is Item item)
            {
                oldInventory?.GetReplacementOrThis().TryPutItem(item, oldSlot, true, false, null, createNetworkEvent: false);
            }
        }

        public override void Cleanup() { }

        public override LocalizedString GetDescription()
        {
            return TextManager.GetWithVariable("Undo.MovedItem", "[item]", targetItem.Name);
        }

        public override IEnumerable<ushort> GetAffectedEntityIds()
        {
            yield return targetItem.ID;
        }
    }

    internal class TransformToolCommand : Command
    {
        private readonly Dictionary<MapEntity, SubEditorScreen.TransformData> originalData;
        public float? ScaleMult, RotationRad;
        public readonly Vector2 Pivot;
        private readonly Vector2 wirePivot;
        public float MinScale, MaxScale;

        public TransformToolCommand(Dictionary<MapEntity, SubEditorScreen.TransformData> data, Vector2 pivot)
        {
            originalData = data;
            Pivot = pivot;
            wirePivot = Pivot - Submarine.MainSub.HiddenSubPosition;

            MinScale = 0.01f / Math.Max(data.Values.Min(data => data.Scale), 0.01f);
            MaxScale = 10f / Math.Min(data.Values.Max(data => data.Scale), 10f);
        }

        public override void Execute() => UpdateTransforms(RotationRad ?? 0f, ScaleMult ?? 1f);
        public override void UnExecute() => UpdateTransforms(0f, 1f);

        public override void Cleanup() => originalData.Clear();

        private void UpdateTransforms(float rotationRad, float scaleMult)
        {
            foreach ((MapEntity receiver, SubEditorScreen.TransformData data) in originalData)
            {
                if (RotationRad.HasValue && receiver is Item { Prefab.AllowRotatingInEditor: true } or Structure { Prefab.AllowRotatingInEditor: true })
                {
                    int rotationDir = receiver is Structure && receiver.FlippedX ^ receiver.FlippedY ? -1 : 1;
                    float newRotation = MathHelper.ToDegrees(data.RotationRad + rotationRad * rotationDir);
                    switch (receiver)
                    {
                        case Item item:
                            item.Rotation = newRotation;
                            break;
                        case Structure structure:
                            structure.Rotation = newRotation;
                            break;
                    }
                    data.TurretLimits?.ForEach(pair => pair.Key.RotationLimits = pair.Value + new Vector2(MathHelper.ToDegrees(rotationRad)));
                }

                if (ScaleMult.HasValue)
                {
                    receiver.Scale = data.Scale * scaleMult;
                    if (receiver.ResizeVertical || receiver.ResizeHorizontal)
                    {
                        if (receiver.ResizeVertical)
                        {
                            receiver.RectHeight = (int)(data.Rect.Height * scaleMult);
                        }
                        if (receiver.ResizeHorizontal)
                        {
                            receiver.RectWidth = (int)(data.Rect.Width * scaleMult);
                        }
                        if (receiver is Structure structure && data.TexOffset.HasValue)
                        {
                            structure.TextureOffset = data.TexOffset.Value * scaleMult;
                        }
                    }
                    data.TextScales?.ForEach(pair => pair.Key.TextScale = pair.Value * scaleMult);
                    data.LightRanges?.ForEach(pair => pair.Key.Range = pair.Value * scaleMult);
                    data.Wires?.ForEach(pair => pair.Key.Width = pair.Value.Width * scaleMult);
                }

                Vector2 newEntityPos = MathUtils.RotatePoint((data.Pos - Pivot) * scaleMult, -rotationRad) + Pivot;
                receiver.Move(newEntityPos - receiver.DrawPosition);

                data.Wires?.ForEach(pair => pair.Key.SetNodes(pair.Value.Nodes.Select(TransformWireNode)));
                Vector2 TransformWireNode(Vector2 node) => MathUtils.RotatePoint((node - wirePivot) * scaleMult, -rotationRad) + wirePivot;
            }
        }

        public override LocalizedString GetDescription() => originalData.Count > 1
            ? TextManager.GetWithVariable("Undo.ChangedTransformMultiple", "[amount]", originalData.Count.ToString())
            : TextManager.GetWithVariable("Undo.ChangedTransform", "[item]", originalData.First().Key.Name);

        public override IEnumerable<ushort> GetAffectedEntityIds()
            => originalData.Keys.Select(e => e.ID);
    }

    internal enum WireCommandType
    {
        Connect,
        Disconnect,
        NodeMove,
        NodeAdd,
        NodeRemove
    }

    internal class WireCommand : Command
    {
        public readonly WireCommandType Type;
        public readonly ushort WireId;
        public readonly Identifier WirePrefabId;
        public readonly ushort TargetItemId;
        public readonly string ConnectionName;
        public readonly ushort OtherItemId;
        public readonly string OtherConnectionName;
        public readonly int NodeIndex;
        public readonly Vector2 OldNodePos;
        public readonly Vector2 NewNodePos;
        public readonly int AddedNodeIndex;
        public readonly Vector2 AddedNodePos;
        public readonly List<Vector2> NodeSnapshot;

        public WireCommand(WireCommandType type, Wire wire, Connection connection, Connection otherConnection = null)
        {
            Type = type;
            WireId = wire.Item.ID;
            WirePrefabId = wire.Item.Prefab.Identifier;
            TargetItemId = connection?.Item?.ID ?? 0;
            ConnectionName = connection?.Name ?? "";
            OtherItemId = otherConnection?.Item?.ID ?? 0;
            OtherConnectionName = otherConnection?.Name ?? "";
            NodeSnapshot = new List<Vector2>(wire.GetNodes());
        }

        public WireCommand(Wire wire, int nodeIndex, Vector2 oldPos, Vector2 newPos)
        {
            Type = WireCommandType.NodeMove;
            WireId = wire.Item.ID;
            WirePrefabId = wire.Item.Prefab.Identifier;
            NodeIndex = nodeIndex;
            OldNodePos = oldPos;
            NewNodePos = newPos;
            NodeSnapshot = new List<Vector2>(wire.GetNodes());
        }

        public WireCommand(WireCommandType type, Wire wire, int nodeIndex, Vector2 nodePos)
        {
            Debug.Assert(type == WireCommandType.NodeAdd || type == WireCommandType.NodeRemove);
            Type = type;
            WireId = wire.Item.ID;
            WirePrefabId = wire.Item.Prefab.Identifier;
            AddedNodeIndex = nodeIndex;
            AddedNodePos = nodePos;
            NodeSnapshot = new List<Vector2>(wire.GetNodes());
        }

        private Wire FindWire()
        {
            if (Entity.FindEntityByID(WireId) is Item wireItem)
            {
                return wireItem.GetComponent<Wire>();
            }
            return null;
        }

        private Connection FindConnection(ushort itemId, string connName)
        {
            if (string.IsNullOrEmpty(connName) || itemId == 0) return null;
            if (Entity.FindEntityByID(itemId) is Item item)
            {
                var panel = item.GetComponent<ConnectionPanel>();
                return panel?.Connections?.Find(c => c.Name == connName);
            }
            return null;
        }

        public override void Execute()
        {
            Wire wire = FindWire();
            if (wire == null) return;

            switch (Type)
            {
                case WireCommandType.Connect:
                    var conn = FindConnection(TargetItemId, ConnectionName);
                    if (conn != null)
                    {
                        conn.ConnectWire(wire);
                        wire.TryConnect(conn, false);
                    }
                    break;
                case WireCommandType.Disconnect:
                    var disconnConn = FindConnection(TargetItemId, ConnectionName);
                    if (disconnConn != null)
                    {
                        disconnConn.DisconnectWire(wire);
                    }
                    var disconnOther = FindConnection(OtherItemId, OtherConnectionName);
                    if (disconnOther != null)
                    {
                        disconnOther.DisconnectWire(wire);
                    }
                    break;
                case WireCommandType.NodeMove:
                    if (NodeIndex >= 0 && NodeIndex < wire.GetNodes().Count)
                    {
                        wire.MoveNode(NodeIndex, NewNodePos - wire.GetNodes()[NodeIndex]);
                    }
                    break;
                case WireCommandType.NodeAdd:
                    wire.SetNodes(NodeSnapshot);
                    break;
                case WireCommandType.NodeRemove:
                    wire.SetNodes(NodeSnapshot);
                    break;
            }
            if (!wire.Item.Removed) { wire.UpdateSections(); }
        }

        public override void UnExecute()
        {
            Wire wire = FindWire();
            if (wire == null && Type != WireCommandType.Disconnect) return;

            switch (Type)
            {
                case WireCommandType.Connect:
                    if (wire == null) return;
                    // disconnect from both ends
                    var conn = FindConnection(TargetItemId, ConnectionName);
                    if (conn != null)
                    {
                        conn.DisconnectWire(wire);
                        wire.RemoveConnection(conn);
                    }
                    var otherConn = FindConnection(OtherItemId, OtherConnectionName);
                    if (otherConn != null)
                    {
                        otherConn.DisconnectWire(wire);
                        wire.RemoveConnection(otherConn);
                    }
                    // this Connect command created the wire
                    wire.Item.Remove();
                    break;
                case WireCommandType.Disconnect:
                    if (wire == null)
                    {
                        // Wire was deleted — recreate it from prefab
                        var prefab = MapEntityPrefab.FindByIdentifier(WirePrefabId) as ItemPrefab;
                        if (prefab == null) return;
                        var sub = Submarine.MainSub;
                        if (sub == null) return;
                        var newItem = new Item(prefab, Vector2.Zero, sub, WireId);
                        wire = newItem.GetComponent<Wire>();
                        if (wire == null) return;
                        if (NodeSnapshot != null) { wire.SetNodes(NodeSnapshot); }
                    }
                    var reconnConn = FindConnection(TargetItemId, ConnectionName);
                    if (reconnConn != null)
                    {
                        reconnConn.ConnectWire(wire);
                        wire.TryConnect(reconnConn, false);
                    }
                    var reconnOther = FindConnection(OtherItemId, OtherConnectionName);
                    if (reconnOther != null)
                    {
                        reconnOther.ConnectWire(wire);
                        wire.TryConnect(reconnOther, false);
                    }
                    break;
                case WireCommandType.NodeMove:
                    if (NodeIndex >= 0 && NodeIndex < wire.GetNodes().Count)
                    {
                        wire.MoveNode(NodeIndex, OldNodePos - wire.GetNodes()[NodeIndex]);
                    }
                    break;
                case WireCommandType.NodeAdd:
                    if (AddedNodeIndex >= 0 && AddedNodeIndex < wire.GetNodes().Count)
                    {
                        var nodes = new List<Vector2>(wire.GetNodes());
                        nodes.RemoveAt(AddedNodeIndex);
                        wire.SetNodes(nodes);
                    }
                    break;
                case WireCommandType.NodeRemove:
                    {
                        var nodes = new List<Vector2>(wire.GetNodes());
                        if (AddedNodeIndex <= nodes.Count)
                        {
                            nodes.Insert(AddedNodeIndex, AddedNodePos);
                            wire.SetNodes(nodes);
                        }
                    }
                    break;
            }
            if (wire != null && !wire.Item.Removed) { wire.UpdateSections(); }
        }

        public override void Cleanup() { }

        public override LocalizedString GetDescription()
        {
            string wireName = WirePrefabId.Value ?? "wire";
            Item targetItem = Entity.FindEntityByID(TargetItemId) as Item;
            string targetName = targetItem?.Name ?? $"Item#{TargetItemId}";

            switch (Type)
            {
                case WireCommandType.Connect:
                    Item otherItem = Entity.FindEntityByID(OtherItemId) as Item;
                    string otherName = otherItem?.Name ?? $"Item#{OtherItemId}";
                    return new RawLString($"{targetName} [{ConnectionName}] wired to {otherName} [{OtherConnectionName}] using {wireName}");
                case WireCommandType.Disconnect:
                    if (OtherItemId != 0)
                    {
                        Item otherDisc = Entity.FindEntityByID(OtherItemId) as Item;
                        string otherDiscName = otherDisc?.Name ?? $"Item#{OtherItemId}";
                        return new RawLString($"{wireName} deleted ({targetName} [{ConnectionName}] — {otherDiscName} [{OtherConnectionName}])");
                    }
                    return new RawLString($"{wireName} disconnected from {targetName} [{ConnectionName}]");
                case WireCommandType.NodeMove:
                    return new RawLString($"Node on {wireName} moved");
                case WireCommandType.NodeAdd:
                    return new RawLString($"Node added to {wireName}");
                case WireCommandType.NodeRemove:
                    return new RawLString($"Node removed from {wireName}");
                default:
                    return new RawLString($"Wire change ({wireName})");
            }
        }

        public override IEnumerable<ushort> GetAffectedEntityIds()
        {
            yield return WireId;
            if (TargetItemId != 0) yield return TargetItemId;
            if (OtherItemId != 0) yield return OtherItemId;
        }
    }

    internal class LinkCommand : Command
    {
        public enum LinkCommandType { Link, Unlink }

        public LinkCommandType Type { get; }
        public ushort EntityId1 { get; }
        public ushort EntityId2 { get; }

        public LinkCommand(LinkCommandType type, ushort entityId1, ushort entityId2)
        {
            Type = type;
            EntityId1 = entityId1;
            EntityId2 = entityId2;
        }

        public override void Execute()
        {
            var entity1 = Entity.FindEntityByID(EntityId1) as MapEntity;
            var entity2 = Entity.FindEntityByID(EntityId2) as MapEntity;
            if (entity1 == null || entity2 == null) return;

            if (Type == LinkCommandType.Link)
            {
                if (!entity1.linkedTo.Contains(entity2)) entity1.linkedTo.Add(entity2);
                if (!entity2.linkedTo.Contains(entity1)) entity2.linkedTo.Add(entity1);
            }
            else
            {
                entity1.linkedTo.Remove(entity2);
                entity2.linkedTo.Remove(entity1);
            }
        }

        public override void UnExecute()
        {
            var entity1 = Entity.FindEntityByID(EntityId1) as MapEntity;
            var entity2 = Entity.FindEntityByID(EntityId2) as MapEntity;
            if (entity1 == null || entity2 == null) return;

            if (Type == LinkCommandType.Link)
            {
                entity1.linkedTo.Remove(entity2);
                entity2.linkedTo.Remove(entity1);
            }
            else
            {
                if (!entity1.linkedTo.Contains(entity2)) entity1.linkedTo.Add(entity2);
                if (!entity2.linkedTo.Contains(entity1)) entity2.linkedTo.Add(entity1);
            }
        }

        public override void Cleanup() { }

        public override LocalizedString GetDescription()
        {
            string name1 = (Entity.FindEntityByID(EntityId1) as MapEntity)?.Name ?? $"#{EntityId1}";
            string name2 = (Entity.FindEntityByID(EntityId2) as MapEntity)?.Name ?? $"#{EntityId2}";
            return Type == LinkCommandType.Link
                ? new RawLString($"Linked {name1} to {name2}")
                : new RawLString($"Unlinked {name1} from {name2}");
        }

        public override IEnumerable<ushort> GetAffectedEntityIds()
        {
            yield return EntityId1;
            yield return EntityId2;
        }
    }
}