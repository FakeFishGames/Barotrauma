using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public void ServerEventRead(IReadMessage msg, Client c)
        {
            List<Item> prevItems = new List<Item>(AllItems.Distinct());

            SharedRead(msg, out var newItemIDs);

            if (c == null || c.Character == null) { return; }

            bool accessible = c.Character.CanAccessInventory(this);
            if (this is CharacterInventory characterInventory && accessible)
            {
                if (Owner == null || Owner is not Character ownerCharacter)
                {
                    accessible = false;
                }
                else if (!characterInventory.AccessibleWhenAlive && !ownerCharacter.IsDead && !characterInventory.AccessibleByOwner)
                {
                    accessible = false;
                }
            }

            if (!accessible)
            {
                //create a network event to correct the client's inventory state
                //otherwise they may have an item in their inventory they shouldn't have been able to pick up,
                //and receiving an event for that inventory later will cause the item to be dropped
                CreateNetworkEvent();
                for (int i = 0; i < capacity; i++)
                {
                    foreach (ushort id in newItemIDs[i])
                    {
                        if (Entity.FindEntityByID(id) is not Item item) { continue; }
                        item.PositionUpdateInterval = 0.0f;
                        if (item.ParentInventory != null && item.ParentInventory != this)
                        {
                            item.ParentInventory.CreateNetworkEvent();
                        }
                    }
                }
                return;
            }

            List<Inventory> prevItemInventories = new List<Inventory>() { this };

            for (int i = 0; i < capacity; i++)
            {
                foreach (Item item in slots[i].Items.ToList())
                {
                    if (!newItemIDs[i].Contains(item.ID))
                    {
                        Item droppedItem = item;
                        Entity prevOwner = Owner;
                        Inventory previousInventory = droppedItem.ParentInventory;
                        droppedItem.Drop(null);
                        droppedItem.PreviousParentInventory = previousInventory;

                        var previousCharacterInventory = prevOwner switch
                        {
                            Item itemInventory => itemInventory.FindParentInventory(inventory => inventory is CharacterInventory) as CharacterInventory,
                            Character character => character.Inventory,
                            _ => null
                        };

                        if (previousCharacterInventory != null && previousCharacterInventory != c.Character?.Inventory)
                        {
                            GameMain.Server?.KarmaManager.OnItemTakenFromPlayer(previousCharacterInventory, c, droppedItem);
                        }
                        
                        if (droppedItem.body != null && prevOwner != null)
                        {
                            droppedItem.body.SetTransform(prevOwner.SimPosition, 0.0f);
                        }
                    }
                }

                foreach (ushort id in newItemIDs[i])
                {
                    Item newItem = id == 0 ? null : Entity.FindEntityByID(id) as Item;
                    prevItemInventories.Add(newItem?.ParentInventory);
                }                
            }

            for (int i = 0; i < capacity; i++)
            {
                foreach (ushort id in newItemIDs[i])
                {
                    if (Entity.FindEntityByID(id) is not Item item || slots[i].Contains(item)) { continue; }

                    if (item.GetComponent<Pickable>() is not Pickable pickable ||
                        (pickable.IsAttached && !pickable.PickingDone) ||
                        item.AllowedSlots.None())
                    {
                        DebugConsole.AddWarning($"Client {c.Name} tried to pick up a non-pickable item \"{item}\" (parent inventory: {item.ParentInventory?.Owner.ToString() ?? "null"})");
                        continue;
                    }

                    if (GameMain.Server != null)
                    {
                        var holdable = item.GetComponent<Holdable>();
                        if (holdable != null && !holdable.CanBeDeattached()) { continue; }

                        if (!prevItems.Contains(item) && !item.CanClientAccess(c) && 
                            (c.Character == null || item.PreviousParentInventory == null || !c.Character.CanAccessInventory(item.PreviousParentInventory)))
                        {
    #if DEBUG || UNSTABLE
                            DebugConsole.NewMessage($"Client {c.Name} failed to pick up item \"{item}\" (parent inventory: {item.ParentInventory?.Owner.ToString() ?? "null"}). No access.", Color.Yellow);
    #endif
                            if (item.body != null && !c.PendingPositionUpdates.Contains(item))
                            {
                                c.PendingPositionUpdates.Enqueue(item);
                            }
                            item.PositionUpdateInterval = 0.0f;                            
                            continue;
                        }
                    }
                    TryPutItem(item, i, true, true, c.Character, false);
                    for (int j = 0; j < capacity; j++)
                    {
                        if (slots[j].Contains(item) && !newItemIDs[j].Contains(item.ID))
                        {
                            slots[j].RemoveItem(item);
                        }
                    }
                }
            }

            EnsureItemsInBothHands(c.Character);

            CreateNetworkEvent();
            foreach (Inventory prevInventory in prevItemInventories.Distinct())
            {
                if (prevInventory != this) { prevInventory?.CreateNetworkEvent(); }
            }

            foreach (Item item in AllItems.Distinct())
            {
                if (item == null) { continue; }
                if (!prevItems.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        HumanAIController.ItemTaken(item, c.Character);
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " picked up " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " placed " + item.Name + " in " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
            foreach (Item item in prevItems.Distinct())
            {
                if (item == null) { continue; }
                if (!AllItems.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " dropped " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " removed " + item.Name + " from " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
        }

        private void EnsureItemsInBothHands(Character character)
        {
            if (this is not CharacterInventory charInv) { return; }

            int leftHandSlot = charInv.FindLimbSlot(InvSlotType.LeftHand),
                rightHandSlot = charInv.FindLimbSlot(InvSlotType.RightHand);

            if (IsSlotIndexOutOfBound(leftHandSlot) || IsSlotIndexOutOfBound(rightHandSlot)) { return; }

            TryPutInOppositeHandSlot(rightHandSlot, leftHandSlot);
            TryPutInOppositeHandSlot(leftHandSlot, rightHandSlot);

            void TryPutInOppositeHandSlot(int originalSlot, int otherHandSlot)
            {
                const InvSlotType bothHandSlot = InvSlotType.LeftHand | InvSlotType.RightHand;

                foreach (Item it in slots[originalSlot].Items)
                {
                    if (it.AllowedSlots.None(static s => s.HasFlag(bothHandSlot)) || slots[otherHandSlot].Contains(it)) { continue; }

                    TryPutItem(it, otherHandSlot, true, true, character, false);
                }
            }

            bool IsSlotIndexOutOfBound(int index) => index < 0 || index >= slots.Length;
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            SharedWrite(msg, extraData);
        }
    }
}
