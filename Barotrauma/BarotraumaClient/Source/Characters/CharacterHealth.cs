using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        private static Sprite noiseOverlay, damageOverlay;
        
        private static Sprite statusIconOxygen;
        private static Sprite statusIconPressure;
        private static Sprite statusIconBloodloss;

        private GUIButton suicideButton;

        private GUIProgressBar healthBar;

        private float damageOverlayTimer;

        private GUIFrame healthWindow;
        private GUIProgressBar healthWindowHealthBar;
        private GUIFrame limbIndicatorContainer;
        private GUIListBox afflictionContainer;
        private GUIListBox healItemContainer;

        private int highlightedLimbIndex = -1;
        private int selectedLimbIndex = -1;
        
        private static CharacterHealth openHealthWindow;
        public static CharacterHealth OpenHealthWindow
        {
            get { return openHealthWindow; }
            set
            {
                if (openHealthWindow == value) return;
                openHealthWindow = value;
                if (openHealthWindow != null)
                {
                    openHealthWindow.UpdateAfflictionContainer(null);
                    openHealthWindow.UpdateItemContainer();
                }
            }
        }

        static CharacterHealth()
        {
            noiseOverlay = new Sprite("Content/UI/noise.png", Vector2.Zero);            
            damageOverlay = new Sprite("Content/UI/damageOverlay.png", Vector2.Zero);
            
            statusIconOxygen = new Sprite("Content/UI/Health/statusIcons.png", new Rectangle(96, 48, 48, 48), null);
            statusIconPressure = new Sprite("Content/UI/Health/statusIcons.png", new Rectangle(0, 48, 48, 48), null);
            statusIconBloodloss = new Sprite("Content/UI/Health/statusIcons.png", new Rectangle(48, 0, 48, 48), null);
        }

        partial void InitProjSpecific(XElement element, Character character)
        {
            healthBar = new GUIProgressBar(new Rectangle(5, GameMain.GraphicsHeight - 138, 20, 128), Color.White, null, 1.0f, Alignment.TopLeft);
            healthBar.IsHorizontal = false;

            healthWindow = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Black * 0.5f, null);
            GUIFrame healthFrame = new GUIFrame(new Rectangle(0, 0, (int)MathHelper.Clamp(GameMain.GraphicsWidth * 0.7f, 500, 800), (int)MathHelper.Clamp(GameMain.GraphicsWidth * 0.7f, 400, 600)),
                null, Alignment.Center, "", healthWindow);
            healthFrame.Color *= 0.8f;
            limbIndicatorContainer = new GUIFrame(new Rectangle(20, 0, 240, 0), Color.Black * 0.5f, "", healthFrame);
            healthWindowHealthBar = new GUIProgressBar(new Rectangle(0, 0, 30, 0), Color.Green, 1.0f, healthFrame);
            healthWindowHealthBar.IsHorizontal = false;

            int listBoxWidth = (int)(healthFrame.Rect.Width - limbIndicatorContainer.Rect.Width - healthFrame.Padding.X - healthFrame.Padding.Z) / 2;
            afflictionContainer = new GUIListBox(new Rectangle(limbIndicatorContainer.Rect.Right - healthFrame.Rect.X - (int)healthFrame.Padding.X, 30, listBoxWidth, 0),
                "", healthFrame);
            new GUITextBlock(new Rectangle(limbIndicatorContainer.Rect.Right - healthFrame.Rect.X - (int)healthFrame.Padding.X, 0, 20, 20), "Afflictions", "", healthFrame);

            healItemContainer = new GUIListBox(new Rectangle(afflictionContainer.Rect.Right - healthFrame.Rect.X - (int)healthFrame.Padding.X, 30, listBoxWidth, 0),
                "", healthFrame);
            new GUITextBlock(new Rectangle(afflictionContainer.Rect.Right - healthFrame.Rect.X - (int)healthFrame.Padding.X, 0, 20, 20), "Items", "", healthFrame);

            healItemContainer.OnSelected += (GUIComponent component, object userdata) =>
            {
                Item item = userdata as Item;
                if (item == null) return false;
#if CLIENT
                if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect });
                    return true;
                }
#endif

                if (GameMain.Server != null)
                {
                    GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, character.ID });
                }

                item.ApplyStatusEffects(ActionType.OnUse, 1.0f, character, character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == selectedLimbIndex));
                UpdateItemContainer();
                return true;
            };

            suicideButton = new GUIButton(
                        new Rectangle(new Point(GameMain.GraphicsWidth / 2 - 60, 20), new Point(120, 20)), TextManager.Get("GiveInButton"), "");
            suicideButton.ToolTip = TextManager.Get(GameMain.NetworkMember == null ? "GiveInHelpSingleplayer" : "GiveInHelpMultiplayer");
            suicideButton.OnClicked = (button, userData) =>
            {
                GUIComponent.ForceMouseOn(null);
                if (Character.Controlled != null)
                {
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.CreateEntityEvent(Character.Controlled, new object[] { NetEntityEvent.Type.Status });
                    }
                    else
                    {
                        Character.Controlled.Kill(GetCauseOfDeath());
                        Character.Controlled = null;
                    }
                }
                return true;
            };
        }

        partial void UpdateOxygenProjSpecific(float prevOxygen)
        {
            if (prevOxygen > 0.0f && OxygenAmount <= 0.0f && Character.Controlled == character)
            {
                SoundPlayer.PlaySound("drown");
            }
        }


        public void UpdateHUD(float deltaTime)
        {
            if (damageOverlayTimer > 0.0f) damageOverlayTimer -= deltaTime;

            if (PlayerInput.KeyHit(Keys.H))
            {
                OpenHealthWindow = null;
                UpdateItemContainer();
            }
            
            if (character.IsDead)
            {
                healthBar.Color = Color.Black;
                healthBar.BarSize = 1.0f;
            }
            else
            {
                healthBar.Color = (vitality > 0.0f) ? Color.Lerp(Color.Orange, Color.Green, vitality / MaxVitality) : healthBar.Color = Color.Red;
                healthBar.BarSize = (vitality > 0.0f) ? vitality / MaxVitality : 1.0f - vitality / minVitality;
            }
            
            healthBar.Update(deltaTime);
            if (OpenHealthWindow == this)
            {
                UpdateLimbIndicators(limbIndicatorContainer.Rect);
                UpdateAfflictionContainer(selectedLimbIndex < 0 ? null : limbHealths[selectedLimbIndex]);
                healItemContainer.Enabled = selectedLimbIndex > -1;
                healthWindowHealthBar.Color = healthBar.Color;
                healthWindowHealthBar.BarSize = healthBar.BarSize;
                healthWindow.Update(deltaTime);
            }
            else
            {
                highlightedLimbIndex = -1;
            }
            
            if (character == Character.Controlled && character.IsUnconscious && !character.IsDead)
            {
                suicideButton.Visible = true;
                suicideButton.Update(deltaTime);
            }
            else if (suicideButton != null)
            {
                suicideButton.Visible = false;
            }
        }
        
        public void AddToGUIUpdateList()
        {
            if (OpenHealthWindow == this) healthWindow.AddToGUIUpdateList();
            if (suicideButton.Visible && character == Character.Controlled) suicideButton.AddToGUIUpdateList();
        }

        public void DrawHUD(SpriteBatch spriteBatch, Vector2 drawOffset)
        {
            float noiseAlpha = character.IsUnconscious ? 1.0f : MathHelper.Clamp(1.0f - OxygenAmount / 100.0f, 0.0f, 0.8f);

            if (noiseAlpha > 0.0f)
            {
                Vector2 noiseOffset = Rand.Vector(noiseOverlay.size.X);
                noiseOffset.X = Math.Abs(noiseOffset.X);
                noiseOffset.Y = Math.Abs(noiseOffset.Y);
                noiseOverlay.DrawTiled(spriteBatch, Vector2.Zero - noiseOffset, 
                    new Vector2(GameMain.GraphicsWidth, GameMain.GraphicsHeight) + noiseOffset,
                    Vector2.Zero, Color.White * noiseAlpha);
            }

            if (damageOverlayTimer > 0.0f)
            {
                damageOverlay.Draw(spriteBatch, Vector2.Zero, Color.White * damageOverlayTimer, Vector2.Zero, 0.0f,
                    new Vector2(GameMain.GraphicsWidth / damageOverlay.size.X, GameMain.GraphicsHeight / damageOverlay.size.Y));
            }

            DrawStatusHUD(spriteBatch, drawOffset);

            if (suicideButton.Visible) suicideButton.Draw(spriteBatch);
        }

        public void DrawStatusHUD(SpriteBatch spriteBatch, Vector2 drawOffset)
        {
            healthBar.Rect = new Rectangle(5 + (int)drawOffset.X, GameMain.GraphicsHeight - 138 + (int)drawOffset.Y, 20, 128);

            Rectangle limbArea = new Rectangle(30 + (int)drawOffset.X, GameMain.GraphicsHeight - 135 + (int)drawOffset.Y, 70, 128);
            DrawLimbIndicators(spriteBatch, limbArea, false, limbArea.Contains(PlayerInput.MousePosition) && OpenHealthWindow != this);
            if (limbArea.Contains(PlayerInput.MousePosition) && PlayerInput.LeftButtonClicked())
            {
                OpenHealthWindow = this;
                UpdateItemContainer();
            }

            List<Pair<Sprite, string>> statusIcons = new List<Pair<Sprite, string>>();
            if (character.CurrentHull == null || character.CurrentHull.LethalPressure > 5.0f) statusIcons.Add(new Pair<Sprite, string>(statusIconPressure, "High pressure"));

            var allAfflictions = GetAllAfflictions(true);
            foreach (Affliction affliction in allAfflictions)
            {
                if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                statusIcons.Add(new Pair<Sprite, string>(affliction.Prefab.Icon, affliction.Prefab.Description));
            }

            Vector2 pos = healthBar.Rect.Location.ToVector2() + new Vector2(0.0f, -55);
            foreach (Pair<Sprite, string> statusIcon in statusIcons)
            {
                if (statusIcon.First != null) statusIcon.First.Draw(spriteBatch, pos);
                GUI.DrawString(spriteBatch, pos + new Vector2(55, 10), statusIcon.Second, Color.White * 0.8f, Color.Black * 0.5f);
                pos.Y -= 50.0f;
            }

            healthBar.Draw(spriteBatch);

            if (OpenHealthWindow == this)
            {
                healthWindow.Draw(spriteBatch);
                DrawLimbIndicators(spriteBatch, limbIndicatorContainer.Rect, true, false);
            }
        }

        private void UpdateAfflictionContainer(LimbHealth selectedLimb)
        {
            if (selectedLimb == null)
            {
                afflictionContainer.ClearChildren();
                return;
            }

            List<Affliction> limbAfflictions = new List<Affliction>(selectedLimb.Afflictions);
            limbAfflictions.AddRange(afflictions.FindAll(a =>
                limbHealths[character.AnimController.GetLimb(a.Prefab.IndicatorLimb).HealthIndex] == selectedLimb));

            List<GUIComponent> currentChildren = new List<GUIComponent>();
            foreach (Affliction affliction in limbAfflictions)
            {
                if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                var child = afflictionContainer.FindChild(affliction);
                if (child == null)
                {
                    child = new GUIFrame(new Rectangle(0, 0, afflictionContainer.Rect.Width, 50), "ListBoxElement", afflictionContainer);
                    child.Padding = Vector4.Zero;
                    child.UserData = affliction;
                    currentChildren.Add(child);

                    new GUIImage(new Rectangle(0, 0, 0, 0), affliction.Prefab.Icon, Alignment.CenterLeft, child);
                    new GUITextBlock(new Rectangle(50, 0, 0, 0), affliction.Prefab.Description, "", child);
                    new GUITextBlock(new Rectangle(50, 20, 0, 0), (int)Math.Ceiling(affliction.Strength) +" %", "", child).UserData = "percentage";
                }
                else
                {
                    var percentageText = child.GetChild("percentage") as GUITextBlock;
                    percentageText.Text = (int)Math.Ceiling(affliction.Strength) + " %";
                    percentageText.TextColor = Color.Lerp(Color.Orange, Color.Red, affliction.Strength / 100.0f);

                    currentChildren.Add(child);
                }
            }

            for (int i = afflictionContainer.CountChildren - 1; i>= 0; i--)
            {
                if (!currentChildren.Contains(afflictionContainer.children[i]))
                        afflictionContainer.RemoveChild(afflictionContainer.children[i]);
            }

            afflictionContainer.children.Sort((c1, c2) =>
            {
                Affliction affliction1 = c1.UserData as Affliction;
                Affliction affliction2 = c2.UserData as Affliction;
                return (int)(affliction1.Strength - affliction2.Strength);
            });
        }

        private void UpdateItemContainer()
        {
            healItemContainer.ClearChildren();

            List<Item> items = character.Inventory.Items.ToList();
            if (character.SelectedCharacter != null) items.AddRange(character.SelectedCharacter.Inventory.Items);
            if (character.SelectedBy != null) items.AddRange(character.SelectedBy.Inventory.Items);

            foreach (Item item in items)
            {
                if (item == null) continue;
                if (!item.HasTag("medical") && !item.HasTag("chem")) continue;

                var child = new GUIFrame(new Rectangle(0, 0, healItemContainer.Rect.Width, 50), "ListBoxElement", healItemContainer);
                child.Padding = new Vector4(10.0f, 0.0f, 0.0f, 0.0f);
                child.UserData = item;
                child.ToolTip = item.Description;

                new GUIImage(new Rectangle(0, 0, 0, 0), item.Sprite, Alignment.CenterLeft, child).Color = item.SpriteColor;

                string itemName = item.Name;
                if (item.ContainedItems != null && item.ContainedItems.Length > 0)
                {
                    itemName += " (" + item.ContainedItems[0].Name + ")";
                }
                new GUITextBlock(new Rectangle(50, 0, 0, 0), itemName, "", child);
            }
        }

        private void UpdateLimbIndicators(Rectangle drawArea)
        {
            highlightedLimbIndex = -1;
            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;
                
                float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);
                
                Rectangle highlightArea = new Rectangle(
                    (int)(drawArea.Center.X - (limbHealth.IndicatorSprite.Texture.Width / 2 - limbHealth.HighlightArea.X) * scale),
                    (int)(drawArea.Center.Y - (limbHealth.IndicatorSprite.Texture.Height / 2 - limbHealth.HighlightArea.Y) * scale),
                    (int)(limbHealth.HighlightArea.Width * scale),
                    (int)(limbHealth.HighlightArea.Height * scale));

                if (highlightArea.Contains(PlayerInput.MousePosition))
                {
                    highlightedLimbIndex = i;
                }
                i++;
            }

            if (PlayerInput.LeftButtonClicked() && highlightedLimbIndex > -1)
            {
                selectedLimbIndex = highlightedLimbIndex;
            }
        }

        private void DrawLimbIndicators(SpriteBatch spriteBatch, Rectangle drawArea, bool allowHighlight, bool highlightAll)
        {
            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;

                float damageLerp = limbHealth.TotalDamage > 0.0f ? MathHelper.Lerp(0.2f, 1.0f, limbHealth.TotalDamage / 100.0f) : 0.0f;

                Color color = damageLerp < 0.5f ?
                    Color.Lerp(Color.Green, Color.Orange, damageLerp * 2.0f) : Color.Lerp(Color.Orange, Color.Red, (damageLerp - 0.5f) * 2.0f);

                float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);

                if (((i == highlightedLimbIndex || i == selectedLimbIndex) && allowHighlight) || highlightAll)
                {
                    color = Color.Lerp(color, Color.White, 0.5f);
                }

                limbHealth.IndicatorSprite.Draw(spriteBatch,
                    drawArea.Center.ToVector2(), color,
                    limbHealth.IndicatorSprite.Origin,
                    0, scale);
                i++;
            }

            i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);

                Rectangle highlightArea = new Rectangle(
                    (int)(drawArea.Center.X - (limbHealth.IndicatorSprite.Texture.Width / 2 - limbHealth.HighlightArea.X) * scale),
                    (int)(drawArea.Center.Y - (limbHealth.IndicatorSprite.Texture.Height / 2 - limbHealth.HighlightArea.Y) * scale),
                    (int)(limbHealth.HighlightArea.Width * scale),
                    (int)(limbHealth.HighlightArea.Height * scale));

                float iconScale = 0.4f * scale;
                Vector2 iconPos = highlightArea.Center.ToVector2() - new Vector2(24.0f, 24.0f) * iconScale;
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                    affliction.Prefab.Icon.Draw(spriteBatch, iconPos, 0, iconScale);
                    iconPos += new Vector2(10.0f, 10.0f) * iconScale;
                    iconScale *= 0.9f;
                }

                foreach (Affliction affliction in afflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                    if (character.AnimController.GetLimb(affliction.Prefab.IndicatorLimb).HealthIndex == i)
                    {
                        affliction.Prefab.Icon.Draw(spriteBatch, iconPos, 0, iconScale);
                        iconPos += new Vector2(10.0f, 10.0f) * iconScale;
                        iconScale *= 0.9f;
                    }
                }
                i++;
            }
        }

        partial void RemoveProjSpecific()
        {
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite != null)
                {
                    limbHealth.IndicatorSprite.Remove();
                    limbHealth.IndicatorSprite = null;
                }
            }
        }
    }
}
