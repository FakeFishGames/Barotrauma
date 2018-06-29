using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        private static Sprite damageOverlay;

        private GUIButton cprButton;

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
        
        private GUIListBox healItemContainer;

        private GUIFrame healthWindow;

        private GUIProgressBar healthWindowHealthBar;

        private GUIComponent draggingMed;

        private int highlightedLimbIndex = -1;
        private int selectedLimbIndex = -1;

        private float distortTimer;

        private bool openedThisFrame;

        private float healthShadowSize;
        private float healthShadowDelay;

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
                if (value != null && !value.UseHealthWindow) return;

                if (value == null && 
                    Character.Controlled.SelectedCharacter?.CharacterHealth == openHealthWindow && 
                    !Character.Controlled.SelectedCharacter.CanInventoryBeAccessed)
                {
                    Character.Controlled.DeselectCharacter();
                }

                openHealthWindow = value;
                if (openHealthWindow != null)
                {
                    OpenHealthWindow.openedThisFrame = true;
                    OpenHealthWindow.healthWindow.GetChild<GUITextBlock>().Text = value.character.Name;
                    Character.Controlled.SelectedConstruction = null;
                }
            }
        }

        static CharacterHealth()
        {        
            damageOverlay = new Sprite("Content/UI/damageOverlay.png", Vector2.Zero);
        }

        partial void InitProjSpecific(Character character)
        {
            character.OnAttacked += OnAttacked;

            healthBar = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: "GUIProgressBarVertical")
            {
                IsHorizontal = false
            };
            healthShadowSize = 1.0f;

            afflictionContainer = new GUIListBox(new RectTransform(new Point(HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, 200), GUI.Canvas));
            healthWindow = new GUIFrame(new RectTransform(new Point(100, 200), GUI.Canvas));
            healItemContainer = new GUIListBox(new RectTransform(new Point(100, 200), GUI.Canvas), isHorizontal: true);
            healItemContainer.Spacing = (int)(5 * GUI.Scale);

            new GUICustomComponent(new RectTransform(new Vector2(0.9f, 0.9f), healthWindow.RectTransform, anchor: Anchor.Center),
                (spriteBatch, component) => 
                {
                    DrawHealthWindow(spriteBatch, component.RectTransform.Rect, true, false);
                },
                (deltaTime, component) => 
                {
                    UpdateLimbIndicators(component.RectTransform.Rect);
                }
            );
            new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.1f), healthWindow.RectTransform, anchor: Anchor.TopCenter), "", textAlignment: Alignment.Center);

            healthWindowHealthBar = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: "GUIProgressBarVertical")
            {
                IsHorizontal = false
            };
            cprButton = new GUIButton(new RectTransform(new Point(80, 80), GUI.Canvas), text: "", style: "CPRButton");
            cprButton.OnClicked = (button, userData) =>
            {
                Character selectedCharacter = Character.Controlled?.SelectedCharacter;
                if (selectedCharacter == null || (!selectedCharacter.IsUnconscious && selectedCharacter.Stun <= 0.0f)) return false;

                Character.Controlled.AnimController.Anim = (Character.Controlled.AnimController.Anim == AnimController.Animation.CPR) ?
                    AnimController.Animation.None : AnimController.Animation.CPR;

                foreach (Limb limb in selectedCharacter.AnimController.Limbs)
                {
                    limb.pullJoint.Enabled = false;
                }

                if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(Character.Controlled, new object[] { NetEntityEvent.Type.Repair });
                }

                return true;
            };

            UpdateAlignment();

            suicideButton = new GUIButton(new RectTransform(new Vector2(0.06f, 0.02f), GUI.Canvas, Anchor.TopCenter)
                { MinSize = new Point(120, 20), RelativeOffset = new Vector2(0.0f, 0.01f) },
                TextManager.Get("GiveInButton"));
            suicideButton.ToolTip = TextManager.Get(GameMain.NetworkMember == null ? "GiveInHelpSingleplayer" : "GiveInHelpMultiplayer");
            suicideButton.OnClicked = (button, userData) =>
            {
                GUI.ForceMouseOn(null);
                if (Character.Controlled != null)
                {
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.CreateEntityEvent(Character.Controlled, new object[] { NetEntityEvent.Type.Status });
                    }
                    else
                    {
                        var causeOfDeath = GetCauseOfDeath();
                        Character.Controlled.Kill(causeOfDeath.First, causeOfDeath.Second);
                        Character.Controlled = null;
                    }
                }
                return true;
            };
        }

        private Color HealthColorLerp(Color fullHealthColor, Color midHealthColor, Color lowHealthColor, float t)
        {
            t = MathHelper.Clamp(t, 0.0f, 1.0f);
            return t < 0.5f ?
                Color.Lerp(lowHealthColor, midHealthColor, t * 2.0f) : 
                Color.Lerp(midHealthColor, fullHealthColor, (t - 0.5f) * 2.0f);
        }

        private void OnAttacked(Character attacker, AttackResult attackResult)
        {
            damageOverlayTimer = MathHelper.Clamp(attackResult.Damage / MaxVitality, damageOverlayTimer, 1.0f);
            if (healthShadowDelay <= 0.0f) healthShadowDelay = 1.0f;
        }

        private void UpdateAlignment()
        {
            healthBar.RectTransform.RelativeOffset = Vector2.Zero;
            healthWindowHealthBar.RectTransform.RelativeOffset = Vector2.Zero;

            if (alignment == Alignment.Left)
            {
                healthBar.RectTransform.AbsoluteOffset = HUDLayoutSettings.HealthBarAreaLeft.Location;
                healthBar.RectTransform.NonScaledSize = HUDLayoutSettings.HealthBarAreaLeft.Size;

                healthWindow.RectTransform.AbsoluteOffset = HUDLayoutSettings.HealthWindowAreaLeft.Location;
                healthWindow.RectTransform.NonScaledSize = new Point(HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, HUDLayoutSettings.HealthWindowAreaLeft.Height);

                int afflictionContainerHeight = (int)(HUDLayoutSettings.HealthWindowAreaLeft.Height * 0.66f);
                afflictionContainer.RectTransform.AbsoluteOffset = new Point(HUDLayoutSettings.HealthWindowAreaLeft.Center.X, HUDLayoutSettings.HealthWindowAreaLeft.Bottom - afflictionContainerHeight);
                afflictionContainer.RectTransform.NonScaledSize = new Point(HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, afflictionContainerHeight);

                healItemContainer.RectTransform.AbsoluteOffset = new Point(HUDLayoutSettings.HealthWindowAreaLeft.Center.X, HUDLayoutSettings.HealthWindowAreaLeft.Y);
                healItemContainer.RectTransform.NonScaledSize = new Point(HUDLayoutSettings.HealthWindowAreaLeft.Width, (int)(140 * GUI.Scale));
                
                int cprButtonSize = Math.Min((int)(80 * GUI.Scale), afflictionContainer.Rect.Y - healItemContainer.Rect.Bottom - 5);
                cprButton.RectTransform.AbsoluteOffset = new Point(healthWindow.Rect.Right, healItemContainer.Rect.Bottom + 5);
                cprButton.RectTransform.NonScaledSize = new Point(cprButtonSize, cprButtonSize);

                healthWindowHealthBar.RectTransform.NonScaledSize = new Point((int)(30 * GUI.Scale), healthWindow.Rect.Height);
                healthWindowHealthBar.RectTransform.AbsoluteOffset = new Point(healthWindow.Rect.X - (int)(30 * GUI.Scale), healthWindow.Rect.Y);
            }
            else
            {
                healthBar.RectTransform.AbsoluteOffset = HUDLayoutSettings.HealthBarAreaRight.Location;
                healthBar.RectTransform.NonScaledSize = HUDLayoutSettings.HealthBarAreaRight.Size;
                healthWindow.RectTransform.AbsoluteOffset = new Point(HUDLayoutSettings.HealthWindowAreaRight.Center.X, HUDLayoutSettings.HealthWindowAreaRight.Location.Y);
                healthWindow.RectTransform.NonScaledSize = new Point(HUDLayoutSettings.HealthWindowAreaRight.Width / 2, HUDLayoutSettings.HealthWindowAreaRight.Height);

                int afflictionContainerHeight = (int)(HUDLayoutSettings.HealthWindowAreaRight.Height * 0.66f);
                afflictionContainer.RectTransform.AbsoluteOffset = new Point(HUDLayoutSettings.HealthWindowAreaRight.X, HUDLayoutSettings.HealthWindowAreaRight.Bottom - afflictionContainerHeight);
                afflictionContainer.RectTransform.NonScaledSize = new Point(HUDLayoutSettings.HealthWindowAreaRight.Width / 2, afflictionContainerHeight);

                healItemContainer.RectTransform.AbsoluteOffset = new Point(healthWindow.Rect.X - HUDLayoutSettings.HealthWindowAreaRight.Width, HUDLayoutSettings.HealthWindowAreaRight.Y);
                healItemContainer.RectTransform.NonScaledSize = new Point(HUDLayoutSettings.HealthWindowAreaRight.Width, (int)(140 * GUI.Scale));

                int cprButtonSize = Math.Min((int)(80 * GUI.Scale), afflictionContainer.Rect.Y - healItemContainer.Rect.Bottom - 5);
                cprButton.RectTransform.AbsoluteOffset = new Point(healthWindow.Rect.X - cprButtonSize, healItemContainer.Rect.Bottom + 5);
                cprButton.RectTransform.NonScaledSize = new Point(cprButtonSize, cprButtonSize);
                
                healthWindowHealthBar.RectTransform.NonScaledSize = new Point((int)(30 * GUI.Scale), healthWindow.Rect.Height);
                healthWindowHealthBar.RectTransform.AbsoluteOffset = new Point(healthWindow.Rect.Right, healthWindow.Rect.Y);
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
            if (GUI.DisableHUD) return;
            if (openHealthWindow != null)
            {
                if (openHealthWindow != Character.Controlled?.CharacterHealth && openHealthWindow != Character.Controlled?.SelectedCharacter?.CharacterHealth)
                {
                    openHealthWindow = null;
                    return;
                }
            }

            if (damageOverlayTimer > 0.0f) damageOverlayTimer -= deltaTime;

            if (healthShadowDelay > 0.0f)
            {
                healthShadowDelay -= deltaTime;
            }
            else
            {
                healthShadowSize = healthBar.BarSize > healthShadowSize ?
                    Math.Min(healthShadowSize + deltaTime, healthBar.BarSize) :
                    Math.Max(healthShadowSize - deltaTime, healthBar.BarSize);
            }
            
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

            if (PlayerInput.KeyHit(InputType.Health) && GUI.KeyboardDispatcher.Subscriber == null && character.AllowInput && !openedThisFrame)
            {
                if (openHealthWindow != null)
                    OpenHealthWindow = null;
                else
                    OpenHealthWindow = this;
            }
            if (PlayerInput.RightButtonClicked()) OpenHealthWindow = null;
            openedThisFrame = false;
            
            if (character.IsDead)
            {
                healthBar.Color = healthWindowHealthBar.Color = Color.Black;
                healthBar.BarSize = healthWindowHealthBar.BarSize = 1.0f;
            }
            else
            {
                healthBar.Color = healthWindowHealthBar.Color = HealthColorLerp(Color.Green, Color.Orange, Color.Red, vitality / MaxVitality);
                healthBar.HoverColor = healthWindowHealthBar.HoverColor = healthBar.Color * 2.0f;
                healthBar.BarSize = healthWindowHealthBar.BarSize = (vitality > 0.0f) ? vitality / MaxVitality : 1.0f - vitality / minVitality;
            }
            
            if (OpenHealthWindow == this)
            {
                if (character == Character.Controlled && !character.AllowInput)
                {
                    openHealthWindow = null;
                }

                Rectangle limbArea = healthWindow.Children.First().Rect;
                UpdateAfflictionContainer(highlightedLimbIndex < 0 ? (selectedLimbIndex < 0 ? null : limbHealths[selectedLimbIndex]) : limbHealths[highlightedLimbIndex]);
                UpdateItemContainer();

                if (draggingMed != null)
                {
                    if (!PlayerInput.LeftButtonHeld())
                    {
                        OnItemDropped(draggingMed.UserData as Item);
                        draggingMed = null;
                    }
                }
            }
            else
            {
                if (openHealthWindow != null && character != Character.Controlled && character != Character.Controlled?.SelectedCharacter)
                {
                    openHealthWindow = null;
                }
                highlightedLimbIndex = -1;
            }

            Rectangle hoverArea = alignment == Alignment.Left ?
                Rectangle.Union(HUDLayoutSettings.AfflictionAreaLeft, HUDLayoutSettings.HealthBarAreaLeft) :
                Rectangle.Union(HUDLayoutSettings.AfflictionAreaRight, HUDLayoutSettings.HealthBarAreaRight);

            if (character.AllowInput && UseHealthWindow && hoverArea.Contains(PlayerInput.MousePosition))
            {
                healthBar.State = GUIComponent.ComponentState.Hover;
                if (PlayerInput.LeftButtonClicked())
                {
                    OpenHealthWindow = openHealthWindow == this ? null : this;
                }
            }
            else
            {
                healthBar.State = GUIComponent.ComponentState.None;
            }

            suicideButton.Visible = character == Character.Controlled && character.IsUnconscious && !character.IsDead;

            cprButton.Visible =
                character == Character.Controlled?.SelectedCharacter
                && (character.IsUnconscious || character.Stun > 0.0f)
                && openHealthWindow == this;
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            if (OpenHealthWindow == this)
            {
                afflictionContainer.AddToGUIUpdateList();
                healItemContainer.AddToGUIUpdateList();
                healthWindow.AddToGUIUpdateList();
                healthWindowHealthBar.AddToGUIUpdateList();
            }
            else
            {
                healthBar.AddToGUIUpdateList();
            }
            if (suicideButton.Visible && character == Character.Controlled) suicideButton.AddToGUIUpdateList();
            if (cprButton != null && cprButton.Visible) cprButton.AddToGUIUpdateList();
        }

        public void DrawHUD(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;
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

            DrawStatusHUD(spriteBatch);
        }

        public void DrawStatusHUD(SpriteBatch spriteBatch)
        {
            Rectangle interactArea = healthBar.Rect;
            if (openHealthWindow != this)
            {
                List<Pair<Sprite, string>> statusIcons = new List<Pair<Sprite, string>>();
                if (character.CurrentHull == null || character.CurrentHull.LethalPressure > 5.0f)
                    statusIcons.Add(new Pair<Sprite, string>(AfflictionPrefab.Pressure.Icon, "High pressure"));

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
                
                if (vitality > 0.0f)
                {
                    float currHealth = healthBar.BarSize;
                    Color prevColor = healthBar.Color;
                    healthBar.BarSize = healthShadowSize;
                    healthBar.Color = Color.Red;
                    healthBar.BarSize = currHealth;
                    healthBar.Color = prevColor;
                }
            }
            else
            {
                if (vitality > 0.0f)
                {
                    float currHealth = healthWindowHealthBar.BarSize;
                    Color prevColor = healthWindowHealthBar.Color;
                    healthWindowHealthBar.BarSize = healthShadowSize;
                    healthWindowHealthBar.Color = Color.Red;
                    healthWindowHealthBar.BarSize = currHealth;
                    healthWindowHealthBar.Color = prevColor;
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
                var child = afflictionContainer.Content.FindChild(affliction);
                if (child == null)
                {
                    child = new GUIFrame(new RectTransform(new Point(afflictionContainer.Rect.Width, 250), afflictionContainer.Content.RectTransform), style: "ListBoxElement")
                    {
                        UserData = affliction
                    };
                    currentChildren.Add(child);

                    var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), child.RectTransform, Anchor.Center), style: null);
                    new GUIImage(new RectTransform(affliction.Prefab.Icon.size.ToPoint(), paddedFrame.RectTransform), affliction.Prefab.Icon);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform)
                        { AbsoluteOffset = new Point((int)affliction.Prefab.Icon.size.X + 10, 0) }, affliction.Prefab.Name);
                    var strengthBar = new GUIProgressBar(new RectTransform(new Point(paddedFrame.Rect.Width - (int)affliction.Prefab.Icon.size.X - 10, 15), paddedFrame.RectTransform)
                        {AbsoluteOffset = new Point((int)affliction.Prefab.Icon.size.X + 10, 20) },
                        barSize: 1.0f, color: Color.Green)
                    {
                        IsHorizontal = true,
                        UserData = "strength"
                    };

                    new GUITextBlock(new RectTransform(strengthBar.Rect.Size, paddedFrame.RectTransform) { AbsoluteOffset = new Point(0, 50) }, "")
                    {
                        UserData = "vitality"
                    };

                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(0, 70) }, 
                        affliction.Prefab.Description, wrap: true);

                    child.RectTransform.NonScaledSize = new Point(
                        child.RectTransform.NonScaledSize.X, 
                        child.RectTransform.Children.Sum(c => c.Rect.Height) - (int)affliction.Prefab.Icon.size.Y);
                }
                else
                {
                    var strengthBar = child.Children.First().GetChildByUserData("strength") as GUIProgressBar;
                    strengthBar.BarSize = Math.Max(affliction.Strength / affliction.Prefab.MaxStrength, 0.05f);
                    strengthBar.Color = Color.Lerp(Color.Orange, Color.Red, affliction.Strength / affliction.Prefab.MaxStrength);

                    var vitalityText = child.Children.First().GetChildByUserData("vitality") as GUITextBlock;
                    int vitalityDecrease = (int)affliction.GetVitalityDecrease(this);
                    vitalityText.Text = "Vitality -" + vitalityDecrease;
                    vitalityText.TextColor = vitalityDecrease <= 0 ? Color.LightGreen :
                    Color.Lerp(Color.Orange, Color.Red, affliction.Strength / affliction.Prefab.MaxStrength);

                    currentChildren.Add(child);
                }
            }
            
            for (int i = afflictionContainer.Content.CountChildren - 1; i>= 0; i--)
            {
                var child = afflictionContainer.Content.GetChild(i);
                if (!currentChildren.Contains(child))
                {
                    afflictionContainer.RemoveChild(child);
                }
            }

            afflictionContainer.Content.RectTransform.SortChildren((c1, c2) =>
            {
                Affliction affliction1 = c1.GUIComponent.UserData as Affliction;
                Affliction affliction2 = c2.GUIComponent.UserData as Affliction;
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
            List<Item> allInventoryItems = new List<Item>();
            allInventoryItems.AddRange(character.Inventory.Items);
            if (character.SelectedCharacter?.Inventory != null && character.CanAccessInventory(character.SelectedCharacter.Inventory))
            {
                allInventoryItems.AddRange(character.SelectedCharacter.Inventory.Items);
            }
            if (character.SelectedBy?.Inventory != null)
            {
                allInventoryItems.AddRange(character.SelectedBy.Inventory.Items);
            }

            List<Item> medicalItems = new List<Item>();
            foreach (Item item in allInventoryItems)
            {
                if (item == null) continue;

                var containedItems = item.ContainedItems;
                if (containedItems != null)
                {
                    foreach (Item containedItem in containedItems)
                    {
                        if (containedItem == null) continue;
                        if (!containedItem.HasTag("medical") && !containedItem.HasTag("chem")) continue;
                        medicalItems.Add(containedItem);
                    }
                }

                if (!item.HasTag("medical") && !item.HasTag("chem")) continue;
                medicalItems.Add(item);
            }

            return medicalItems.Distinct().ToList();
        }

        private bool ItemContainerNeedsRefresh(List<Item> availableItems)
        {
            if (healItemContainer.Content.CountChildren == 0) return true;
            int childrenCount = healItemContainer.Content.Children.Where(c => c.UserData as string != "noavailableitems").Count();
            if (availableItems.Count != childrenCount) return true;
 
            foreach (Item item in availableItems)
            {
                //no button for this item, need to refresh
                if (!healItemContainer.Content.Children.Any(c => c.UserData as Item == item))
                {
                    return true;
                }
            }

            foreach (GUIComponent child in healItemContainer.Content.Children)
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

            healItemContainer.Content.ClearChildren();
            
            int itemButtonSize = healItemContainer.Rect.Height - (int)(20 * GUI.Scale);
            
            if (items.Count == 0)
            {
                var noItemsText = new GUITextBlock(new RectTransform(Vector2.One, healItemContainer.Content.RectTransform),
                    TextManager.Get("NoAvailableMedicalItems"), textAlignment: Alignment.Center)
                {
                    UserData = "noavailableitems",
                    CanBeFocused = false
                };
                return;
            }
            
            foreach (Item item in items)
            {
                if (item == null) continue;
                if (!item.HasTag("medical") && !item.HasTag("chem")) continue;

                var child = new GUIButton(new RectTransform(new Point(itemButtonSize, itemButtonSize), healItemContainer.Content.RectTransform),
                    text: "", style: "InventorySlotSmall")
                {
                    UserData = item
                };
                child.OnClicked += OnTreatmentButtonClicked;
                child.OnPressed += () =>
                {
                    if (draggingMed == null) draggingMed = child;
                    return true;
                };

                Sprite itemSprite = item.Prefab.InventoryIcon ?? item.Sprite;
                var itemIcon = new GUIImage(new RectTransform(new Vector2(0.8f, 0.8f), child.RectTransform, Anchor.Center),
                    itemSprite, scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = itemSprite == item.Sprite ? item.SpriteColor : Color.White,
                    HoverColor = item.SpriteColor,
                    SelectedColor = item.SpriteColor
                };
                
                string itemName = item.Name;
                if (item.ContainedItems != null && item.ContainedItems.Length > 0)
                {
                    itemName += " (" + item.ContainedItems[0].Name + ")";
                }
                child.ToolTip = itemName + "\n" + item.Description;
            }
        }

        private void UpdateTreatmentSuitabilityHints()
        {
            List<Affliction> selectedAfflictions = new List<Affliction>();
            foreach (GUIComponent child in afflictionContainer.Children)
            {
                Affliction affliction = child.UserData as Affliction;
                if (affliction != null) selectedAfflictions.Add(affliction);
            }

            foreach (GUIComponent child in healItemContainer.Content.Children)
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
            if (item == null || selectedLimbIndex < 0) return false;

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

        private void DrawHealthWindow(SpriteBatch spriteBatch, Rectangle drawArea, bool allowHighlight, bool highlightAll)
        {
            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;

                float damageLerp = limbHealth.TotalDamage > 0.0f ? MathHelper.Lerp(0.2f, 1.0f, limbHealth.TotalDamage / 100.0f) : 0.0f;
                Color color = HealthColorLerp(Color.Green, Color.Orange, Color.Red, 1.0f - damageLerp);
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
            
            if (draggingMed != null)
            {
                GUIImage itemImage = draggingMed.GetChild<GUIImage>();
                float scale = Math.Min(40.0f / itemImage.Sprite.size.X, 40.0f / itemImage.Sprite.size.Y);
                itemImage.Sprite.Draw(spriteBatch, PlayerInput.MousePosition, itemImage.Color, 0, scale);
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
                    if (existingAffliction == stunAffliction) character.SetStun(existingAffliction.Strength, true, true);
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
