using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;
using Barotrauma.Items.Components;

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
        public override bool TryPutItem(Item item, List<InvSlotType> allowedSlots = null, bool createNetworkEvent = true)
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

                    PutItem(item, i, true, createNetworkEvent);
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
                        PutItem(item, i, !placed, createNetworkEvent);
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

        public override bool TryPutItem(Item item, int index, bool allowSwapping, bool createNetworkEvent = true)
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
                    if (TryPutItem(existingItem, currentIndex, false, createNetworkEvent) &&
                        TryPutItem(item, index, false, createNetworkEvent))
                    {
                        
                    }
                    else
                    {
                        Items[currentIndex] = null;
                        Items[index] = null;

                        //swapping the items failed -> move them back to where they were
                        TryPutItem(item, currentIndex, false, createNetworkEvent);
                        TryPutItem(existingItem, index, false, createNetworkEvent);
                    }
                }
                
                return combined;
            }

            if (limbSlots[index] == InvSlotType.Any)
            {
                if (!item.AllowedSlots.Contains(InvSlotType.Any)) return false;
                if (Items[index] != null) return Items[index] == item;

                PutItem(item, index, true, createNetworkEvent);
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
            
            return TryPutItem(item, new List<InvSlotType>() {placeToSlots}, createNetworkEvent);
        }

        protected override void PutItem(Item item, int i, bool removeItem = true, bool createNetworkEvent = true)
        {
            base.PutItem(item, i, removeItem, createNetworkEvent);
            CreateSlots();
        }

        public override void RemoveItem(Item item)
        {
            base.RemoveItem(item);
            CreateSlots();
        }

        public override void Update(float deltaTime, bool subInventory = false)
        {
            base.Update(deltaTime);

            if (doubleClickedItem != null)
            {
                if (doubleClickedItem.ParentInventory != this)
                {
                    TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots, true);
                }
                else
                {
                    if (character.SelectedConstruction != null)
                    {
                        var selectedContainer = character.SelectedConstruction.GetComponent<ItemContainer>();
                        if (selectedContainer != null && selectedContainer.Inventory != null)
                        {
                            selectedContainer.Inventory.TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots, true);
                        }
                    }
                    else if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                    {
                        character.SelectedCharacter.Inventory.TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots, true);
                    }
                    else //doubleclicked and no other inventory is selected
                    {
                        //not equipped -> attempt to equip
                        if (IsInLimbSlot(doubleClickedItem, InvSlotType.Any))
                        {
                            TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots.FindAll(i => i != InvSlotType.Any), true);
                        }
                        //equipped -> attempt to unequip
                        else if (doubleClickedItem.AllowedSlots.Contains(InvSlotType.Any))
                        {
                            TryPutItem(doubleClickedItem, new List<InvSlotType>() { InvSlotType.Any }, true);
                        }
                    }
                }
            }

            if (selectedSlot > -1)
            {
                UpdateSubInventory(deltaTime, selectedSlot);
            }

#if CLIENT
            if (character == Character.Controlled)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (selectedSlot != i &&
                        Items[i] != null && Items[i].CanUseOnSelf && character.HasSelectedItem(Items[i]))
                    {
                        //-3 because selected items are in slots 3 and 4 (hands)
                        useOnSelfButton[i - 3].Update(deltaTime);
                    }
                }
            }
#endif

            //cancel dragging if too far away from the container of the dragged item
            if (draggingItem != null)
            {
                var rootContainer = draggingItem.GetRootContainer();
                var rootInventory = draggingItem.ParentInventory;

                if (rootContainer != null)
                {
                    rootInventory = rootContainer.ParentInventory != null ?
                        rootContainer.ParentInventory : rootContainer.GetComponent<Items.Components.ItemContainer>().Inventory;
                }

                if (rootInventory != null &&
                    rootInventory.Owner != Character.Controlled &&
                    rootInventory.Owner != Character.Controlled.SelectedConstruction &&
                    rootInventory.Owner != Character.Controlled.SelectedCharacter)
                {
                    draggingItem = null;
                }
            }


            doubleClickedItem = null;
        }

        private void MergeSlots()
        {
            for (int i = 0; i < capacity-1; i++)
            {
                if (slots[i].Disabled || Items[i] == null) continue;
                
                for (int n = i+1; n < capacity; n++)
                {
                    if (Items[n] == Items[i])
                    {
                        slots[i].Rect = Rectangle.Union(slots[i].Rect, slots[n].Rect);
                        slots[n].Disabled = true;
                    }
                }               
            }

            selectedSlot = -1;
        }
    }
}
