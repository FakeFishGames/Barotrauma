using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

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

        private static Dictionary<Entity, int> orderIndicatorCount = new Dictionary<Entity, int>();

        public static void Draw(SpriteBatch spriteBatch, Character character, Camera cam)
        {
            if (GUI.DisableHUD) return;

            if (GameMain.GameSession?.CrewManager != null)
            {
                orderIndicatorCount.Clear();
                foreach (Pair<Order, float> timedOrder in GameMain.GameSession.CrewManager.ActiveOrders)
                {
                    DrawOrderIndicator(spriteBatch, cam, character, timedOrder.First, MathHelper.Clamp(timedOrder.Second / 10.0f, 0.2f, 1.0f));
                }

                if (character.CurrentOrder != null)
                {
                    DrawOrderIndicator(spriteBatch, cam, character, character.CurrentOrder, 1.0f);                    
                }

                /*//recreate order list if it doesn't exist, is for some other character, 
                //if there are new orders, or if the list contains orders that don't exist anymore
                if (orderList == null || orderList.UserData != character ||
                    currentOrders.Any(o => orderList.FindChild(o) == null) ||
                    orderList.children.Any(c => !currentOrders.Contains(c.UserData as Order)))
                {
                    orderList = new GUIListBox(new Rectangle(GameMain.GraphicsWidth - 150, 50, 150, 500), Color.Transparent, null);
                    orderList.UserData = character;

                    foreach (Order order in currentOrders)
                    {
                        var orderFrame = new GUITextBlock(
                            new Rectangle(0, 0, 0, 50), order.Name, "ListBoxElement", 
                            Alignment.TopLeft, Alignment.TopRight, orderList, false, GUI.SmallFont);
                        orderFrame.UserData = order;

                        var dismissButton = new GUIButton(new Rectangle(0, 0, 80, 20), "Dismiss", Alignment.BottomRight, "", orderFrame);
                        //dismissButton.Font = GUI.SmallFont;
                        dismissButton.UserData = order;
                        dismissButton.OnClicked += (btn, userdata) =>
                        {
                            Order dismissedOrder = userdata as Order;
                            GameMain.GameSession.CrewManager.RemoveOrder(dismissedOrder);
                            if (dismissedOrder == character.CurrentOrder)
                            {
                                character.SetOrder(null, "");
                            }
                            return true;
                        };
                    }
                }

                orderList.Draw(spriteBatch);*/

            }

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

        private static void DrawOrderIndicator(SpriteBatch spriteBatch, Camera cam, Character character, Order order, float iconAlpha = 1.0f)
        {
            if (order.TargetAllCharacters && !order.HasAppropriateJob(character)) return;

            Entity target = order.ConnectedController != null ? order.ConnectedController.Item : order.TargetEntity;
            if (target == null) return;

            if (!orderIndicatorCount.ContainsKey(target)) orderIndicatorCount.Add(target, 0);

            Vector2 drawPos = target.WorldPosition + Vector2.UnitX * order.SymbolSprite.size.X * 1.5f * orderIndicatorCount[target];
            GUI.DrawIndicator(spriteBatch, drawPos, cam, 100.0f, order.SymbolSprite, order.Color * iconAlpha);

            orderIndicatorCount[target] = orderIndicatorCount[target] + 1;
        }

        /*private static void DrawStatusIcons(SpriteBatch spriteBatch, Character character)
        {
            if (GameMain.DebugDraw)
            {
                GUI.DrawString(spriteBatch, new Vector2(30, GameMain.GraphicsHeight - 260), TextManager.Get("Stun") + ": " + character.Stun, Color.White);
            }

            if (oxygenBar == null)
            {
                int width = 100, height = 20;

                oxygenBar = new GUIProgressBar(new Rectangle(30, GameMain.GraphicsHeight - 200, width, height), Color.Blue, "", 1.0f, Alignment.TopLeft);
                new GUIImage(new Rectangle(-27, -7, 20, 20), new Rectangle(17, 0, 20, 24), statusIcons, Alignment.TopLeft, oxygenBar);

                healthBar = new GUIProgressBar(new Rectangle(30, GameMain.GraphicsHeight - 230, width, height), Color.Red, "", 1.0f, Alignment.TopLeft);
                new GUIImage(new Rectangle(-26, -7, 20, 20), new Rectangle(0, 0, 13, 24), statusIcons, Alignment.TopLeft, healthBar);
            }

            oxygenBar.BarSize = character.Oxygen / 100.0f;
            if (oxygenBar.BarSize < 0.99f)
            {
                oxygenBar.Draw(spriteBatch);
                if (!oxyMsgShown)
                {
                    GUI.AddMessage(TextManager.Get("OxygenBarInfo"), new Vector2(oxygenBar.Rect.Right + 10, oxygenBar.Rect.Center.Y), Alignment.CenterLeft, Color.White, 5.0f);
                    oxyMsgShown = true;
                }
            }

            healthBar.BarSize = character.Health / character.MaxHealth;
            if (healthBar.BarSize < 1.0f)
            {
                healthBar.Draw(spriteBatch);
            }

            float bloodDropCount = character.Bleeding;
            bloodDropCount = MathHelper.Clamp(bloodDropCount, 0.0f, 5.0f);
            for (int i = 0; i < Math.Ceiling(bloodDropCount); i++)
            {
                float alpha = MathHelper.Clamp(bloodDropCount-i, 0.2f, 1.0f);
                spriteBatch.Draw(statusIcons.Texture, new Vector2(25.0f + 20 * i, healthBar.Rect.Y - 20.0f), new Rectangle(39, 3, 15, 19), Color.White * alpha);
            }

            float pressureFactor = (character.AnimController.CurrentHull == null) ?
                100.0f : Math.Min(character.AnimController.CurrentHull.LethalPressure, 100.0f);
            if (character.PressureProtection > 0.0f) pressureFactor = 0.0f;

            if (pressureFactor > 0.0f)
            {
                float indicatorAlpha = ((float)Math.Sin(character.PressureTimer * 0.1f) + 1.0f) * 0.5f;
                indicatorAlpha = MathHelper.Clamp(indicatorAlpha, 0.1f, pressureFactor / 100.0f);
                
                if (pressureMsgTimer > 0.5f && !pressureMsgShown)
                {
                    GUI.AddMessage(TextManager.Get("PressureInfo"), new Vector2(40.0f, healthBar.Rect.Y - 75.0f), Alignment.CenterLeft, Color.White, 5.0f);
                    pressureMsgShown = true;                    
                }

                spriteBatch.Draw(statusIcons.Texture, new Vector2(10.0f, healthBar.Rect.Y - 60.0f), new Rectangle(0, 24, 24, 25), Color.White * indicatorAlpha);            
            }
        }*/
    }
}
