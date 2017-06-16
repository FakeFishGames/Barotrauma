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
    }
}
