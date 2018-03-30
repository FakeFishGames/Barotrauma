using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    [Flags]
    public enum InvSlotType
    {
        None = 0, Any = 1, RightHand = 2, LeftHand = 4, Head = 8, InnerClothes = 16, OuterClothes = 32, Headset = 64, Card = 128, Pack = 256
    };

    partial class CharacterInventory : Inventory
    {
        private Character character;

        public static InvSlotType[] SlotTypes = new InvSlotType[] {
            InvSlotType.InnerClothes, InvSlotType.OuterClothes, InvSlotType.RightHand,
            InvSlotType.LeftHand, InvSlotType.Head, InvSlotType.Headset, InvSlotType.Card,
            InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any,
            InvSlotType.Pack };

        protected bool[] IsEquipped;

        public CharacterInventory(int capacity, Character character)
            : base(character, capacity)
        {
            this.character = character;
            IsEquipped = new bool[capacity];

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        private bool UseItemOnSelf(int slotIndex)
        {
            if (Items[slotIndex] == null) return false;

#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(Items[slotIndex], new object[] { NetEntityEvent.Type.ApplyStatusEffect });
                return true;
            }
#endif

            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(Items[slotIndex], new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, character.ID });
            }

            Items[slotIndex].ApplyStatusEffects(ActionType.OnUse, 1.0f, character);
            foreach (ItemComponent ic in Items[slotIndex].components)
            {
                if (ic.DeleteOnUse)
                {
                    Entity.Spawner.AddToRemoveQueue(Items[slotIndex]);
                }
            }
            
            return true;
        }
        
        public int FindLimbSlot(InvSlotType limbSlot)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (SlotTypes[i] == limbSlot) return i;
            }
            return -1;
        }

        public bool IsInLimbSlot(Item item, InvSlotType limbSlot)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i] == item && SlotTypes[i] == limbSlot) return true;
            }
            return false;
        }

        public override bool CanBePut(Item item, int i)
        {
            return base.CanBePut(item, i) && item.AllowedSlots.Contains(SlotTypes[i]);
        } 

        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public override bool TryPutItem(Item item, Character user, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            if (allowedSlots == null || !allowedSlots.Any()) return false;

            bool inSuitableSlot = false;
            bool inWrongSlot = false;
            int currentSlot = -1;
            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] == item)
                {
                    currentSlot = i;
                    if (allowedSlots.Any(a => a.HasFlag(SlotTypes[i])))
                        inSuitableSlot = true;
                    else if (!allowedSlots.Any(a => a.HasFlag(SlotTypes[i])))
                        inWrongSlot = true;
                }
            }
            //all good
            if (inSuitableSlot && !inWrongSlot) return true;

            //try to place the item in a LimbSlot.Any slot if that's allowed
            if (allowedSlots.Contains(InvSlotType.Any))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (SlotTypes[i] != InvSlotType.Any) continue;
                    if (Items[i] == item)
                    {
                        PutItem(item, i, user, true, createNetworkEvent);
                        item.Unequip(character);
                        return true;
                    }
                }
                for (int i = 0; i < capacity; i++)
                {
                    if (SlotTypes[i] != InvSlotType.Any) continue;
                    if (inWrongSlot)
                    {
                        if (Items[i] != item && Items[i] != null) continue;
                    }
                    else
                    {
                        if (Items[i] != null) continue;
                    }

                    PutItem(item, i, user, true, createNetworkEvent);
                    item.Unequip(character);
                    return true;
                }
            }

            int placedInSlot = -1;
            foreach (InvSlotType allowedSlot in allowedSlots)
            {
                //check if all the required slots are free
                bool free = true;
                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(SlotTypes[i]) && Items[i] != null && Items[i] != item)
                    {
                        free = false;
#if CLIENT
                        if (slots != null) slots[i].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
#endif
                    }
                }

                if (!free) continue;

                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(SlotTypes[i]) && Items[i] == null)
                    {
                        bool removeFromOtherSlots = item.ParentInventory != this;
                        if (placedInSlot == -1 && inWrongSlot)
                        {
                            if (!hideEmptySlot[i] || SlotTypes[currentSlot] != InvSlotType.Any) removeFromOtherSlots = true;
                        }

                        PutItem(item, i, user, removeFromOtherSlots, createNetworkEvent);
                        item.Equip(character);
                        placedInSlot = i;
                    }
                }

                if (placedInSlot > -1)
                {
                    if (item.AllowedSlots.Contains(InvSlotType.Any) && hideEmptySlot[placedInSlot])
                    {
                        bool isInAnySlot = false;
                        for (int i = 0; i < capacity; i++)
                        {
                            if (SlotTypes[i] == InvSlotType.Any && Items[i]==item)
                            {
                                isInAnySlot = true;
                                break;
                            }
                        }
                        if (!isInAnySlot)
                        {
                            for (int i = 0; i < capacity; i++)
                            {
                                if (SlotTypes[i] == InvSlotType.Any && Items[i] == null)
                                {
                                    Items[i] = item;
                                    break;
                                }
                            }
                        }
                    }
                    return true;
                }
            }


            return placedInSlot > -1;
        }

        public override bool TryPutItem(Item item, int index, bool allowSwapping, bool allowCombine, Character user, bool createNetworkEvent = true)
        {
            //there's already an item in the slot
            if (Items[index] != null)
            {
                if (Items[index] == item) return false;

                bool combined = false;
                if (allowCombine && Items[index].Combine(item))
                {
                    System.Diagnostics.Debug.Assert(Items[index] != null);
                 
                    Inventory otherInventory = Items[index].ParentInventory;
                    if (otherInventory != null && otherInventory.Owner!=null)
                    {
                        
                    }

                    combined = true;
                }
                //if moving the item between slots in the same inventory
                else if (item.ParentInventory == this && allowSwapping)
                {
                    int currentIndex = Array.IndexOf(Items, item);                    
                    Item existingItem = Items[index];

                    if (character.HasEquippedItem(existingItem) && existingItem.AllowedSlots.Contains(InvSlotType.Any))
                    {
                        for (int i = 0; i < capacity; i++)
                        {
                            if (Items[i] == existingItem && SlotTypes[i] != InvSlotType.Any)
                            {
                                Items[i] = null;
                            }
                        }
                    }

                    for (int i = 0; i < capacity; i++)
                    {
                        if (Items[i] == item || Items[i] == existingItem)
                        {
                            Items[i] = null;
                        }
                    }
                    
                    //if the item in the slot can be moved to the slot of the moved item
                    if (TryPutItem(existingItem, currentIndex, false, false, user, createNetworkEvent) &&
                        TryPutItem(item, index, false, false, user, createNetworkEvent))
                    {
#if CLIENT
                        for (int i = 0; i < capacity; i++)
                        {
                            if (Items[i] == item || Items[i] == existingItem)
                            {
                                slots[i].ShowBorderHighlight(Color.Green, 0.1f, 0.9f);
                            }
                        }
#endif
                    }
                    else
                    {
                        for (int i = 0; i < capacity; i++)
                        {
                            if (Items[i] == item || Items[i] == existingItem) Items[i] = null;
                        }

                        //swapping the items failed -> move them back to where they were
                        TryPutItem(item, currentIndex, false, false, user, createNetworkEvent);
                        TryPutItem(existingItem, index, false, false, user, createNetworkEvent);
#if CLIENT
                        for (int i = 0; i < capacity; i++)
                        {
                            if (Items[i] == existingItem)
                            {
                                slots[i].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                            }
                        }
#endif

                    }
                }

                return combined;
            }

            if (SlotTypes[index] == InvSlotType.Any)
            {
                if (!item.AllowedSlots.Contains(InvSlotType.Any)) return false;
                if (Items[index] != null) return Items[index] == item;

                PutItem(item, index, user, true, createNetworkEvent);
                return true;
            }

            InvSlotType placeToSlots = InvSlotType.None;

            bool slotsFree = true;
            List<InvSlotType> allowedSlots = item.AllowedSlots;
            foreach (InvSlotType allowedSlot in allowedSlots)
            {
                if (!allowedSlot.HasFlag(SlotTypes[index])) continue;

                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(SlotTypes[i]) && Items[i] != null && Items[i] != item)
                    {
                        slotsFree = false;
                        break;
                    }

                    placeToSlots = allowedSlot;
                }
            }

            if (!slotsFree) return false;

            return TryPutItem(item, user, new List<InvSlotType>() { placeToSlots }, createNetworkEvent);
        }
    }
}
