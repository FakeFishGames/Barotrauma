using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IClientSerializable
    {
        private readonly Dictionary<Client, List<ushort>[]> receivedItemIds = new Dictionary<Client, List<ushort>[]>();

        public void ServerEventRead(IReadMessage msg, Client sender)
        {
            // if the dictionary doesn't contain the client entry, create a new one
            if (!receivedItemIds.TryGetValue(sender, out List<ushort>[] receivedItemIdsFromClient))
            {
                receivedItemIdsFromClient = new List<ushort>[capacity];
                receivedItemIds.Add(sender, receivedItemIdsFromClient);
            }

            // Read some item ids from the message. readyToApply waits for all the data from possible multiple messages.
            SharedRead(msg, receivedItemIdsFromClient, out bool readyToApply);
            if (!readyToApply) { return; }

            if (sender == null || sender.Character == null) { return; }
            
            if (!IsInventoryAccessible())
            {
                CreateCorrectiveNetworkEvent();
                return;
            }
            
            List<Item> prevItems = new List<Item>(AllItems.Distinct());
            List<Inventory> prevItemInventories = new List<Inventory>() { this };

            //we need to check which of the items the client (sender) can access at this point, before we start shuffling things around
            //otherwise if you're e.g. holding an item to access a cabinet, and picking up an item from the cabinet unequips the item you're holding,
            //you would fail to pick up the item because it gets unequipped before checking whether you can access the cabinet.
            var itemAccessibility = GetItemAccessibility();
            
            HandleRemovedItems();

            HandleAddedItems();

            EnsureItemsInBothHands(sender.Character);

            receivedItemIds.Remove(sender);

            CreateNetworkEvent();
            foreach (Inventory prevInventory in prevItemInventories.Distinct())
            {
                if (prevInventory != this) { prevInventory?.CreateNetworkEvent(); }
            }

            ServerLogAddedItems();

            ServerLogRemovedItems();
            
            #region local functions
            bool IsInventoryAccessible() => sender.Character.CanAccessInventory(this, IsDragAndDropGiveAllowed ? CharacterInventory.AccessLevel.Allowed : CharacterInventory.AccessLevel.Limited);
            
            void CreateCorrectiveNetworkEvent()
            {
                // create a network event to correct the client's inventory state.
                // Otherwise they may have an item in their inventory they shouldn't have been able to pick up,
                // and receiving an event for that inventory later will cause the item to be dropped
                CreateNetworkEvent();
                for (int i = 0; i < capacity; i++)
                {
                    foreach (ushort itemId in receivedItemIdsFromClient[i])
                    {
                        if (Entity.FindEntityByID(itemId) is not Item item) { continue; }
                        item.PositionUpdateInterval = 0.0f;
                        if (item.ParentInventory != null && item.ParentInventory != this)
                        {
                            item.ParentInventory.CreateNetworkEvent();
                        }
                    }
                }
            }
            
            Dictionary<Item, bool> GetItemAccessibility()
            {
                Dictionary<Item, bool> itemAccessibility = new Dictionary<Item, bool>();
                
                for (int i = 0; i < capacity; i++)
                {
                    // for every item that the new inventory state contains
                    foreach (ushort itemId in receivedItemIdsFromClient[i])
                    {
                        // if there is no such item, skip
                        if (Entity.FindEntityByID(itemId) is not Item item) { continue; }
                        // add entry: can the sender access the item?
                        itemAccessibility[item] = item.CanClientAccess(sender);
                    }
                }
                
                // we now have accessibility for every item in the new inventory state
                // but not for the items that were in the inventory before and perhaps dropped, so let's add those as well
                foreach (var item in prevItems)
                {
                    if (!itemAccessibility.ContainsKey(item))
                    {
                        itemAccessibility[item] = item.CanClientAccess(sender);
                    }
                }
                
                return itemAccessibility;
            }

            void HandleRemovedItems()
            {
                for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
                {
                    foreach (Item item in slots[slotIndex].Items.ToList())
                    {
                        bool shouldBeRemoved = !receivedItemIdsFromClient[slotIndex].Contains(item.ID) && 
                                               item.IsInteractable(sender.Character); // item is interactable to sender: not hidden and player team
                        if (shouldBeRemoved)
                        {
                            bool itemAccessDenied = prevItems.Contains(item) && // if the item was in the inventory before
                                                       !itemAccessibility[item] && // and the sender is not allowed to access it
                                                       (item.PreviousParentInventory == null || // and either the item has no previous inventory
                                                        !sender.Character.CanAccessInventory(item.PreviousParentInventory)); // or the sender can't access the previous inventory
                            
                            if (itemAccessDenied)
                            {
#if DEBUG || UNSTABLE
                                DebugConsole.NewMessage($"Client {sender.Name} failed to drop item \"{item}\" (parent inventory: {item.ParentInventory?.Owner.ToString() ?? "null"}). No access.", Color.Yellow);
#endif
                                continue;
                            }
                            
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

                            if (previousCharacterInventory != null && previousCharacterInventory != sender.Character?.Inventory)
                            {
                                GameMain.Server?.KarmaManager.OnItemTakenFromPlayer(previousCharacterInventory, sender, droppedItem);
                            }
                        
                            if (droppedItem.body != null && prevOwner != null)
                            {
                                droppedItem.body.SetTransform(prevOwner.SimPosition, 0.0f);
                            }
                        }
                    }

                    foreach (ushort id in receivedItemIdsFromClient[slotIndex])
                    {
                        Item newItem = id == 0 ? null : Entity.FindEntityByID(id) as Item;
                        prevItemInventories.Add(newItem?.ParentInventory);
                    }                
                }
            }

            void HandleAddedItems()
            {
                for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
                {
                    foreach (ushort id in receivedItemIdsFromClient[slotIndex])
                    {
                        if (Entity.FindEntityByID(id) is not Item item || slots[slotIndex].Contains(item)) { continue; }

                        if (item.GetComponent<Pickable>() is not Pickable pickable ||
                            (pickable.IsAttached && !pickable.PickingDone) || item.AllowedSlots.None() || !item.IsInteractable(sender.Character))
                        {
                            DebugConsole.AddWarning($"Client {sender.Name} tried to pick up a non-pickable item \"{item}\" (parent inventory: {item.ParentInventory?.Owner.ToString() ?? "null"})",
                                item.Prefab.ContentPackage);
                            continue;
                        }

                        if (GameMain.Server != null)
                        {
                            var holdable = item.GetComponent<Holdable>();
                            if (holdable != null && !holdable.CanBeDeattached()) { continue; }

                            bool itemAccessDenied = !prevItems.Contains(item) && !itemAccessibility[item] &&
                                                 (sender.Character == null || item.PreviousParentInventory == null || !sender.Character.CanAccessInventory(item.PreviousParentInventory));
                            
                            if (itemAccessDenied)
                            {
#if DEBUG || UNSTABLE
                                DebugConsole.NewMessage($"Client {sender.Name} failed to pick up item \"{item}\" (parent inventory: {item.ParentInventory?.Owner.ToString() ?? "null"}). No access.", Color.Yellow);
#endif
                                if (item.body != null && !sender.PendingPositionUpdates.Contains(item))
                                {
                                    sender.PendingPositionUpdates.Enqueue(item);
                                }
                                item.PositionUpdateInterval = 0.0f;                            
                                continue;
                            }
                        }
                        TryPutItem(item, slotIndex, true, true, sender.Character, false);
                        for (int j = 0; j < capacity; j++)
                        {
                            if (slots[j].Contains(item) && !receivedItemIdsFromClient[j].Contains(item.ID))
                            {
                                slots[j].RemoveItem(item);
                            }
                        }
                    }
                }
            }

            void ServerLogAddedItems()
            {
                foreach (Item item in AllItems.DistinctBy(it => it.Prefab))
                {
                    if (item == null) { continue; }
                    if (!prevItems.Contains(item))
                    {
                        int amount = AllItems.Count(it => it.Prefab == item.Prefab && !prevItems.Contains(it));
                        string amountText = amount > 1 ? $"x{amount} " : string.Empty;
                        if (Owner == sender.Character)
                        {
                            HumanAIController.ItemTaken(item, sender.Character);
                            GameServer.Log($"{GameServer.CharacterLogName(sender.Character)} picked up {amountText}{item.Name}", ServerLog.MessageType.Inventory);
                        }
                        else
                        {
                            GameServer.Log($"{GameServer.CharacterLogName(sender.Character)} placed {amountText}{item.Name} in the inventory of {Owner}", ServerLog.MessageType.Inventory);
                        }
                    }
                }
            }

            void ServerLogRemovedItems()
            {
                var droppedItems = prevItems.Where(it => it != null && !AllItems.Contains(it));
                foreach (Item item in droppedItems.DistinctBy(it => it.Prefab))
                {
                    var matchingItems = prevItems.Where(it => it.Prefab == item.Prefab && !AllItems.Contains(it));
                    int amount = matchingItems.Count();
                    string amountText = amount > 1 ? $"x{amount} " : string.Empty;
                    if (Owner == sender.Character)
                    {
                        GameServer.Log($"{GameServer.CharacterLogName(sender.Character)} dropped {amountText}{item.Name}", ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log($"{GameServer.CharacterLogName(sender.Character)} removed {amountText}{item.Name} from the inventory of {Owner}", ServerLog.MessageType.Inventory);
                    }
                    item.CreateDroppedStack(matchingItems, allowClientExecute: true);                
                }
            }
            #endregion
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
    }
}
