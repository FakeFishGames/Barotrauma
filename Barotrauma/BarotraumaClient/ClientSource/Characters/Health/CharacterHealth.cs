using Barotrauma.Extensions;
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

        public static string DamageOverlayFile;

        private static string[] strengthTexts;

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
        private float healthShadowSize;
        private float healthShadowDelay;
        private float healthBarPulsateTimer;
        private float healthBarPulsatePhase;

        private float bloodParticleTimer;

        // healing interface
        private GUIFrame healthInterfaceFrame;

        private GUIFrame healthWindow;

        private GUIComponent deadIndicator;

        private GUIComponent lowSkillIndicator;

        private GUILayoutGroup cprLayout;
        private GUIFrame cprFrame;
        private GUIButton cprButton;

        private GUIListBox afflictionTooltip;

        private struct HeartratePosition
        {
            public float Time;
            public float Height;

            public HeartratePosition ScaleHeight(float scale)
            {
                return new HeartratePosition
                {
                    Time = this.Time,
                    Height = this.Height * scale
                };
            }

            public HeartratePosition ScaleTime(float scale)
            {
                return new HeartratePosition
                {
                    Time = this.Time * scale,
                    Height = this.Height
                };
            }

            public HeartratePosition AddTime(float time)
            {
                return new HeartratePosition
                {
                    Time = this.Time + time,
                    Height = this.Height
                };
            }

            public static IEnumerable<HeartratePosition> ScaleAndDisplace(IEnumerable<HeartratePosition> positions, float heightScale, float timeScale, float timeAdd)
            {
                HeartratePosition prevPos = new HeartratePosition
                {
                    Time = 0.0f,
                    Height = 0.0f
                };
                bool wrapped = false;
                foreach (HeartratePosition pos in positions)
                {
                    HeartratePosition newPos = pos.ScaleHeight(heightScale).ScaleTime(timeScale).AddTime(timeAdd);
                    if (newPos.Time > 1.0f)
                    {
                        if (!wrapped)
                        {
                            yield return new HeartratePosition
                            {
                                Time = 1.0f,
                                Height = (newPos.Height - prevPos.Height) / (newPos.Time - prevPos.Time) * (1.0f - prevPos.Time) + prevPos.Height
                            };
                            yield return new HeartratePosition
                            {
                                Time = 0.0f,
                                Height = (newPos.Height - prevPos.Height) / (newPos.Time - prevPos.Time) * (1.0f - prevPos.Time) + prevPos.Height
                            };
                            wrapped = true;
                        }
                        newPos.Time -= 1.0f;
                    }
                    prevPos = newPos;
                    yield return newPos;
                }
            }
        }
        private List<HeartratePosition> heartratePositions;
        private float currentHeartrateTime;
        private float heartbeatTimer;
        private Texture2D heartrateFade;

        private readonly HeartratePosition[] heartbeatPattern = 
        {
            new HeartratePosition() { Time = 0.0f, Height = 0.0f },
            new HeartratePosition() { Time = 0.15f, Height = 0.2f },
            new HeartratePosition() { Time = 0.2f, Height = -0.2f },
            new HeartratePosition() { Time = 0.36f, Height = 0.0f },
            new HeartratePosition() { Time = 0.43f, Height = 0.8f },
            new HeartratePosition() { Time = 0.57f, Height = -0.8f },
            new HeartratePosition() { Time = 0.64f, Height = 0.0f },
            new HeartratePosition() { Time = 0.8f, Height = 0.2f },
            new HeartratePosition() { Time = 0.85f, Height = -0.2f },
            new HeartratePosition() { Time = 1.0f, Height = 0.0f },
        };

        private SpriteSheet limbIndicatorOverlay;
        private float limbIndicatorOverlayAnimState;

        private SpriteSheet medUIExtra;
        private float medUIExtraAnimState;

        private GUIComponent draggingMed;

        private int highlightedLimbIndex = -1;
        private int selectedLimbIndex = -1;
        private LimbHealth currentDisplayedLimb;

        private GUIProgressBar healthWindowHealthBar;
        private GUIProgressBar healthWindowHealthBarShadow;

        private GUITextBlock characterName;
        private GUIFrame afflictionInfoFrame;
        private GUIListBox afflictionIconContainer;
        private GUIListBox afflictionInfoContainer;
        private GUILayoutGroup treatmentLayout;
        private GUIListBox recommendedTreatmentContainer;
        private GUITextBlock selectedLimbText;

        private float distortTimer;

        // 0-1
        private float damageIntensity;
        private readonly float damageIntensityDropdownRate = 0.1f;

        public float DamageOverlayTimer { get; private set; }

        private float updateDisplayedAfflictionsTimer;
        private const float UpdateDisplayedAfflictionsInterval = 0.5f;
        private List<Affliction> currentDisplayedAfflictions = new List<Affliction>();

        public float DisplayedVitality, DisplayVitalityDelay;

        public bool MouseOnElement
        {
            get { return highlightedLimbIndex > -1; }
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

            bool horizontal = true;
            healthBar = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: GUIColorSettings.HealthBarColorHigh, style: horizontal ? "CharacterHealthBar" : "GUIProgressBarVertical")
            {
                Enabled = true,
                HoverCursor = CursorState.Hand,
                IsHorizontal = horizontal
            };
            healthBarShadow = new GUIProgressBar(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.HealthBarAreaLeft, GUI.Canvas),
                barSize: 1.0f, color: Color.Green, style: horizontal ? "CharacterHealthBar" : "GUIProgressBarVertical", showFrame: false)
            {
                IsHorizontal = horizontal
            };
            healthShadowSize = 1.0f;

            healthInterfaceFrame = new GUIFrame(new RectTransform(new Vector2(0.85f * 1.1f, 0.66f * 0.85f * 1.1f), GUI.Canvas, anchor: Anchor.Center, scaleBasis: ScaleBasis.Smallest), style: "ItemUI");

            var healthInterfaceLayout = new GUILayoutGroup(new RectTransform(Vector2.One / 1.05f, healthInterfaceFrame.RectTransform, anchor: Anchor.Center), true);

            var healthWindowContainer = new GUIFrame(new RectTransform(new Vector2(0.45f, 1.0f), healthInterfaceLayout.RectTransform), style: null);

            //limb selection frame
            healthWindow = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), healthWindowContainer.RectTransform, Anchor.CenterRight, Pivot.CenterRight), style: "GUIFrameListBox");

            var healthWindowVerticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One * 0.95f, healthWindow.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var paddedHealthWindow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.95f), healthWindowVerticalLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var limbSelection = new GUICustomComponent(new RectTransform(new Vector2(0.6f, 1.0f), paddedHealthWindow.RectTransform),
                (spriteBatch, component) =>
                {
                    DrawHealthWindow(spriteBatch, component.RectTransform.Rect, true);
                },
                (deltaTime, component) =>
                {
                    UpdateLimbIndicators(deltaTime, component.RectTransform.Rect);
                }
            );
            deadIndicator = new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.1f), limbSelection.RectTransform, Anchor.Center),
                text: TextManager.Get("Deceased"), font: GUI.LargeFont, textAlignment: Alignment.Center, wrap: true, style: "GUIToolTip")
            {
                Visible = false,
                CanBeFocused = false
            };

            var rightSide = new GUIFrame(new RectTransform(new Vector2(0.4f, 1.0f), paddedHealthWindow.RectTransform), style: null);

            new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.3f), rightSide.RectTransform, Anchor.BottomRight, Pivot.BottomRight),
                (sb, component) =>
                {
                    if (medUIExtra == null) { return; }
                    float overlayScale = Math.Min(
                        component.Rect.Width / (float)medUIExtra.FrameSize.X,
                        component.Rect.Height / (float)medUIExtra.FrameSize.Y);

                    int frame = (int)medUIExtraAnimState;

                    medUIExtra.Draw(sb, frame, component.Rect.Center.ToVector2(), Color.Gray, origin: medUIExtra.FrameSize.ToVector2() / 2, rotate: 0.0f,
                        scale: Vector2.One * overlayScale);
                },
                (dt, component) =>
                {
                    medUIExtraAnimState += dt * 10.0f;
                    while (medUIExtraAnimState >= 16.0f)
                    {
                        medUIExtraAnimState -= 16.0f;
                    }
                });

            GUILayoutGroup selectedLimbLayout = new GUILayoutGroup(new RectTransform(Vector2.One, rightSide.RectTransform));

            selectedLimbText = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.08f), selectedLimbLayout.RectTransform), "", font: GUI.LargeFont, textAlignment: Alignment.Center)
            {
                AutoScaleHorizontal = true
            };

            afflictionIconContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.92f), selectedLimbLayout.RectTransform), style: null)
            {
                KeepSpaceForScrollBar = true
            };

            var healthBarContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.07f), healthWindowVerticalLayout.RectTransform), style: null);

            var healthBarIcon = new GUIFrame(new RectTransform(new Vector2(0.095f, 1.0f), healthBarContainer.RectTransform), style: "GUIHealthBarIcon");

            healthWindowHealthBarShadow = new GUIProgressBar(new RectTransform(new Vector2(0.91f, 1.0f), healthBarContainer.RectTransform, Anchor.CenterRight),
                barSize: 1.0f, color: GUI.Style.Green, style: "GUIHealthBar")
            {
                IsHorizontal = true
            };
            healthWindowHealthBar = new GUIProgressBar(new RectTransform(new Vector2(0.91f, 1.0f), healthBarContainer.RectTransform, Anchor.CenterRight),
                barSize: 1.0f, color: GUI.Style.Green, style: "GUIHealthBar")
            {
                IsHorizontal = true
            };

            //affliction info frame
            afflictionInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.55f, 1.0f), healthInterfaceLayout.RectTransform), style: null);
            var paddedInfoFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), afflictionInfoFrame.RectTransform, Anchor.Center), style: null);

            var infoLayout = new GUILayoutGroup(new RectTransform(Vector2.One, paddedInfoFrame.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var textContainer = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.6f), infoLayout.RectTransform), style: "GUIFrameListBox");

            var textLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1.0f), textContainer.RectTransform, Anchor.Center, Pivot.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            var nameContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), textLayout.RectTransform) { MinSize = new Point(0, 20) }, isHorizontal: true)
            {
                Stretch = true
            };

            new GUICustomComponent(new RectTransform(new Vector2(0.15f, 1.0f), nameContainer.RectTransform),
                onDraw: (spriteBatch, component) =>
                {
                    character.Info.DrawPortrait(spriteBatch, new Vector2(component.Rect.X, component.Rect.Center.Y - component.Rect.Width / 2), component.Rect.Width);
                    character.Info.DrawJobIcon(spriteBatch, new Vector2(component.Rect.Right + component.Rect.Width, (float)component.Rect.Top + component.Rect.Height * 0.75f), 0.75f);
                });
            characterName = new GUITextBlock(new RectTransform(new Vector2(0.85f, 1.0f), nameContainer.RectTransform), "", textAlignment: Alignment.BottomLeft, font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), textLayout.RectTransform), style: "HorizontalLine");

            afflictionInfoContainer = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), textLayout.RectTransform, Anchor.TopLeft), style: null);

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), textLayout.RectTransform), style: "HorizontalLine");

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), textLayout.RectTransform, Anchor.TopLeft), TextManager.Get("SuitableTreatments"), font: GUI.SubHeadingFont);

            treatmentLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), textLayout.RectTransform), true)
            {
                Stretch = false
            };

            recommendedTreatmentContainer = new GUIListBox(new RectTransform(new Vector2(0.9f, 1.0f), treatmentLayout.RectTransform, Anchor.Center, Pivot.Center), isHorizontal: true, style: null)
            {
                KeepSpaceForScrollBar = false
            };

            lowSkillIndicator = new GUIImage(new RectTransform(new Vector2(0.1f, 1.0f), treatmentLayout.RectTransform, Anchor.TopLeft, Pivot.Center),
                style: "GUINotificationButton")
            {
                ToolTip = TextManager.Get("lowmedicalskillwarning"),
                Color = GUI.Style.Orange,
                HoverColor = Color.Lerp(GUI.Style.Orange, Color.White, 0.5f),
                PressedColor = Color.Lerp(GUI.Style.Orange, Color.White, 0.5f),
                Visible = false
            };
            lowSkillIndicator.RectTransform.MaxSize = new Point(lowSkillIndicator.Rect.Height);

            var tempFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), textLayout.RectTransform), style: null);

            cprLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), infoLayout.RectTransform), true)
            {
                Stretch = true
            };

            cprFrame = new GUIFrame(new RectTransform(new Vector2(0.7f, 1.0f), cprLayout.RectTransform), style: "GUIFrameListBox");

            heartrateFade = TextureLoader.FromFile("Content/UI/Health/HeartrateFade.png");

            new GUICustomComponent(new RectTransform(Vector2.One * 0.95f, cprFrame.RectTransform, Anchor.Center), DrawHeartrate, UpdateHeartrate);

            heartbeatTimer = 0.46f;

            heartratePositions = new List<HeartratePosition>
            {
                heartbeatPattern.First(),
                heartbeatPattern.Last()
            };

            cprButton = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), cprLayout.RectTransform, scaleBasis: ScaleBasis.Smallest), text: "", style: "CPRButton")
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
                TextManager.Get("GiveInButton"), style: "GUIButtonLarge")
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
                        case "meduisilhouette":
                            limbIndicatorOverlay = new SpriteSheet(subElement);
                            break;
                        case "meduiextra":
                            medUIExtra = new SpriteSheet(subElement);
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
            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            inventoryScale = Inventory.UIScale;
            uiScale = GUI.Scale;

            switch (alignment)
            {
                case Alignment.Left:
                    healthInterfaceFrame.RectTransform.Anchor = Anchor.CenterLeft;
                    healthInterfaceFrame.RectTransform.Pivot = Pivot.CenterLeft;
                    break;
                case Alignment.Right:
                    healthInterfaceFrame.RectTransform.Anchor = Anchor.CenterRight;
                    healthInterfaceFrame.RectTransform.Pivot = Pivot.CenterRight;
                    break;
            }
            healthInterfaceFrame.RectTransform.RecalculateChildren(false);
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
                if (HUD.CloseHUD(healthInterfaceFrame.Rect))
                {
                    //emulate a Health input to get the character to deselect the item server-side
                    if (GameMain.Client != null)
                    {
                        Character.Controlled.Keys[(int)InputType.Health].Hit = true;
                    }
                    OpenHealthWindow = null;
                }

                if (GUI.MouseOn != null && GUI.MouseOn.UserData is string str && str == "selectaffliction")
                {
                    Affliction affliction = GUI.MouseOn.Parent.UserData as Affliction;

                    if (afflictionTooltip == null || afflictionTooltip.UserData != affliction)
                    {
                        afflictionTooltip = new GUIListBox(new RectTransform(new Vector2(0.4f, 0.2f), GUI.Canvas, scaleBasis: ScaleBasis.Smallest))
                        {
                            UserData = affliction,
                            CanBeFocused = false
                        };

                        CreateAfflictionInfoElements(afflictionTooltip.Content, affliction);

                        int height = afflictionTooltip.Content.Children.Sum(c => c.Rect.Height) + 10;
                        afflictionTooltip.RectTransform.Resize(new Point(afflictionTooltip.Rect.Width, height), true);
                        afflictionTooltip.RectTransform.AbsoluteOffset = new Point(GUI.MouseOn.Rect.Right, GUI.MouseOn.Rect.Y);
                        afflictionTooltip.ScrollBarVisible = false;

                        var labelContainer = afflictionTooltip.Content.GetChildByUserData("label");

                        labelContainer.RectTransform.Resize(new Point(labelContainer.Rect.Width, (int)(GUI.LargeFont.Size * 1.5f)));
                    }
                }
                else
                {
                    afflictionTooltip = null;
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
                healthBar.Color = healthWindowHealthBar.Color = ToolBox.GradientLerp(DisplayedVitality / MaxVitality, GUIColorSettings.HealthBarColorLow, GUIColorSettings.HealthBarColorMedium, GUIColorSettings.HealthBarColorHigh);
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
                lowSkillIndicator.IgnoreLayoutGroups = !lowSkillIndicator.Visible;

                recommendedTreatmentContainer.RectTransform.Resize(new Vector2(0.9f, 1.0f));
                lowSkillIndicator.RectTransform.Resize(new Vector2(0.1f, 1.0f));

                treatmentLayout.Recalculate();

                lowSkillIndicator.Color = new Color(lowSkillIndicator.Color, MathHelper.Lerp(0.1f, 1.0f, (float)(Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 2.0f));

                if (Inventory.draggingItem != null)
                {
                    if (highlightedLimbIndex > -1)
                    {
                        selectedLimbIndex = highlightedLimbIndex;
                    }
                }

                if (draggingMed != null)
                {
                    if (!PlayerInput.PrimaryMouseButtonHeld())
                    {
                        OnItemDropped(draggingMed.UserData as Item, ignoreMousePos: false);
                        draggingMed = null;
                    }
                }

                /*if (GUI.MouseOn?.UserData is Affliction affliction)
                {
                    ShowAfflictionInfo(affliction, afflictionInfoContainer);
                }*/
            }
            else
            {
                if (openHealthWindow != null && Character != Character.Controlled && Character != Character.Controlled?.SelectedCharacter)
                {
                    openHealthWindow = null;
                }
                highlightedLimbIndex = -1;
            }

            Rectangle hoverArea = Rectangle.Union(HUDLayoutSettings.AfflictionAreaLeft, HUDLayoutSettings.HealthBarAreaLeft);

            if (Character.AllowInput && UseHealthWindow && 
                Character.SelectedConstruction?.GetComponent<Controller>()?.User != Character &&
                hoverArea.Contains(PlayerInput.MousePosition) && Inventory.SelectedSlot == null)
            {
                healthBar.State = GUIComponent.ComponentState.Hover;
                if (PlayerInput.PrimaryMouseButtonClicked())
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
            cprButton.IgnoreLayoutGroups = !cprButton.Visible;

            cprFrame.RectTransform.Resize(new Vector2(0.7f, 1.0f));
            cprButton.RectTransform.Resize(new Vector2(1.0f, 1.0f));

            cprLayout.Recalculate();

            deadIndicator.Visible = Character.IsDead;
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            if (OpenHealthWindow == this)
            {
                healthInterfaceFrame.AddToGUIUpdateList();
                afflictionTooltip?.AddToGUIUpdateList();
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
                healthBar.RectTransform.ScreenSpaceOffset = healthBarShadow.RectTransform.ScreenSpaceOffset = Point.Zero;
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
                Rectangle afflictionArea = HUDLayoutSettings.AfflictionAreaLeft;
                Point pos = afflictionArea.Location + healthBar.RectTransform.ScreenSpaceOffset;

                bool horizontal = afflictionArea.Width > afflictionArea.Height;
                int iconSize = horizontal ? afflictionArea.Height : afflictionArea.Width;

                foreach (Pair<Affliction, string> statusIcon in statusIcons)
                {
                    Affliction affliction = statusIcon.First;
                    AfflictionPrefab afflictionPrefab = affliction.Prefab;

                    Rectangle afflictionIconRect = new Rectangle(pos, new Point(iconSize));
                    if (afflictionIconRect.Contains(PlayerInput.MousePosition))
                    {
                        highlightedIcon = statusIcon;
                        highlightedIconPos = afflictionIconRect.Center.ToVector2();
                    }

                    if (affliction.DamagePerSecond > 1.0f)
                    {
                        Rectangle glowRect = afflictionIconRect;
                        glowRect.Inflate((int)(25 * GUI.Scale), (int)(25 * GUI.Scale));
                        var glow = GUI.Style.GetComponentStyle("OuterGlow");
                        glow.Sprites[GUIComponent.ComponentState.None][0].Draw(
                            spriteBatch, glowRect,
                            GUI.Style.Red * (float)((Math.Sin(affliction.DamagePerSecondTimer * MathHelper.TwoPi - MathHelper.PiOver2) + 1.0f) * 0.5f));
                    }

                    /*var slot = GUI.Style.GetComponentStyle("AfflictionIconSlot");
                    slot.Sprites[highlightedIcon == statusIcon ? GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None][0].Draw(
                        spriteBatch, afflictionIconRect,
                        highlightedIcon == statusIcon ? slot.HoverColor : slot.Color);*/


                    float alphaMultiplier = highlightedIcon == statusIcon ? 1f : 0.8f;

                    afflictionPrefab.Icon?.Draw(spriteBatch,
                        pos.ToVector2(),
                        /*highlightedIcon == statusIcon ? statusIcon.First.Prefab.IconColor : statusIcon.First.Prefab.IconColor * 0.8f,*/ // OLD IMPLEMENTATION
                        GetAfflictionIconColor(afflictionPrefab, affliction) * alphaMultiplier,
                        rotate: 0,
                        scale: iconSize / afflictionPrefab.Icon.size.X);

                    if (horizontal)
                        pos.X += iconSize + (int)(5 * GUI.Scale);
                    else
                        pos.Y += iconSize + (int)(5 * GUI.Scale);
                }

                if (highlightedIcon != null)
                {
                    GUI.DrawString(spriteBatch,
                        alignment == Alignment.Left ? highlightedIconPos + new Vector2(60 * GUI.Scale, 5) : highlightedIconPos + new Vector2(-iconSize / 2, iconSize / 2),
                        highlightedIcon.Second,
                        Color.White * 0.8f, Color.Black * 0.5f);
                }

                if (Vitality > 0.0f)
                {
                    float currHealth = healthBar.BarSize;
                    Color prevColor = healthBar.Color;
                    healthBarShadow.BarSize = healthShadowSize;
                    healthBarShadow.Color = GUI.Style.Red;
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
                    healthWindowHealthBarShadow.Color = GUI.Style.Red;
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

        private Color GetAfflictionIconColor(AfflictionPrefab prefab, Affliction affliction)
        {
            // No specific colors, use generic
            if (prefab.IconColors == null)
            {
                if (prefab.IsBuff)
                {
                    return ToolBox.GradientLerp(affliction.Strength / prefab.MaxStrength, GUIColorSettings.BuffColorLow, GUIColorSettings.BuffColorMedium, GUIColorSettings.BuffColorHigh);
                }
                else
                {
                    return ToolBox.GradientLerp(affliction.Strength / prefab.MaxStrength, GUIColorSettings.DebuffColorLow, GUIColorSettings.DebuffColorMedium, GUIColorSettings.DebuffColorHigh);
                }
            }
            else
            {
                return ToolBox.GradientLerp(affliction.Strength / prefab.MaxStrength, prefab.IconColors);
            }
        }

        private void UpdateAfflictionContainer(LimbHealth selectedLimb)
        {
            selectedLimbText.Text = selectedLimb == null ? "" : selectedLimb.Name;

            if (selectedLimb == null)
            {
                afflictionIconContainer.Content.ClearChildren();
                return;
            }
            var currentAfflictions = GetMatchingAfflictions(selectedLimb, a => a.Strength >= a.Prefab.ShowIconThreshold);
            var displayedAfflictions = afflictionIconContainer.Content.Children.Select(c => c.UserData as Affliction);
            if (currentAfflictions.Any(a => !displayedAfflictions.Contains(a)) || 
                displayedAfflictions.Any(a => !currentAfflictions.Contains(a)))
            {
                CreateAfflictionInfos(currentAfflictions);
            }

            UpdateAfflictionInfos(displayedAfflictions);
        }

        private void CreateAfflictionInfos(IEnumerable<Affliction> afflictions)
        {
            afflictionIconContainer.ClearChildren();
            afflictionInfoContainer.ClearChildren();
            afflictionInfoContainer.UserData = null;
            recommendedTreatmentContainer.Content.ClearChildren();
            
            float characterSkillLevel = Character.Controlled == null ? 0.0f : Character.Controlled.GetSkillLevel("medical");

            //random variance is 200% when the skill is 0
            //no random variance if the skill is 50 or more
            float randomVariance = MathHelper.Lerp(2.0f, 0.0f, characterSkillLevel / 50.0f);

            //key = item identifier
            //float = suitability
            Dictionary<string, float> treatmentSuitability = new Dictionary<string, float>();
            GetSuitableTreatments(treatmentSuitability, normalize: true, randomization: randomVariance);

            Affliction mostSevereAffliction = afflictions.FirstOrDefault(a => !a.Prefab.IsBuff && !afflictions.Any(a2 => !a2.Prefab.IsBuff && a2.Strength > a.Strength)) ?? afflictions.FirstOrDefault();
            GUIButton buttonToSelect = null;

            foreach (Affliction affliction in afflictions)
            {
                var child = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), afflictionIconContainer.Content.RectTransform, Anchor.TopCenter))
                {
                    UserData = affliction
                };

                var button = new GUIButton(new RectTransform(new Vector2(1.0f, 0.9f), child.RectTransform), style: null)
                {
                    Color = Color.Gray.Multiply(0.1f).Opaque(),
                    HoverColor = Color.Gray.Multiply(0.4f).Opaque(),
                    SelectedColor = Color.Gray.Multiply(0.25f).Opaque(),
                    PressedColor = Color.Gray.Multiply(0.2f).Opaque(),
                    UserData = "selectaffliction",
                    OnClicked = SelectAffliction
                };

                if (affliction == mostSevereAffliction)
                {
                    buttonToSelect = button;
                }

                var afflictionIcon = new GUIImage(new RectTransform(Vector2.One * 0.9f, button.RectTransform, Anchor.Center), affliction.Prefab.Icon, scaleToFit: true)
                {
                    Color = GetAfflictionIconColor(affliction.Prefab, affliction),
                    CanBeFocused = false
                };
                afflictionIcon.PressedColor = afflictionIcon.Color;
                afflictionIcon.HoverColor = Color.Lerp(afflictionIcon.Color, Color.White, 0.6f);
                afflictionIcon.SelectedColor = Color.Lerp(afflictionIcon.Color, Color.White, 0.5f);

                float afflictionVitalityDecrease = affliction.GetVitalityDecrease(this);

                Color afflictionEffectColor = Color.White;
                if (afflictionVitalityDecrease > 0.0f)
                {
                    afflictionEffectColor = GUI.Style.Red;
                }
                else if (afflictionVitalityDecrease < 0.0f)
                {
                    afflictionEffectColor = GUI.Style.Green;
                }

                new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.1f), child.RectTransform), 0.0f, afflictionEffectColor, style: "CharacterHealthBar")
                {
                    UserData = "afflictionstrength"
                };

                child.Recalculate();
            }

            if (buttonToSelect != null) { buttonToSelect.OnClicked(buttonToSelect, "selectaffliction"); }

            afflictionIconContainer.RecalculateChildren();

            List<KeyValuePair<string, float>> treatmentSuitabilities = treatmentSuitability.OrderByDescending(t => t.Value).ToList();

            int count = 0;
            foreach (KeyValuePair<string, float> treatment in treatmentSuitabilities)
            {
                count++;
                if (count > 5) { break; }
                if (!(MapEntityPrefab.Find(name: null, identifier: treatment.Key, showErrorMessages: false) is ItemPrefab item)) { continue; }

                var itemSlot = new GUIFrame(new RectTransform(new Vector2(1.0f / 7.0f, 1.0f), recommendedTreatmentContainer.Content.RectTransform, Anchor.TopLeft),
                    style: null)
                {
                    UserData = item
                };

                var innerFrame = new GUIFrame(new RectTransform(Vector2.One, itemSlot.RectTransform, Anchor.Center, Pivot.Center, scaleBasis: ScaleBasis.Smallest), style: "GUIFrameListBox")
                {
                    CanBeFocused = false
                };
                Sprite itemSprite = item.InventoryIcon ?? item.sprite;
                Color itemColor = itemSprite == item.sprite ? item.SpriteColor : item.InventoryIconColor;
                var itemIcon = new GUIImage(new RectTransform(new Vector2(0.8f, 0.8f), innerFrame.RectTransform, Anchor.Center),
                    itemSprite, scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = itemColor,
                    HoverColor = itemColor,
                    SelectedColor = itemColor
                };
                itemSlot.ToolTip = item.Name;
            }

            recommendedTreatmentContainer.RecalculateChildren();

            afflictionIconContainer.Content.RectTransform.SortChildren((r1, r2) =>
            {
                var first = r1.GUIComponent.UserData as Affliction;
                var second = r2.GUIComponent.UserData as Affliction;
                int dmgPerSecond = Math.Sign(second.DamagePerSecond - first.DamagePerSecond);
                return dmgPerSecond != 0 ? dmgPerSecond : Math.Sign(second.Strength - first.Strength);
            });

            //afflictionIconContainer.Content.RectTransform.SortChildren((r1, r2) =>
            //{
            //    return Math.Sign(((Affliction)r2.GUIComponent.UserData).GetVitalityDecrease(this) - ((Affliction)r1.GUIComponent.UserData).GetVitalityDecrease(this));
            //});
        }

        private void CreateAfflictionInfoElements(GUIComponent parent, Affliction affliction)
        {
            var labelContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = 10,
                UserData = "label",
                CanBeFocused = false
            };

            var afflictionName = new GUITextBlock(new RectTransform(new Vector2(0.65f, 1.0f), labelContainer.RectTransform), affliction.Prefab.Name, textAlignment: Alignment.CenterLeft, font: GUI.LargeFont)
            {
                CanBeFocused = false
            };
            var afflictionStrength = new GUITextBlock(new RectTransform(new Vector2(0.35f, 0.6f), labelContainer.RectTransform), "", textAlignment: Alignment.TopRight, font: GUI.LargeFont)
            {
                Padding = Vector4.Zero,
                UserData = "strength",
                CanBeFocused = false
            };
            var vitality = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.4f), labelContainer.RectTransform, Anchor.BottomRight), "", textAlignment: Alignment.BottomRight)
            {
                IgnoreLayoutGroups = true,
                UserData = "vitality",
                CanBeFocused = false
            };

            var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), parent.RectTransform),
                affliction.Prefab.Description, textAlignment: Alignment.TopLeft, wrap: true)
            {
                CanBeFocused = false
            };

            if (description.Font.MeasureString(description.WrappedText).Y > description.Rect.Height)
            {
                description.Font = GUI.SmallFont;
            }

            Point nameDims = new Point(afflictionName.Rect.Width, (int)(GUI.LargeFont.Size * 1.5f));

            labelContainer.RectTransform.Resize(new Point(labelContainer.Rect.Width, nameDims.Y));
            afflictionName.RectTransform.Resize(nameDims);
            afflictionStrength.RectTransform.Resize(new Point(afflictionStrength.Rect.Width, nameDims.Y));

            afflictionStrength.Text = strengthTexts[
                MathHelper.Clamp((int)Math.Floor((affliction.Strength / affliction.Prefab.MaxStrength) * strengthTexts.Length), 0, strengthTexts.Length - 1)];

            afflictionStrength.TextColor = Color.Lerp(GUI.Style.Orange, GUI.Style.Red,
                affliction.Strength / affliction.Prefab.MaxStrength);

            description.RectTransform.Resize(new Point(description.Rect.Width, (int)(description.TextSize.Y + 10)));
            //labelContainer.Recalculate();

            int vitalityDecrease = (int)affliction.GetVitalityDecrease(this);
            if (vitalityDecrease == 0)
            {
                vitality.Visible = false;
            }
            else
            {
                vitality.Visible = true;
                vitality.Text = TextManager.Get("Vitality") + " -" + vitalityDecrease;
                vitality.TextColor = vitalityDecrease <= 0 ? GUI.Style.Green :
                Color.Lerp(GUI.Style.Orange, GUI.Style.Red, affliction.Strength / affliction.Prefab.MaxStrength);
            }

            vitality.AutoDraw = true;
        }

        private bool SelectAffliction(GUIButton button, object userData)
        {
            Affliction affliction = button.Parent.UserData as Affliction;

            bool selected = button.Selected;

            afflictionInfoContainer.UserData = null;
            afflictionInfoContainer.ClearChildren();
            if (!selected)
            {
                afflictionInfoContainer.UserData = affliction;

                CreateAfflictionInfoElements(afflictionInfoContainer.Content, affliction);

                afflictionInfoContainer.RecalculateChildren();
            }

            foreach (var child in afflictionIconContainer.Content.Children)
            {
                GUIButton btn = child.GetChild<GUIButton>();
                if (btn != null)
                {
                    btn.Selected = btn == button && !selected;
                }
            }

            return false;
        }

        private void UpdateHeartrate(float deltaTime, GUICustomComponent component)
        {
            if (GameMain.Instance.Paused) { return; }

            heartbeatTimer -= deltaTime * 0.5f;

            if (heartbeatTimer <= 0.0f)
            {
                while (heartbeatTimer <= 0.0f) { heartbeatTimer += 0.5f; }

                IEnumerable<HeartratePosition> newPositions;
                if (Character == null || Character.IsDead || Character.IsUnconscious)
                {
                    newPositions = Enumerable.Repeat(new HeartratePosition { Time = currentHeartrateTime, Height = 0.0f }, 1);
                }
                else
                {
                    newPositions = HeartratePosition.ScaleAndDisplace(heartbeatPattern, 1.0f, 0.1f, currentHeartrateTime);
                }

                float visibleRangeStart = currentHeartrateTime - 0.35f;
                if (visibleRangeStart < 0.0f)
                {
                    visibleRangeStart += 1.0f;
                }
                heartratePositions.RemoveAll(hp => (hp.Time < visibleRangeStart || hp.Time > currentHeartrateTime) &&
                                                   ((hp.Time < visibleRangeStart && hp.Time > currentHeartrateTime) || visibleRangeStart < currentHeartrateTime));

                heartratePositions.AddRange(newPositions);

                if (!heartratePositions.Any(hp => hp.Time >= 1.0f))
                {
                    heartratePositions.Add(new HeartratePosition { Time = 1.0f, Height = 0.0f });
                }
                if (!heartratePositions.Any(hp => hp.Time <= 0.0f))
                {
                    heartratePositions.Add(new HeartratePosition { Time = 0.0f, Height = 0.0f });
                }
            }

            currentHeartrateTime += deltaTime * 0.5f;
            while (currentHeartrateTime >= 1.0f)
            {
                currentHeartrateTime -= 1.0f;
            }
        }

        private void DrawHeartrate(SpriteBatch spriteBatch, GUICustomComponent component)
        {
            Rectangle targetRect = component.Parent.Rect;
            targetRect.Location += new Point(6, 6);
            targetRect.Size -= new Point(12, 12);

            //GUI.DrawRectangle(spriteBatch, targetRect, Color.Black, true);

            bool first = true;
            Vector2 prevPos = Vector2.Zero;
            foreach (var heartratePosition in heartratePositions.OrderBy(hp => hp.Time))
            {
                Vector2 pos = new Vector2(heartratePosition.Time, -heartratePosition.Height * 0.5f + 0.5f) * targetRect.Size.ToVector2() + targetRect.Location.ToVector2();

                if (pos.X < targetRect.Left + 1) { pos.X = targetRect.Left + 1; }
                if (pos.X > targetRect.Right - 1) { pos.X = targetRect.Right - 1; }

                if (first)
                {
                    first = false;
                }
                else
                {
                    int thickness = (int)(GUI.Scale * 2.5f);
                    if (thickness < 1) { thickness = 1; }
                    GUI.DrawLine(spriteBatch, prevPos, pos, Color.Lime, 0, thickness);
                    GUI.DrawLine(spriteBatch, prevPos + new Vector2(0.0f, 1.0f), pos + new Vector2(0.0f, 1.0f), Color.Lime * 0.5f, 0, thickness);
                    GUI.DrawLine(spriteBatch, prevPos - new Vector2(0.0f, 1.0f), pos - new Vector2(0.0f, 1.0f), Color.Lime * 0.5f, 0, thickness);
                }

                prevPos = pos;
            }

            Rectangle sourceRect = heartrateFade.Bounds;

            Rectangle destinationRectangle = new Rectangle(
                new Point((int)(currentHeartrateTime * targetRect.Width) + targetRect.Left - targetRect.Height, targetRect.Top),
                new Point((int)(targetRect.Height * ((float)sourceRect.Width / (float)sourceRect.Height)), targetRect.Height));

            if (destinationRectangle.Left < targetRect.Left)
            {
                Rectangle destinationRectangle2 = new Rectangle();
                destinationRectangle2.Location = new Point(targetRect.Right - (targetRect.Left - destinationRectangle.Left), targetRect.Top);
                destinationRectangle2.Size = new Point(targetRect.Right - destinationRectangle2.Left, targetRect.Height);

                int originalWidth = sourceRect.Width;
                sourceRect.Width = (int)(sourceRect.Width * ((float)(destinationRectangle.Right - targetRect.Left) / (float)targetRect.Height));
                sourceRect.X += originalWidth - sourceRect.Width;

                Rectangle sourceRect2 = heartrateFade.Bounds;
                sourceRect2.Width -= sourceRect.Width;
                spriteBatch.Draw(heartrateFade, destinationRectangle2, sourceRect2, Color.White);

                originalWidth = destinationRectangle.Width;
                int newWidth = destinationRectangle.Right - targetRect.Left;

                destinationRectangle.Size = new Point(newWidth, targetRect.Height);
                destinationRectangle.X += originalWidth - newWidth;

                GUI.DrawRectangle(spriteBatch, new Rectangle(destinationRectangle.Right, destinationRectangle.Top,
                     destinationRectangle2.Left - destinationRectangle.Right, destinationRectangle2.Height), Color.Black, true);
            }
            else
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(destinationRectangle.Right, destinationRectangle.Top,
                     targetRect.Right - destinationRectangle.Right, destinationRectangle.Height), Color.Black, true);
                GUI.DrawRectangle(spriteBatch, new Rectangle(targetRect.Left, destinationRectangle.Top,
                     destinationRectangle.Left - targetRect.Left, destinationRectangle.Height), Color.Black, true);
            }

            spriteBatch.Draw(heartrateFade, destinationRectangle, sourceRect, Color.White);
        }

        private void UpdateAfflictionInfos(IEnumerable<Affliction> afflictions)
        {
            foreach (Affliction affliction in afflictions)
            {
                var child = afflictionIconContainer.Content.FindChild(affliction);
                var afflictionStrengthBar = child.GetChildByUserData("afflictionstrength") as GUIProgressBar;
                afflictionStrengthBar.BarSize = affliction.Strength / affliction.Prefab.MaxStrength;

                if (afflictionInfoContainer.UserData == affliction)
                {
                    UpdateAfflictionInfo(afflictionInfoContainer.Content, affliction);
                }

                if (afflictionTooltip != null && afflictionTooltip.UserData == affliction)
                {
                    UpdateAfflictionInfo(afflictionTooltip.Content, affliction);
                }
            }
        }

        private void UpdateAfflictionInfo(GUIComponent parent, Affliction affliction)
        {
            var labelContainer = parent.GetChildByUserData("label");

            var strengthText = labelContainer.GetChildByUserData("strength") as GUITextBlock;

            strengthText.Text = strengthTexts[
                MathHelper.Clamp((int)Math.Floor((affliction.Strength / affliction.Prefab.MaxStrength) * strengthTexts.Length), 0, strengthTexts.Length - 1)];

            strengthText.TextColor = Color.Lerp(GUI.Style.Orange, GUI.Style.Red,
                affliction.Strength / affliction.Prefab.MaxStrength);

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
                vitalityText.TextColor = vitalityDecrease <= 0 ? GUI.Style.Green :
                Color.Lerp(GUI.Style.Orange, GUI.Style.Red, affliction.Strength / affliction.Prefab.MaxStrength);
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
            }

            Limb targetLimb = Character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == selectedLimbIndex);

            item.ApplyTreatment(Character.Controlled, Character, targetLimb);

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

            if (PlayerInput.PrimaryMouseButtonClicked() && highlightedLimbIndex > -1)
            {
                selectedLimbIndex = highlightedLimbIndex;
                //afflictionContainer.ClearChildren();
                afflictionIconContainer.ClearChildren();
                afflictionInfoContainer.ClearChildren();
                afflictionInfoContainer.UserData = null;
            }
        }

        private void DrawHealthWindow(SpriteBatch spriteBatch, Rectangle drawArea, bool allowHighlight)
        {
            if (Character.Removed) { return; }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Immediate, blendState: BlendState.NonPremultiplied, rasterizerState: GameMain.ScissorTestEnable, effect: GameMain.GameScreen.GradientEffect);

            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (limbHealth.IndicatorSprite == null) continue;

                Rectangle limbEffectiveArea = new Rectangle(limbHealth.IndicatorSprite.SourceRect.X + limbHealth.HighlightArea.X,
                                                            limbHealth.IndicatorSprite.SourceRect.Y + limbHealth.HighlightArea.Y,
                                                            limbHealth.HighlightArea.Width,
                                                            limbHealth.HighlightArea.Height);

                float damageLerp = limbHealth.TotalDamage > 0.0f ? MathHelper.Lerp(0.2f, 1.0f, limbHealth.TotalDamage / 100.0f) : 0.0f;

                var tempAfflictions = GetMatchingAfflictions(limbHealth, a => true);

                float negativeEffect = tempAfflictions.Where(a => !a.Prefab.IsBuff && a.Strength >= a.Prefab.ShowIconThreshold).Sum(a => a.Strength);
                //float negativeMaxEffect = tempAfflictions.Where(a => !a.Prefab.IsBuff).Sum(a => a.Prefab.MaxStrength);
                float positiveEffect = tempAfflictions.Where(a => a.Prefab.IsBuff && a.Strength >= a.Prefab.ShowIconThreshold).Sum(a => a.Strength * 0.2f);
                //float positiveMaxEffect = tempAfflictions.Where(a => a.Prefab.IsBuff).Sum(a => a.Prefab.MaxStrength);

                float midPoint = (float)limbEffectiveArea.Center.Y / (float)limbHealth.IndicatorSprite.Texture.Height;
                float fadeDist = 0.6f * (float)limbEffectiveArea.Height / (float)limbHealth.IndicatorSprite.Texture.Height;

                if (negativeEffect > 0.0f && negativeEffect < 5.0f) { negativeEffect = 10.0f; }
                if (positiveEffect > 0.0f && positiveEffect < 5.0f) { positiveEffect = 10.0f; }

                Color positiveColor = Color.Lerp(Color.Orange, Color.Lime, Math.Min(positiveEffect / 25.0f, 1.0f));
                Color negativeColor = Color.Lerp(Color.Orange, Color.Red, Math.Min(negativeEffect / 25.0f, 1.0f));

                Color color1 = Color.Orange;
                Color color2 = Color.Orange;

                if (negativeEffect + positiveEffect > 0.0f)
                {
                    if (negativeEffect >= positiveEffect)
                    {
                        color1 = Color.Lerp(positiveColor, negativeColor, (negativeEffect - positiveEffect) / negativeEffect);
                        color2 = negativeColor;
                    }
                    else
                    {
                        color1 = positiveColor;
                        color2 = Color.Lerp(negativeColor, positiveColor, (positiveEffect - negativeEffect) / positiveEffect);
                    }
                }

                if (Character.IsDead)
                {
                    color1 = Color.Lerp(color1, Color.Black, 0.75f);
                    color2 = Color.Lerp(color2, Color.Black, 0.75f);
                }

                GameMain.GameScreen.GradientEffect.Parameters["color1"].SetValue(color1.ToVector4());
                GameMain.GameScreen.GradientEffect.Parameters["color2"].SetValue(color2.ToVector4());
                GameMain.GameScreen.GradientEffect.Parameters["midPoint"].SetValue(midPoint);
                GameMain.GameScreen.GradientEffect.Parameters["fadeDist"].SetValue(fadeDist);

                float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);

                limbHealth.IndicatorSprite.Draw(spriteBatch,
                    drawArea.Center.ToVector2(), Color.White,
                    limbHealth.IndicatorSprite.Origin,
                    0, scale);

                if (GameMain.DebugDraw)
                {
                    Rectangle highlightArea = GetLimbHighlightArea(limbHealth, drawArea);

                    GUI.DrawRectangle(spriteBatch, highlightArea, Color.Red, false);
                    GUI.DrawRectangle(spriteBatch, drawArea, Color.Red, false);
                }

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

            if (allowHighlight)
            {
                i = 0;
                foreach (LimbHealth limbHealth in limbHealths)
                {
                    if (limbHealth.HighlightSprite == null) { continue; }

                    float scale = Math.Min(drawArea.Width / (float)limbHealth.HighlightSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.HighlightSprite.SourceRect.Height);

                    int drawCount = 0;
                    if (i == highlightedLimbIndex) { drawCount++; }
                    if (i == selectedLimbIndex) { drawCount++; }
                    for (int j = 0; j < drawCount; j++)
                    {
                        limbHealth.HighlightSprite.Draw(spriteBatch,
                            drawArea.Center.ToVector2(), Color.White,
                            limbHealth.HighlightSprite.Origin,
                            0, scale);
                    }
                    i++;
                }
            }
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, blendState: BlendState.NonPremultiplied, rasterizerState: GameMain.ScissorTestEnable);

            i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                IEnumerable<Affliction> thisAfflictions = limbHealth.Afflictions.Where(a => a.Strength >= a.Prefab.ShowIconThreshold);
                thisAfflictions = thisAfflictions.Concat(afflictions.Where(a =>
                {
                    Limb indicatorLimb = Character.AnimController.GetLimb(a.Prefab.IndicatorLimb);
                    return (indicatorLimb != null && indicatorLimb.HealthIndex == i && a.Strength >= a.Prefab.ShowIconThreshold);
                }));

                if (thisAfflictions.Count() <= 0) { i++; continue; }
                if (limbHealth.IndicatorSprite == null) { continue; }

                float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);
                
                Rectangle highlightArea = GetLimbHighlightArea(limbHealth, drawArea);
                
                float iconScale = 0.25f * scale;
                Vector2 iconPos = highlightArea.Center.ToVector2();

                Affliction mostSevereAffliction = thisAfflictions.FirstOrDefault(a => !a.Prefab.IsBuff && !thisAfflictions.Any(a2 => !a2.Prefab.IsBuff && a2.Strength > a.Strength)) ?? thisAfflictions.FirstOrDefault();
                
                if (mostSevereAffliction != null) { DrawLimbAfflictionIcon(spriteBatch, mostSevereAffliction, iconScale, ref iconPos); }

                if (thisAfflictions.Count() > 1)
                {
                    string additionalAfflictionCount = $"+{thisAfflictions.Count() - 1}";
                    Vector2 displace = GUI.SubHeadingFont.MeasureString(additionalAfflictionCount);
                    GUI.SubHeadingFont.DrawString(spriteBatch, additionalAfflictionCount, iconPos + new Vector2(displace.X * 1.1f, -displace.Y * 0.45f), Color.Black * 0.75f);
                    GUI.SubHeadingFont.DrawString(spriteBatch, additionalAfflictionCount, iconPos + new Vector2(displace.X, -displace.Y * 0.5f), Color.White);
                }

                i++;
            }

            if (selectedLimbIndex > -1)
            {
                var selectedLimbArea = GetLimbHighlightArea(limbHealths[selectedLimbIndex], drawArea);
                GUI.DrawLine(spriteBatch,
                    new Vector2(selectedLimbText.Rect.X, selectedLimbText.Rect.Center.Y),
                    selectedLimbArea.Center.ToVector2(),
                    Color.LightGray * 0.5f, width: 4);
            }

            if (draggingMed != null)
            {
                GUIImage itemImage = draggingMed.GetChild<GUIImage>();
                float scale = Math.Min(40.0f / itemImage.Sprite.size.X, 40.0f / itemImage.Sprite.size.Y);
                itemImage.Sprite.Draw(spriteBatch, PlayerInput.MousePosition, itemImage.Color, 0, scale);
            }
        }

        private void DrawLimbAfflictionIcon(SpriteBatch spriteBatch, Affliction affliction, float iconScale, ref Vector2 iconPos)
        {
            if (affliction.Strength < affliction.Prefab.ShowIconThreshold) return;
            Vector2 iconSize = (affliction.Prefab.Icon.size * iconScale);

            //afflictions that have a strength of less than 10 are faded out slightly
            float alpha = MathHelper.Lerp(0.3f, 1.0f,
                (affliction.Strength - affliction.Prefab.ShowIconThreshold) / Math.Min(affliction.Prefab.MaxStrength - affliction.Prefab.ShowIconThreshold, 10.0f));

            affliction.Prefab.Icon.Draw(spriteBatch, iconPos - iconSize / 2.0f, GetAfflictionIconColor(affliction.Prefab, affliction) * alpha, 0, iconScale);
            iconPos += new Vector2(10.0f, 20.0f) * iconScale;
        }

        private Rectangle GetLimbHighlightArea(LimbHealth limbHealth, Rectangle drawArea)
        {
            float scale = Math.Min(drawArea.Width / (float)limbHealth.IndicatorSprite.SourceRect.Width, drawArea.Height / (float)limbHealth.IndicatorSprite.SourceRect.Height);
            return new Rectangle(
                (int)(drawArea.Center.X - (limbHealth.IndicatorSprite.SourceRect.Width / 2 - limbHealth.HighlightArea.X) * scale),
                (int)(drawArea.Center.Y - (limbHealth.IndicatorSprite.SourceRect.Height / 2 - limbHealth.HighlightArea.Y) * scale),
                (int)(limbHealth.HighlightArea.Width * scale),
                (int)(limbHealth.HighlightArea.Height * scale));
        }
        
        public void ClientRead(IReadMessage inc)
        {
            List<Pair<AfflictionPrefab, float>> newAfflictions = new List<Pair<AfflictionPrefab, float>>();

            byte afflictionCount = inc.ReadByte();
            for (int i = 0; i < afflictionCount; i++)
            {
                AfflictionPrefab afflictionPrefab = AfflictionPrefab.Prefabs[inc.ReadString()];
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
                AfflictionPrefab afflictionPrefab = AfflictionPrefab.Prefabs[inc.ReadString()];
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
