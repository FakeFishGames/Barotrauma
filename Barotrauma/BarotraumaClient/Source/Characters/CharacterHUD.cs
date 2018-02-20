using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    class CharacterHUD
    {
        private static GUIButton cprButton;        
        private static GUIButton grabHoldButton;
                
        public static void TakeDamage(float amount)
        {
        }

        public static void AddToGUIUpdateList(Character character)
        {
            if (GUI.DisableHUD) return;

            if (cprButton != null && cprButton.Visible) cprButton.AddToGUIUpdateList();

            if (grabHoldButton != null && cprButton.Visible) grabHoldButton.AddToGUIUpdateList();

            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
                if (character.Inventory != null)
                {
                    for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                    {
                        var item = character.Inventory.Items[i];
                        if (item == null || CharacterInventory.limbSlots[i] == InvSlotType.Any) continue;

                        foreach (ItemComponent ic in item.components)
                        {
                            if (ic.DrawHudWhenEquipped) ic.AddToGUIUpdateList();
                        }
                    }
                }

                if (character.IsHumanoid && character.SelectedCharacter != null)
                {
                    character.SelectedCharacter.CharacterHealth.AddToGUIUpdateList();
                }
            }
        }

        public static void Update(float deltaTime, Character character)
        {            
            if (Inventory.SelectedSlot == null)
            {
                if (cprButton != null && cprButton.Visible) cprButton.Update(deltaTime);
                if (grabHoldButton != null && grabHoldButton.Visible) grabHoldButton.Update(deltaTime);
            }

            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
                if (character.Inventory != null)
                {
                    if (!character.LockHands && character.Stun >= -0.1f)
                    {
                        character.Inventory.Update(deltaTime);
                    }

                    for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                    {
                        var item = character.Inventory.Items[i];
                        if (item == null || CharacterInventory.limbSlots[i] == InvSlotType.Any) continue;

                        foreach (ItemComponent ic in item.components)
                        {
                            if (ic.DrawHudWhenEquipped) ic.UpdateHUD(character);
                        }
                    }
                }

                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    character.SelectedCharacter.Inventory.Update(deltaTime);
                    character.SelectedCharacter.CharacterHealth.UpdateHUD(deltaTime);
                }

                Inventory.UpdateDragging();
            }
        }

        public static void Draw(SpriteBatch spriteBatch, Character character, Camera cam)
        {
            if (GUI.DisableHUD) return;

            if (character.Inventory != null)
            {
                for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                {
                    var item = character.Inventory.Items[i];
                    if (item == null || CharacterInventory.limbSlots[i] == InvSlotType.Any) continue;

                    foreach (ItemComponent ic in item.components)
                    {
                        if (ic.DrawHudWhenEquipped) ic.DrawHUD(spriteBatch, character);
                    }
                }
            }

            //DrawStatusIcons(spriteBatch, character);

            if (character.Inventory != null && !character.LockHands && character.Stun >= -0.1f)
            {
                character.Inventory.DrawOffset = Vector2.Zero;
                character.Inventory.DrawOwn(spriteBatch);
            }

            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    if (cprButton == null)
                    {
                        cprButton = new GUIButton(
                            new Rectangle(character.SelectedCharacter.Inventory.SlotPositions[0].ToPoint() + new Point(540, -30), new Point(140, 20)), "Perform CPR", "");

                        cprButton.OnClicked = (button, userData) =>
                        {
                            if (Character.Controlled == null || Character.Controlled.SelectedCharacter == null) return false;

                            Character.Controlled.AnimController.Anim = (Character.Controlled.AnimController.Anim == AnimController.Animation.CPR) ?
                                AnimController.Animation.None : AnimController.Animation.CPR;

                            foreach (Limb limb in Character.Controlled.SelectedCharacter.AnimController.Limbs)
                            {
                                limb.pullJoint.Enabled = false;
                            }
                            
                            if (GameMain.Client != null)
                            {
                                GameMain.Client.CreateEntityEvent(Character.Controlled, new object[] { NetEntityEvent.Type.Repair });
                            }
                            
                            return true;
                        };
                    }

                    if (grabHoldButton == null)
                    {
                        grabHoldButton = new GUIButton(
                            new Rectangle(character.SelectedCharacter.Inventory.SlotPositions[0].ToPoint() + new Point(540, -60), new Point(140, 20)),
                                TextManager.Get("Grabbing") + ": " + TextManager.Get(character.AnimController.GrabLimb == LimbType.None ? "Hands" : character.AnimController.GrabLimb.ToString()), "");

                        grabHoldButton.OnClicked = (button, userData) =>
                        {
                            if (Character.Controlled == null || Character.Controlled.SelectedCharacter == null) return false;

                            Character.Controlled.AnimController.GrabLimb = Character.Controlled.AnimController.GrabLimb == LimbType.None ? LimbType.Torso : LimbType.None;

                            foreach (Limb limb in Character.Controlled.SelectedCharacter.AnimController.Limbs)
                            {
                                limb.pullJoint.Enabled = false;
                            }

                            if (GameMain.Client != null)
                            {
                                GameMain.Client.CreateEntityEvent(Character.Controlled, new object[] { NetEntityEvent.Type.Control });
                            }

                            grabHoldButton.Text = TextManager.Get("Grabbing") + ": " + TextManager.Get(character.AnimController.GrabLimb == LimbType.None ? "Hands" : character.AnimController.GrabLimb.ToString());
                            return true;
                        };
                    }
                    
                    if (cprButton.Visible) cprButton.Draw(spriteBatch);
                    if (grabHoldButton.Visible) grabHoldButton.Draw(spriteBatch);

                    character.SelectedCharacter.Inventory.DrawOffset = new Vector2(320.0f + 120.0f, 0.0f);
                    character.SelectedCharacter.Inventory.DrawOwn(spriteBatch);
                    character.SelectedCharacter.CharacterHealth.DrawStatusHUD(spriteBatch, new Vector2(320.0f + 120, 0.0f));
                }

                if (character.Inventory != null && !character.LockHands && character.Stun >= -0.1f)
                {
                    Inventory.DrawDragging(spriteBatch);
                }

                if (character.FocusedCharacter != null && character.FocusedCharacter.CanBeSelected)
                {
                    Vector2 startPos = character.DrawPosition + (character.FocusedCharacter.DrawPosition - character.DrawPosition) * 0.7f;
                    startPos = cam.WorldToScreen(startPos);

                    string focusName = character.FocusedCharacter.SpeciesName;
                    if (character.FocusedCharacter.Info != null)
                    {
                        focusName = character.FocusedCharacter.Info.DisplayName;
                    }
                    Vector2 textPos = startPos;
                    textPos -= new Vector2(GUI.Font.MeasureString(focusName).X / 2, 20);

                    GUI.DrawString(spriteBatch, textPos, focusName, Color.White, Color.Black, 2);
                }
                else if (character.SelectedCharacter == null && character.FocusedItem != null && character.SelectedConstruction == null)
                {
                    var hudTexts = character.FocusedItem.GetHUDTexts(character);

                    Vector2 startPos = new Vector2((int)(GameMain.GraphicsWidth / 2.0f), GameMain.GraphicsHeight);
                    startPos.Y -= 50 + hudTexts.Count * 25;

                    Vector2 textPos = startPos;
                    textPos -= new Vector2((int)GUI.Font.MeasureString(character.FocusedItem.Name).X / 2, 20);

                    GUI.DrawString(spriteBatch, textPos, character.FocusedItem.Name, Color.White, Color.Black * 0.7f, 2);

                    textPos.Y += 30.0f;
                    foreach (ColoredText coloredText in hudTexts)
                    {
                        textPos.X = (int)(startPos.X - GUI.SmallFont.MeasureString(coloredText.Text).X / 2);

                        GUI.DrawString(spriteBatch, textPos, coloredText.Text, coloredText.Color, Color.Black * 0.7f, 2, GUI.SmallFont);

                        textPos.Y += 25;
                    }
                }
                
                foreach (HUDProgressBar progressBar in character.HUDProgressBars.Values)
                {
                    progressBar.Draw(spriteBatch, cam);
                }
            }
        }

        /*private static void DrawStatusIcons(SpriteBatch spriteBatch, Character character)
        {
            if (GameMain.DebugDraw)
            {
                GUI.DrawString(spriteBatch, new Vector2(30, GameMain.GraphicsHeight - 260), TextManager.Get("Stun") + ": " + character.Stun, Color.White);
            }            
        }*/
    }
}
