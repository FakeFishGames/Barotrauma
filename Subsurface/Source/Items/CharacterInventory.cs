using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    [Flags]
    public enum LimbSlot
    {
        Any = 1, RightHand = 2, LeftHand = 4, Head = 8, Torso = 16, Legs = 32, BothHands = 64
    };

    class CharacterInventory : Inventory
    {
        private static Texture2D icons;

        private Character character;

        private static LimbSlot[] limbSlots = new LimbSlot[] { 
            LimbSlot.Head, LimbSlot.Torso, LimbSlot.Legs, LimbSlot.LeftHand, LimbSlot.RightHand,
            LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any,
            LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any, LimbSlot.Any};

        private Vector2[] slotPositions;

        public CharacterInventory(int capacity, Character character)
            : base(capacity)
        {
            this.character = character;

            if (icons == null) icons = TextureLoader.FromFile("Content/UI/inventoryIcons.png");

            slotPositions = new Vector2[limbSlots.Length];


            int rectWidth = 40, rectHeight = 40;
            int spacing = 10;
            for (int i = 0; i < slotPositions.Length; i++)
            {
                switch (i)
                {
                    //head, torso, legs
                    case 0:
                    case 1:
                    case 2:
                        slotPositions[i] = new Vector2(
                            spacing, 
                            GameMain.GraphicsHeight - (spacing + rectHeight) * (3 - i));
                        break;
                    //lefthand, righthand
                    case 3:
                    case 4:
                        slotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 3),
                            GameMain.GraphicsHeight - (spacing + rectHeight)*3);
                        break;
                    default:
                        slotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * ((i - 3)%5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * ((i>9) ? 2 : 1));
                        break;
                }
            }
        }

        protected override void DropItem(Item item)
        {
            if (item.body == null) return;

            bool enabled = item.body.Enabled;
            item.Drop(character);

            if (!enabled)
            {
                item.SetTransform(character.SimPosition, 0.0f);
            }
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

            if (allowedSlots.HasFlag(LimbSlot.BothHands)) TryPutItem(item, 3, createNetworkEvent);

            return false;
        }

        public override bool TryPutItem(Item item, int i, bool createNetworkEvent)
        {
            LimbSlot usedSlots = item.AllowedSlots;

            //there's already an item in the slot
            if (items[i] != null)
            {
                bool combined = false;
                //if (item.Combine(items[i]))
                //{
                //    //PutItem(item, i, false, false);
                //    combined = true;
                //}
                //else 
                if (items[i].Combine(item))
                {
                    //PutItem(items[i], i, false, false);
                    Inventory otherInventory = items[i].inventory;
                    if (otherInventory!=null)
                    {
                        new Networking.NetworkEvent(Networking.NetworkEventType.InventoryUpdate, otherInventory.ID, true);
                    }

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

                    PutItem(item, rightHandSlot, createNetworkEvent, true);
                    PutItem(item, leftHandSlot, createNetworkEvent, false);
                    item.Equip(character);
                    return true;
                }

                
                return false;
            }
            
        }
         
        public void DrawOwn(SpriteBatch spriteBatch)
        {
            if (doubleClickedItem!=null &&  doubleClickedItem.inventory!=this)
            {
                TryPutItem(doubleClickedItem, doubleClickedItem.AllowedSlots, true);
            }
            doubleClickedItem = null;

            int rectWidth = 40, rectHeight = 40;
            Rectangle slotRect = new Rectangle(0, 0, rectWidth, rectHeight);
            Rectangle draggingItemSlot = slotRect;

            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)slotPositions[i].X;
                slotRect.Y = (int)slotPositions[i].Y;

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
                slotRect.X = (int)slotPositions[i].X;
                slotRect.Y = (int)slotPositions[i].Y;


                UpdateSlot(spriteBatch, slotRect, i, items[i], i>4);
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
                    DropItem(draggingItem);

                    new Networking.NetworkEvent(Barotrauma.Networking.NetworkEventType.DropItem, draggingItem.ID, true);
                    //draggingItem = null;
                }
            }                       
        }

    }
}
