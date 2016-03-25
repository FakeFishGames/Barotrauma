using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using Lidgren.Network;
using System.Collections.Generic;

namespace Barotrauma
{
    [Flags]
    public enum LimbSlot
    {
        None = 0, Any = 1, RightHand = 2, LeftHand = 4, Head = 8, Torso = 16, Legs = 32
    };

    class CharacterInventory : Inventory
    {
        private static Texture2D icons;

        private Character character;

        public static LimbSlot[] limbSlots = new LimbSlot[] { 
            LimbSlot.Head, LimbSlot.Torso, LimbSlot.Legs, LimbSlot.LeftHand, LimbSlot.RightHand,
            LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any,
            LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any};

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
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 3),
                            GameMain.GraphicsHeight - (spacing + rectHeight)*3);

                        useOnSelfButton[i - 3] = new GUIButton(
                            new Rectangle((int) SlotPositions[i].X, (int) (SlotPositions[i].Y - spacing - rectHeight),
                                rectWidth, rectHeight), "Use", GUI.Style)
                        {
                            UserData = i,
                            OnClicked = UseItemOnSelf
                        };


                        break;
                    default:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * ((i - 3)%5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * ((i>9) ? 2 : 1));
                        break;
                }
            }
        }

        private bool UseItemOnSelf(GUIButton button, object obj)
        {
            if (!(obj is int)) return false;

            int slotIndex = (int)obj;

            if (Items[slotIndex] == null) return false;

            Items[slotIndex].ApplyStatusEffects(ActionType.OnUse, 0.016f, character);

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

        public int FindLimbSlot(LimbSlot limbSlot)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (limbSlots[i] == limbSlot) return i;
            }
            return -1;
        }

        public bool IsInLimbSlot(Item item, LimbSlot limbSlot)
        {
            for (int i = 0; i<Items.Length; i++)
            {
                if (Items[i] == item && limbSlots[i] == limbSlot) return true;
            }
            return false;
        }

        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public override bool TryPutItem(Item item, List<LimbSlot> allowedSlots = null, bool createNetworkEvent = true)
        {
            if (allowedSlots == null) return false;

            //try to place the item in LimBlot.Any slot if that's allowed
            if (allowedSlots.Contains(LimbSlot.Any))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (Items[i] != null || limbSlots[i] != LimbSlot.Any) continue;

                    PutItem(item, i, createNetworkEvent);
                    item.Unequip(character);
                    return true;
                }
            }

            bool placed = false;
            foreach (LimbSlot allowedSlot in allowedSlots)
            {
                //check if all the required slots are free
                bool free = true;
                for (int i = 0; i < capacity; i++)
                {
                    if (allowedSlot.HasFlag(limbSlots[i]) && Items[i]!=null && Items[i]!=item)
                    {
                        free = false;
                        break;
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

            if (limbSlots[index] == LimbSlot.Any)
            {
                if (!item.AllowedSlots.Contains(LimbSlot.Any)) return false;
                if (Items[index] != null) return Items[index] == item;

                PutItem(item, index, createNetworkEvent, true);
                return true;
            }

            LimbSlot placeToSlots = LimbSlot.None;

            bool slotsFree = true;
            List<LimbSlot> allowedSlots = item.AllowedSlots;
            foreach (LimbSlot allowedSlot in allowedSlots)
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
            
            return TryPutItem(item, new List<LimbSlot>() {placeToSlots}, createNetworkEvent);
        }
         
        public void DrawOwn(SpriteBatch spriteBatch, Vector2 offset)
        {
            string toolTip = "";
            Rectangle highlightedSlot = Rectangle.Empty;


            if (doubleClickedItem!=null &&  doubleClickedItem.ParentInventory!=this)
            {
                TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots, true);
            }
            doubleClickedItem = null;

            const int rectWidth = 40, rectHeight = 40;
            Rectangle slotRect = new Rectangle(0, 0, rectWidth, rectHeight);
            Rectangle draggingItemSlot = slotRect;

            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(SlotPositions[i].X + offset.X);
                slotRect.Y = (int)(SlotPositions[i].Y + offset.Y);

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
            }
            
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(SlotPositions[i].X + offset.X);
                slotRect.Y = (int)(SlotPositions[i].Y + offset.Y);

                bool multiSlot = false;
                //skip if the item is in multiple slots
                if (Items[i]!=null)
                {                    
                    for (int n = 0; n < capacity; n++ )
                    {
                        if (i==n || Items[n] != Items[i]) continue;
                        multiSlot = true;
                        break;
                    }
                }


                if (multiSlot) continue;

                if (Items[i] != null && slotRect.Contains(PlayerInput.MousePosition))
                {
                    if (GameMain.DebugDraw)
                    {
                        toolTip = Items[i].ToString();
                    }
                    else
                    {
                        toolTip = string.IsNullOrEmpty(Items[i].Description) ? Items[i].Name : Items[i].Name + '\n' + Items[i].Description;                   
                    }

                    highlightedSlot = slotRect;
                }

                UpdateSlot(spriteBatch, slotRect, i, Items[i], false, i>4 ? 0.2f : 0.4f);
                
                if (draggingItem!=null && draggingItem == Items[i]) draggingItemSlot = slotRect;
            }


            for (int i = 0; i < capacity; i++)
            {

                //Rectangle multiSlotRect = Rectangle.Empty;
                bool multiSlot = false;

                //check if the item is in multiple slots
                if (Items[i] != null)
                {
                    slotRect.X = (int)(SlotPositions[i].X + offset.X);
                    slotRect.Y = (int)(SlotPositions[i].Y + offset.Y);
                    slotRect.Width = 40;
                    slotRect.Height = 40;

                    for (int n = 0; n < capacity; n++)
                    {
                        if (Items[n] != Items[i]) continue;

                        if (!multiSlot && i > n) break;
                        
                        if (i!=n)
                        {
                            multiSlot = true;
                            slotRect = Rectangle.Union(
                                new Rectangle((int)(SlotPositions[n].X+offset.X), (int)(SlotPositions[n].Y+offset.Y), rectWidth, rectHeight), slotRect);
                        }
                    }
                }



                if (multiSlot)
                {
                    if (Items[i] != null && slotRect.Contains(PlayerInput.MousePosition))
                    {
                        toolTip = string.IsNullOrEmpty(Items[i].Description) ? Items[i].Name : Items[i].Name + '\n' + Items[i].Description;
                        highlightedSlot = slotRect;
                    }    
 
                    UpdateSlot(spriteBatch, slotRect, i, Items[i], i > 4);               
                }


                if (character==Character.Controlled && selectedSlot != i &&
                    Items[i] != null && Items[i].CanUseOnSelf && character.HasSelectedItem(Items[i]))
                {
                    useOnSelfButton[i - 3].Update(0.016f);
                    useOnSelfButton[i - 3].Draw(spriteBatch);
                }


            }

            slotRect.Width = rectWidth;
            slotRect.Height = rectHeight;

            if (!string.IsNullOrWhiteSpace(toolTip))
            {
                DrawToolTip(spriteBatch, toolTip, highlightedSlot);
            }

            if (draggingItem == null) return;

            if (!draggingItemSlot.Contains(PlayerInput.MousePosition))
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    slotRect.X = (int)PlayerInput.MousePosition.X - slotRect.Width / 2;
                    slotRect.Y = (int)PlayerInput.MousePosition.Y - slotRect.Height / 2;

                    DrawSlot(spriteBatch, slotRect, draggingItem, false, false);
                }
                else
                {                    
                    DropItem(draggingItem);

                    new NetworkEvent(NetworkEventType.DropItem, draggingItem.ID, true);
                    //draggingItem = null;
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
