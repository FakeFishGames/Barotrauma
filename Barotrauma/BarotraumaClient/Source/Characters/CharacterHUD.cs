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

        const float ItemOverlayDelay = 1.0f;
        private static Item focusedItem;
        private static float focusedItemOverlayTimer;
                
        public static void TakeDamage(float amount)
        {
        }

        public static void AddToGUIUpdateList(Character character)
        {
            if (GUI.DisableHUD) return;

            if (cprButton != null && cprButton.Visible) cprButton.AddToGUIUpdateList();
            
            if (!character.IsUnconscious && character.Stun <= 0.0f &&
                (GameMain.GameSession?.CrewManager?.CrewCommander == null || !GameMain.GameSession.CrewManager.CrewCommander.IsOpen))
            {
                if (character.Inventory != null)
                {
                    for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                    {
                        var item = character.Inventory.Items[i];
                        if (item == null || CharacterInventory.SlotTypes[i] == InvSlotType.Any) continue;

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
            }
            
            if (!character.IsUnconscious && character.Stun <= 0.0f &&
                (GameMain.GameSession?.CrewManager?.CrewCommander == null || !GameMain.GameSession.CrewManager.CrewCommander.IsOpen))
            {
                if (character.Inventory != null)
                {
                    if (!character.LockHands && character.Stun < 0.1f)
                    {
                        character.Inventory.Update(deltaTime);
                    }

                    for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                    {
                        var item = character.Inventory.Items[i];
                        if (item == null || CharacterInventory.SlotTypes[i] == InvSlotType.Any) continue;

                        foreach (ItemComponent ic in item.components)
                        {
                            if (ic.DrawHudWhenEquipped) ic.UpdateHUD(character);
                        }
                    }
                }

                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    if (character.SelectedCharacter.CanInventoryBeAccessed)
                    {
                        character.SelectedCharacter.Inventory.Update(deltaTime);
                    }
                    character.SelectedCharacter.CharacterHealth.UpdateHUD(deltaTime);
                }

                Inventory.UpdateDragging();
            }

            if (focusedItem != null)
            {
                if (character.FocusedItem != null)
                {
                    focusedItemOverlayTimer = Math.Min(focusedItemOverlayTimer + deltaTime, ItemOverlayDelay + 1.0f);
                }
                else
                {
                    focusedItemOverlayTimer = Math.Max(focusedItemOverlayTimer - deltaTime, 0.0f);
                    if (focusedItemOverlayTimer <= 0.0f) focusedItem = null;
                }
            }
        }

        private static Dictionary<Entity, int> orderIndicatorCount = new Dictionary<Entity, int>();

        public static void Draw(SpriteBatch spriteBatch, Character character, Camera cam)
        {
            if (GUI.DisableHUD) return;

            character.CharacterHealth.Alignment = Alignment.Left;

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

            foreach (Optimizable optimizable in Optimizable.CurrentlyOptimizable)
            {
                if (optimizable.DegreeOfSuccess(character) < 0.5f) continue;

                float dist = Vector2.Distance(character.WorldPosition, optimizable.Item.WorldPosition);
                if (dist < 1000.0f)
                {
                    Vector2 drawPos = optimizable.Item.DrawPosition;
                    //TODO: proper icon
                    float alpha = (1000.0f - dist) / 1000.0f * 2.0f;

                    GUI.DrawIndicator(spriteBatch, drawPos, cam, 100.0f, GUI.SubmarineIcon, Color.Yellow * alpha);
                }
            }

            if (character.Inventory != null)
            {
                for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                {
                    var item = character.Inventory.Items[i];
                    if (item == null || CharacterInventory.SlotTypes[i] == InvSlotType.Any) continue;

                    foreach (ItemComponent ic in item.components)
                    {
                        if (ic.DrawHudWhenEquipped) ic.DrawHUD(spriteBatch, character);
                    }
                }
            }
            
            if (character.Inventory != null && !character.LockHands && character.Stun <= 0.1f && 
                (GameMain.GameSession?.CrewManager?.CrewCommander == null || !GameMain.GameSession.CrewManager.CrewCommander.IsOpen))
            {
                character.Inventory.DrawOwn(spriteBatch);
            }

            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    if (cprButton == null)
                    {
                        cprButton = new GUIButton(
                            new Rectangle(
                                new Point((int)(GameMain.GraphicsWidth - 40 - 160 * GUI.Scale), (int)(GameMain.GraphicsHeight - 280 * GUI.Scale)), 
                                new Point((int)(160 * GUI.Scale), (int)(30 * GUI.Scale))), 
                            "Perform CPR", "");
                        cprButton.Font = GUI.Scale < 0.8f ? GUI.SmallFont : GUI.Font;

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
                    
                    if (cprButton.Visible) cprButton.Draw(spriteBatch);

                    character.Inventory.Alignment = Alignment.Left;
                    if (character.SelectedCharacter.CanInventoryBeAccessed)
                    {
                        character.SelectedCharacter.Inventory.Alignment = Alignment.Right;
                        character.SelectedCharacter.Inventory.DrawOwn(spriteBatch);
                    }
                    character.SelectedCharacter.CharacterHealth.Alignment = Alignment.Right;
                    character.SelectedCharacter.CharacterHealth.DrawStatusHUD(spriteBatch, new Vector2(320.0f + 120, 0.0f));
                }
                else if (character.Inventory != null)
                {
                    character.Inventory.Alignment = Alignment.Center;
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
                    focusedItem = character.FocusedItem;
                }

                if (focusedItem != null && focusedItemOverlayTimer > ItemOverlayDelay)
                {
                    var hudTexts = focusedItem.GetHUDTexts(character);

                    int dir = Math.Sign(focusedItem.WorldPosition.X - character.WorldPosition.X);
                    Vector2 startPos = cam.WorldToScreen(focusedItem.DrawPosition);
                    startPos.Y -= (hudTexts.Count + 1) * 20;
                    if (focusedItem.Sprite != null)
                    {
                        startPos.X += (int)Math.Sqrt(focusedItem.Sprite.size.X / 2) * dir;
                        startPos.Y -= (int)Math.Sqrt(focusedItem.Sprite.size.Y / 2);
                    }

                    Vector2 textPos = startPos;
                    if (dir == -1) textPos.X -= (int)GUI.Font.MeasureString(focusedItem.Name).X;

                    float alpha = MathHelper.Clamp((focusedItemOverlayTimer - ItemOverlayDelay) * 2.0f, 0.0f, 1.0f);

                    GUI.DrawString(spriteBatch, textPos, focusedItem.Name, Color.White * alpha, Color.Black * alpha * 0.7f, 2);
                    textPos.Y += 20.0f;
                    foreach (ColoredText coloredText in hudTexts)
                    {
                        if (dir == -1) textPos.X = (int)(startPos.X - GUI.SmallFont.MeasureString(coloredText.Text).X);
                        GUI.DrawString(spriteBatch, textPos, coloredText.Text, coloredText.Color * alpha, Color.Black * alpha * 0.7f, 2, GUI.SmallFont);
                        textPos.Y += 20;
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
    }
}
