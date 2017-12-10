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
        None = 0, Any = 1, RightHand = 2, LeftHand = 4, Head = 8, Torso = 16, Legs = 32, Face=64
    };

    partial class CharacterInventory : Inventory
    {
        private Character character;

        public static InvSlotType[] limbSlots = new InvSlotType[] { 
            InvSlotType.Head, InvSlotType.Torso, InvSlotType.Legs, InvSlotType.LeftHand, InvSlotType.RightHand, InvSlotType.Face,
            InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any,
            InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any};
        
        public CharacterInventory(int capacity, Character character)
            : base(character, capacity)
        {
            this.character = character;

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        private bool UseItemOnSelf(int slotIndex)
        {
            if (Items[slotIndex] == null) return false;

//Isn't this useless? Client isn't handling status effects. Plus, this doesn't send the ActionType so it can be wrong even.
#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(Items[slotIndex], new object[] { NetEntityEvent.Type.ApplyStatusEffect });
                return true;
            }
#endif

            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(Items[slotIndex], new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnHudUse, character.ID });
            }

            Items[slotIndex].ApplyStatusEffects(ActionType.OnHudUse, 1.0f, character);

            return true;
        }
        
        public int FindLimbSlot(InvSlotType limbSlot)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (limbSlots[i] == limbSlot) return i;
            }
            return -1;
        }

        public bool IsInLimbSlot(Item item, InvSlotType limbSlot)
        {
            for (int i = 0; i<Items.Length; i++)
            {
                if (Items[i] == item && limbSlots[i] == limbSlot) return true;
            }
            return false;
        }

        public override bool CanBePut(Item item, int i)
        {
            return base.CanBePut(item, i) && item.AllowedSlots.Contains(limbSlots[i]);
        } 

        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public override bool TryPutItem(Item item, Character user, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
        {
            if (allowedSlots == null || !allowedSlots.Any()) return false;

            for (int i = 0; i < capacity; i++)
            {
                //already in the inventory and in a suitable slot
                if (Items[i] == item && allowedSlots.Any(a => a.HasFlag(limbSlots[i])))
                {
                    return true;
                }
            }

            //try to place the item in LimBlot.Any slot if that's allowed
            if (allowedSlots.Contains(InvSlotType.Any))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (Items[i] != null || limbSlots[i] != InvSlotType.Any) continue;

                    PutItem(item, i, user, true, createNetworkEvent);
                    item.Unequip(character);
                    return true;
                }
            }

            bool placed = false;
            foreach (InvSlotType allowedSlot in allowedSlots)
            {
                //check if all the required slots are free
                bool free = true;
                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(limbSlots[i]) && Items[i]!=null && Items[i]!=item)
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
                    if (allowedSlot.HasFlag(limbSlots[i]) && Items[i] == null)
                    {
                        PutItem(item, i, user, !placed, createNetworkEvent);
                        item.Equip(character);
                        placed = true;
                    }
                }

                if (placed)
                {
                    return true;
                }
            }


            return placed;
        }

        public override bool TryPutItem(Item item, int index, bool allowSwapping, Character user, bool createNetworkEvent = true)
        {
            //there's already an item in the slot
            if (Items[index] != null)
            {
                if (Items[index] == item) return false;

                bool combined = false;
                if (Items[index].Combine(item))
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

                    Items[currentIndex] = null;
                    Items[index] = null;
                    //if the item in the slot can be moved to the slot of the moved item
                    if (TryPutItem(existingItem, currentIndex, false, user, createNetworkEvent) &&
                        TryPutItem(item, index, false, user, createNetworkEvent))
                    {

                    }
                    else
                    {
                        Items[currentIndex] = null;
                        Items[index] = null;

                        //swapping the items failed -> move them back to where they were
                        TryPutItem(item, currentIndex, false, user, createNetworkEvent);
                        TryPutItem(existingItem, index, false, user, createNetworkEvent);
                    }
                }

                return combined;
            }

            if (limbSlots[index] == InvSlotType.Any)
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
                if (!allowedSlot.HasFlag(limbSlots[index])) continue;

                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(limbSlots[i]) && Items[i] != null && Items[i] != item)
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
