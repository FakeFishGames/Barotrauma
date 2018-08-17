using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class CharacterHUD
    {
        private static Dictionary<Entity, int> orderIndicatorCount = new Dictionary<Entity, int>();
        const float ItemOverlayDelay = 1.0f;
        private static Item focusedItem;
        private static float focusedItemOverlayTimer;

        private static List<Item> brokenItems = new List<Item>();
        private static float brokenItemsCheckTimer;

        public static void AddToGUIUpdateList(Character character)
        {
            if (GUI.DisableHUD) return;

            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
                if (character.Inventory != null)
                {
                    for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                    {
                        var item = character.Inventory.Items[i];
                        if (item == null || character.Inventory.SlotTypes[i] == InvSlotType.Any) continue;

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
            if (!character.IsUnconscious && character.Stun <= 0.0f)
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
                        if (item == null || character.Inventory.SlotTypes[i] == InvSlotType.Any) continue;

                        foreach (ItemComponent ic in item.components)
                        {
                            if (ic.DrawHudWhenEquipped) ic.UpdateHUD(character, deltaTime);
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

            if (brokenItemsCheckTimer > 0.0f)
            {
                brokenItemsCheckTimer -= deltaTime;
            }
            else
            {
                brokenItems.Clear();
                brokenItemsCheckTimer = 1.0f;
                foreach (Item item in Item.ItemList)
                {
                    if (item.Condition > 0.0f || item.FixRequirements.Count == 0 || item.CurrentHull == null) continue;
                    if (item.CurrentHull == character.CurrentHull)
                    {
                        brokenItems.Add(item);
                    }
                }
            }
        }
        
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
            }

            foreach (Optimizable optimizable in Optimizable.CurrentlyOptimizable)
            {
                if (optimizable.DegreeOfSuccess(character) < 0.5f) continue;

                float distSqr = Vector2.DistanceSquared(character.WorldPosition, optimizable.Item.WorldPosition);
                if (distSqr < 1000.0f * 1000.0f)
                {
                    float dist = (float)Math.Sqrt(distSqr);
                    Vector2 drawPos = optimizable.Item.DrawPosition;
                    //TODO: proper icon
                    float alpha = (1000.0f - dist) / 1000.0f * 2.0f;

                    GUI.DrawIndicator(spriteBatch, drawPos, cam, 100.0f, GUI.SubmarineIcon, Color.Yellow * alpha);
                }
            }

            foreach (Item brokenItem in brokenItems)
            {
                float dist = Vector2.Distance(character.WorldPosition, brokenItem.WorldPosition);
                Vector2 drawPos = brokenItem.DrawPosition;
                //TODO: proper icon
                float alpha = Math.Min((1000.0f - dist) / 1000.0f * 2.0f, 1.0f);
                if (alpha <= 0.0f) continue;
                GUI.DrawIndicator(spriteBatch, drawPos, cam, 100.0f, GUI.SubmarineIcon, Color.OrangeRed * alpha);                
            }

            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
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

                    GUI.DrawString(spriteBatch, textPos, focusName, Color.White, Color.Black * 0.7f, 2);
                    textPos.Y += 20;
                    if (character.FocusedCharacter.CanInventoryBeAccessed)
                    {
                        GUI.DrawString(spriteBatch, textPos, TextManager.Get("GrabHint").Replace("[key]", GameMain.Config.KeyBind(InputType.Select).ToString()), 
                            Color.LightGreen, Color.Black, 2, GUI.SmallFont);
                        textPos.Y += 15;
                    }
                    if (character.FocusedCharacter.CharacterHealth.UseHealthWindow)
                    {
                        GUI.DrawString(spriteBatch, textPos, TextManager.Get("HealHint").Replace("[key]", GameMain.Config.KeyBind(InputType.Health).ToString()), Color.LightGreen, Color.Black, 2, GUI.SmallFont);
                        textPos.Y += 15;
                    }
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

            if (character.SelectedConstruction != null && 
                (character.CanInteractWith(Character.Controlled.SelectedConstruction) || Screen.Selected == GameMain.SubEditorScreen))
            {
                character.SelectedConstruction.DrawHUD(spriteBatch, cam, Character.Controlled);
            }

            if (character.Inventory != null)
            {
                for (int i = 0; i < character.Inventory.Items.Length - 1; i++)
                {
                    var item = character.Inventory.Items[i];
                    if (item == null || character.Inventory.SlotTypes[i] == InvSlotType.Any) continue;

                    foreach (ItemComponent ic in item.components)
                    {
                        if (ic.DrawHudWhenEquipped) ic.DrawHUD(spriteBatch, character);
                    }
                }
            }
            
            if (character.Inventory != null && !character.LockHands && character.Stun <= 0.1f)
            {
                character.Inventory.DrawOwn(spriteBatch);
            }
            
            if (!character.IsUnconscious && character.Stun <= 0.0f)
            {
                if (character.IsHumanoid && character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    if (character.SelectedCharacter.CanInventoryBeAccessed && CharacterHealth.OpenHealthWindow == null)
                    {
                        character.Inventory.Alignment = Alignment.Left;
                        character.SelectedCharacter.Inventory.Alignment = Alignment.Right;
                        character.SelectedCharacter.Inventory.DrawOwn(spriteBatch);
                    }
                    else
                    {
                        character.Inventory.Alignment = (CharacterHealth.OpenHealthWindow == null) ? Alignment.Center : Alignment.Left;
                    }
                    if (CharacterHealth.OpenHealthWindow == character.SelectedCharacter.CharacterHealth)
                    {
                        character.SelectedCharacter.CharacterHealth.Alignment = Alignment.Right;
                        character.SelectedCharacter.CharacterHealth.DrawStatusHUD(spriteBatch);
                    }
                }
                else if (character.Inventory != null)
                {
                    character.Inventory.Alignment = (CharacterHealth.OpenHealthWindow == null) ? Alignment.Center : Alignment.Left;
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
