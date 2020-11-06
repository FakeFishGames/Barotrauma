using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public abstract string GetDescription();
    }

    /// <summary>
    /// A command for setting and reverting a MapEntity rectangle
    /// <see cref="SubEditorScreen"/>
    /// <see cref="MapEntity"/>
    /// </summary>
    internal class TransformCommand : Command
    {
        private readonly List<MapEntity> Receivers;
        private readonly List<Rectangle> NewData;
        private readonly List<Rectangle> OldData;
        private readonly bool Resized;

        /// <summary>
        /// A command for setting and reverting a MapEntity rectangle
        /// </summary>
        /// <param name="receivers">Entities whose rectangle has been altered</param>
        /// <param name="newData">The new rectangle that is or will be applied to the map entity</param>
        /// <param name="oldData">Old rectangle the map entity had before</param>
        /// <param name="resized">If the transform was resized or not</param>
        /// <remarks>
        /// All lists should be equal in length, for every receiver there should be a corresponding entry at the same position in newData and oldData.
        /// </remarks>
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

        public override string GetDescription()
        {
            if (Resized)
            {
                return TextManager.GetWithVariable("Undo.ResizedItem", "[item]", Receivers.FirstOrDefault()?.Name);
            }

            return Receivers.Count > 1
                ? TextManager.GetWithVariable("Undo.MovedItemsMultiple", "[count]", Receivers.Count.ToString())
                : TextManager.GetWithVariable("Undo.MovedItem", "[item]", Receivers.FirstOrDefault()?.Name);
        }
    }

    /// <summary>
    /// A command that removes and unremoves map entities
    /// <see cref="ItemPrefab"/>
    /// <see cref="StructurePrefab"/>
    /// <seealso cref="SubEditorScreen"/>
    /// </summary>
    internal class AddOrDeleteCommand : Command
    {
        private readonly Dictionary<InventorySlotItem, Inventory> PreviousInventories = new Dictionary<InventorySlotItem, Inventory>();
        private readonly List<MapEntity> Receivers;
        private readonly List<MapEntity> CloneList;
        private readonly bool WasDeleted;
        private readonly List<AddOrDeleteCommand> ContainedItemsCommand = new List<AddOrDeleteCommand>();

        /// <summary>
        /// Creates a command where all entities share the same state.
        /// </summary>
        /// <param name="receivers">Entities that were deleted or added</param>
        /// <param name="wasDeleted">Whether or not all entities are or are going to be deleted</param>
        /// <param name="handleInventoryBehavior">Ignore item inventories when set to false, workaround for pasting</param>
        public AddOrDeleteCommand(List<MapEntity> receivers, bool wasDeleted, bool handleInventoryBehavior = true)
        {
            WasDeleted = wasDeleted;
            Receivers = receivers;

            try
            {
                foreach (MapEntity receiver in receivers)
                {
                    if (receiver is Item it && it.ParentInventory != null)
                    {
                        PreviousInventories.Add(new InventorySlotItem(Array.IndexOf(it.ParentInventory.Items, it), it), it.ParentInventory);
                    }
                }

                List<MapEntity> clonedTargets = MapEntity.Clone(receivers);

                List<MapEntity> itemsToDelete = new List<MapEntity>();
                foreach (MapEntity receiver in Receivers)
                {
                    if (receiver is Item it)
                    {
                        foreach (ItemContainer component in it.GetComponents<ItemContainer>())
                        {
                            if (component.Inventory == null) { continue; }

                            itemsToDelete.AddRange(component.Inventory.Items.Where(item => item != null && !item.Removed));
                        }
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
        {
            DeleteUndelete(true);
            ContainedItemsCommand?.ForEach(cmd => cmd.Execute());
        }

        public override void UnExecute()
        {
            DeleteUndelete(false);
            ContainedItemsCommand?.ForEach(cmd => cmd.UnExecute());
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
            ContainedItemsCommand?.ForEach(cmd => cmd.Cleanup());
        }

        private void DeleteUndelete(bool redo)
        {
            bool wasDeleted = WasDeleted;

            // We are redoing instead of undoing, flip the behavior
            if (redo) { wasDeleted = !wasDeleted; }

            if (wasDeleted)
            {
                Debug.Assert(Receivers.All(entity => entity.GetReplacementOrThis().Removed), "Tried to redo a deletion but some items were not deleted");

                List<MapEntity> clones = MapEntity.Clone(CloneList);
                int length = Math.Min(Receivers.Count, clones.Count);
                for (int i = 0; i < length; i++)
                {
                    MapEntity clone = clones[i], receiver = Receivers[i];

                    if (receiver.GetReplacementOrThis() is Item item && clone is Item cloneItem)
                    {
                        foreach (ItemComponent ic in item.Components)
                        {
                            int index = item.GetComponentIndex(ic);
                            ItemComponent component = cloneItem.Components.ElementAtOrDefault(index);
                            switch (component)
                            {
                                case null:
                                    continue;
                                case ItemContainer newContainer when newContainer.Inventory != null && ic is ItemContainer itemContainer && itemContainer.Inventory != null:
                                    itemContainer.Inventory.GetReplacementOrThiS().ReplacedBy = newContainer.Inventory;
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
                    MapEntity clone = clones[i], receiver = Receivers[i];

                    if (clone is Item it)
                    {
                        foreach (var (slotRef, inventory) in PreviousInventories)
                        {
                            if (slotRef.Item == receiver)
                            {
                                inventory.GetReplacementOrThiS().TryPutItem(it, slotRef.Slot, false, false, null, createNetworkEvent: false);
                            }
                        }
                    }
                }

                foreach (MapEntity clone in clones)
                {
                    clone.Submarine = Submarine.MainSub;
                }
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

        public override string GetDescription()
        {
            if (WasDeleted)
            {
                return Receivers.Count > 1
                    ? TextManager.GetWithVariable("Undo.RemovedItemsMultiple", "[count]", Receivers.Count.ToString())
                    : TextManager.GetWithVariable("Undo.RemovedItem", "[item]", Receivers.FirstOrDefault()?.Name);
            }

            return Receivers.Count > 1
                ? TextManager.GetWithVariable("Undo.AddedItemsMultiple", "[count]", Receivers.Count.ToString())
                : TextManager.GetWithVariable("Undo.AddedItem", "[item]", Receivers.FirstOrDefault()?.Name);
        }
    }

    /// <summary>
    /// A command that places or drops items out of inventories
    /// </summary>
    /// <see cref="Inventory"/>
    /// <see cref="MapEntity"/>
    internal class InventoryPlaceCommand : Command
    {
        private readonly Inventory Inventory;
        private readonly List<InventorySlotItem> Receivers;
        private readonly bool wasDropped;

        public InventoryPlaceCommand(Inventory inventory, List<Item> items, bool dropped)
        {
            Inventory = inventory;
            Receivers = items.Select(item => new InventorySlotItem(Array.IndexOf(inventory.Items, item), item)).ToList();
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
            // flip the behavior if the item was dropped instead of inserted
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
                    Inventory.GetReplacementOrThiS().TryPutItem(item, slot, false, false, null, createNetworkEvent: false);
                }
            }
        }

        public override string GetDescription()
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
                ? TextManager.GetWithVariables("Undo.ContainedItemsMultiple", new[] { "[count]", "[container]" }, new[] { Receivers.Count.ToString(), container })
                : TextManager.GetWithVariables("Undo.ContainedItem", new[] { "[item]", "[container]" }, new[] { Receivers.FirstOrDefault().Item.Name, container });
        }
    }

    /// <summary>
    /// A command that sets item properties
    /// </summary>
    internal class PropertyCommand : Command
    {
        private Dictionary<object, List<ISerializableEntity>> OldProperties;
        private readonly List<ISerializableEntity> Receivers;
        private readonly string PropertyName;
        private readonly object NewProperties;
        private string sanitizedProperty;

        public readonly int PropertyCount;

        /// <summary>
        /// A command that sets item properties
        /// </summary>
        /// <param name="receivers">Affected entities</param>
        /// <param name="propertyName">Real property name, not all lowercase</param>
        /// <param name="newData"></param>
        /// <param name="oldData"></param>
        public PropertyCommand(List<ISerializableEntity> receivers, string propertyName, object newData, Dictionary<object, List<ISerializableEntity>> oldData)
        {
            Receivers = receivers;
            PropertyName = propertyName;
            OldProperties = oldData;
            NewProperties = newData;
            PropertyCount = receivers.Count;
            SanitizeProperty();
        }

        public PropertyCommand(ISerializableEntity receiver, string propertyName, object newData, object oldData)
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
                    Dictionary<string, SerializableProperty> props = receiver.SerializableProperties;

                    if (props.TryGetValue(PropertyName.ToLowerInvariant(), out SerializableProperty prop))
                    {
                        prop.TrySetValue(receiver, data);
                        // Update the editing hud
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

        public override string GetDescription()
        {
            return Receivers.Count > 1
                ? TextManager.GetWithVariables("Undo.ChangedPropertyMultiple", new[] { "[property]", "[count]", "[value]" }, new[] { PropertyName, Receivers.Count.ToString(), sanitizedProperty })
                : TextManager.GetWithVariables("Undo.ChangedProperty", new[] { "[property]", "[item]", "[value]" }, new[] { PropertyName, Receivers.FirstOrDefault()?.Name, sanitizedProperty });
        }
    }

    /// <summary>
    /// A command that moves items around in inventories
    /// </summary>
    /// <see cref="oldInventory"/>
    /// <see cref="MapEntity"/>
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
                newInventory?.GetReplacementOrThiS().TryPutItem(item, newSlot, true, false, null, createNetworkEvent: false);
            }
        }

        public override void UnExecute()
        {
            if (targetItem.GetReplacementOrThis() is Item item)
            {
                oldInventory?.GetReplacementOrThiS().TryPutItem(item, oldSlot, true, false, null, createNetworkEvent: false);
            }
        }

        public override void Cleanup() { }

        public override string GetDescription()
        {
            return TextManager.GetWithVariable("Undo.MovedItem", "[item]", targetItem.Name);
        }
    }
}