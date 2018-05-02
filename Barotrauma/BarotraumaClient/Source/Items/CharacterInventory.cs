using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterInventory : Inventory
    {
        private static Texture2D icons;

        public Vector2[] SlotPositions;

        private GUIButton[] useOnSelfButton;

        partial void InitProjSpecific()
        {
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
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 1),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * 3);

                        useOnSelfButton[i - 3] = new GUIButton(
                            new Rectangle((int)SlotPositions[i].X, (int)(SlotPositions[i].Y - spacing - rectHeight),
                                rectWidth, rectHeight), TextManager.Get("UseItemButton"), "")
                        {
                            UserData = i,
                            OnClicked = UseItemOnSelf
                        };


                        break;
                    //face
                    case 5:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * 3);

                        break;
                    //id card
                    case 6:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * 3);

                        break;
                    default:
                        SlotPositions[i] = new Vector2(
                            spacing * 2 + rectWidth + (spacing + rectWidth) * ((i - 7) % 5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * ((i > 11) ? 2 : 1));
                        break;
                }
            }
        }

        private bool UseItemOnSelf(GUIButton button, object obj)
        {
            if (!(obj is int)) return false;

            int slotIndex = (int)obj;

            return UseItemOnSelf(slotIndex);
        }


        protected override void PutItem(Item item, int i, Character user, bool removeItem = true, bool createNetworkEvent = true)
        {
            base.PutItem(item, i, user, removeItem, createNetworkEvent);
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

            for (int i = 0; i < capacity; i++)
            {
                Rectangle slotRect = new Rectangle(
                    (int)(SlotPositions[i].X + DrawOffset.X), 
                    (int)(SlotPositions[i].Y + DrawOffset.Y), 
                    rectWidth, rectHeight);

                slots[i] = new InventorySlot(slotRect);
                slots[i].Disabled = false;                
                slots[i].Color = limbSlots[i] == InvSlotType.Any ? Color.White * 0.2f : Color.White * 0.4f;
            }

            MergeSlots();
        }


        public override void Update(float deltaTime, bool isSubInventory = false)
        {
            base.Update(deltaTime);

            if (doubleClickedItem != null)
            {
                bool wasPut = false;
                if (doubleClickedItem.ParentInventory != this)
                {
                    wasPut = TryPutItem(doubleClickedItem, Character.Controlled, doubleClickedItem.AllowedSlots, true);
                }
                else
                {
                    var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
                    if (selectedContainer != null && selectedContainer.Inventory != null)
                    {
                        wasPut = selectedContainer.Inventory.TryPutItem(doubleClickedItem, Character.Controlled, doubleClickedItem.AllowedSlots, true);
                    }
                    else if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                    {
                        wasPut = character.SelectedCharacter.Inventory.TryPutItem(doubleClickedItem, Character.Controlled, doubleClickedItem.AllowedSlots, true);
                    }
                    else if (character.SelectedBy != null && Character.Controlled == character.SelectedBy && character.SelectedBy.Inventory != null)
                    {
                        wasPut = character.SelectedBy.Inventory.TryPutItem(doubleClickedItem, Character.Controlled, doubleClickedItem.AllowedSlots, true);
                    }
                    else //doubleclicked and no other inventory is selected
                    {
                        //not equipped -> attempt to equip
                        if (IsInLimbSlot(doubleClickedItem, InvSlotType.Any))
                        {
                            for (int i = 0; i < capacity; i++)
                            {
                                if (limbSlots[i] == InvSlotType.Any || !doubleClickedItem.AllowedSlots.Any(a => a.HasFlag(limbSlots[i]))) continue;
                                wasPut = TryPutItem(doubleClickedItem, i, true, false, Character.Controlled, true);
                                if (wasPut) break;
                            }
                        }
                        //equipped -> attempt to unequip
                        else if (doubleClickedItem.AllowedSlots.Contains(InvSlotType.Any))
                        {
                            wasPut = TryPutItem(doubleClickedItem, Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                        }
                    }
                }

                if (wasPut)
                {
                    for (int i = 0; i < capacity; i++)
                    {
                        if (Items[i] == doubleClickedItem) slots[i].ShowBorderHighlight(Color.Green, 0.1f, 0.9f);
                    }
                }

                draggingItem = null;
                GUI.PlayUISound(wasPut ? GUISoundType.PickItem : GUISoundType.PickItemFail);
            }


            if (highlightedSubInventorySlot != null)
            {
                if (highlightedSubInventorySlot.Inventory == this)
                {
                    UpdateSubInventory(deltaTime, highlightedSubInventorySlot.SlotIndex);
                }

                if (highlightedSubInventory.slots == null || 
                    (!highlightedSubInventorySlot.Slot.InteractRect.Contains(PlayerInput.MousePosition) && !highlightedSubInventory.slots.Any(s => s.InteractRect.Contains(PlayerInput.MousePosition))))
                {
                    highlightedSubInventory = null;
                    highlightedSubInventorySlot = null;
                }
            }
            else
            {
                if (selectedSlot?.Inventory == this)
                {
                    var subInventory = GetSubInventory(selectedSlot.SlotIndex);
                    if (subInventory != null)
                    {
                        highlightedSubInventory = subInventory;
                        highlightedSubInventorySlot = selectedSlot;
                        UpdateSubInventory(deltaTime, highlightedSubInventorySlot.SlotIndex);
                    }
                }
            }

            if (character == Character.Controlled)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if ((selectedSlot == null || selectedSlot.SlotIndex != i) &&
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
                        rootContainer.ParentInventory : rootContainer.GetComponent<ItemContainer>().Inventory;
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
            for (int i = 0; i < capacity - 1; i++)
            {
                slots[i].State = GUIComponent.ComponentState.None;
                if (slots[i].Disabled || Items[i] == null) continue;

                for (int n = i + 1; n < capacity; n++)
                {
                    if (Items[n] == Items[i])
                    {
                        slots[i].Rect = Rectangle.Union(slots[i].Rect, slots[n].Rect);
                        slots[i].InteractRect = Rectangle.Union(slots[i].InteractRect, slots[n].InteractRect);
                        slots[n].Disabled = true;
                    }
                }
            }

            highlightedSubInventory = null;
            highlightedSubInventorySlot = null;
            selectedSlot = null;
        }

        public void DrawOwn(SpriteBatch spriteBatch)
        {
            if (slots == null) CreateSlots();

            Rectangle slotRect = new Rectangle(0, 0, 40, 40);

            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(SlotPositions[i].X + DrawOffset.X);
                slotRect.Y = (int)(SlotPositions[i].Y + DrawOffset.Y);

                if (i == 1) //head
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y),
                        new Rectangle(0, 0, 56, 128), Color.White * 0.7f, 0.0f,
                        new Vector2(28.0f, 64.0f), Vector2.One,
                        SpriteEffects.None, 0.1f);
                }
                else if (i == 3 || i == 4)
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y),
                        new Rectangle(92, 41 * (4 - i), 36, 40), Color.White * 0.7f, 0.0f,
                        new Vector2(18.0f, 20.0f), Vector2.One,
                        SpriteEffects.None, 0.1f);
                }
                else if (i == 5)
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y),
                        new Rectangle(57, 0, 31, 32), Color.White * 0.7f, 0.0f,
                        new Vector2(15.0f, 16.0f), Vector2.One,
                        SpriteEffects.None, 0.1f);
                }
                else if (i == 6)
                {
                    spriteBatch.Draw(icons, new Vector2(slotRect.Center.X, slotRect.Center.Y),
                        new Rectangle(62, 36, 22, 18), Color.White * 0.7f, 0.0f,
                        new Vector2(11.0f, 9.0f), Vector2.One,
                        SpriteEffects.None, 0.1f);
                }
            }

            base.Draw(spriteBatch);

            if (character == Character.Controlled)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if ((selectedSlot == null || selectedSlot.SlotIndex != i) &&
                        Items[i] != null && Items[i].CanUseOnSelf && character.HasSelectedItem(Items[i]))
                    {
                        useOnSelfButton[i - 3].Draw(spriteBatch);
                    }
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (slots[i].IsHighlighted)
                {
                    DrawSubInventory(spriteBatch, i);
                }
            }
        }
    }
}
