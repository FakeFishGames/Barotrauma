using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{
    [Flags]
    public enum LimbSlot
    {
        Any = 1, RightHand = 2, LeftHand = 4, Head = 8, Torso = 16, Legs = 32, BothHands = 64
    };

    class CharacterInventory : Inventory
    {
        private Character character;

        private static LimbSlot[] limbSlots = new LimbSlot[] { 
            LimbSlot.Head, LimbSlot.Torso, LimbSlot.LeftHand, LimbSlot.RightHand, LimbSlot.Legs,
            LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any };

        public CharacterInventory(int capacity, Character character)
            : base(capacity)
        {
            this.character = character;
        }

        protected override void DropItem(Item item)
        {
            item.Drop(character);
            item.body.SetTransform(character.SimPosition, 0.0f);
        }

        public int FindLimbSlot(LimbSlot limbSlot)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if ( limbSlots[i] == limbSlot) return i;
            }
            return -1;
        }

        public bool IsInLimbSlot(Item item, LimbSlot limbSlot)
        {
            for (int i = 0; i<items.Length; i++)
            {
                if (items[i] == item && limbSlots[i] == limbSlot) return true;
            }
            return false;
        }

        /// <summary>
        /// If there is room, puts the item in the inventory and returns true, otherwise returns false
        /// </summary>
        public override bool TryPutItem(Item item, LimbSlot allowedSlots, bool createNetworkEvent = true)
        {
            for (int i = 0; i < capacity; i++)
            {
                //item is already in the inventory!
                if (items[i] == item) return true;
            }

            if (allowedSlots.HasFlag(LimbSlot.Any))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (items[i] != null) continue;
                    if (limbSlots[i] != LimbSlot.Any) continue;
                    PutItem(item, i, createNetworkEvent);
                    item.Unequip(character);
                    return true;                   
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (allowedSlots.HasFlag(limbSlots[i]) && items[i]!=null) return false;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (allowedSlots.HasFlag(limbSlots[i]) && items[i] == null)
                {
                    PutItem(item, i, createNetworkEvent);
                    item.Equip(character);
                    return true;
                }
            }

            return false;
        }

        public override bool TryPutItem(Item item, int i, bool createNetworkEvent)
        {
            LimbSlot usedSlots = item.AllowedSlots;

            //there's already an item in the slot
            if (items[i] != null)
            {
                bool combined = false;
                if (item.Combine(items[i]))
                {
                    //PutItem(item, i, false, false);
                    combined = true;
                }
                else if (items[i].Combine(item))
                {
                    //PutItem(items[i], i, false, false);
                    combined = true;
                }

                if (!combined) return false;                

                if (usedSlots.HasFlag(LimbSlot.BothHands))
                {
                    if (limbSlots[i] == LimbSlot.LeftHand)
                    {
                        PutItem(item, FindLimbSlot(LimbSlot.RightHand), createNetworkEvent, false);
                    }
                    else if (limbSlots[i] == LimbSlot.RightHand)
                    {
                        PutItem(item, FindLimbSlot(LimbSlot.LeftHand), createNetworkEvent, false);
                    }                        
                }
                if (limbSlots[i] == LimbSlot.Any) item.Unequip(character);

                return true;
            }
            
            if (limbSlots[i]==LimbSlot.Any)
            {
                if (usedSlots.HasFlag(LimbSlot.Any))
                {
                    item.Unequip(character);
                    PutItem(item, i, createNetworkEvent);
                    return true;
                }
                else
                {
                    return false;
                }

            }
            else
            {

                if (limbSlots[i] != LimbSlot.Any && usedSlots.HasFlag(limbSlots[i]) && items[i] == null)
                {
                    item.Unequip(character);
                    PutItem(item, i, createNetworkEvent);
                    item.Equip(character);
                    return true;
                }
                
                if (usedSlots.HasFlag(LimbSlot.BothHands) && (limbSlots[i]==LimbSlot.LeftHand || limbSlots[i]==LimbSlot.RightHand))
                {
                    int rightHandSlot = FindLimbSlot(LimbSlot.LeftHand);
                    int leftHandSlot = FindLimbSlot(LimbSlot.RightHand);

                    if (items[rightHandSlot] != null) return false;
                    if (items[leftHandSlot] != null) return false;

                    PutItem(item, rightHandSlot, true, true);
                    PutItem(item, leftHandSlot, true, false);
                    item.Equip(character);
                    return true;
                }

                
                return false;
            }
            
        }
         
        public override void Draw(SpriteBatch spriteBatch)
        {
            if (doubleClickedItem!=null &&  doubleClickedItem.inventory!=this)
            {
                TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots, true);
            }
            doubleClickedItem = null;

            int rectWidth = 40, rectHeight = 40;

            int spacing = 10;

            Rectangle slotRect = new Rectangle(0, 0, rectWidth, rectHeight);
            Rectangle draggingItemSlot = slotRect;

            for (int i = 0; i < capacity; i++)
            {
                int x, y;
                switch (i)
                {
                    //head
                    case 0:
                    //legs
                    case 4:
                        x = spacing * 2 + rectWidth;
                        y = Game1.GraphicsHeight - (spacing + rectHeight) * ((i == 0) ? 3 : 1);
                        break;
                    //lefthand
                    case 2:
                    //torso
                    case 1:
                    //righthand
                    case 3:
                        x = spacing;
                        if (i == 1) x += (spacing + rectWidth);
                        if (i == 3) x += (spacing + rectWidth) * 2;
                        y = Game1.GraphicsHeight - (spacing + rectHeight) * 2;
                        break;
                    default:
                        x = spacing * (4 + (i - 5)) + rectWidth * (3 + (i - 5));
                        y = Game1.GraphicsHeight - (spacing + rectHeight);
                        break;
                }

                slotRect.X = x;
                slotRect.Y = y;

                UpdateSlot(spriteBatch, slotRect, i, items[i], false);
                if (draggingItem!=null && draggingItem == items[i]) draggingItemSlot = slotRect;

            }

            if (draggingItem != null && !draggingItemSlot.Contains(PlayerInput.MousePosition))
            {
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                {
                    slotRect.X = PlayerInput.GetMouseState.X - slotRect.Width / 2;
                    slotRect.Y = PlayerInput.GetMouseState.Y - slotRect.Height / 2;
                    //GUI.DrawRectangle(spriteBatch, rect, Color.White, true);
                    //draggingItem.sprite.Draw(spriteBatch, new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), Color.White);

                    DrawSlot(spriteBatch, slotRect, draggingItem, false, false);
                }
                else
                {
                    draggingItem.body.SetTransform(character.SimPosition, 0.0f);
                    DropItem(draggingItem);
                    //draggingItem = null;
                }
            }                       
        }

    }
}
