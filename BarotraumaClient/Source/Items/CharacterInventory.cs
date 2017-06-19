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
    partial class CharacterInventory : Inventory
    {
        public Vector2[] SlotPositions;

        private GUIButton[] useOnSelfButton;

        void InitProjSpecific()
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
                            spacing * 2 + rectWidth + (spacing + rectWidth) * (i - 2),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * 3);

                        useOnSelfButton[i - 3] = new GUIButton(
                            new Rectangle((int)SlotPositions[i].X, (int)(SlotPositions[i].Y - spacing - rectHeight),
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
                            spacing * 2 + rectWidth + (spacing + rectWidth) * ((i - 6) % 5),
                            GameMain.GraphicsHeight - (spacing + rectHeight) * ((i > 10) ? 2 : 1));
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
    }
}
