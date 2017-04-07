using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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

    class CharacterInventory : Inventory
    {
        private static Texture2D icons;

        private Character character;

        public static InvSlotType[] limbSlots = new InvSlotType[] { 
            InvSlotType.Head, InvSlotType.Torso, InvSlotType.Legs, InvSlotType.LeftHand, InvSlotType.RightHand, InvSlotType.Face,
            InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any,
            InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any, InvSlotType.Any};

        public Vector2[] SlotPositions;

        private GUIButton[] useOnSelfButton;

        public CharacterInventory(int capacity, Character character)
            : base(character, capacity)
        {
            this.character = character;

            useOnSelfButton = new GUIButton[2];

            if (icons == null) icons = TextureLoader.FromFile("Content/UI/inventoryIcons.png");

            SlotPositions = new Vector2[limbSlots.Length];
            
            int rectWidth = 40, rectHeight = 40;
            int spacing = 10;
            for (int i = 0; i < SlotPositions.Length; i++)
            {
                switch (i)
                {
                    //head, torso, legs
                    case 0:
                    case 1:
                    case 2:
                        SlotPositions[i] = new Vector2(
                            spacing, 
                            GameMain.GraphicsHeight - (spacing + rectHeight) * (3 - i));
                        break;
                    //lefthand, righthand
                    case 3:
                    case 4:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 2),
                            GameMain.GraphicsHeight - (spacing + rectHeight)*3);

                        useOnSelfButton[i - 3] = new GUIButton(
                            new Rectangle((int) SlotPositions[i].X, (int) (SlotPositions[i].Y - spacing - rectHeight),
                                rectWidth, rectHeight), "Use", "")
                        {
                            UserData = i,
                            OnClicked = UseItemOnSelf
                        };


                        break;
                    case 5:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * 3);

                        break;
                    default:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * ((i - 6)%5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * ((i>10) ? 2 : 1));
                        break;
                }
            }
        }

        private bool UseItemOnSelf(GUIButton button, object obj)
        {
            if (!(obj is int)) return false;

            int slotIndex = (int)obj;

            if (Items[slotIndex] == null) return false;

            //save the ID in a variable in case the statuseffect causes the item to be dropped/destroyed
            ushort itemID = Items[slotIndex].ID;
            
            Items[slotIndex].ApplyStatusEffects(ActionType.OnUse, 1.0f, character);
            new NetworkEvent(NetworkEventType.ApplyStatusEffect, character.ID, true, itemID);

            return true;
        }

        protected override void DropItem(Item item)
        {
            bool enabled = item.body!=null && item.body.Enabled;
            item.Drop(character);

            if (!enabled)
            {
                item.SetTransform(character.SimPosition, 0.0f);
            }
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
            if (allowedSlots == null || ! allowedSlots.Any()) return false;

            //try to place the item in LimBlot.Any slot if that's allowed
            if (allowedSlots.Contains(InvSlotType.Any))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (Items[i] != null || limbSlots[i] != InvSlotType.Any) continue;

                    PutItem(item, i, createNetworkEvent);
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
                        if (slots != null) slots[i].ShowBorderHighlight(Color.Red, 0.1f, 0.9f);
                    }
                }

                if (!free) continue;

                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(limbSlots[i]) && Items[i] == null)
                    {
                        PutItem(item, i, createNetworkEvent, !placed);
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

        public override bool TryPutItem(Item item, int index, bool allowSwapping, bool createNetworkEvent)
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
                    if (otherInventory != null && otherInventory.Owner!=null && createNetworkEvent)
                    {
                        new Networking.NetworkEvent(Networking.NetworkEventType.InventoryUpdate, otherInventory.Owner.ID, true, true);
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
                    if (TryPutItem(existingItem, currentIndex, false, false) &&
                        TryPutItem(item, index, false, false))
                    {
                        new Networking.NetworkEvent(Networking.NetworkEventType.InventoryUpdate, Owner.ID, true, true);
                    }
                    else
                    {
                        Items[currentIndex] = null;
                        Items[index] = null;

                        //swapping the items failed -> move them back to where they were
                        TryPutItem(item, currentIndex, false, false);
                        TryPutItem(existingItem, index, false, false);
                    }                    
                }
                
                return combined;
            }

            if (limbSlots[index] == InvSlotType.Any)
            {
                if (!item.AllowedSlots.Contains(InvSlotType.Any)) return false;
                if (Items[index] != null) return Items[index] == item;

                PutItem(item, index, createNetworkEvent, true);
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

        protected override void PutItem(Item item, int i, bool createNetworkEvent, bool removeItem = true)
        {
            base.PutItem(item, i, createNetworkEvent, removeItem);
            CreateSlots();
        }

        public override void RemoveItem(Item item)
        {
            base.RemoveItem(item);
            CreateSlots();
        }

        protected override void CreateSlots()
        {
            if (slots == null) slots = new InventorySlot[capacity];

            int rectWidth = 40, rectHeight = 40;
            
            Rectangle slotRect = new Rectangle(0, 0, rectWidth, rectHeight);
            for (int i = 0; i < capacity; i++)
            {
                if (slots[i] == null) slots[i] = new InventorySlot(slotRect);

                slots[i].Disabled = false;

                slotRect.X = (int)(SlotPositions[i].X + DrawOffset.X);
                slotRect.Y = (int)(SlotPositions[i].Y + DrawOffset.Y);

                slots[i].Rect = slotRect;

                slots[i].Color = limbSlots[i] == InvSlotType.Any ? Color.White * 0.2f : Color.White * 0.4f;
            }

            MergeSlots();
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
                 
        public void DrawOwn(SpriteBatch spriteBatch)
        {
            if (slots == null) CreateSlots();

            Rectangle slotRect = new Rectangle(0, 0, 40, 40);

            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(SlotPositions[i].X + DrawOffset.X);
                slotRect.Y = (int)(SlotPositions[i].Y + DrawOffset.Y);

                if (i==1) //head
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y), 
                        new Rectangle(0,0,56,128), Color.White*0.7f, 0.0f, 
                        new Vector2(28.0f, 64.0f), Vector2.One, 
                        SpriteEffects.None, 0.1f);
                } 
                else if (i==3 || i==4)
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y),
                        new Rectangle(92, 41*(4-i), 36, 40), Color.White * 0.7f, 0.0f,
                        new Vector2(18.0f, 20.0f), Vector2.One,
                        SpriteEffects.None, 0.1f);
                }
                else if (i==5)
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y),
                        new Rectangle(57,0,31,32), Color.White * 0.7f, 0.0f,
                        new Vector2(15.0f, 16.0f), Vector2.One,
                        SpriteEffects.None, 0.1f);
                }
            }

            base.Draw(spriteBatch);
            
            if (character == Character.Controlled)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (selectedSlot != i &&
                        Items[i] != null && Items[i].CanUseOnSelf && character.HasSelectedItem(Items[i]))
                    {
                        useOnSelfButton[i - 3].Draw(spriteBatch);
                    }
                }
            }

            if (selectedSlot > -1)
            {
                DrawSubInventory(spriteBatch, selectedSlot);

                if (selectedSlot > -1 && 
                    !slots[selectedSlot].IsHighlighted &&
                    (draggingItem == null || draggingItem.Container != Items[selectedSlot]))
                {
                    selectedSlot = -1;
                }
            }
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            for (int i = 0; i < capacity; i++)
            {
                message.Write(Items[i]==null ? (ushort)0 : (ushort)Items[i].ID);
            }
            
            return true;
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime)
        {
            if (sendingTime < lastUpdate) return;

            character.ClearInput(InputType.Use);

            List<Item> droppedItems = new List<Item>();
            List<Item> prevItems = new List<Item>(Items);

            for (int i = 0; i<capacity; i++)
            {
                ushort itemId = message.ReadUInt16();
                if (itemId == 0)
                {
                    if (Items[i] != null)
                    {
                        droppedItems.Add(Items[i]);
                        Items[i].Drop(character, false);                        
                    }
                }
                else
                {
                    Item item = Entity.FindEntityByID(itemId) as Item;
                    if (item == null) continue;

                    //item already in the right slot, no need to do anything
                    if (Items[i] == item) continue;

                    //if the item is in the inventory of some other character and said character isn't dead/unconscious,
                    //don't let this character pick it up
                    if (GameMain.Server != null)
                    {
                        var owner = item.ParentInventory == null ? null : item.ParentInventory.Owner as Character;
                        if (owner != null && owner != character)
                        {
                            if (!character.CanAccessItem(item)) return;
                        }
                    }

                    //some other item already in the slot -> drop it
                    if (Items[i] != null) Items[i].Drop(character, false);

                    if (TryPutItem(item, i, false, false))
                    {
                        if (droppedItems.Contains(item)) droppedItems.Remove(item);
                    }
                }
            }

            lastUpdate = sendingTime;

            if (GameMain.Server == null) return;

            var sender = GameMain.Server.ConnectedClients.Find(c => c.Connection == message.SenderConnection);
            if (sender != null && sender.Character != null)
            {
                foreach (Item item in droppedItems)
                {
                    GameServer.Log(sender.Character == character ?
                        character.Name + " dropped " + item.Name :
                        sender.Character + " removed " + item.Name + " from " + character + "'s inventory", Color.Orange);
                }

                foreach (Item item in Items)
                {
                    if (item == null || prevItems.Contains(item)) continue;
                    GameServer.Log(sender.Character == character ?
                        character.Name + " picked up " + item.Name :
                        sender.Character + " placed " + item.Name + " in " + character + "'s inventory", Color.Orange);
                }
            }
        }

    }
}
