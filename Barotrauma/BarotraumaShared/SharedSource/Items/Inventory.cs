using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public const int MaxStackSize = 32;

        public class ItemSlot
        {
            private readonly List<Item> items = new List<Item>(MaxStackSize);

            public bool HideIfEmpty;

            public IEnumerable<Item> Items
            {
                get { return items; }
            }

            public int ItemCount
            {
                get { return items.Count; }
            }

            public bool CanBePut(Item item, bool ignoreCondition = false)
            {
                if (item == null) { return false; }
                if (items.Count > 0)
                {
                    if (!ignoreCondition)
                    {
                        if (item.IsFullCondition)
                        {
                            if (items.Any(it => !it.IsFullCondition)) { return false; }
                        }
                        else if (MathUtils.NearlyEqual(item.Condition, 0.0f))
                        {
                            if (items.Any(it => !MathUtils.NearlyEqual(it.Condition, 0.0f))) { return false; }
                        }
                        else
                        {
                            return false;
                        }
                    }
                    if (items[0].Quality != item.Quality) { return false; }
                    if (items[0].Prefab.Identifier != item.Prefab.Identifier || items.Count + 1 > item.Prefab.MaxStackSize)
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool CanBePut(ItemPrefab itemPrefab, float? condition = null)
            {
                if (itemPrefab == null) { return false; }
                if (items.Count > 0)
                {
                    if (condition.HasValue)
                    {
                        if (MathUtils.NearlyEqual(condition.Value, 0.0f))
                        {
                            if (items.Any(it => it.Condition > 0.0f)) { return false; }
                        }
                        else if (MathUtils.NearlyEqual(condition.Value, itemPrefab.Health))
                        {
                            if (items.Any(it => !it.IsFullCondition)) { return false; }
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (items.Any(it => !it.IsFullCondition)) { return false; }
                    }

                    if (items[0].Prefab.Identifier != itemPrefab.Identifier ||
                        items.Count + 1 > itemPrefab.MaxStackSize)
                    {
                        return false;
                    }
                }
                return true;
            }

            /// <param name="maxStackSize">Defaults to <see cref="ItemPrefab.MaxStackSize"/> if null</param>
            public int HowManyCanBePut(ItemPrefab itemPrefab, int? maxStackSize = null, float? condition = null)
            {
                if (itemPrefab == null) { return 0; }
                maxStackSize ??= itemPrefab.MaxStackSize;
                if (items.Count > 0)
                {
                    if (condition.HasValue)
                    {
                        if (MathUtils.NearlyEqual(condition.Value, 0.0f))
                        {
                            if (items.Any(it => it.Condition > 0.0f)) { return 0; }
                        }
                        else if (MathUtils.NearlyEqual(condition.Value, itemPrefab.Health))
                        {
                            if (items.Any(it => !it.IsFullCondition)) { return 0; }
                        }
                        else
                        {
                            return 0;
                        }
                    }
                    else
                    {
                        if (items.Any(it => !it.IsFullCondition)) { return 0; }
                    }
                    if (items[0].Prefab.Identifier != itemPrefab.Identifier) { return 0; }
                    return maxStackSize.Value - items.Count;
                }
                else
                {
                    return maxStackSize.Value;
                }
            }

            public void Add(Item item)
            {
                if (item == null)
                {
                    throw new InvalidOperationException("Tried to add a null item to an inventory slot.");
                }
                if (items.Count > 0)
                {
                    if (items[0].Prefab.Identifier != item.Prefab.Identifier)
                    {
                        throw new InvalidOperationException("Tried to stack different types of items.");
                    }
                    else if (items.Count + 1 > item.Prefab.MaxStackSize)
                    {
                        throw new InvalidOperationException("Tried to add an item to a full inventory slot (stack already full).");
                    }
                }
                if (items.Contains(item)) { return; }
                items.Add(item);
            }

            /// <summary>
            /// Removes one item from the slot
            /// </summary>
            public Item RemoveItem()
            {
                if (items.Count == 0) { return null; }

                var item = items[0];
                items.RemoveAt(0);
                return item;
            }

            public void RemoveItem(Item item)
            {
                items.Remove(item);
            }

            /// <summary>
            /// Removes all items from the slot
            /// </summary>
            public void RemoveAllItems()
            {
                items.Clear();
            }

            public bool Any()
            {
                return items.Count > 0;
            }

            public bool Empty()
            {
                return items.Count == 0;
            }

            public Item First()
            {
                return items[0];
            }

            public Item FirstOrDefault()
            {
                return items.FirstOrDefault();
            }

            public Item LastOrDefault()
            {
                return items.LastOrDefault();
            }

            public bool Contains(Item item)
            {
                return items.Contains(item);
            }

        }

        public readonly Entity Owner;

        protected readonly int capacity;
        protected readonly ItemSlot[] slots;
        
        public bool Locked;

        protected float syncItemsDelay;

        /// <summary>
        /// All items contained in the inventory. Stacked items are returned as individual instances. DO NOT modify the contents of the inventory while enumerating this list.
        /// </summary>
        public IEnumerable<Item> AllItems
        {
            get
            {
                for (int i = 0; i < capacity; i++)
                {
                    foreach (var item in slots[i].Items)
                    {
                        bool duplicateFound = false;
                        for (int j = 0; j < i; j++)
                        {
                            if (slots[j].Items.Contains(item))
                            {
                                duplicateFound = true;
                                break;
                            }
                        }
                        if (!duplicateFound) { yield return item; }
                    }
                }
            }
        }

        private readonly List<Item> allItemsList = new List<Item>();
        /// <summary>
        /// All items contained in the inventory. Allows modifying the contents of the inventory while being enumerated.
        /// </summary>
        public IEnumerable<Item> AllItemsMod
        {
            get
            {
                allItemsList.Clear();
                allItemsList.AddRange(AllItems);
                return allItemsList;
            }
        }

        public int Capacity
        {
            get { return capacity; }
        }

        public bool AllowSwappingContainedItems = true;

        public Inventory(Entity owner, int capacity, int slotsPerRow = 5)
        {
            this.capacity = capacity;

            this.Owner = owner;

            slots = new ItemSlot[capacity];
            for (int i = 0; i < capacity; i++)
            {
                slots[i] = new ItemSlot();
            }

#if CLIENT
            this.slotsPerRow = slotsPerRow;

            if (DraggableIndicator == null)
            {
                DraggableIndicator = GUI.Style.GetComponentStyle("GUIDragIndicator").GetDefaultSprite();

                slotHotkeySprite = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(258, 7, 120, 120), null, 0);

                EquippedIndicator = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(550, 137, 87, 16), new Vector2(0.5f, 0.5f), 0);
                EquippedHoverIndicator = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(550, 157, 87, 16), new Vector2(0.5f, 0.5f), 0);
                EquippedClickedIndicator = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(550, 177, 87, 16), new Vector2(0.5f, 0.5f), 0);

                UnequippedIndicator = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(550, 197, 87, 16), new Vector2(0.5f, 0.5f), 0);
                UnequippedHoverIndicator = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(550, 217, 87, 16), new Vector2(0.5f, 0.5f), 0);
                UnequippedClickedIndicator = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(550, 237, 87, 16), new Vector2(0.5f, 0.5f), 0);
            }
#endif
        }

        /// <summary>
        /// Is the item contained in this inventory. Does not recursively check items inside items.
        /// </summary>
        public bool Contains(Item item)
        {
            return slots.Any(i => i.Contains(item));
        }

        /// <summary>
        /// Return the first item in the inventory, or null if the inventory is empty.
        /// </summary>
        public Item FirstOrDefault()
        {
            foreach (var itemSlot in slots)
            {
                var item = itemSlot.FirstOrDefault();
                if (item != null) { return item; }
            }
            return null;
        }

        /// <summary>
        /// Return the last item in the inventory, or null if the inventory is empty.
        /// </summary>
        public Item LastOrDefault()
        {
            for (int i = slots.Length - 1; i >= 0; i--)
            {
                var item = slots[i].LastOrDefault();
                if (item != null) { return item; }
            }
            return null;
        }

        /// <summary>
        /// Get the item stored in the specified inventory slot. If the slot contains a stack of items, returns the first item in the stack.
        /// </summary>
        public Item GetItemAt(int index)
        {
            if (index < 0 || index >= slots.Length) { return null; }
            return slots[index].FirstOrDefault();
        }

        /// <summary>
        /// Get all the item stored in the specified inventory slot. Can return more than one item if the slot contains a stack of items.
        /// </summary>
        public IEnumerable<Item> GetItemsAt(int index)
        {
            if (index < 0 || index >= slots.Length) { return Enumerable.Empty<Item>(); }
            return slots[index].Items;
        }

        /// <summary>
        /// Find the index of the first slot the item is contained in.
        /// </summary>
        public int FindIndex(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Contains(item)) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// Find the indices of all the slots the item is contained in (two-hand items for example can be in multiple slots). Note that this method instantiates a new list.
        /// </summary>
        public List<int> FindIndices(Item item)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Contains(item)) { indices.Add(i); }
            }
            return indices;
        }

        /// <summary>
        /// Returns true if the item owns any of the parent inventories.
        /// </summary>
        public virtual bool ItemOwnsSelf(Item item)
        {
            if (Owner == null) { return false; }
            if (!(Owner is Item)) { return false; }
            Item ownerItem = Owner as Item;
            if (ownerItem == item) { return true; }
            if (ownerItem.ParentInventory == null) { return false; }
            return ownerItem.ParentInventory.ItemOwnsSelf(item);
        }

        public virtual int FindAllowedSlot(Item item, bool ignoreCondition = false)
        {
            if (ItemOwnsSelf(item)) { return -1; }

            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (slots[i].Contains(item)) { return -1; }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].CanBePut(item, ignoreCondition)) { return i; }
            }

            return -1;
        }

        /// <summary>
        /// Can the item be put in the inventory (i.e. is there a suitable free slot or a stack the item can be put in).
        /// </summary>
        public bool CanBePut(Item item)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (CanBePutInSlot(item, i)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Can the item be put in the specified slot.
        /// </summary>
        public virtual bool CanBePutInSlot(Item item, int i, bool ignoreCondition = false)
        {
            if (ItemOwnsSelf(item)) { return false; }
            if (i < 0 || i >= slots.Length) { return false; }
            return slots[i].CanBePut(item, ignoreCondition);
        }

        public bool CanBePut(ItemPrefab itemPrefab, float? condition = null)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (CanBePutInSlot(itemPrefab, i, condition)) { return true; }
            }
            return false;
        }

        public virtual bool CanBePutInSlot(ItemPrefab itemPrefab, int i, float? condition = null)
        {
            if (i < 0 || i >= slots.Length) { return false; }
            return slots[i].CanBePut(itemPrefab, condition);
        }

        public int HowManyCanBePut(ItemPrefab itemPrefab, float? condition = null)
        {
            int count = 0;
            for (int i = 0; i < capacity; i++)
            {
                count += HowManyCanBePut(itemPrefab, i, condition);
            }
            return count;
        }

        public virtual int HowManyCanBePut(ItemPrefab itemPrefab, int i, float? condition)
        {
            if (i < 0 || i >= slots.Length) { return 0; }
            return slots[i].HowManyCanBePut(itemPrefab, condition: condition);
        }

        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public virtual bool TryPutItem(Item item, Character user, IEnumerable<InvSlotType> allowedSlots = null, bool createNetworkEvent = true, bool ignoreCondition = false)
        {
            int slot = FindAllowedSlot(item, ignoreCondition);
            if (slot < 0) { return false; }

            PutItem(item, slot, user, true, createNetworkEvent);
            return true;
        }

        public virtual bool TryPutItem(Item item, int i, bool allowSwapping, bool allowCombine, Character user, bool createNetworkEvent = true, bool ignoreCondition = false)
        {
            if (i < 0 || i >= slots.Length)
            {
                string errorMsg = "Inventory.TryPutItem failed: index was out of range(" + i + ").\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Inventory.TryPutItem:IndexOutOfRange", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return false;
            }

            if (Owner == null) return false;
            //there's already an item in the slot
            if (slots[i].Any() && allowCombine)
            {
                if (slots[i].First().Combine(item, user))
                {
                    //item in the slot removed as a result of combining -> put this item in the now free slot
                    if (!slots[i].Any())
                    {
                        return TryPutItem(item, i, allowSwapping, allowCombine, user, createNetworkEvent, ignoreCondition);
                    }
                    return true;
                }
            }
            if (CanBePutInSlot(item, i, ignoreCondition))
            {
                PutItem(item, i, user, true, createNetworkEvent);
                return true;
            }
            else if (slots[i].Any() && item.ParentInventory != null && allowSwapping)
            {
                var itemInSlot = slots[i].First();
                if (itemInSlot.OwnInventory != null && 
                    !itemInSlot.OwnInventory.Contains(item) &&
                    (itemInSlot.GetComponent<ItemContainer>()?.GetMaxStackSize(0) ?? 0) == 1 && 
                    itemInSlot.OwnInventory.TrySwapping(0, item, user, createNetworkEvent, swapWholeStack: false))
                {
                    return true;
                }
                return 
                    TrySwapping(i, item, user, createNetworkEvent, swapWholeStack: true) || 
                    TrySwapping(i, item, user, createNetworkEvent, swapWholeStack: false);
            }
            else
            {
#if CLIENT
                if (visualSlots != null && createNetworkEvent) { visualSlots[i].ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.9f); }
#endif
                return false;
            }
        }

        protected virtual void PutItem(Item item, int i, Character user, bool removeItem = true, bool createNetworkEvent = true)
        {
            if (i < 0 || i >= slots.Length)
            {
                string errorMsg = "Inventory.PutItem failed: index was out of range(" + i + ").\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Inventory.PutItem:IndexOutOfRange", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            if (Owner == null) { return; }

            Inventory prevInventory = item.ParentInventory;
            Inventory prevOwnerInventory = item.FindParentInventory(inv => inv is CharacterInventory);

            if (createNetworkEvent)
            {
                CreateNetworkEvent();
                //also delay syncing the inventory the item was inside
                if (prevInventory != null && prevInventory != this) { prevInventory.syncItemsDelay = 1.0f; }
            }

            if (removeItem)
            {
                item.Drop(user);
                if (item.ParentInventory != null) { item.ParentInventory.RemoveItem(item); }
            }

            slots[i].Add(item);
            item.ParentInventory = this;

#if CLIENT
            if (visualSlots != null)
            {
                visualSlots[i].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                if (selectedSlot?.Inventory == this) { selectedSlot.ForceTooltipRefresh = true; }
            }
#endif

            if (item.body != null)
            {
                item.body.Enabled = false;
                item.body.BodyType = FarseerPhysics.BodyType.Dynamic;
            }
            
#if SERVER
            if (prevOwnerInventory is CharacterInventory characterInventory && characterInventory != this && Owner == user)
            {
                var client = GameMain.Server?.ConnectedClients?.Find(cl => cl.Character == user);
                GameMain.Server?.KarmaManager.OnItemTakenFromPlayer(characterInventory, client, item);
            }
#endif
            if (this is CharacterInventory)
            {
                if (prevInventory != this && prevOwnerInventory != this)
                {
                    HumanAIController.ItemTaken(item, user);
                }
            }
            else
            {
                if (item.FindParentInventory(inv => inv is CharacterInventory) is CharacterInventory currentInventory)
                {
                    if (currentInventory != prevInventory)
                    {
                        HumanAIController.ItemTaken(item, user);
                    }
                }
            }
        }

        public bool IsEmpty()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].Any()) { return false; }
            }

            return true;
        }

        /// <summary>
        /// Is there room to put more items in the inventory. Doesn't take stacking into account by default.
        /// </summary>
        /// <param name="takeStacksIntoAccount">If true, the inventory is not considered full if all the stacks are not full.</param>
        public virtual bool IsFull(bool takeStacksIntoAccount = false)
        {
            if (takeStacksIntoAccount)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (!slots[i].Any()) { return false; }
                    var item = slots[i].FirstOrDefault();
                    if (slots[i].ItemCount < item.Prefab.MaxStackSize) { return false; }
                }
            }
            else
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (!slots[i].Any()) { return false; }
                }
            }

            return true;
        }

        protected bool TrySwapping(int index, Item item, Character user, bool createNetworkEvent, bool swapWholeStack)
        {
            if (item?.ParentInventory == null || !slots[index].Any()) { return false; }
            if (slots[index].Items.Any(it => !it.IsInteractable(user))) { return false; }
            if (!AllowSwappingContainedItems) { return false; }

            //swap to InvSlotType.Any if possible
            Inventory otherInventory = item.ParentInventory;
            bool otherIsEquipped = false;
            int otherIndex = -1;
            for (int i = 0; i < otherInventory.slots.Length; i++)
            {
                if (!otherInventory.slots[i].Contains(item)) { continue; }
                if (otherInventory is CharacterInventory characterInventory)
                {
                    if (characterInventory.SlotTypes[i] == InvSlotType.Any)
                    {
                        otherIndex = i;
                        break;
                    }
                    else
                    {
                        otherIsEquipped = true;
                    }
                }
            }
            if (otherIndex == -1)
            {
                otherIndex = otherInventory.FindIndex(item);
                if (otherIndex == -1)
                {
                    DebugConsole.ThrowError("Something went wrong when trying to swap items between inventory slots: couldn't find the source item from it's inventory.\n" + Environment.StackTrace.CleanupStackTrace());
                    return false;
                }
            }

            List<Item> existingItems = new List<Item>();
            if (swapWholeStack)
            {
                existingItems.AddRange(slots[index].Items);
                for (int j = 0; j < capacity; j++)
                {
                    if (existingItems.Any(existingItem => slots[j].Contains(existingItem))) { slots[j].RemoveAllItems(); }
                }
            }
            else
            {
                existingItems.Add(slots[index].FirstOrDefault());
                for (int j = 0; j < capacity; j++)
                {
                    if (existingItems.Any(existingItem => slots[j].Contains(existingItem))) { slots[j].RemoveItem(existingItems.First()); }
                }
            }

            List<Item> stackedItems = new List<Item>();
            if (swapWholeStack)
            {
                for (int j = 0; j < otherInventory.capacity; j++)
                {
                    if (otherInventory.slots[j].Contains(item)) 
                    {
                        stackedItems.AddRange(otherInventory.slots[j].Items);
                        otherInventory.slots[j].RemoveAllItems(); 
                    }
                }
            }
            else
            {
                stackedItems.Add(item);
                otherInventory.slots[otherIndex].RemoveItem(item);
            }


            bool swapSuccessful = false;
            if (otherIsEquipped)
            {
                swapSuccessful =
                    stackedItems.Distinct().All(stackedItem => TryPutItem(stackedItem, index, false, false, user, createNetworkEvent))
                    &&
                    (existingItems.All(existingItem => otherInventory.TryPutItem(existingItem, otherIndex, false, false, user, createNetworkEvent)) ||
                    existingItems.Count == 1 && otherInventory.TryPutItem(existingItems.First(), user, CharacterInventory.anySlot, createNetworkEvent));
            }
            else
            {
                swapSuccessful =
                    (existingItems.All(existingItem => otherInventory.TryPutItem(existingItem, otherIndex, false, false, user, createNetworkEvent)) ||
                    existingItems.Count == 1 && otherInventory.TryPutItem(existingItems.First(), user, CharacterInventory.anySlot, createNetworkEvent))
                    &&
                    stackedItems.Distinct().All(stackedItem => TryPutItem(stackedItem, index, false, false, user, createNetworkEvent));

                if (!swapSuccessful && existingItems.Count == 1 && existingItems[0].AllowDroppingOnSwapWith(item))
                {
                    if (!(existingItems[0].Container?.ParentInventory is CharacterInventory characterInv) ||
                        !characterInv.TryPutItem(existingItems[0], user, new List<InvSlotType>() { InvSlotType.Any }))
                    {
                        existingItems[0].Drop(user, createNetworkEvent);
                    }
                    swapSuccessful = stackedItems.Distinct().Any(stackedItem => TryPutItem(stackedItem, index, false, false, user, createNetworkEvent));
#if CLIENT
                    if (swapSuccessful)
                    {
                        SoundPlayer.PlayUISound(GUISoundType.DropItem);
                        if (otherInventory.visualSlots != null && otherIndex > -1)
                        {
                            otherInventory.visualSlots[otherIndex].ShowBorderHighlight(Color.Transparent, 0.1f, 0.1f);
                        }
                    }
#endif
                }
            }

            //if the item in the slot can be moved to the slot of the moved item
            if (swapSuccessful)
            {
                System.Diagnostics.Debug.Assert(slots[index].Contains(item), "Something when wrong when swapping items, item is not present in the inventory.");
                System.Diagnostics.Debug.Assert(!existingItems.Any(it => !it.Prefab.AllowDroppingOnSwap && !otherInventory.Contains(it)), "Something when wrong when swapping items, item is not present in the other inventory.");
#if CLIENT
                if (visualSlots != null)
                {
                    for (int j = 0; j < capacity; j++)
                    {
                        if (slots[j].Contains(item)) { visualSlots[j].ShowBorderHighlight(GUI.Style.Green, 0.1f, 0.9f); }                       
                    }
                    for (int j = 0; j < otherInventory.capacity; j++)
                    {
                        if (otherInventory.slots[j].Contains(existingItems.FirstOrDefault())) { otherInventory.visualSlots[j].ShowBorderHighlight(GUI.Style.Green, 0.1f, 0.9f); }                          
                    }
                }
#endif
                return true;
            }
            else //swapping the items failed -> move them back to where they were
            {
                if (swapWholeStack)
                {
                    foreach (Item stackedItem in stackedItems)
                    {
                        for (int j = 0; j < capacity; j++)
                        {
                            if (slots[j].Contains(stackedItem)) { slots[j].RemoveItem(stackedItem); };
                        }
                    }
                    foreach (Item existingItem in existingItems)
                    {
                        for (int j = 0; j < otherInventory.capacity; j++)
                        {
                            if (otherInventory.slots[j].Contains(existingItem)) { otherInventory.slots[j].RemoveItem(existingItem); }
                        }
                    }
                }
                else
                {
                    for (int j = 0; j < capacity; j++)
                    {
                        if (slots[j].Contains(item)) { slots[j].RemoveAllItems(); };
                    }
                    for (int j = 0; j < otherInventory.capacity; j++)
                    {
                        if (otherInventory.slots[j].Contains(existingItems.FirstOrDefault())) { otherInventory.slots[j].RemoveAllItems(); }
                    }
                }

                if (otherIsEquipped)
                {
                    existingItems.ForEach(existingItem => TryPutItem(existingItem, index, false, false, user, createNetworkEvent));
                    stackedItems.ForEach(stackedItem => otherInventory.TryPutItem(stackedItem, otherIndex, false, false, user, createNetworkEvent));
                }
                else
                {
                    stackedItems.ForEach(stackedItem => otherInventory.TryPutItem(stackedItem, otherIndex, false, false, user, createNetworkEvent));
                    existingItems.ForEach(existingItem => TryPutItem(existingItem, index, false, false, user, createNetworkEvent));
                }

#if CLIENT                
                if (visualSlots != null)
                {
                    for (int j = 0; j < capacity; j++)
                    {
                        if (slots[j].Contains(existingItems.FirstOrDefault()))
                        {
                            visualSlots[j].ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.9f);
                        }
                    }
                }
#endif
                return false;
            }
        }

        public virtual void CreateNetworkEvent()
        {
            if (GameMain.NetworkMember != null)
            {
                if (GameMain.NetworkMember.IsClient) { syncItemsDelay = 1.0f; }
                GameMain.NetworkMember.CreateEntityEvent(Owner as INetSerializable, new object[] { NetEntityEvent.Type.InventoryState });
            }
        }

        public Item FindItem(Func<Item, bool> predicate, bool recursive)
        {
            Item match = AllItems.FirstOrDefault(i => predicate(i));
            if (match == null && recursive)
            {
                foreach (var item in AllItems)
                {
                    if (item == null) { continue; }
                    if (item.OwnInventory != null)
                    {
                        match = item.OwnInventory.FindItem(predicate, recursive: true);
                        if (match != null)
                        {
                            return match;
                        }
                    }
                }
            }
            return match;
        }

        public List<Item> FindAllItems(Func<Item, bool> predicate = null, bool recursive = false, List<Item> list = null)
        {
            list ??= new List<Item>();
            foreach (var item in AllItems)
            {
                if (predicate == null || predicate(item))
                {
                    list.Add(item);
                }
                if (recursive)
                {
                    if (item.OwnInventory != null)
                    {
                        item.OwnInventory.FindAllItems(predicate, recursive: true, list);
                    }
                }
            }
            return list;
        }

        public Item FindItemByTag(string tag, bool recursive = false)
        {
            if (tag == null) { return null; }
            return FindItem(i => i.HasTag(tag), recursive);
        }

        public Item FindItemByIdentifier(string identifier, bool recursive = false)
        {
            if (identifier == null) { return null; }
            return FindItem(i => i.Prefab.Identifier == identifier, recursive);
        }

        public virtual void RemoveItem(Item item)
        {
            if (item == null) { return; }

            //go through the inventory and remove the item from all slots
            for (int n = 0; n < capacity; n++)
            {
                if (!slots[n].Contains(item)) { continue; }

                slots[n].RemoveItem(item);
                item.ParentInventory = null;
#if CLIENT
                if (visualSlots != null)
                {
                    visualSlots[n].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                    if (selectedSlot?.Inventory == this) { selectedSlot.ForceTooltipRefresh = true; }
                }
#endif
            }
        }

        /// <summary>
        /// Forces an item to a specific slot. Doesn't remove the item from existing slots/inventories or do any other sanity checks, use with caution! 
        /// </summary>
        public void ForceToSlot(Item item, int index)
        {
            slots[index].Add(item);
            item.ParentInventory = this;
            bool equipped = (this as CharacterInventory)?.Owner is Character character && character.HasEquippedItem(item);
            if (item.body != null && !equipped)
            {
                item.body.Enabled = false;
                item.body.BodyType = FarseerPhysics.BodyType.Dynamic;
            }
        }

        /// <summary>
        /// Removes an item from a specific slot. Doesn't do any sanity checks, use with caution! 
        /// </summary>
        public void ForceRemoveFromSlot(Item item, int index)
        {
            slots[index].RemoveItem(item);
        }


        public void SharedWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write((byte)capacity);
            for (int i = 0; i < capacity; i++)
            {
                msg.WriteRangedInteger(slots[i].ItemCount, 0, MaxStackSize);
                foreach (Item item in slots[i].Items)
                {
                    msg.Write((ushort)(item == null ? 0 : item.ID));
                }
            }
        }

        /// <summary>
        /// Deletes all items inside the inventory (and also recursively all items inside the items)
        /// </summary>
        public void DeleteAllItems()
        {
            for (int i = 0; i < capacity; i++)
            {
                if (!slots[i].Any()) { continue; }
                foreach (Item item in slots[i].Items)
                {
                    foreach (ItemContainer itemContainer in item.GetComponents<ItemContainer>())
                    {
                        itemContainer.Inventory.DeleteAllItems();
                    }
                }
                slots[i].Items.ForEachMod(it => it.Remove());
                slots[i].RemoveAllItems();
            }
        }
    }
}
