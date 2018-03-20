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

            SlotPositions = new Vector2[SlotTypes.Length];

            int x = GameMain.GraphicsWidth / 2;
            int rectWidth = 40, rectHeight = 40;
            int spacing = 10;


            for (int i = 0; i < SlotPositions.Length; i++)
            {
                switch (SlotTypes[i])
                {
                    case InvSlotType.Headset:
                        SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - slotSpriteSmall.size.X - spacing, 50);
                        break;
                    case InvSlotType.Card:
                        SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - (slotSpriteSmall.size.X + spacing) * 2, 50);
                        break;
                    case InvSlotType.InnerClothes:
                        SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - (slotSpriteSmall.size.X + spacing) * 2 - slotSpriteHorizontal.size.X - spacing, 50);
                        break;
                    case InvSlotType.Head:
                        SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - slotSpriteSmall.size.X * 3 - spacing * 4 - slotSpriteHorizontal.size.X, 50);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.OuterClothes:
                        SlotPositions[i] = new Vector2(100, GameMain.GraphicsHeight - 160);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.LeftHand:
                        SlotPositions[i] = new Vector2(180, GameMain.GraphicsHeight - 160);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.RightHand:
                        SlotPositions[i] = new Vector2(260, GameMain.GraphicsHeight - 160);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.Any:
                        SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - 160);
                        x += (int)slotSpriteVertical.size.X + spacing;
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
            
            for (int i = 0; i < capacity; i++)
            {
                Sprite slotSprite = slotSpriteSmall;
                switch (SlotTypes[i])
                {
                    case InvSlotType.InnerClothes:
                        slotSprite = slotSpriteHorizontal;
                        break;
                    case InvSlotType.OuterClothes:
                    case InvSlotType.LeftHand:
                    case InvSlotType.RightHand:
                    case InvSlotType.Any:
                        slotSprite = slotSpriteVertical;
                        break;
                }

                Rectangle slotRect = new Rectangle(
                    (int)(SlotPositions[i].X + DrawOffset.X), 
                    (int)(SlotPositions[i].Y + DrawOffset.Y),
                    (int)slotSprite.size.X, (int)slotSprite.size.Y);

                slots[i] = new InventorySlot(slotRect);
                slots[i].Disabled = false;
                slots[i].SlotSprite = slotSprite;
                slots[i].Color = SlotTypes[i] == InvSlotType.Any ? Color.White * 0.2f : Color.White * 0.4f;

            }

            //MergeSlots();
        }

        protected override bool HideSlot(int i)
        {
            if (slots[i].Disabled || (hideEmptySlot[i] && Items[i] == null)) return true;

            //don't show the equip slot if the item is also in the default inventory
            if (SlotTypes[i] != InvSlotType.Any && Items[i] != null)
            {
                for (int j = 0; j < capacity; j++)
                {
                    if (SlotTypes[j] == InvSlotType.Any && Items[j] == Items[i]) return true;
                }
            }

            return false;
        }

        public override void Update(float deltaTime, bool isSubInventory = false)
        {
            base.Update(deltaTime);

            if (doubleClickedItem != null)
            {
                DoubleClickItem(doubleClickedItem);
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

                    if (Items[i] != null && Items[i].AllowedSlots.Any(a => a != InvSlotType.Any))
                    {
                        int buttonDir = System.Math.Sign(GameMain.GraphicsHeight / 2 - slots[i].Rect.Center.Y);
                        Rectangle equipButtonRect = new Rectangle(
                            (int)(slots[i].Rect.Center.X - equipIndicator.size.X / 2), (int)(slots[i].Rect.Center.Y + (slots[i].Rect.Height / 2 + 20) * buttonDir - equipIndicator.size.Y/2),
                            (int)equipIndicator.size.X, (int)equipIndicator.size.Y);
                        slots[i].EquipButtonState = equipButtonRect.Contains(PlayerInput.MousePosition) ? GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None;

                        if (slots[i].EquipButtonState == GUIComponent.ComponentState.Hover)
                        {
                            if (PlayerInput.LeftButtonDown()) slots[i].EquipButtonState = GUIComponent.ComponentState.Pressed;
                            if (PlayerInput.LeftButtonClicked())
                            {
                                DoubleClickItem(Items[i]);
                            }
                        }

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

        private void DoubleClickItem(Item item)
        {
            bool wasPut = false;
            if (item.ParentInventory != this)
            {
                wasPut = TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
            }
            else
            {
                var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
                if (selectedContainer != null && selectedContainer.Inventory != null)
                {
                    wasPut = selectedContainer.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    wasPut = character.SelectedCharacter.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (character.SelectedBy != null && Character.Controlled == character.SelectedBy && character.SelectedBy.Inventory != null)
                {
                    wasPut = character.SelectedBy.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else //doubleclicked and no other inventory is selected
                {
                    //not equipped -> attempt to equip
                    if (!character.HasEquippedItem(item))
                    {
                        for (int i = 0; i < capacity; i++)
                        {
                            if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) continue;

                            //something else already equipped in the slot, attempt to unequip it
                            if (Items[i] != null && Items[i].AllowedSlots.Contains(InvSlotType.Any))
                            {
                                TryPutItem(Items[i], Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                            }

                            wasPut = TryPutItem(item, i, true, false, Character.Controlled, true);
                            if (wasPut) break;
                        }
                    }
                    //equipped -> attempt to unequip
                    else if (item.AllowedSlots.Contains(InvSlotType.Any))
                    {
                        wasPut = TryPutItem(item, Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                    }
                    else
                    {

                    }
                }
            }

            if (wasPut)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (Items[i] == item) slots[i].ShowBorderHighlight(Color.Green, 0.1f, 0.9f);
                }
            }

            draggingItem = null;
            GUI.PlayUISound(wasPut ? GUISoundType.PickItem : GUISoundType.PickItemFail);
        }

        /*private void MergeSlots()
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
        }*/

        public void DrawOwn(SpriteBatch spriteBatch)
        {
            if (slots == null) CreateSlots();

            Rectangle slotRect = new Rectangle(0, 0, 40, 40);

            for (int i = 0; i < capacity; i++)
            {
               /* slotRect.X = (int)(SlotPositions[i].X + DrawOffset.X);
                slotRect.Y = (int)(SlotPositions[i].Y + DrawOffset.Y);
                slotRect.Width = (int)slotSprites[i].size.X;
                slotRect.Height = (int)slotSprites[i].size.Y;*/

                /*if (i == 1) //head
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
                }*/
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
                if (HideSlot(i)) continue;
                if (slots[i].IsHighlighted)
                {
                    DrawSubInventory(spriteBatch, i);
                }
                if (Items[i] != null && Items[i].AllowedSlots.Any(a => a != InvSlotType.Any))
                {
                    Color color = slots[i].EquipButtonState == GUIComponent.ComponentState.Hover ? Color.White : Color.White * 0.8f;
                    if (slots[i].EquipButtonState == GUIComponent.ComponentState.Pressed) color = Color.Gray;

                    int buttonDir = System.Math.Sign(GameMain.GraphicsHeight / 2 - slots[i].Rect.Center.Y);
                    equipIndicator.Draw(spriteBatch, new Vector2(slots[i].Rect.Center.X, slots[i].Rect.Center.Y + (slots[i].Rect.Height / 2 + 20) * buttonDir), color, equipIndicator.size / 2);
                    if (character.HasEquippedItem(Items[i]))
                    {
                        equipIndicatorOn.Draw(spriteBatch, new Vector2(slots[i].Rect.Center.X, slots[i].Rect.Center.Y + (slots[i].Rect.Height / 2 + 20) * buttonDir), color * 0.9f, equipIndicatorOn.size / 2, 0, 0.85f);
                    }
                }
            }
        }
    }
}
