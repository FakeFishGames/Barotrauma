using Barotrauma.Networking;
using Lidgren.Network;
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

        private Alignment alignment = Alignment.Left;

        public Alignment Alignment
        {
            get { return alignment; }
            set
            {
                if (alignment == value) return;
                alignment = value;
                UpdateAlignment();
            }
        }

        private GUIButton suicideButton;

        private GUIProgressBar healthBar;

        private float damageOverlayTimer;

        private GUIListBox afflictionContainer;

        private float bloodParticleTimer;
        
        /*private GUIFrame healthWindow;
        private GUIProgressBar healthWindowHealthBar;
        private GUIFrame limbIndicatorContainer;
        ;*/
        private GUIFrame healItemContainer;

        private GUIFrame healthWindow;

        private int highlightedLimbIndex = -1;
        private int selectedLimbIndex = -1;

        private float distortTimer;

        public float DamageOverlayTimer
        {
            get { return damageOverlayTimer; }
        }
        
        private static CharacterHealth openHealthWindow;
        public static CharacterHealth OpenHealthWindow
        {
            get
            {
                return openHealthWindow;
            }
            set
            {
                if (openHealthWindow == value) return;
                openHealthWindow = value;
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

        partial void InitProjSpecific(Character character)
        {
            healthBar = new GUIProgressBar(HUDLayoutSettings.HealthBarAreaLeft, Color.White, null, 1.0f, Alignment.TopLeft);
            healthBar.IsHorizontal = false;

            afflictionContainer = new GUIListBox(new Rectangle(0, 0, 100, 200), "");
            healthWindow = new GUIFrame(new Rectangle(0, 0, 100, 200), "");
            healItemContainer = new GUIFrame(new Rectangle(0, 0, 100, 200), "InnerFrame");
            healItemContainer.Padding = Vector4.One * 10 * GUI.Scale;
            
            UpdateAlignment();
            
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

        private void UpdateAlignment()
        {
            if (alignment == Alignment.Left)
            {
                healthBar.Rect = HUDLayoutSettings.HealthBarAreaLeft;
                healthWindow.Rect = new Rectangle(
                    HUDLayoutSettings.HealthWindowAreaLeft.X, HUDLayoutSettings.HealthWindowAreaLeft.Y, 
                    HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, HUDLayoutSettings.HealthWindowAreaLeft.Height);

                int afflictionContainerHeight = (int)(HUDLayoutSettings.HealthWindowAreaLeft.Height * 0.66f);
                afflictionContainer.Rect = new Rectangle(
                    HUDLayoutSettings.HealthWindowAreaLeft.Center.X, HUDLayoutSettings.HealthWindowAreaLeft.Bottom - afflictionContainerHeight,
                    HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, afflictionContainerHeight);

                healItemContainer.Rect = new Rectangle(
                    HUDLayoutSettings.HealthWindowAreaLeft.Center.X, HUDLayoutSettings.HealthWindowAreaLeft.Y,
                    HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, HUDLayoutSettings.HealthWindowAreaLeft.Height - afflictionContainerHeight);
            }
            else
            {
                healthBar.Rect = HUDLayoutSettings.HealthBarAreaRight;
                healthWindow.Rect = new Rectangle(
                    HUDLayoutSettings.HealthWindowAreaRight.Center.X, HUDLayoutSettings.HealthWindowAreaRight.Y,
                    HUDLayoutSettings.HealthWindowAreaRight.Width / 2, HUDLayoutSettings.HealthWindowAreaRight.Height);

                int afflictionContainerHeight = (int)(HUDLayoutSettings.HealthWindowAreaLeft.Height * 0.66f);
                afflictionContainer.Rect = new Rectangle(
                    HUDLayoutSettings.HealthWindowAreaRight.X, HUDLayoutSettings.HealthWindowAreaLeft.Bottom - afflictionContainerHeight,
                    HUDLayoutSettings.HealthWindowAreaRight.Width / 2, afflictionContainerHeight);

                healItemContainer.Rect = new Rectangle(
                    HUDLayoutSettings.HealthWindowAreaRight.X, HUDLayoutSettings.HealthWindowAreaRight.Y,
                    HUDLayoutSettings.HealthWindowAreaRight.Width / 2, HUDLayoutSettings.HealthWindowAreaRight.Height - afflictionContainerHeight);
            }
        }

        partial void UpdateOxygenProjSpecific(float prevOxygen)
        {
            if (prevOxygen > 0.0f && OxygenAmount <= 0.0f && Character.Controlled == character)
            {
                SoundPlayer.PlaySound("drown");
            }
        }

        partial void UpdateBleedingProjSpecific(AfflictionBleeding affliction, Limb targetLimb, float deltaTime)
        {
            bloodParticleTimer -= deltaTime * (affliction.Strength / 10.0f);
            if (bloodParticleTimer <= 0.0f)
            {
                float bloodParticleSize = MathHelper.Lerp(0.5f, 1.0f, affliction.Strength / 100.0f);
                if (!character.AnimController.InWater) bloodParticleSize *= 2.0f;
                var blood = GameMain.ParticleManager.CreateParticle(
                    character.AnimController.InWater ? "waterblood" : "blooddrop",
                    targetLimb.WorldPosition, Rand.Vector(affliction.Strength), 0.0f, character.AnimController.CurrentHull);

                if (blood != null)
                {
                    blood.Size *= bloodParticleSize;
                }
                bloodParticleTimer = 1.0f;
            }
        }

        public void UpdateHUD(float deltaTime)
        {
            if (openHealthWindow != null)
            {
                if (openHealthWindow != Character.Controlled?.CharacterHealth && openHealthWindow != Character.Controlled?.SelectedCharacter?.CharacterHealth)
                {
                    openHealthWindow = null;
                    return;
                }
            }

            if (damageOverlayTimer > 0.0f) damageOverlayTimer -= deltaTime;
            
            float blurStrength = 0.0f;
            float distortStrength = 0.0f;
            float distortSpeed = 0.0f;
            
            if (character.IsUnconscious)
            {
                blurStrength = 1.0f;
                distortSpeed = 1.0f;
            }
            else if (OxygenAmount < 100.0f)
            {
                blurStrength = MathHelper.Lerp(0.5f, 1.0f, 1.0f - vitality / MaxVitality);
                distortStrength = blurStrength;
                distortSpeed = (blurStrength + 1.0f);
                distortSpeed *= distortSpeed * distortSpeed * distortSpeed;
            }

            foreach (Affliction affliction in afflictions)
            {
                distortStrength = Math.Max(distortStrength, affliction.GetScreenDistortStrength());
                blurStrength = Math.Max(blurStrength, affliction.GetScreenBlurStrength());
            }
            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    distortStrength = Math.Max(distortStrength, affliction.GetScreenDistortStrength());
                    blurStrength = Math.Max(blurStrength, affliction.GetScreenBlurStrength());
                }
            }

            if (blurStrength > 0.0f)
            {
                distortTimer = (distortTimer + deltaTime * distortSpeed) % MathHelper.TwoPi;
                character.BlurStrength = (float)(Math.Sin(distortTimer) + 1.5f) * 0.25f * blurStrength;
                character.DistortStrength = (float)(Math.Sin(distortTimer) + 1.0f) * 0.1f * distortStrength;
            }
            else
            {
                character.BlurStrength = 0.0f;
                character.DistortStrength = 0.0f;
                distortTimer = 0.0f;
            }

            if (PlayerInput.KeyHit(Keys.H))
            {
                OpenHealthWindow = openHealthWindow == this ? null : this;
            }
            
            if (character.IsDead)
            {
                healthBar.Color = Color.Black;
                healthBar.BarSize = 1.0f;
            }
            else
            {
                healthBar.Color = Color.Red;
                if (vitality / MaxVitality > 0.5f)
                {
                    healthBar.Color = Color.Lerp(Color.Orange, Color.Green * 1.5f, (vitality / MaxVitality - 0.5f) * 2.0f);
                }
                else if (vitality > 0.0f)
                {
                    healthBar.Color = Color.Lerp(Color.Red, Color.Orange, (vitality / MaxVitality) * 2.0f);
                }

                healthBar.HoverColor = healthBar.Color * 2.0f;
                healthBar.BarSize = (vitality > 0.0f) ? vitality / MaxVitality : 1.0f - vitality / minVitality;
            }
            
            healthBar.Update(deltaTime);
            if (OpenHealthWindow == this)
            {
                if (character == Character.Controlled && !character.AllowInput)
                {
                    openHealthWindow = null;
                }

                Rectangle limbArea = healthWindow.Rect;
                limbArea.Inflate(-20, -20);
                UpdateLimbIndicators(limbArea);
                UpdateAfflictionContainer(highlightedLimbIndex < 0 ? (selectedLimbIndex < 0 ? null : limbHealths[selectedLimbIndex]) : limbHealths[highlightedLimbIndex]);
                afflictionContainer.Update(deltaTime);
                healItemContainer.Update(deltaTime);
                UpdateItemContainer();
            }
            else
            {
                if (openHealthWindow != null && character != Character.Controlled && character != Character.Controlled?.SelectedCharacter)
                {
                    openHealthWindow = null;
                }
                highlightedLimbIndex = -1;
            }


            if ((alignment == Alignment.Left &&
                HUDLayoutSettings.AfflictionAreaLeft.Contains(PlayerInput.MousePosition) || 
                HUDLayoutSettings.HealthBarAreaLeft.Contains(PlayerInput.MousePosition)) 
                ||
                (alignment == Alignment.Right &&
                HUDLayoutSettings.AfflictionAreaRight.Contains(PlayerInput.MousePosition) ||
                HUDLayoutSettings.HealthBarAreaRight.Contains(PlayerInput.MousePosition)))
            {
                healthBar.State = GUIComponent.ComponentState.Hover;
                System.Diagnostics.Debug.WriteLine("hover");
                if (PlayerInput.LeftButtonClicked())
                {
                    OpenHealthWindow = openHealthWindow == this ? null : this;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("------");
                healthBar.State = GUIComponent.ComponentState.None;
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
            if (OpenHealthWindow == this)
            {
                afflictionContainer.AddToGUIUpdateList();
                healItemContainer.AddToGUIUpdateList();
            }
            if (suicideButton.Visible && character == Character.Controlled) suicideButton.AddToGUIUpdateList();
        }

        public void DrawHUD(SpriteBatch spriteBatch, Vector2 drawOffset)
        {
            float damageOverlayAlpha = DamageOverlayTimer;
            if (vitality < MaxVitality * 0.1f)
            {
                damageOverlayAlpha = Math.Max(1.0f - (vitality / maxVitality * 10.0f), damageOverlayAlpha);
            }

            if (damageOverlayAlpha > 0.0f)
            {
                damageOverlay.Draw(spriteBatch, Vector2.Zero, Color.White * damageOverlayAlpha, Vector2.Zero, 0.0f,
                    new Vector2(GameMain.GraphicsWidth / damageOverlay.size.X, GameMain.GraphicsHeight / damageOverlay.size.Y));
            }

            DrawStatusHUD(spriteBatch, drawOffset);

            if (suicideButton.Visible) suicideButton.Draw(spriteBatch);
        }

        public void DrawStatusHUD(SpriteBatch spriteBatch, Vector2 drawOffset)
        {
            Rectangle interactArea = healthBar.Rect;
            if (openHealthWindow == null)
            {
                List<Pair<Sprite, string>> statusIcons = new List<Pair<Sprite, string>>();
                if (character.CurrentHull == null || character.CurrentHull.LethalPressure > 5.0f) statusIcons.Add(new Pair<Sprite, string>(statusIconPressure, "High pressure"));

                var allAfflictions = GetAllAfflictions(true);
                foreach (Affliction affliction in allAfflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ShowIconThreshold || affliction.Prefab.Icon == null) continue;
                    statusIcons.Add(new Pair<Sprite, string>(affliction.Prefab.Icon, affliction.Prefab.Name));
                }

                Pair<Sprite, string> highlightedIcon = null;
                Vector2 highlightedIconPos = Vector2.Zero;
                Rectangle afflictionArea =  alignment == Alignment.Left ? HUDLayoutSettings.AfflictionAreaLeft : HUDLayoutSettings.AfflictionAreaRight;
                Point pos = afflictionArea.Location;

                foreach (Pair<Sprite, string> statusIcon in statusIcons)
                {
                    Rectangle afflictionIconRect = new Rectangle(pos, new Point(afflictionArea.Width, afflictionArea.Width));
                    interactArea = Rectangle.Union(interactArea, afflictionIconRect);
                    if (afflictionIconRect.Contains(PlayerInput.MousePosition))
                    {
                        highlightedIcon = statusIcon;
                        highlightedIconPos = afflictionIconRect.Center.ToVector2();
                    }
                    pos.Y += afflictionArea.Width + (int)(5 * GUI.Scale);
                }

                pos = afflictionArea.Location;
                foreach (Pair<Sprite, string> statusIcon in statusIcons)
                {
                    statusIcon.First.Draw(spriteBatch, pos.ToVector2(), highlightedIcon == statusIcon ? Color.White : Color.White * 0.8f, 0, afflictionArea.Width / statusIcon.First.size.X);
                    pos.Y += afflictionArea.Width + (int)(5 * GUI.Scale);
                }

                if (highlightedIcon != null)
                {
                    GUI.DrawString(spriteBatch,
                        alignment == Alignment.Left ? highlightedIconPos + new Vector2(60 * GUI.Scale, 5) : highlightedIconPos + new Vector2(-10.0f - GUI.Font.MeasureString(highlightedIcon.Second).X, 5),
                        highlightedIcon.Second,
                        Color.White * 0.8f, Color.Black * 0.5f);
                }
            }

            healthBar.Draw(spriteBatch);

            if (OpenHealthWindow == this)
            {
                healthWindow.Draw(spriteBatch);
                Rectangle limbArea = healthWindow.Rect;
                limbArea.Inflate(-20, -20);
                DrawLimbIndicators(spriteBatch, limbArea, true, false);

                int previewLimbIndex = highlightedLimbIndex < 0 ? selectedLimbIndex : highlightedLimbIndex;
                if (previewLimbIndex > -1)
                {
                    afflictionContainer.Draw(spriteBatch);
                    GUI.DrawLine(spriteBatch, new Vector2(alignment == Alignment.Left ? afflictionContainer.Rect.X : afflictionContainer.Rect.Right, afflictionContainer.Rect.Center.Y), 
                        GetLimbHighlightArea(limbHealths[previewLimbIndex], healthWindow.Rect).Center.ToVector2(), Color.LightGray, 0, 3);
                }

                healItemContainer.Draw(spriteBatch);

                if (Inventory.draggingItem != null && highlightedLimbIndex > -1)
                {
                    GUI.DrawString(spriteBatch, PlayerInput.MousePosition + Vector2.UnitY * 40.0f, "Use item \"" + Inventory.draggingItem.Name + "\" on [insert limb name here]", Color.Green, Color.Black * 0.8f);
                }
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
                if (affliction.Strength < affliction.Prefab.ShowIconThreshold) continue;
                var child = afflictionContainer.FindChild(affliction);
                if (child == null)
                {
                    child = new GUIFrame(new Rectangle(0, 0, afflictionContainer.Rect.Width, 250), null, afflictionContainer);
                    child.Padding = Vector4.One * 30 * GUI.Scale;
                    child.UserData = affliction;
                    currentChildren.Add(child);

                    new GUIImage(new Rectangle(0, 0, 0, 0), affliction.Prefab.Icon, Alignment.TopLeft, child);
                    new GUITextBlock(new Rectangle((int)(affliction.Prefab.Icon.size.X + 10), 0, 0, 0), affliction.Prefab.Name, "", child);
                    new GUITextBlock(new Rectangle((int)(affliction.Prefab.Icon.size.X + 10), 20, 0, 0), (int)Math.Ceiling(affliction.Strength) +" %", "", child).UserData = "percentage";

                    new GUITextBlock(new Rectangle(0, 60, 0, 0), affliction.Prefab.Description, "", child, true);

                    child.Rect = new Rectangle(child.Rect.X, child.Rect.Y, child.Rect.Width, child.children.Sum(c => c.Rect.Height) + 20);
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
                {
                    afflictionContainer.RemoveChild(afflictionContainer.children[i]);
                }
            }

            afflictionContainer.children.Sort((c1, c2) =>
            {
                Affliction affliction1 = c1.UserData as Affliction;
                Affliction affliction2 = c2.UserData as Affliction;
                return (int)(affliction2.Strength - affliction1.Strength);
            });

            UpdateTreatmentSuitabilityHints();
        }

        public bool OnItemDropped(Item item)
        {
            //items can be dropped outside the health window
            if (!healthWindow.Rect.Contains(PlayerInput.MousePosition) && !afflictionContainer.Rect.Contains(PlayerInput.MousePosition))
            {
                return false;
            }
            
            //can't apply treatment to dead characters
            if (character.IsDead) return true;
            if (highlightedLimbIndex < 0 || item == null) return true;

            Limb targetLimb = character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == selectedLimbIndex);
#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, character.ID, targetLimb });
                return true;
            }
#endif
            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, character.ID, targetLimb });
            }

            item.ApplyStatusEffects(ActionType.OnUse, 1.0f, character, targetLimb);
            return true;
        }

        private List<Item> GetAvailableMedicalItems()
        {
            List<Item> items = new List<Item>();
            //get items in this character's inventory
            foreach (Item item in character.Inventory.Items)
            {
                if (item == null) continue;
                if (!item.HasTag("medical") && !item.HasTag("chem")) continue;
                items.Add(item);
            }

            //get items in the selected character's inventory
            if (character.SelectedCharacter?.Inventory != null && character.CanAccessInventory(character.SelectedCharacter.Inventory))
            {
                foreach (Item item in character.SelectedCharacter.Inventory.Items)
                {
                    if (item == null) continue;
                    if (!item.HasTag("medical") && !item.HasTag("chem")) continue;
                    items.Add(item);
                }
            }

            //get items in the selected character's inventory
            if (character.SelectedBy?.Inventory != null)
            {
                foreach (Item item in character.SelectedBy.Inventory.Items)
                {
                    if (item == null) continue;
                    if (!item.HasTag("medical") && !item.HasTag("chem")) continue;
                    items.Add(item);
                }
            }

            //get items inside items
            for (int i = 0; i < items.Count; i++)
            {
                var containedItems = items[i].ContainedItems;
                if (containedItems == null) continue;
                foreach (Item item in containedItems)
                {
                    if (item == null) continue;
                    if (!item.HasTag("medical") && !item.HasTag("chem")) continue;
                    items.Add(item);
                }
            }
            return items;
        }

        private bool ItemContainerNeedsRefresh(List<Item> availableItems)
        {
            if (availableItems.Count != healItemContainer.CountChildren) return true;
 
            foreach (Item item in availableItems)
            {
                //no button for this item, need to refresh
                if (!healItemContainer.children.Any(c => c.UserData as Item == item))
                {
                    return true;
                }
            }

            foreach (GUIComponent child in healItemContainer.children)
            {
                //there's a button for an item that's not available anymore, need to refresh
                if (!availableItems.Contains(child.UserData as Item)) return true;
            }

            return false;
        }

        private void UpdateItemContainer()
        {
            var items = GetAvailableMedicalItems();
            if (!ItemContainerNeedsRefresh(items)) return;

            healItemContainer.ClearChildren();
            
            int itemButtonSize = (int)(80 * GUI.Scale);
            int padding = (int)(5 * GUI.Scale);

            healItemContainer.Rect = new Rectangle(healItemContainer.Rect.X, healItemContainer.Rect.Y, healItemContainer.Rect.Width, itemButtonSize + (int)healItemContainer.Padding.Y + (int)healItemContainer.Padding.W);

            int x = 0, y = 0;
            foreach (Item item in items)
            {
                if (item == null) continue;
                if (!item.HasTag("medical") && !item.HasTag("chem")) continue;

                var child = new GUIButton(new Rectangle(x, y, itemButtonSize, itemButtonSize), "", "GUIButton", healItemContainer);
                child.Padding = Vector4.Zero;
                child.UserData = item;
                child.OnClicked += OnTreatmentButtonClicked;

                float scale = Math.Min(itemButtonSize / item.Sprite.size.X, itemButtonSize / item.Sprite.size.Y);
                var itemIcon = new GUIImage(new Rectangle(0, 0, 0, 0), item.Sprite.SourceRect, item.Sprite, Alignment.Center, scale, child)
                {
                    CanBeFocused = false,
                    Color = item.SpriteColor,
                    HoverColor = item.SpriteColor,
                    SelectedColor = item.SpriteColor
                };

                string itemName = item.Name;
                if (item.ContainedItems != null && item.ContainedItems.Length > 0)
                {
                    itemName += " (" + item.ContainedItems[0].Name + ")";
                }
                child.ToolTip = itemName+"\n"+item.Description;
                //new GUITextBlock(new Rectangle(50, 0, 0, 0), itemName, "", child);

                x += itemButtonSize + padding;
                if (x > healItemContainer.Rect.Width - itemButtonSize)
                {
                    x = 0;
                    y += itemButtonSize + padding;
                    healItemContainer.SetDimensions(new Point(healItemContainer.Rect.Width, healItemContainer.Rect.Height + itemButtonSize + padding));
                }
            }
        }

        private void UpdateTreatmentSuitabilityHints()
        {
            List<Affliction> selectedAfflictions = new List<Affliction>();
            foreach (GUIComponent child in afflictionContainer.children)
            {
                Affliction affliction = child.UserData as Affliction;
                if (affliction != null) selectedAfflictions.Add(affliction);
            }

            foreach (GUIComponent child in healItemContainer.children)
            {
                Item item = child.UserData as Item;
                float suitability = 0.0f;
                if (selectedAfflictions.Count > 0)
                {
                    foreach (Affliction affliction in selectedAfflictions)
                    {
                        suitability += affliction.Prefab.GetTreatmentSuitability(item);
                    }
                    suitability /= selectedAfflictions.Count;
                }
                child.Color = suitability < 0.0f ? 
                    Color.Lerp(Color.White, Color.Red, -suitability / 100.0f) :
                    Color.Lerp(Color.White, Color.LightGreen, suitability / 100.0f);
            }
        }

        private bool OnTreatmentButtonClicked(GUIButton button, object userdata)
        {
            Item item = userdata as Item;
            if (item == null) return false;

            Limb targetLimb = character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == selectedLimbIndex);
#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, character.ID, targetLimb });
                return true;
            }
#endif
            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, character.ID, targetLimb });
            }

            item.ApplyStatusEffects(ActionType.OnUse, 1.0f, character, targetLimb);
            UpdateItemContainer();
            return true;
        }

        private void UpdateLimbIndicators(Rectangle drawArea)
        {
            highlightedLimbIndex = -1;
            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;
                
                float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);

                Rectangle highlightArea = GetLimbHighlightArea(limbHealth, drawArea);

                if (highlightArea.Contains(PlayerInput.MousePosition))
                {
                    highlightedLimbIndex = i;
                }
                i++;
            }

            if (PlayerInput.LeftButtonClicked() && highlightedLimbIndex > -1)
            {
                selectedLimbIndex = highlightedLimbIndex;
                afflictionContainer.ClearChildren();
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
                if (limbHealth.IndicatorSprite == null) continue;
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
                    if (affliction.Strength < affliction.Prefab.ShowIconThreshold) continue;
                    affliction.Prefab.Icon.Draw(spriteBatch, iconPos, 0, iconScale);
                    iconPos += new Vector2(10.0f, 10.0f) * iconScale;
                    iconScale *= 0.9f;
                }

                foreach (Affliction affliction in afflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ShowIconThreshold) continue;
                    Limb indicatorLimb = character.AnimController.GetLimb(affliction.Prefab.IndicatorLimb);
                    if (indicatorLimb != null && indicatorLimb.HealthIndex == i)
                    {
                        affliction.Prefab.Icon.Draw(spriteBatch, iconPos, 0, iconScale);
                        iconPos += new Vector2(10.0f, 10.0f) * iconScale;
                        iconScale *= 0.9f;
                    }
                }
                i++;
            }
        }

        private Rectangle GetLimbHighlightArea(LimbHealth limbHealth, Rectangle drawArea)
        {
            float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);
            return new Rectangle(
                (int)(drawArea.Center.X - (limbHealth.IndicatorSprite.Texture.Width / 2 - limbHealth.HighlightArea.X) * scale),
                (int)(drawArea.Center.Y - (limbHealth.IndicatorSprite.Texture.Height / 2 - limbHealth.HighlightArea.Y) * scale),
                (int)(limbHealth.HighlightArea.Width * scale),
                (int)(limbHealth.HighlightArea.Height * scale));
        }
        
        public void ClientRead(NetBuffer inc)
        {
            List<Pair<AfflictionPrefab, float>> newAfflictions = new List<Pair<AfflictionPrefab, float>>();

            byte afflictionCount = inc.ReadByte();
            for (int i = 0; i < afflictionCount; i++)
            {
                int afflictionPrefabIndex = inc.ReadRangedInteger(0, AfflictionPrefab.List.Count - 1);
                float afflictionStrength = inc.ReadSingle();

                newAfflictions.Add(new Pair<AfflictionPrefab, float>(AfflictionPrefab.List[afflictionPrefabIndex], afflictionStrength));
            }

            foreach (Affliction affliction in afflictions)
            {
                //deactivate afflictions that weren't included in the network message
                if (!newAfflictions.Any(a => a.First == affliction.Prefab))
                {
                    affliction.Strength = 0.0f;
                }
            }

            foreach (Pair<AfflictionPrefab, float> newAffliction in newAfflictions)
            {
                Affliction existingAffliction = afflictions.Find(a => a.Prefab == newAffliction.First);
                if (existingAffliction == null)
                {
                    afflictions.Add(newAffliction.First.Instantiate(newAffliction.Second));
                }
                else
                {
                    existingAffliction.Strength = newAffliction.Second;
                }
            }

            List<Triplet<LimbHealth, AfflictionPrefab, float>> newLimbAfflictions = new List<Triplet<LimbHealth, AfflictionPrefab, float>>();
            byte limbAfflictionCount = inc.ReadByte();
            for (int i = 0; i < limbAfflictionCount; i++)
            {
                int limbIndex = inc.ReadRangedInteger(0, limbHealths.Count - 1);
                int afflictionPrefabIndex = inc.ReadRangedInteger(0, AfflictionPrefab.List.Count - 1);
                float afflictionStrength = inc.ReadSingle();

                newLimbAfflictions.Add(new Triplet<LimbHealth, AfflictionPrefab, float>(limbHealths[limbIndex], AfflictionPrefab.List[afflictionPrefabIndex], afflictionStrength));
            }

            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    //deactivate afflictions that weren't included in the network message
                    if (!newLimbAfflictions.Any(a => a.First == limbHealth && a.Second == affliction.Prefab))
                    {
                        affliction.Strength = 0.0f;
                    }
                }

                foreach (Triplet<LimbHealth, AfflictionPrefab, float> newAffliction in newLimbAfflictions)
                {
                    if (newAffliction.First != limbHealth) continue;
                    Affliction existingAffliction = limbHealth.Afflictions.Find(a => a.Prefab == newAffliction.Second);
                    if (existingAffliction == null)
                    {
                        limbHealth.Afflictions.Add(newAffliction.Second.Instantiate(newAffliction.Third));
                    }
                    else
                    {
                        existingAffliction.Strength = newAffliction.Third;
                    }
                }
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
