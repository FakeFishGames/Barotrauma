using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        private static bool toggledThisFrame;

        public static Sprite DamageOverlay;

        private static string[] strengthTexts;

        private GUIButton cprButton;

        private Point screenResolution;

        private float uiScale, inventoryScale;

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

        // healthbars
        private GUIProgressBar healthBar;
        private GUIProgressBar healthBarShadow;
        private GUIProgressBar healthWindowHealthBar;
        private GUIProgressBar healthWindowHealthBarShadow;
        private float healthShadowSize;
        private float healthShadowDelay;
        private float healthBarPulsateTimer;
        private float healthBarPulsatePhase;

        private GUITextBlock characterName;
        private GUIFrame afflictionInfoFrame;
        private GUIListBox afflictionInfoContainer;
        private GUIListBox recommendedTreatmentContainer;

        private float bloodParticleTimer;

        private GUIFrame healthWindow;

        private GUIComponent deadIndicator;

        private GUIComponent lowSkillIndicator;

        private SpriteSheet limbIndicatorOverlay;
        private float limbIndicatorOverlayAnimState;

        private GUIFrame dropItemArea;

        private float dropItemAnimDuration = 0.5f;
        private float dropItemAnimTimer;

        public Item DroppedItem
        {
            get
            {
                return droppedItem;
            }
        }
        private Item droppedItem;

        private GUIComponent draggingMed;

        private int highlightedLimbIndex = -1;
        private int selectedLimbIndex = -1;
        private LimbHealth currentDisplayedLimb;

        private float distortTimer;

        // 0-1
        private float damageIntensity;
        private float damageIntensityDropdownRate = 0.1f;

        public float DamageOverlayTimer { get; private set; }

        private float updateDisplayedAfflictionsTimer;
        private const float UpdateDisplayedAfflictionsInterval = 0.5f;
        private List<Affliction> currentDisplayedAfflictions = new List<Affliction>();

        public float DisplayedVitality, DisplayVitalityDelay;

        public bool MouseOnElement
        {
            get { return highlightedLimbIndex > -1 || GUI.MouseOn == dropItemArea; }
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

                var prevOpenHealthWindow = openHealthWindow;

                if (prevOpenHealthWindow != null)
                {
                    prevOpenHealthWindow.selectedLimbIndex = -1;
                    prevOpenHealthWindow.highlightedLimbIndex = -1;
                }

                openHealthWindow = value;
                toggledThisFrame = true;
                if (Character.Controlled == null) { return; }

                if (value == null &&
                    Character.Controlled?.SelectedCharacter?.CharacterHealth != null &&
                    Character.Controlled.SelectedCharacter.CharacterHealth == prevOpenHealthWindow/* &&
                    !Character.Controlled.SelectedCharacter.CanInventoryBeAccessed*/)
                {
                    Character.Controlled.DeselectCharacter();
                }

                Character.Controlled.ResetInteract = true;
                if (openHealthWindow != null)
                {
                    openHealthWindow.characterName.Text = value.Character.Name;
                    Character.Controlled.SelectedConstruction = null;
                }
            }
        }

        public GUIButton CPRButton
        {
            get { return cprButton; }
        }

        public float HealthBarPulsateTimer
        {
            get { return healthBarPulsateTimer; }
            set { healthBarPulsateTimer = MathHelper.Clamp(value, 0.0f, 10.0f); }
        }
        
        partial void InitProjSpecific(XElement element, Character character)
        {
            DisplayedVitality = MaxVitality;

            if (strengthTexts == null)
            {
                strengthTexts = new string[]
                {
                    TextManager.Get("AfflictionStrengthLow"),
                    TextManager.Get("AfflictionStrengthMedium"),
                    TextManager.Get("AfflictionStrengthHigh")
                };
            }

            character.OnAttacked += OnAttacked;

            bool horizontal = HUDLayoutSettings.HealthBarAreaLeft.Width > HUDLayoutSettings.HealthBarAreaLeft.Height;
            healthBar = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: horizontal ? "GUIProgressBar" : "GUIProgressBarVertical")
            {
                IsHorizontal = horizontal
            };
            healthBarShadow = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: horizontal ? "GUIProgressBar" : "GUIProgressBarVertical")
            {
                IsHorizontal = horizontal
            };
            healthShadowSize = 1.0f;

            afflictionInfoFrame = new GUIFrame(new RectTransform(new Point(HUDLayoutSettings.HealthWindowAreaLeft.Width / 2, 200), GUI.Canvas));
            var paddedInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), afflictionInfoFrame.RectTransform, Anchor.Center), style: null);
            new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.08f), paddedInfoFrame.RectTransform), "", font: GUI.LargeFont)
            {
                UserData = "selectedlimbname"
            };

            afflictionInfoContainer = new GUIListBox(new RectTransform(new Vector2(0.7f, 0.85f), paddedInfoFrame.RectTransform, Anchor.BottomLeft));

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.05f), paddedInfoFrame.RectTransform, Anchor.TopRight), TextManager.Get("SuitableTreatments"), textAlignment: Alignment.TopRight);
            
            recommendedTreatmentContainer = new GUIListBox(new RectTransform(new Vector2(0.28f, 0.47f), paddedInfoFrame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.0f, 0.15f) });
            dropItemArea = new GUIFrame(new RectTransform(new Vector2(0.28f, 0.3f), paddedInfoFrame.RectTransform, Anchor.BottomRight)
            { RelativeOffset = new Vector2(0.02f, 0.0f) }, style: null)
            {
                ToolTip = TextManager.Get("HealthItemUseTip")
            };
            dropItemArea.RectTransform.NonScaledSize = new Point(dropItemArea.Rect.Width);

            lowSkillIndicator = new GUIImage(new RectTransform(new Vector2(0.5f, 0.22f), paddedInfoFrame.RectTransform, Anchor.TopRight, Pivot.Center) { RelativeOffset = new Vector2(0.16f, 0.12f) }, 
                style: "GUINotificationButton")
            {
                ToolTip = TextManager.Get("lowmedicalskillwarning"),
                Color = Color.OrangeRed,
                HoverColor = Color.Orange,
                PressedColor = Color.Orange,
                Visible = false
            };
            lowSkillIndicator.RectTransform.MaxSize = new Point(lowSkillIndicator.Rect.Height);

            string[] healthCircleStyles = new string[] { "HealthCircleInner", "HealthCircleMid", "HealthCircleOuter" };
            foreach (string healthCircleStyle in healthCircleStyles)
            {
                for (int i = 1; i < 4; i++)
                {
                    var style = GUI.Style.GetComponentStyle(healthCircleStyle + i);
                    if (style != null)
                    {
                        new GUIImage(new RectTransform(Vector2.One, dropItemArea.RectTransform), healthCircleStyle + i)
                        {
                            CanBeFocused = false
                        };
                    }
                }
            }

            new GUIImage(new RectTransform(Vector2.One * 0.2f, dropItemArea.RectTransform, Anchor.Center), "HealthCross")
            {
                CanBeFocused = false
            };

            healthWindow = new GUIFrame(new RectTransform(new Point(100, 200), GUI.Canvas));
            var paddedHealthWindow = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), healthWindow.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var nameContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), paddedHealthWindow.RectTransform) { MinSize = new Point(0, 20) }, isHorizontal: true)
            {
                Stretch = true
            };

            characterName = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), nameContainer.RectTransform), "", textAlignment: Alignment.CenterLeft, font: GUI.LargeFont)
            {
                AutoScale = true
            };
            new GUICustomComponent(new RectTransform(new Vector2(0.4f, 1.0f), nameContainer.RectTransform),
                onDraw: (spriteBatch, component) =>
                {
                    character.Info.DrawPortrait(spriteBatch, new Vector2(component.Rect.X, component.Rect.Center.Y - component.Rect.Width / 2), component.Rect.Width);
                });

            new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.9f), paddedHealthWindow.RectTransform),
                (spriteBatch, component) =>
                {
                    DrawHealthWindow(spriteBatch, component.RectTransform.Rect, true, false);
                },
                (deltaTime, component) =>
                {
                    UpdateLimbIndicators(deltaTime, component.RectTransform.Rect);
                }
            );
            deadIndicator = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.1f), healthWindow.RectTransform, Anchor.Center),
                text: TextManager.Get("Deceased"), font: GUI.LargeFont, textAlignment: Alignment.Center, wrap: true, style: "GUIToolTip")
            {
                Visible = false,
                CanBeFocused = false
            };

            healthWindowHealthBar = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: "GUIProgressBarVertical")
            {
                IsHorizontal = false
            };
            healthWindowHealthBarShadow = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: "GUIProgressBarVertical")
            {
                IsHorizontal = false
            };
            cprButton = new GUIButton(new RectTransform(new Point((int)(80 * GUI.Scale)), GUI.Canvas), text: "", style: "CPRButton")
            {
                OnClicked = (button, userData) =>
                {
                    Character selectedCharacter = Character.Controlled?.SelectedCharacter;
                    if (selectedCharacter == null || (!selectedCharacter.IsUnconscious && selectedCharacter.Stun <= 0.0f)) return false;

                    Character.Controlled.AnimController.Anim = (Character.Controlled.AnimController.Anim == AnimController.Animation.CPR) ?
                        AnimController.Animation.None : AnimController.Animation.CPR;

                    selectedCharacter.AnimController.ResetPullJoints();

                    if (GameMain.Client != null)
                    {
                        GameMain.Client.CreateEntityEvent(Character.Controlled, new object[] { NetEntityEvent.Type.Treatment });
                    }

                    return true;
                },
                Visible = false
            };

            UpdateAlignment();

            suicideButton = new GUIButton(new RectTransform(new Vector2(0.06f, 0.02f), GUI.Canvas, Anchor.TopCenter)
            { MinSize = new Point(120, 20), RelativeOffset = new Vector2(0.0f, 0.01f) },
                TextManager.Get("GiveInButton"))
            {
                ToolTip = TextManager.Get(GameMain.NetworkMember == null ? "GiveInHelpSingleplayer" : "GiveInHelpMultiplayer"),
                OnClicked = (button, userData) =>
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
                }
            };

            if (element != null)
            {
                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "sprite":
                            limbIndicatorOverlay = new SpriteSheet(subElement);
                            break;
                    }
                }
            }
        }

        private void OnAttacked(Character attacker, AttackResult attackResult)
        {
            if (Math.Abs(attackResult.Damage) < 0.01f && attackResult.Afflictions.Count == 0) { return; }
            DamageOverlayTimer = MathHelper.Clamp(attackResult.Damage / MaxVitality, DamageOverlayTimer, 1.0f);
            if (healthShadowDelay <= 0.0f) { healthShadowDelay = 1.0f; }

            if (healthBarPulsateTimer <= 0.0f) { healthBarPulsatePhase = 0.0f; }
            healthBarPulsateTimer = 1.0f;

            float additionalIntensity = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, 0.1f, attackResult.Damage / MaxVitality));
            damageIntensity = MathHelper.Clamp(damageIntensity + additionalIntensity, 0, 1);

            DisplayVitalityDelay = 0.5f;
        }

        private void UpdateAlignment()
        {
            healthBar.RectTransform.RelativeOffset = healthBarShadow.RectTransform.RelativeOffset = Vector2.Zero;
            healthWindowHealthBar.RectTransform.RelativeOffset = healthWindowHealthBarShadow.RectTransform.RelativeOffset = Vector2.Zero;

            int healthWindowHealthBarWidth = (int)(40 * GUI.Scale);

            if (alignment == Alignment.Left)
            {
                healthBar.RectTransform.SetPosition(Anchor.BottomLeft);
                healthBarShadow.RectTransform.SetPosition(Anchor.BottomLeft);
                healthBar.RectTransform.AbsoluteOffset = healthBarShadow.RectTransform.AbsoluteOffset =
                    new Point(HUDLayoutSettings.HealthBarAreaLeft.X, GameMain.GraphicsHeight - HUDLayoutSettings.HealthBarAreaLeft.Bottom);
                healthBar.RectTransform.NonScaledSize = healthBarShadow.RectTransform.NonScaledSize = HUDLayoutSettings.HealthBarAreaLeft.Size;

                healthWindow.RectTransform.AbsoluteOffset = HUDLayoutSettings.HealthWindowAreaLeft.Location + new Point(healthWindowHealthBarWidth, 0);
                healthWindow.RectTransform.NonScaledSize = new Point(
                    HUDLayoutSettings.HealthWindowAreaLeft.Width / 3 - healthWindowHealthBarWidth,
                    HUDLayoutSettings.HealthWindowAreaLeft.Height);

                afflictionInfoFrame.RectTransform.AbsoluteOffset = new Point(
                    healthWindow.Rect.Right,
                    HUDLayoutSettings.HealthWindowAreaLeft.Y);
                afflictionInfoFrame.RectTransform.NonScaledSize = new Point(
                    (int)(HUDLayoutSettings.HealthWindowAreaLeft.Width * 0.66f),
                    (int)(HUDLayoutSettings.HealthWindowAreaLeft.Height));

                healthWindowHealthBar.RectTransform.NonScaledSize = healthWindowHealthBarShadow.RectTransform.NonScaledSize =
                    new Point(healthWindowHealthBarWidth, healthWindow.Rect.Height);
                healthWindowHealthBar.RectTransform.AbsoluteOffset = healthWindowHealthBarShadow.RectTransform.AbsoluteOffset =
                    HUDLayoutSettings.HealthWindowAreaLeft.Location;

                int cprButtonSize = (int)(100 * GUI.Scale);
                cprButton.RectTransform.AbsoluteOffset = new Point(HUDLayoutSettings.HealthWindowAreaLeft.Right, dropItemArea.Rect.Center.Y - cprButtonSize / 2);
                cprButton.RectTransform.NonScaledSize = new Point(cprButtonSize);
            }
            else
            {
                healthBar.RectTransform.SetPosition(Anchor.TopLeft);
                healthBarShadow.RectTransform.SetPosition(Anchor.TopLeft);
                healthBar.RectTransform.AbsoluteOffset = healthBarShadow.RectTransform.AbsoluteOffset =
                    HUDLayoutSettings.HealthBarAreaRight.Location;
                healthBar.RectTransform.NonScaledSize = healthBarShadow.RectTransform.NonScaledSize = HUDLayoutSettings.HealthBarAreaRight.Size;

                healthWindow.RectTransform.AbsoluteOffset = new Point(
                    HUDLayoutSettings.HealthWindowAreaRight.X + HUDLayoutSettings.HealthWindowAreaRight.Width / 3 * 2,
                    HUDLayoutSettings.HealthWindowAreaRight.Y);
                healthWindow.RectTransform.NonScaledSize = new Point(
                    HUDLayoutSettings.HealthWindowAreaRight.Width / 3 - healthWindowHealthBarWidth,
                    HUDLayoutSettings.HealthWindowAreaRight.Height);

                afflictionInfoFrame.RectTransform.AbsoluteOffset = new Point(
                    HUDLayoutSettings.HealthWindowAreaRight.X,
                    HUDLayoutSettings.HealthWindowAreaLeft.Y);
                afflictionInfoFrame.RectTransform.NonScaledSize = new Point(
                    (int)(HUDLayoutSettings.HealthWindowAreaLeft.Width * 0.66f),
                    (int)(HUDLayoutSettings.HealthWindowAreaLeft.Height));

                healthWindowHealthBar.RectTransform.NonScaledSize = healthWindowHealthBarShadow.RectTransform.NonScaledSize =
                    new Point(healthWindowHealthBarWidth, healthWindow.Rect.Height);
                healthWindowHealthBar.RectTransform.AbsoluteOffset = healthWindowHealthBarShadow.RectTransform.AbsoluteOffset =
                    new Point(HUDLayoutSettings.HealthWindowAreaRight.Right - healthWindowHealthBarWidth, HUDLayoutSettings.HealthWindowAreaRight.Y);

                int cprButtonSize = (int)(100 * GUI.Scale);
                cprButton.RectTransform.AbsoluteOffset = new Point(HUDLayoutSettings.HealthWindowAreaRight.X - cprButtonSize, dropItemArea.Rect.Center.Y - cprButtonSize / 2);
                cprButton.RectTransform.NonScaledSize = new Point(cprButtonSize);
            }

            dropItemArea.RectTransform.NonScaledSize = new Point(dropItemArea.Rect.Width);

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            inventoryScale = Inventory.UIScale;
            uiScale = GUI.Scale;
        }

        public void UpdateClientSpecific(float deltaTime)
        {
            if (GameMain.NetworkMember == null)
            {
                DisplayedVitality = Vitality;
            }
            else
            {
                DisplayVitalityDelay -= deltaTime;
                if (DisplayVitalityDelay <= 0.0f)
                {
                    DisplayedVitality = Vitality;
                }
            }
            
            if (damageIntensity > 0)
            {
                damageIntensity -= deltaTime * damageIntensityDropdownRate;
                if (damageIntensity < 0)
                {
                    damageIntensity = 0;
                }
            }
            if (DamageOverlayTimer > 0.0f)
            {
                DamageOverlayTimer -= deltaTime;
            }
        }

        partial void UpdateOxygenProjSpecific(float prevOxygen)
        {
            if (prevOxygen > 0.0f && OxygenAmount <= 0.0f && 
                Character.Controlled == Character)
            {
                SoundPlayer.PlaySound(Character.Info != null && Character.Info.Gender == Gender.Female ? "drownfemale" : "drownmale");
            }
        }

        partial void UpdateBleedingProjSpecific(AfflictionBleeding affliction, Limb targetLimb, float deltaTime)
        {
            bloodParticleTimer -= deltaTime * (affliction.Strength / 10.0f);
            if (bloodParticleTimer <= 0.0f)
            {
                float bloodParticleSize = MathHelper.Lerp(0.5f, 1.0f, affliction.Strength / 100.0f);
                if (!Character.AnimController.InWater) bloodParticleSize *= 2.0f;
                var blood = GameMain.ParticleManager.CreateParticle(
                    Character.AnimController.InWater ? "waterblood" : "blooddrop",
                    targetLimb.WorldPosition, Rand.Vector(affliction.Strength), 0.0f, Character.AnimController.CurrentHull);

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
            
            bool forceAfflictionContainerUpdate = false;
            if (updateDisplayedAfflictionsTimer > 0.0f)
            {
                updateDisplayedAfflictionsTimer -= deltaTime;
            }
            else
            {
                forceAfflictionContainerUpdate = true;
                currentDisplayedAfflictions = GetAllAfflictions(mergeSameAfflictions: true)
                    .FindAll(a => a.Strength >= a.Prefab.ShowIconThreshold && a.Prefab.Icon != null);
                currentDisplayedAfflictions.Sort((a1, a2) =>
                {
                    int dmgPerSecond = Math.Sign(a2.DamagePerSecond - a1.DamagePerSecond);
                    return dmgPerSecond != 0 ? dmgPerSecond : Math.Sign(a1.Strength - a1.Strength);
                });
                updateDisplayedAfflictionsTimer = UpdateDisplayedAfflictionsInterval;
            }
            
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
            
            dropItemArea.Visible = !Character.IsDead;

            float blurStrength = 0.0f;
            float distortStrength = 0.0f;
            float distortSpeed = 0.0f;
            float radialDistortStrength = 0.0f;
            float chromaticAberrationStrength = 0.0f;

            if (Character.IsUnconscious)
            {
                blurStrength = 1.0f;
                distortSpeed = 1.0f;
            }
            else if (OxygenAmount < 100.0f)
            {
                blurStrength = MathHelper.Lerp(0.5f, 1.0f, 1.0f - Vitality / MaxVitality);
                distortStrength = blurStrength;
                distortSpeed = (blurStrength + 1.0f);
                distortSpeed *= distortSpeed * distortSpeed * distortSpeed;
            }

            foreach (Affliction affliction in afflictions)
            {
                distortStrength = Math.Max(distortStrength, affliction.GetScreenDistortStrength());
                blurStrength = Math.Max(blurStrength, affliction.GetScreenBlurStrength());
                radialDistortStrength = Math.Max(radialDistortStrength, affliction.GetRadialDistortStrength());
                chromaticAberrationStrength = Math.Max(chromaticAberrationStrength, affliction.GetChromaticAberrationStrength());
            }
            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    distortStrength = Math.Max(distortStrength, affliction.GetScreenDistortStrength());
                    blurStrength = Math.Max(blurStrength, affliction.GetScreenBlurStrength());
                    radialDistortStrength = Math.Max(radialDistortStrength, affliction.GetRadialDistortStrength());
                    chromaticAberrationStrength = Math.Max(chromaticAberrationStrength, affliction.GetChromaticAberrationStrength());
                }
            }

            Character.RadialDistortStrength = radialDistortStrength;
            Character.ChromaticAberrationStrength = chromaticAberrationStrength;
            if (blurStrength > 0.0f)
            {
                distortTimer = (distortTimer + deltaTime * distortSpeed) % MathHelper.TwoPi;
                Character.BlurStrength = (float)(Math.Sin(distortTimer) + 1.5f) * 0.25f * blurStrength;
                Character.DistortStrength = (float)(Math.Sin(distortTimer) + 1.0f) * 0.1f * distortStrength;
            }
            else
            {
                Character.BlurStrength = 0.0f;
                Character.DistortStrength = 0.0f;
                distortTimer = 0.0f;
            }

            if (PlayerInput.KeyHit(InputType.Health) && GUI.KeyboardDispatcher.Subscriber == null &&
                Character.Controlled.AllowInput && !toggledThisFrame)
            {
                if (openHealthWindow != null)
                {
                    OpenHealthWindow = null;
                }
                else if (Character.Controlled == Character && Character.Controlled.FocusedCharacter == null)
                {
                    OpenHealthWindow = this;
                    forceAfflictionContainerUpdate = true;
                }
            }
            else if (openHealthWindow == this)
            {
                if (Alignment == Alignment.Right ?
                    HUD.CloseHUD(HUDLayoutSettings.HealthWindowAreaRight) :
                    HUD.CloseHUD(HUDLayoutSettings.HealthWindowAreaLeft))
                {
                    //emulate a Health input to get the character to deselect the item server-side
                    if (GameMain.Client != null)
                    {
                        Character.Controlled.Keys[(int)InputType.Health].Hit = true;
                    }
                    OpenHealthWindow = null;
                }
            }
            toggledThisFrame = false;

            if (OpenHealthWindow == this)
            {
                var highlightedLimb = highlightedLimbIndex < 0 ? null : limbHealths[highlightedLimbIndex];
                if (highlightedLimbIndex < 0 && selectedLimbIndex < 0)
                {
                    // If no limb is selected or highlighted, select the one with the most critical afflictions.
                    var affliction = GetAllAfflictions(a => a.Prefab.IndicatorLimb != LimbType.None)
                        .OrderByDescending(a => a.DamagePerSecond)
                        .ThenByDescending(a => a.Strength).FirstOrDefault();
                    if (affliction.DamagePerSecond > 0 || affliction.Strength > 0)
                    {
                        var limbHealth = GetMatchingLimbHealth(affliction);
                        if (limbHealth != null)
                        {
                            selectedLimbIndex = limbHealths.IndexOf(limbHealth);
                        }
                    }
                    else
                    {
                        // If no affliction is critical, select the limb which has most damage.
                        var limbHealth = limbHealths.OrderByDescending(l => l.TotalDamage).FirstOrDefault();
                        selectedLimbIndex = limbHealths.IndexOf(limbHealth);
                    }
                }
                LimbHealth selectedLimb = selectedLimbIndex < 0 ? highlightedLimb : limbHealths[selectedLimbIndex];
                if (selectedLimb != currentDisplayedLimb || forceAfflictionContainerUpdate)
                {
                    UpdateAfflictionContainer(selectedLimb);
                    currentDisplayedLimb = selectedLimb;
                }
            }

            if (Character.IsDead)
            {
                healthBar.Color = healthWindowHealthBar.Color = Color.Black;
                healthBar.BarSize = healthWindowHealthBar.BarSize = 1.0f;
            }
            else
            {
                healthBar.Color = healthWindowHealthBar.Color = ToolBox.GradientLerp(DisplayedVitality / MaxVitality, Color.Red, Color.Orange, Color.Green);
                healthBar.HoverColor = healthWindowHealthBar.HoverColor = healthBar.Color * 2.0f;
                healthBar.BarSize = healthWindowHealthBar.BarSize = 
                    (DisplayedVitality > 0.0f) ? 
                    (MaxVitality > 0.0f ? DisplayedVitality / MaxVitality : 0.0f) : 
                    (Math.Abs(MinVitality) > 0.0f ? 1.0f - DisplayedVitality / MinVitality : 0.0f);

                if (healthBarPulsateTimer > 0.0f)
                {
                    //0-1
                    float pulsateAmount = (float)(Math.Sin(healthBarPulsatePhase) + 1.0f) / 2.0f;

                    healthBar.RectTransform.LocalScale = healthBarShadow.RectTransform.LocalScale = new Vector2(1.0f, (1.0f + pulsateAmount * healthBarPulsateTimer * 0.5f));
                    healthBarPulsatePhase += deltaTime * 5.0f;
                    healthBarPulsateTimer -= deltaTime;
                }
                else
                {
                    healthBar.RectTransform.LocalScale = Vector2.One;
                }
            }

            if (OpenHealthWindow == this)
            {
                if (Character == Character.Controlled && !Character.AllowInput)
                {
                    openHealthWindow = null;
                }

                lowSkillIndicator.Visible = Character.Controlled != null && Character.Controlled.GetSkillLevel("medical") < 50.0f;
                lowSkillIndicator.Color = new Color(lowSkillIndicator.Color, MathHelper.Lerp(0.1f, 1.0f, (float)(Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 2.0f));

                float rotationSpeed = 0.25f;
                int i = 0;
                foreach (GUIComponent dropItemIndicator in dropItemArea.Children)
                {
                    GUIImage img = dropItemIndicator as GUIImage;
                    if (img == null) continue;

                    img.State = GUI.MouseOn == dropItemArea ? GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None;

                    byte alpha = img.Color.A;
                    byte hoverAlpha = img.HoverColor.A;
                    img.Color = ToolBox.GradientLerp(Vitality / MaxVitality, Color.Red, Color.Orange, Color.Green);
                    img.Color = new Color(img.Color.R, img.Color.G, img.Color.B, alpha);
                    img.HoverColor = new Color(img.Color.R, img.Color.G, img.Color.B, hoverAlpha);
                    img.HoverColor = Color.Lerp(img.HoverColor, Color.White, 0.5f);

                    if (img.State == GUIComponent.ComponentState.Hover && droppedItem == null)
                    {
                        dropItemAnimTimer = Math.Min(0.3f, dropItemAnimTimer + deltaTime * 0.5f);
                    }

                    if (i < 4)
                    {
                        img.Scale = 1.0f - (float)Math.Sin(dropItemAnimTimer / dropItemAnimDuration * MathHelper.TwoPi) * 0.3f;
                    }

                    if (dropItemIndicator == dropItemArea.Children.Last()) break;
                    img.Rotation = (img.Rotation + (rotationSpeed + dropItemAnimTimer * 10.0f) * deltaTime) % MathHelper.TwoPi;
                    rotationSpeed = (rotationSpeed + 0.3f) % 1.0f;

                    i++;
                }
                
                if (Inventory.draggingItem != null)
                {
                    if (highlightedLimbIndex > -1)
                    {
                        selectedLimbIndex = highlightedLimbIndex;
                    }
                }

                if (draggingMed != null)
                {
                    if (!PlayerInput.LeftButtonHeld())
                    {
                        OnItemDropped(draggingMed.UserData as Item, ignoreMousePos: false);
                        draggingMed = null;
                    }
                }

                /*if (GUI.MouseOn?.UserData is Affliction affliction)
                {
                    ShowAfflictionInfo(affliction, afflictionInfoContainer);
                }*/

                if (dropItemAnimTimer > 0.0f)
                {
                    dropItemAnimTimer -= deltaTime;
                    if (dropItemAnimTimer <= 0.0f) droppedItem = null;
                }
            }
            else
            {
                if (openHealthWindow != null && Character != Character.Controlled && Character != Character.Controlled?.SelectedCharacter)
                {
                    openHealthWindow = null;
                }
                highlightedLimbIndex = -1;
            }

            Rectangle hoverArea = alignment == Alignment.Left ?
                Rectangle.Union(HUDLayoutSettings.AfflictionAreaLeft, HUDLayoutSettings.HealthBarAreaLeft) :
                Rectangle.Union(HUDLayoutSettings.AfflictionAreaRight, HUDLayoutSettings.HealthBarAreaRight);

            if (Character.AllowInput && UseHealthWindow && 
                Character.SelectedConstruction?.GetComponent<Controller>()?.User != Character &&
                hoverArea.Contains(PlayerInput.MousePosition) && Inventory.SelectedSlot == null)
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

            suicideButton.Visible = Character == Character.Controlled && Character.IsUnconscious && !Character.IsDead;

            cprButton.Visible =
                Character == Character.Controlled?.SelectedCharacter
                && (Character.IsUnconscious || Character.Stun > 0.0f)
                && !Character.IsDead
                && openHealthWindow == this;

            deadIndicator.Visible = Character.IsDead;
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            if (OpenHealthWindow == this)
            {
                //afflictionContainer.AddToGUIUpdateList();
                afflictionInfoFrame.AddToGUIUpdateList();
                healthWindow.AddToGUIUpdateList();
                healthWindowHealthBarShadow.AddToGUIUpdateList();
                healthWindowHealthBar.AddToGUIUpdateList();
            }
            else if (Character.Controlled == Character)
            {
                healthBarShadow.AddToGUIUpdateList();
                healthBar.AddToGUIUpdateList();
            }
            if (suicideButton.Visible && Character == Character.Controlled) suicideButton.AddToGUIUpdateList();
            if (cprButton != null && cprButton.Visible) cprButton.AddToGUIUpdateList();
        }

        public void DrawHUD(SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD) return;
            if (GameMain.GraphicsWidth != screenResolution.X ||
                GameMain.GraphicsHeight != screenResolution.Y ||
                Math.Abs(inventoryScale - Inventory.UIScale) > 0.01f ||
                Math.Abs(uiScale - GUI.Scale) > 0.01f)
            {
                UpdateAlignment();
            }

            float damageOverlayAlpha = DamageOverlayTimer;
            if (Vitality < MaxVitality * 0.1f)
            {
                damageOverlayAlpha = Math.Max(1.0f - (Vitality / maxVitality * 10.0f), damageOverlayAlpha);
            }
            else
            {
                float pulsateAmount = (float)(Math.Sin(healthBarPulsatePhase) + 1.0f) / 2.0f;
                damageOverlayAlpha = pulsateAmount * healthBarPulsateTimer * damageIntensity;
            }

            if (damageOverlayAlpha > 0.0f)
            {
                DamageOverlay?.Draw(spriteBatch, Vector2.Zero, Color.White * damageOverlayAlpha, Vector2.Zero, 0.0f,
                    new Vector2(GameMain.GraphicsWidth / DamageOverlay.size.X, GameMain.GraphicsHeight / DamageOverlay.size.Y));
            }

            if (Character.Inventory != null)
            {
                if (Character.Inventory.CurrentLayout == CharacterInventory.Layout.Right)
                {
                    //move the healthbar on top of the inventory slots
                    healthBar.RectTransform.ScreenSpaceOffset = new Point(
                        (GameMain.GraphicsWidth - HUDLayoutSettings.Padding) - HUDLayoutSettings.HealthBarAreaRight.Right,
                        HUDLayoutSettings.HealthBarAreaRight.Y - (int)(Character.Inventory.SlotPositions.Max(s => s.Y) + Inventory.EquipIndicator.size.Y * Inventory.UIScale * 2) - HUDLayoutSettings.HealthBarAreaRight.Height);
                    healthBarShadow.RectTransform.ScreenSpaceOffset = healthBar.RectTransform.ScreenSpaceOffset;
                }
                else
                {
                    healthBar.RectTransform.ScreenSpaceOffset = healthBarShadow.RectTransform.ScreenSpaceOffset = Point.Zero;
                }
            }

            DrawStatusHUD(spriteBatch);
        }

        public void DrawStatusHUD(SpriteBatch spriteBatch)
        {
            //Rectangle interactArea = healthBar.Rect;
            if (openHealthWindow != this)
            {
                List<Pair<Affliction, string>> statusIcons = new List<Pair<Affliction, string>>();
                if (Character.CurrentHull == null || Character.CurrentHull.LethalPressure > 5.0f)
                    statusIcons.Add(new Pair<Affliction, string>(pressureAffliction, TextManager.Get("PressureHUDWarning")));
                if (Character.CurrentHull != null && Character.OxygenAvailable < LowOxygenThreshold && oxygenLowAffliction.Strength < oxygenLowAffliction.Prefab.ShowIconThreshold)
                    statusIcons.Add(new Pair<Affliction, string>(oxygenLowAffliction, TextManager.Get("OxygenHUDWarning")));
                
                foreach (Affliction affliction in currentDisplayedAfflictions)
                {
                    statusIcons.Add(new Pair<Affliction, string>(affliction, affliction.Prefab.Name));
                }

                Pair<Affliction, string> highlightedIcon = null;
                Vector2 highlightedIconPos = Vector2.Zero;
                Rectangle afflictionArea = alignment == Alignment.Left ? HUDLayoutSettings.AfflictionAreaLeft : HUDLayoutSettings.AfflictionAreaRight;
                Point pos = afflictionArea.Location + healthBar.RectTransform.ScreenSpaceOffset;

                bool horizontal = afflictionArea.Width > afflictionArea.Height;
                int iconSize = horizontal ? afflictionArea.Height : afflictionArea.Width;

                foreach (Pair<Affliction, string> statusIcon in statusIcons)
                {
                    Rectangle afflictionIconRect = new Rectangle(pos, new Point(iconSize));
                    if (afflictionIconRect.Contains(PlayerInput.MousePosition))
                    {
                        highlightedIcon = statusIcon;
                        highlightedIconPos = afflictionIconRect.Center.ToVector2();
                    }

                    if (statusIcon.First.DamagePerSecond > 1.0f)
                    {
                        Rectangle glowRect = afflictionIconRect;
                        glowRect.Inflate((int)(25 * GUI.Scale), (int)(25 * GUI.Scale));
                        var glow = GUI.Style.GetComponentStyle("OuterGlow");
                        glow.Sprites[GUIComponent.ComponentState.None][0].Draw(
                            spriteBatch, glowRect,
                            Color.Red * (float)((Math.Sin(statusIcon.First.DamagePerSecondTimer * MathHelper.TwoPi - MathHelper.PiOver2) + 1.0f) * 0.5f));
                    }

                    var slot = GUI.Style.GetComponentStyle("AfflictionIconSlot");
                    slot.Sprites[highlightedIcon == statusIcon ? GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None][0].Draw(
                        spriteBatch, afflictionIconRect,
                        highlightedIcon == statusIcon ? slot.HoverColor : slot.Color);


                    statusIcon.First.Prefab.Icon?.Draw(spriteBatch,
                        pos.ToVector2(),
                        highlightedIcon == statusIcon ? statusIcon.First.Prefab.IconColor : statusIcon.First.Prefab.IconColor * 0.8f,
                        rotate: 0,
                        scale: iconSize / statusIcon.First.Prefab.Icon.size.X);

                    if (horizontal)
                        pos.X += iconSize + (int)(5 * GUI.Scale);
                    else
                        pos.Y += iconSize + (int)(5 * GUI.Scale);
                }

                if (highlightedIcon != null)
                {
                    GUI.DrawString(spriteBatch,
                        alignment == Alignment.Left ? highlightedIconPos + new Vector2(60 * GUI.Scale, 5) : highlightedIconPos + new Vector2(-10.0f - GUI.Font.MeasureString(highlightedIcon.Second).X, 5),
                        highlightedIcon.Second,
                        Color.White * 0.8f, Color.Black * 0.5f);
                }

                if (Vitality > 0.0f)
                {
                    float currHealth = healthBar.BarSize;
                    Color prevColor = healthBar.Color;
                    healthBarShadow.BarSize = healthShadowSize;
                    healthBarShadow.Color = Color.Red;
                    healthBarShadow.Visible = true;
                    healthBar.BarSize = currHealth;
                    healthBar.Color = prevColor;
                }
                else
                {
                    healthBarShadow.Visible = false;
                }
            }
            else
            {
                if (Vitality > 0.0f)
                {
                    float currHealth = healthWindowHealthBar.BarSize;
                    Color prevColor = healthWindowHealthBar.Color;
                    healthWindowHealthBarShadow.BarSize = healthShadowSize;
                    healthWindowHealthBarShadow.Color = Color.Red;
                    healthWindowHealthBarShadow.Visible = true;
                    healthWindowHealthBar.BarSize = currHealth;
                    healthWindowHealthBar.Color = prevColor;
                }
                else
                {
                    healthWindowHealthBarShadow.Visible = false;
                }
            }
        }

        private void UpdateAfflictionContainer(LimbHealth selectedLimb)
        {
            ((GUITextBlock)afflictionInfoContainer.Parent.GetChildByUserData("selectedlimbname")).Text = selectedLimb == null ? "" : selectedLimb.Name;

            if (selectedLimb == null)
            {
                afflictionInfoContainer.Content.ClearChildren();
                return;
            }
            var currentAfflictions = GetMatchingAfflictions(selectedLimb, a => a.Strength >= a.Prefab.ShowIconThreshold);
            var displayedAfflictions = afflictionInfoContainer.Content.Children.Select(c => c.UserData as Affliction);
            if (currentAfflictions.Any(a => !displayedAfflictions.Contains(a)) || 
                displayedAfflictions.Any(a => !currentAfflictions.Contains(a)))
            {
                CreateAfflictionInfos(currentAfflictions);
            }

            UpdateAfflictionInfos(displayedAfflictions);
        }

        private void CreateAfflictionInfos(IEnumerable<Affliction> afflictions)
        {
            afflictionInfoContainer.Content.ClearChildren();
            recommendedTreatmentContainer.Content.ClearChildren();
            
            float characterSkillLevel = Character.Controlled == null ? 0.0f : Character.Controlled.GetSkillLevel("medical");

            //random variance is 200% when the skill is 0
            //no random variance if the skill is 50 or more
            float randomVariance = MathHelper.Lerp(2.0f, 0.0f, characterSkillLevel / 50.0f);

            //key = item identifier
            //float = suitability
            Dictionary<string, float> treatmentSuitability = new Dictionary<string, float>();
            GetSuitableTreatments(treatmentSuitability, normalize: true, randomization: randomVariance);

            foreach (Affliction affliction in afflictions)
            {
                var child = new GUILayoutGroup(new RectTransform(Vector2.One * 0.95f, afflictionInfoContainer.Content.RectTransform, Anchor.TopCenter))
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f,
                    UserData = affliction
                };

                var headerContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), child.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    UserData = "header"
                };

                new GUIImage(new RectTransform(new Vector2(0.15f, 1.0f), headerContainer.RectTransform), affliction.Prefab.Icon, scaleToFit: true)
                {
                    Color = affliction.Prefab.IconColor
                };

                var labelContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1.0f), headerContainer.RectTransform), isHorizontal: true)
                {
                    Stretch = true,
                    AbsoluteSpacing = 10,
                    UserData = "label"
                };
                var afflictionName = new GUITextBlock(new RectTransform(new Vector2(0.65f, 1.0f), labelContainer.RectTransform), affliction.Prefab.Name, textAlignment: Alignment.CenterLeft, font: GUI.LargeFont);
                var afflictionStrength = new GUITextBlock(new RectTransform(new Vector2(0.35f, 0.6f), labelContainer.RectTransform), "", textAlignment: Alignment.TopRight, font: GUI.LargeFont)
                {
                    Padding = Vector4.Zero,
                    UserData = "strength"
                };
                var vitality = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), labelContainer.RectTransform, Anchor.BottomRight), "", textAlignment: Alignment.BottomRight)
                {
                    IgnoreLayoutGroups = true,
                    UserData = "vitality"
                };

                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), child.RectTransform),
                    affliction.Prefab.Description, textAlignment: Alignment.TopLeft, wrap: true);
                if (description.Font.MeasureString(description.WrappedText).Y > description.Rect.Height)
                {
                    description.Font = GUI.SmallFont;
                }
                description.RectTransform.Resize(new Point(description.Rect.Width, (int)(description.TextSize.Y + 10)));
                child.RectTransform.Resize(new Point(child.Rect.Width, child.Children.Sum(c => c.Rect.Height)));
                child.Recalculate();
                headerContainer.Recalculate();
                labelContainer.Recalculate();
                afflictionStrength.AutoScale = true;
                afflictionName.AutoScale = true;
                vitality.AutoDraw = true;
            }

            List<KeyValuePair<string, float>> treatmentSuitabilities = treatmentSuitability.OrderByDescending(t => t.Value).ToList();

            foreach (KeyValuePair<string, float> treatment in treatmentSuitabilities)
            {
                ItemPrefab item = MapEntityPrefab.Find(name: null, identifier: treatment.Key, showErrorMessages: false) as ItemPrefab;
                if (item == null) continue;
                int slotSize = (int)(recommendedTreatmentContainer.Content.Rect.Width * 0.5f);

                var itemSlot = new GUIFrame(new RectTransform(new Point(recommendedTreatmentContainer.Content.Rect.Width, slotSize), recommendedTreatmentContainer.Content.RectTransform, Anchor.TopCenter),
                    style: "InnerGlow")
                {
                    UserData = item
                };
                itemSlot.Color = ToolBox.GradientLerp(treatment.Value, Color.Red, Color.Orange, Color.LightGreen);
                itemSlot.SelectedColor = itemSlot.HoverColor = itemSlot.Color;

                Sprite itemSprite = item.InventoryIcon ?? item.sprite;
                Color itemColor = itemSprite == item.sprite ? item.SpriteColor : item.InventoryIconColor;
                var itemIcon = new GUIImage(new RectTransform(new Vector2(0.8f, 0.8f), itemSlot.RectTransform, Anchor.Center),
                    itemSprite, scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = itemColor,
                    HoverColor = itemColor,
                    SelectedColor = itemColor
                };
                itemSlot.ToolTip = item.Name;
            }

            afflictionInfoContainer.Content.RectTransform.SortChildren((r1, r2) =>
            {
                var first = r1.GUIComponent.UserData as Affliction;
                var second = r2.GUIComponent.UserData as Affliction;
                int dmgPerSecond = Math.Sign(second.DamagePerSecond - first.DamagePerSecond);
                return dmgPerSecond != 0 ? dmgPerSecond : Math.Sign(second.Strength - first.Strength);
            });

            //afflictionInfoContainer.Content.RectTransform.SortChildren((r1, r2) =>
            //{
            //    return Math.Sign(((Affliction)r2.GUIComponent.UserData).GetVitalityDecrease(this) - ((Affliction)r1.GUIComponent.UserData).GetVitalityDecrease(this));
            //});
        }

        private void UpdateAfflictionInfos(IEnumerable<Affliction> afflictions)
        {
            foreach (Affliction affliction in afflictions)
            {
                var child = afflictionInfoContainer.Content.FindChild(affliction);
                var headerContainer = child.GetChildByUserData("header");
                var labelContainer = headerContainer.GetChildByUserData("label");
                var strengthText = labelContainer.GetChildByUserData("strength") as GUITextBlock;

                strengthText.Text = strengthTexts[
                    MathHelper.Clamp((int)Math.Floor((affliction.Strength / affliction.Prefab.MaxStrength) * strengthTexts.Length), 0, strengthTexts.Length - 1)];

                strengthText.TextColor = ToolBox.GradientLerp(
                    affliction.Strength / affliction.Prefab.MaxStrength,
                    Color.Yellow, Color.Orange, Color.Red);

                var vitalityText = labelContainer.GetChildByUserData("vitality") as GUITextBlock;
                int vitalityDecrease = (int)affliction.GetVitalityDecrease(this);
                if (vitalityDecrease == 0)
                {
                    vitalityText.Visible = false;
                }
                else
                {
                    vitalityText.Visible = true;
                    vitalityText.Text = TextManager.Get("Vitality") + " -" + vitalityDecrease;
                    vitalityText.TextColor = vitalityDecrease <= 0 ? Color.LightGreen :
                    Color.Lerp(Color.Orange, Color.Red, affliction.Strength / affliction.Prefab.MaxStrength);
                }
            }
        }

        public bool OnItemDropped(Item item, bool ignoreMousePos)
        {
            //items can be dropped outside the health window
            if (!ignoreMousePos &&
                !healthWindow.Rect.Contains(PlayerInput.MousePosition) &&
                !afflictionInfoFrame.Rect.Contains(PlayerInput.MousePosition))
            {
                return false;
            }

            //can't apply treatment to dead characters
            if (Character.IsDead) return true;
            if (item == null || !item.UseInHealthInterface) return true;
            if (!ignoreMousePos)
            {
                if (highlightedLimbIndex > -1)
                {
                    selectedLimbIndex = highlightedLimbIndex;
                }
                else if (!dropItemArea.Rect.Contains(PlayerInput.MousePosition))
                {
                    return true;
                }
            }

            Limb targetLimb = Character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == selectedLimbIndex);

            item.ApplyTreatment(Character.Controlled, Character, targetLimb);

            dropItemAnimTimer = dropItemAnimDuration;
            droppedItem = item;
            return true;
        }

        private List<Item> GetAvailableMedicalItems()
        {
            List<Item> allInventoryItems = new List<Item>();
            allInventoryItems.AddRange(Character.Inventory.Items);
            if (Character.SelectedCharacter?.Inventory != null && Character.CanAccessInventory(Character.SelectedCharacter.Inventory))
            {
                allInventoryItems.AddRange(Character.SelectedCharacter.Inventory.Items);
            }
            if (Character.SelectedBy?.Inventory != null)
            {
                allInventoryItems.AddRange(Character.SelectedBy.Inventory.Items);
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

        private void UpdateLimbIndicators(float deltaTime, Rectangle drawArea)
        {
            limbIndicatorOverlayAnimState += deltaTime * 8.0f;

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
                //afflictionContainer.ClearChildren();
                afflictionInfoContainer.ClearChildren();
            }
        }

        private void DrawHealthWindow(SpriteBatch spriteBatch, Rectangle drawArea, bool allowHighlight, bool highlightAll)
        {
            if (Character.Removed) { return; }

            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;

                float damageLerp = limbHealth.TotalDamage > 0.0f ? MathHelper.Lerp(0.2f, 1.0f, limbHealth.TotalDamage / 100.0f) : 0.0f;
                Color color = Character.IsDead ?
                    Color.Lerp(Color.Black, new Color(150, 100, 100), damageLerp) :
                    ToolBox.GradientLerp(damageLerp, Color.Green, Color.Orange, Color.Red);
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

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, Lights.CustomBlendStates.Multiplicative);

            float overlayScale = Math.Min(
                drawArea.Width / (float)limbIndicatorOverlay.FrameSize.X,
                drawArea.Height / (float)limbIndicatorOverlay.FrameSize.Y);
            
            int frame = 0;
            int frameCount = 17;
            if (limbIndicatorOverlayAnimState >= frameCount * 2) limbIndicatorOverlayAnimState = 0.0f;
            if (limbIndicatorOverlayAnimState < frameCount)
            {
                frame = (int)limbIndicatorOverlayAnimState;
            }
            else
            {
                frame = frameCount - (int)(limbIndicatorOverlayAnimState - (frameCount - 1));
            }

            limbIndicatorOverlay.Draw(spriteBatch, frame, drawArea.Center.ToVector2(), Color.Gray, origin: limbIndicatorOverlay.FrameSize.ToVector2() / 2, rotate: 0.0f,
                scale: Vector2.One * overlayScale);

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, blendState: BlendState.AlphaBlend, rasterizerState: GameMain.ScissorTestEnable);

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

                if (selectedLimbIndex == i)
                {
                    if (alignment == Alignment.Left)
                    {
                        GUI.DrawLine(spriteBatch,
                            highlightArea.Center.ToVector2(),
                            afflictionInfoContainer.Parent.Rect.Location.ToVector2() + Vector2.UnitY * 20,
                            Color.LightBlue * 0.3f, 0, 4);
                    }
                    else
                    {
                        GUI.DrawLine(spriteBatch,
                            highlightArea.Center.ToVector2(),
                           new Vector2(afflictionInfoContainer.Parent.Rect.Right, afflictionInfoContainer.Parent.Rect.Y + 20),
                           Color.LightBlue * 0.3f, 0, 4);
                    }
                }
                
                var slot = GUI.Style.GetComponentStyle("AfflictionIconSlot");

                float iconScale = 0.3f * scale;
                Vector2 iconPos = highlightArea.Center.ToVector2();
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    DrawLimbAfflictionIcon(spriteBatch, affliction, slot, iconScale, ref iconPos);
                }

                foreach (Affliction affliction in afflictions)
                {
                    Limb indicatorLimb = Character.AnimController.GetLimb(affliction.Prefab.IndicatorLimb);
                    if (indicatorLimb != null && indicatorLimb.HealthIndex == i)
                    {
                        DrawLimbAfflictionIcon(spriteBatch, affliction, slot, iconScale, ref iconPos);
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

            if (dropItemAnimTimer > 0.0f && droppedItem?.Prefab.InventoryIcon != null)
            {
                var droppedItemSprite = droppedItem.Prefab.InventoryIcon ?? droppedItem.Sprite;
                droppedItemSprite.Draw(spriteBatch, dropItemArea.Rect.Center.ToVector2(),
                    droppedItemSprite == droppedItem.Sprite ? droppedItem.GetSpriteColor() : droppedItem.GetInventoryIconColor(),
                    origin: droppedItemSprite.size / 2,
                    scale: MathHelper.SmoothStep(0.0f, 100.0f / droppedItemSprite.size.Length(), dropItemAnimTimer / dropItemAnimDuration));
            }
        }

        private void DrawLimbAfflictionIcon(SpriteBatch spriteBatch, Affliction affliction, GUIComponentStyle slotStyle, float iconScale, ref Vector2 iconPos)
        {
            if (affliction.Strength < affliction.Prefab.ShowIconThreshold) return;
            Vector2 iconSize = (affliction.Prefab.Icon.size * iconScale);

            //afflictions that have a strength of less than 10 are faded out slightly
            float alpha = MathHelper.Lerp(0.3f, 1.0f,
                (affliction.Strength - affliction.Prefab.ShowIconThreshold) / Math.Min(affliction.Prefab.MaxStrength - affliction.Prefab.ShowIconThreshold, 10.0f));

            slotStyle.Sprites[GUIComponent.ComponentState.None][0].Draw(
                spriteBatch,
                new Rectangle((iconPos - iconSize / 2.0f).ToPoint(), iconSize.ToPoint()),
                slotStyle.Color * alpha);
            affliction.Prefab.Icon.Draw(spriteBatch, iconPos - iconSize / 2.0f, affliction.Prefab.IconColor * alpha, 0, iconScale);
            iconPos += new Vector2(10.0f, 20.0f) * iconScale;
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
        
        public void ClientRead(IReadMessage inc)
        {
            List<Pair<AfflictionPrefab, float>> newAfflictions = new List<Pair<AfflictionPrefab, float>>();

            byte afflictionCount = inc.ReadByte();
            for (int i = 0; i < afflictionCount; i++)
            {
                AfflictionPrefab afflictionPrefab = AfflictionPrefab.Prefabs[inc.ReadString()].Last();
                float afflictionStrength = inc.ReadRangedSingle(0.0f, afflictionPrefab.MaxStrength, 8);

                newAfflictions.Add(new Pair<AfflictionPrefab, float>(afflictionPrefab, afflictionStrength));
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
                    if (existingAffliction == stunAffliction) Character.SetStun(existingAffliction.Strength, true, true);
                }
            }

            List<Triplet<LimbHealth, AfflictionPrefab, float>> newLimbAfflictions = new List<Triplet<LimbHealth, AfflictionPrefab, float>>();
            byte limbAfflictionCount = inc.ReadByte();
            for (int i = 0; i < limbAfflictionCount; i++)
            {
                int limbIndex = inc.ReadRangedInteger(0, limbHealths.Count - 1);
                AfflictionPrefab afflictionPrefab = AfflictionPrefab.Prefabs[inc.ReadString()].Last();
                float afflictionStrength = inc.ReadRangedSingle(0.0f, afflictionPrefab.MaxStrength, 8);

                newLimbAfflictions.Add(new Triplet<LimbHealth, AfflictionPrefab, float>(limbHealths[limbIndex], afflictionPrefab, afflictionStrength));
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

            CalculateVitality();
            DisplayedVitality = Vitality;
        }

        partial void UpdateLimbAfflictionOverlays()
        {
            foreach (Limb limb in Character.AnimController.Limbs)
            {
                if (limb.HealthIndex < 0 || limb.HealthIndex >= limbHealths.Count) { continue; }

                limb.BurnOverlayStrength = 0.0f;
                limb.DamageOverlayStrength = 0.0f;
                if (limbHealths[limb.HealthIndex].Afflictions.Count == 0) continue;
                foreach (Affliction a in limbHealths[limb.HealthIndex].Afflictions)
                {
                    limb.BurnOverlayStrength += a.Strength / a.Prefab.MaxStrength * a.Prefab.BurnOverlayAlpha;
                    limb.DamageOverlayStrength += a.Strength / a.Prefab.MaxStrength * a.Prefab.DamageOverlayAlpha;
                }
                limb.BurnOverlayStrength /= limbHealths[limb.HealthIndex].Afflictions.Count;
                limb.DamageOverlayStrength /= limbHealths[limb.HealthIndex].Afflictions.Count;
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

            limbIndicatorOverlay?.Remove();
            limbIndicatorOverlay = null;
        }
    }
}
