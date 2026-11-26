using Barotrauma.Networking;
using Barotrauma.Particles;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Turret : Powered, IDrawableComponent, IServerSerializable
    {
        private Sprite crosshairSprite, crosshairPointerSprite;
        public Sprite WeaponIndicatorSprite;

        private GUIProgressBar powerIndicator;

        private Vector2? debugDrawTargetPos;

        public int UIElementHeight
        {
            get 
            {
                int height = 0;
                if (ShowChargeIndicator) { height += powerIndicator.Rect.Height; }
                if (ShowProjectileIndicator) { height += (int)(Inventory.SlotSpriteSmall.size.Y * Inventory.UIScale) + 5; }
                return height;
            }
        }

        private float recoilTimer;

        private float RetractionTime => Math.Max(Reload * RetractionDurationMultiplier, RecoilTime);

        private RoundSound startMoveSound, endMoveSound, moveSound;

        private RoundSound chargeSound;

        private bool loopChargeSound = true;

        private SoundChannel moveSoundChannel, chargeSoundChannel;
        private Vector2 oldRotation = Vector2.Zero;

        private Vector2 crosshairPos, crosshairPointerPos;

        private readonly Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();
        private float prevAngle;

        private float currentBarrelSpin = 0f;

        private bool flashLowPower;
        private bool flashNoAmmo, flashLoaderBroken;
        private float flashTimer;
        private readonly float flashLength = 1;

        private const float MaxCircle = 360f;
        private const float HalfCircle = 180f;
        private const float QuarterCircle = 90f;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();
        private readonly List<ParticleEmitter> particleEmitterCharges = new List<ParticleEmitter>();

        [Serialize(false, IsPropertySaveable.No, description: "Should the charge of the connected batteries/supercapacitors be shown at the top of the screen when operating the item.")]
        public bool ShowChargeIndicator
        {
            get;
            private set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Should the available ammunition be shown at the top of the screen when operating the item.")]
        public bool ShowProjectileIndicator
        {
            get;
            private set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How far the barrel \"recoils back\" when the turret is fired (in pixels)."), Editable(MinValueFloat = 0)]
        public float RecoilDistance
        {
            get;
            private set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The distance in which the spinning barrels rotate. Only used if spinning barrels are created."), Editable(MinValueFloat = 0)]
        public float SpinningBarrelDistance
        {
            get;
            private set;
        }

        public Vector2 DrawSize
        {
            get
            {
                float size = Math.Max(transformedBarrelPos.X, transformedBarrelPos.Y);       
                if (railSprite != null && barrelSprite != null)
                {
                    size += Math.Max(Math.Max(barrelSprite.size.X, barrelSprite.size.Y), Math.Max(railSprite.size.X, railSprite.size.Y)) * item.Scale;
                }
                else if (railSprite != null)
                {
                    size += Math.Max(railSprite.size.X, railSprite.size.Y) * item.Scale;
                }
                else if (barrelSprite != null)
                {
                    size += Math.Max(barrelSprite.size.X, barrelSprite.size.Y) * item.Scale;
                }                
                return Vector2.One * size * 2;
            }
        }

        public Sprite BarrelSprite => barrelSprite;
        public Sprite RailSprite => railSprite;
        public IEnumerable<Sprite> ChargeSprites => chargeSprites.Select(cs => cs.Sprite);
        public IReadOnlyList<Sprite> SpinningBarrelSprites => spinningBarrelSprites;

        [Serialize(false, IsPropertySaveable.No)]
        public bool HideBarrelWhenBroken
        {
            get;
            private set;
        }
        
        [Serialize(defaultValue: "0.5, 1.5", IsPropertySaveable.No, description: "Pitch slides from X to Y over the charge time")]
        public Vector2 ChargeSoundWindupPitchSlide
        {
            get => _chargeSoundWindupPitchSlide;
            set
            {
                _chargeSoundWindupPitchSlide = new Vector2(
                    Math.Max(value.X, SoundChannel.MinFrequencyMultiplier), 
                    Math.Min(value.Y, SoundChannel.MaxFrequencyMultiplier));
            }
        }
        
        private Vector2 _chargeSoundWindupPitchSlide;
        
        [Serialize(0f, IsPropertySaveable.No, description: "When set, the railsprite spins when the turret is charging. By default it just rotates along the turret. Peak angular speed in radians per second. The final value is modified by the charge ratio.")]
        public float RailSpriteSpin { get; private set; }
        private float railSpriteRotation;

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                string textureDir = GetTextureDirectory(subElement);
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "crosshair":
                        crosshairSprite = new Sprite(subElement, path: textureDir);
                        break;
                    case "weaponindicator":
                        WeaponIndicatorSprite = new Sprite(subElement, path: textureDir);
                        break;
                    case "crosshairpointer":
                        crosshairPointerSprite = new Sprite(subElement, path: textureDir);
                        break;
                    case "startmovesound":
                        startMoveSound = RoundSound.Load(subElement);
                        break;
                    case "endmovesound":
                        endMoveSound = RoundSound.Load(subElement);
                        break;
                    case "movesound":
                        moveSound = RoundSound.Load(subElement);
                        break;
                    case "chargesound":
                        chargeSound = RoundSound.Load(subElement);
                        loopChargeSound = subElement.GetAttributeBool("loop", true);
                        break;
                    case "particleemitter":
                        var emitter = new ParticleEmitter(subElement);
                        particleEmitters.Add(emitter);
                        TryAddParticleEmitterByID(emitter);
                        break;
                    case "particleemittercharge":
                        var chargeEmitter = new ParticleEmitter(subElement);
                        particleEmitterCharges.Add(chargeEmitter);
                        TryAddParticleEmitterByID(chargeEmitter);
                        break;
                }
            }
            
            powerIndicator = new GUIProgressBar(new RectTransform(new Vector2(0.18f, 0.03f), GUI.Canvas, Anchor.BottomCenter)
            {
                MinSize = new Point(100, 20),
                RelativeOffset = new Vector2(0.0f, 0.01f)
            },
            barSize: 0.0f, style: "DeviceProgressBar")
            {
                CanBeFocused = false
            };
        }

        public override void Move(Vector2 amount, bool ignoreContacts = false)
        {
            widgets.Clear();
        }

        partial void LaunchProjSpecific()
        {
            TurretAmmo turretAmmo = LastLaunchedTurretAmmo;
            recoilTimer = RetractionTime;
            if (User != null)
            {
                recoilTimer /= 1 + User.GetStatValue(StatTypes.TurretAttackSpeed);
            }
            PlaySound(ActionType.OnUse);
            Vector2 particlePos = GetFiringPosition();
            foreach (ParticleEmitter emitter in particleEmitters)
            {
                emitter.Emit(1.0f, particlePos, hullGuess: null, angle: -FiringRotation, particleRotation: FiringRotation, colorMultiplier: turretAmmo?.GetParticleEmitterColor(emitter.Prefab.Properties.ID));
            }
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            recoilTimer -= deltaTime;
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            recoilTimer -= deltaTime;

            if (crosshairSprite != null)
            {
                Vector2 itemPos = cam.WorldToScreen(new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y));
                Vector2 turretDir = new Vector2((float)Math.Cos(BarrelRotation), (float)Math.Sin(BarrelRotation));

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + turretDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + turretDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));
            }

            crosshairPointerPos = PlayerInput.MousePosition;

            if (Math.Abs(angularVelocity) > 0.1f)
            {
                if (moveSoundChannel == null && startMoveSound != null)
                {
                    moveSoundChannel = SoundPlayer.PlaySound(startMoveSound, item.WorldPosition, hullGuess: item.CurrentHull);
                }
                else if (moveSoundChannel == null || !moveSoundChannel.IsPlaying)
                {
                    if (moveSound != null)
                    {
                        moveSoundChannel?.FadeOutAndDispose();
                        moveSoundChannel = SoundPlayer.PlaySound(moveSound, item.WorldPosition, hullGuess: item.CurrentHull);
                        if (moveSoundChannel != null) { moveSoundChannel.Looping = true;}
                    }
                }
            }
            else if (Math.Abs(angularVelocity) < 0.05f)
            {
                if (moveSoundChannel != null)
                {
                    if (endMoveSound != null && moveSoundChannel.Sound != endMoveSound.Sound)
                    {
                        moveSoundChannel.FadeOutAndDispose();
                        moveSoundChannel = SoundPlayer.PlaySound(endMoveSound, item.WorldPosition, hullGuess: item.CurrentHull);
                        if (moveSoundChannel != null) { moveSoundChannel.Looping = false; }
                    }
                    else if (!moveSoundChannel.IsPlaying)
                    {
                        moveSoundChannel.FadeOutAndDispose();
                        moveSoundChannel = null;
                    }
                }
            }

            float maxChargeTime = CurrentMaxChargeTime;
            float chargeRatio = currentChargeTime / maxChargeTime;
            currentBarrelSpin = (currentBarrelSpin + MaxCircle * chargeRatio * deltaTime * 3f) % MaxCircle;
            
            if (RailSpriteSpin != 0)
            {
                if (currentChargingState != ChargingState.Inactive)
                {
                    railSpriteRotation += RailSpriteSpin * chargeRatio * deltaTime;
                    railSpriteRotation = MathUtils.WrapAnglePi(railSpriteRotation);
                }
            }

            switch (currentChargingState)
            {
                case ChargingState.WindingUp:
                    Vector2 particlePos = GetFiringPosition();
                    foreach ((Sprite sprite, Vector2 offset, float targetRot) in chargeSprites)
                    {
                        if (sprite == barrelSprite)
                        {
                            // Rotate the particle position to match the barrel's current charge rotation.
                            Vector2 pivot = new Vector2(item.WorldRect.X + transformedBarrelPos.X, item.WorldRect.Y - transformedBarrelPos.Y);
                            Vector2 pivotToMuzzle = particlePos - pivot;
                            pivotToMuzzle = MathUtils.RotatePoint(pivotToMuzzle, -(targetRot * chargeRatio));
                            particlePos = pivot + pivotToMuzzle;
                            
                            // Offset the particle position according to the barrel's current charge position.
                            // Convert Draw Rotation (CW) to Sim Rotation (CCW) by negating the angle.
                            Vector2 localOffset = new Vector2(offset.X, -offset.Y);
                            float simRotation = -CalculateChargeRotation(targetRot, chargeRatio);
                            particlePos -= CalculateChargePositionOffset(localOffset, simRotation, chargeRatio);
                            break;
                        }
                    }
                    float sizeMultiplier = Math.Clamp(chargeRatio, 0.1f, 1f);
                    TurretAmmo turretAmmo = particleEmitterCharges.Any() ? GetActiveTurretAmmo() : null;
                    foreach (ParticleEmitter emitter in particleEmitterCharges)
                    {
                        emitter.Emit(deltaTime, particlePos, hullGuess: null, angle: -FiringRotation, particleRotation: FiringRotation, sizeMultiplier: sizeMultiplier,
                            colorMultiplier: turretAmmo?.GetParticleEmitterColor(emitter.Prefab.Properties.ID) ?? emitter.Prefab.Properties.ColorMultiplier);
                    }

                    if (currentChargeTime >= maxChargeTime)
                    {
                        StopChargeSoundChannel();
                    }
                    else if (chargeSoundChannel == null || !chargeSoundChannel.IsPlaying)
                    {
                        if (chargeSound != null)
                        {
                            chargeSoundChannel = SoundPlayer.PlaySound(chargeSound, item.WorldPosition, hullGuess: item.CurrentHull);
                            if (chargeSoundChannel != null) { chargeSoundChannel.Looping = loopChargeSound; }
                        }
                    }
                    else if (chargeSoundChannel != null)
                    {
                        chargeSoundChannel.FrequencyMultiplier = MathHelper.Lerp(ChargeSoundWindupPitchSlide.X, ChargeSoundWindupPitchSlide.Y, chargeRatio);
                        chargeSoundChannel.Position = new Vector3(item.WorldPosition, 0.0f);
                    }
                    break;
                default:
                    StopChargeSoundChannel();
                    break;
            }

            void StopChargeSoundChannel()
            {
                if (chargeSoundChannel != null)
                {
                    if (chargeSoundChannel.IsPlaying)
                    {
                        chargeSoundChannel.FadeOutAndDispose();
                        chargeSoundChannel.Looping = false;
                    }
                    else
                    {
                        chargeSoundChannel = null;
                    }
                }
            }

            if (moveSoundChannel != null && moveSoundChannel.IsPlaying)
            {
                moveSoundChannel.Gain = MathHelper.Clamp(Math.Abs(angularVelocity), 0.5f, 1.0f) * moveSound.Volume;
            }

            if (flashLowPower || flashNoAmmo || flashLoaderBroken)
            {
                flashTimer += deltaTime;
                if (flashTimer >= flashLength)
                {
                    flashTimer = 0;
                    flashLowPower = false;
                    flashNoAmmo = false;
                    flashLoaderBroken = false;
                }
            }
        }

        public override void UpdateEditing(float deltaTime)
        {
            if (Screen.Selected == GameMain.SubEditorScreen && item.IsSelected)
            {
                if (widgets.ContainsKey("maxrotation"))
                {
                    widgets["maxrotation"].Update(deltaTime);
                }
                if (widgets.ContainsKey("minrotation"))
                {
                    widgets["minrotation"].Update(deltaTime);
                }
            }
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (crosshairSprite != null)
            {
                Vector2 itemPos = cam.WorldToScreen(item.WorldPosition);
                Vector2 turretDir = MathUtils.RotatedUnitXRadians(BarrelRotation);

                Vector2 mouseDiff = itemPos - PlayerInput.MousePosition;
                crosshairPos = new Vector2(
                    MathHelper.Clamp(itemPos.X + turretDir.X * mouseDiff.Length(), 0, GameMain.GraphicsWidth),
                    MathHelper.Clamp(itemPos.Y + turretDir.Y * mouseDiff.Length(), 0, GameMain.GraphicsHeight));
            }

            crosshairPointerPos = PlayerInput.MousePosition;
        }

        public Vector2 GetRecoilOffset()
        {
            float recoilOffset = 0.0f;
            if (Math.Abs(RecoilDistance) > 0.0f && recoilTimer > 0.0f)
            {
                float diff = RetractionTime - RecoilTime;
                if (recoilTimer >= diff)
                {
                    //move the barrel backwards 0.1 seconds (defined by RecoilTime) after launching
                    recoilOffset = RecoilDistance * (1.0f - (recoilTimer - diff) / RecoilTime);
                }
                else if (recoilTimer <= diff - RetractionDelay)
                {
                    //move back to normal position while reloading
                    float t = diff - RetractionDelay;
                    recoilOffset = RecoilDistance * recoilTimer / t;
                }
                else
                {
                    recoilOffset = RecoilDistance;
                }
            }
            return MathUtils.RotatedUnitXRadians(FiringRotation) * recoilOffset;
        }
        
        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1, Color? overrideColor = null)
        {
            if (!MathUtils.NearlyEqual(item.Rotation, prevBaseRotation) || !MathUtils.NearlyEqual(item.Scale, prevScale))
            {
                UpdateTransformedBarrelPos();
            }
            Vector2 drawPos = GetDrawPos();

            if (item.Condition > 0.0f || !HideBarrelWhenBroken)
            {
                var currentRailSprite = item.Condition <= 0.0f && railSpriteBroken != null ? railSpriteBroken : railSprite;
                if (currentRailSprite != null && chargeSprites.None(cs => cs.Sprite == currentRailSprite))
                {
                    currentRailSprite.Draw(spriteBatch,
                        drawPos,
                        overrideColor ?? item.SpriteColor,
                        railSpriteRotation, item.Scale,
                        SpriteEffects.None, item.SpriteDepth + (currentRailSprite.Depth - item.Sprite.Depth));
                }
                
                var currentBarrelSprite = item.Condition <= 0.0f && barrelSpriteBroken != null ? barrelSpriteBroken : barrelSprite;
                if (currentBarrelSprite != null && chargeSprites.None(cs => cs.Sprite == currentBarrelSprite))
                {
                    currentBarrelSprite.Draw(spriteBatch,
                        drawPos - GetRecoilOffset() * item.Scale,
                        overrideColor ?? item.SpriteColor,
                        BarrelRotation + MathHelper.PiOver2, item.Scale,
                        SpriteEffects.None, item.SpriteDepth + (currentBarrelSprite.Depth - item.Sprite.Depth));   
                }

                float chargeRatio = currentChargeTime / CurrentMaxChargeTime;

                foreach ((Sprite sprite, Vector2 offset, float targetRot) in chargeSprites)
                {
                    if (sprite == null) { continue; }
                    float rotation = CalculateChargeRotation(targetRot, chargeRatio);
                    // Need to take the possible recoil offset into account here, in case we are drawing the barrel sprite here.
                    Vector2 recoilOffset = sprite == barrelSprite ? GetRecoilOffset() * item.Scale : Vector2.Zero;
                    sprite.Draw(spriteBatch,
                        drawPos - recoilOffset - CalculateChargePositionOffset(offset, rotation, chargeRatio),
                        item.SpriteColor,
                        rotation, item.Scale,
                        SpriteEffects.None, item.SpriteDepth + (sprite.Depth - item.Sprite.Depth));
                }

                int spinningBarrelCount = spinningBarrelSprites.Count;

                for (int i = 0; i < spinningBarrelCount; i++)
                {
                    // this block is messy since I was debugging it with a bunch of values, should be cleaned up / optimized if prototype is accepted
                    Sprite spinningBarrel = spinningBarrelSprites[i];
                    float barrelCirclePosition = (MaxCircle * i / spinningBarrelCount + currentBarrelSpin) % MaxCircle;

                    float newDepth = item.SpriteDepth + (spinningBarrel.Depth - item.Sprite.Depth) + (barrelCirclePosition > HalfCircle ? 0.0f : 0.001f);

                    float barrelColorPosition = (barrelCirclePosition + QuarterCircle) % MaxCircle;
                    float colorOffset = Math.Abs(barrelColorPosition - HalfCircle) / HalfCircle;
                    Color newColorModifier = Color.Lerp(Color.Black, Color.Gray, colorOffset);

                    float barrelHalfCirclePosition = Math.Abs(barrelCirclePosition - HalfCircle);
                    float barrelPositionModifier = MathUtils.SmoothStep(barrelHalfCirclePosition / HalfCircle);
                    float newPositionOffset = barrelPositionModifier * SpinningBarrelDistance;

                    spinningBarrel.Draw(spriteBatch,
                        drawPos - MathUtils.RotatePoint(new Vector2(newPositionOffset, 0f) * item.Scale, BarrelRotation + MathHelper.PiOver2),
                        Color.Lerp(overrideColor ?? item.SpriteColor, newColorModifier, 0.8f),
                        BarrelRotation + MathHelper.PiOver2, item.Scale,
                        SpriteEffects.None, newDepth);
                }
            }

            if (GameMain.DebugDraw)
            {
                float debugIndicatorSize = Math.Max(5f, 5f / Screen.Selected.Cam.Zoom);
                float debugIndicatorWidth = Math.Max(1f, 2f / Screen.Selected.Cam.Zoom);
                float debugLineWidth = Math.Max(1f, 2f / Screen.Selected.Cam.Zoom);

                DrawFiringPos(GetBarrelFiringPosition(), BarrelRotation);
                if (useFixedFiringPos) { DrawFiringPos(GetFixedFiringPosition(), FixedFiringRotation); }
                if (debugDrawTargetPos.HasValue) { DrawTargetPos(debugDrawTargetPos.Value); }

                void DrawFiringPos(Vector2 firingPos, float rotationRad)
                {
                    firingPos.Y = -firingPos.Y;
                    Vector2 endPos = firingPos + MathUtils.RotatedUnitXRadians(rotationRad) * Math.Max(500, AIRange);
                    DrawIndicatorWithLine(firingPos, endPos, Color.Red, Color.LightGray, depth: 0.002f);
                }

                void DrawTargetPos(Vector2 targetPos)
                {
                    targetPos.Y = -targetPos.Y;
                    Vector2 barrelPos = item.WorldRect.Location.ToVector2().FlipY() + GetTransformedBarrelPos(item.Rotation);
                    DrawIndicatorWithLine(targetPos, barrelPos, Color.Magenta, Color.Magenta, depth: 0.001f);
                }

                void DrawIndicatorWithLine(Vector2 startPos, Vector2 endPos, Color indicatorColor, Color lineColor, float depth)
                {
                    GUI.DrawLine(spriteBatch, startPos - Vector2.UnitX * debugIndicatorSize, startPos + Vector2.UnitX * debugIndicatorSize, indicatorColor, depth, debugIndicatorWidth);
                    GUI.DrawLine(spriteBatch, startPos - Vector2.UnitY * debugIndicatorSize, startPos + Vector2.UnitY * debugIndicatorSize, indicatorColor, depth, debugIndicatorWidth);
                    GUI.DrawLine(spriteBatch, startPos, endPos, lineColor, depth + 0.0005f, debugLineWidth);
                }
            }

            if (!editing || GUI.DisableHUD || !item.IsSelected) { return; }

            const float widgetRadius = 60.0f;

            Vector2 center = new Vector2((float)Math.Cos((maxRotation + minRotation) / 2), (float)Math.Sin((maxRotation + minRotation) / 2));
            GUI.DrawLine(spriteBatch,
                drawPos,
                drawPos + center * widgetRadius,
                Color.LightGreen);

            const float coneRadius = 300.0f;
            float radians = maxRotation - minRotation;
            float circleRadius = coneRadius / Screen.Selected.Cam.Zoom * GUI.Scale;
            float lineThickness = 1f / Screen.Selected.Cam.Zoom;

            if (Math.Abs(minRotation - maxRotation) < 0.02f)
            {
                spriteBatch.DrawLine(drawPos, drawPos + center * circleRadius, GUIStyle.Green, thickness: lineThickness);
            }
            else if (radians >= MathHelper.TwoPi)
            {
                spriteBatch.DrawCircle(drawPos, circleRadius, 180, GUIStyle.Green, thickness: lineThickness);
            }
            else
            {
                spriteBatch.DrawSector(drawPos, circleRadius, radians, (int)Math.Abs(90 * radians), GUIStyle.Green, offset: minRotation, thickness: lineThickness);
            }

            int baseWidgetScale = GUI.IntScale(16);
            int widgetSize = (int) (Math.Max(baseWidgetScale, baseWidgetScale / Screen.Selected.Cam.Zoom));
            float widgetThickness = Math.Max(1f, lineThickness);
            Widget minRotationWidget = GetWidget("minrotation", spriteBatch, size: widgetSize, thickness: widgetThickness, initMethod: (widget) =>
            {
                widget.Selected += () =>
                {
                    oldRotation = RotationLimits;
                };
                widget.MouseDown += () =>
                {
                    widget.Color = GUIStyle.Green;
                    prevAngle = minRotation;
                };
                widget.Deselected += () =>
                {
                    widget.Color = Color.Yellow;
                    item.CreateEditingHUD();
                    RotationLimits = RotationLimits;
                    if (SubEditorScreen.IsSubEditor())
                    {
                        SubEditorScreen.StoreCommand(new PropertyCommand(this, "RotationLimits".ToIdentifier(), RotationLimits, oldRotation));
                    }
                };
                widget.MouseHeld += (deltaTime) =>
                {
                    float newMinRotation = GetRotationAngle(GetDrawPos());
                    AngleWrapAdjustment(minRotation, newMinRotation, ref maxRotation);
                    
                    // clamp value here to keep widget movement within max range
                    minRotation = MathHelper.Clamp(newMinRotation, maxRotation - MathHelper.TwoPi, maxRotation);
                    
                    UpdateBarrel();
                    MapEntity.DisableSelect = true;
                };
                widget.PreUpdate += (deltaTime) =>
                {
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                    widget.DrawPos = Screen.Selected.Cam.WorldToScreen(widget.DrawPos);
                };
                widget.PostUpdate += (deltaTime) =>
                {
                    widget.DrawPos = Screen.Selected.Cam.ScreenToWorld(widget.DrawPos);
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                };
                widget.PreDraw += (sprtBtch, deltaTime) =>
                {
                    widget.Tooltip = "Min: " + (int)MathHelper.ToDegrees(minRotation);
                    widget.DrawPos = GetDrawPos() + new Vector2((float)Math.Cos(minRotation), (float)Math.Sin(minRotation)) * coneRadius / Screen.Selected.Cam.Zoom * GUI.Scale;
                };
            });

            Widget maxRotationWidget = GetWidget("maxrotation", spriteBatch, size: widgetSize, thickness: widgetThickness, initMethod: (widget) =>
            {
                widget.Selected += () =>
                {
                    oldRotation = RotationLimits;
                };
                widget.MouseDown += () =>
                {
                    widget.Color = GUIStyle.Green;
                    prevAngle = maxRotation;
                };
                widget.Deselected += () =>
                {
                    widget.Color = Color.Yellow;
                    item.CreateEditingHUD();
                    RotationLimits = RotationLimits;
                    if (SubEditorScreen.IsSubEditor())
                    {
                        SubEditorScreen.StoreCommand(new PropertyCommand(this, "RotationLimits".ToIdentifier(), RotationLimits, oldRotation));
                    }
                };
                widget.MouseHeld += (deltaTime) =>
                {
                    float newMaxRotation = GetRotationAngle(GetDrawPos());
                    AngleWrapAdjustment(maxRotation, newMaxRotation, ref minRotation);
                    
                    // clamp value here to keep widget movement within max range
                    maxRotation = MathHelper.Clamp(newMaxRotation, minRotation, minRotation + MathHelper.TwoPi);
                    
                    UpdateBarrel();
                    MapEntity.DisableSelect = true;
                };
                widget.PreUpdate += (deltaTime) =>
                {
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                    widget.DrawPos = Screen.Selected.Cam.WorldToScreen(widget.DrawPos);
                };
                widget.PostUpdate += (deltaTime) =>
                {
                    widget.DrawPos = Screen.Selected.Cam.ScreenToWorld(widget.DrawPos);
                    widget.DrawPos = new Vector2(widget.DrawPos.X, -widget.DrawPos.Y);
                };
                widget.PreDraw += (sprtBtch, deltaTime) =>
                {
                    widget.Tooltip = "Max: " + (int)MathHelper.ToDegrees(maxRotation);
                    widget.DrawPos = GetDrawPos() + new Vector2((float)Math.Cos(maxRotation), (float)Math.Sin(maxRotation)) * coneRadius / Screen.Selected.Cam.Zoom * GUI.Scale;
                    widget.Update(deltaTime);
                };
            });
            minRotationWidget.Draw(spriteBatch, (float)Timing.Step);
            maxRotationWidget.Draw(spriteBatch, (float)Timing.Step);

            void UpdateBarrel()
            {
                BarrelRotation = (minRotation + maxRotation) / 2;
            }
        }
        
        private static void AngleWrapAdjustment(float currentRotation, float newRotation, ref float rangeLockedRotation)
        {
            if (DetectAngleWrapAround(currentRotation, newRotation))
            {
                // if there's a wrap-around, also wrap the other rotation limit to keep range
                if (newRotation < currentRotation)
                {
                    rangeLockedRotation -= MathHelper.TwoPi;
                }
                else
                {
                    rangeLockedRotation += MathHelper.TwoPi;
                }
            }
        }
        
        private static bool DetectAngleWrapAround(float rotation, float newRotation)
        {
            float deltaRotation = MathF.Abs(rotation - newRotation);
            
            // turret angle wraps around to 0 from -2Pi and 2Pi.
            // Detect wrap-around when dragging the widgets, where usual rotation delta is small,
            // so a large jump in rotation (here, an arbitrary big value in the range of 0 to 2Pi)
            // is considered a wrap-around for this purpose.
            // NOTE: this is not a reliable way to detect angle wrap-around in general, and is only intended for
            // the angle widgets!
            if (deltaRotation > MathHelper.TwoPi * 0.8f)
            {
                return true;
            }
            
            return false;
        }
        
        public Vector2 GetDrawPos()
        {
            Vector2 drawPos = new Vector2(item.Rect.X + transformedBarrelPos.X, item.Rect.Y - transformedBarrelPos.Y);
            if (item.Submarine != null) { drawPos += item.Submarine.DrawPosition; }
            drawPos.Y = -drawPos.Y;
            return drawPos;
        }

        private Widget GetWidget(string id, SpriteBatch spriteBatch, int size = 5, float thickness = 1f, Action<Widget> initMethod = null)
        {
            Vector2 offset = new Vector2(size / 2 + 5, -10);
            if (!widgets.TryGetValue(id, out Widget widget))
            {
                widget = new Widget(id, size, WidgetShape.Rectangle)
                {
                    Color = Color.Yellow,
                    TooltipOffset = offset,
                    InputAreaMargin = 20,
                    RequireMouseOn = false
                };
                widgets.Add(id, widget);
                initMethod?.Invoke(widget);
            }

            widget.Size = size;
            widget.TooltipOffset = offset;
            widget.Thickness = thickness;
            return widget;
        }

        private void GetAvailablePower(out float availableCharge, out float availableCapacity)
        {
            availableCharge = 0.0f;
            availableCapacity = 0.0f;
            if (item.Connections == null || powerIn == null) { return; }       
            var recipients = powerIn.Recipients;
            foreach (Connection recipient in recipients)
            {
                if (!recipient.IsPower || !recipient.IsOutput) { continue; }
                var battery = recipient.Item?.GetComponent<PowerContainer>();
                if (battery == null || battery.Item.Condition <= 0.0f) { continue; }
                if (battery.OutputDisabled) { continue; }
                availableCharge += battery.Charge;
                availableCapacity += battery.GetCapacity();
            }            
        }

        /// <summary>
        /// Returns correct angle between -2PI and +2PI
        /// </summary>
        /// <param name="drawPosition"></param>
        /// <returns></returns>
        private float GetRotationAngle(Vector2 drawPosition)
        {
            Vector2 mouseVector = Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition);
            mouseVector.Y = -mouseVector.Y;
            Vector2 rotationVector = mouseVector - drawPosition;
            rotationVector.Normalize();
            double angle = Math.Atan2(MathHelper.ToRadians(rotationVector.Y), MathHelper.ToRadians(rotationVector.X));
            if (angle < 0)
            {// calculates which coterminal angle is closer to previous angle
                angle = Math.Abs(angle - prevAngle) < Math.Abs((angle + Math.PI * 2) - prevAngle) ? angle : angle + Math.PI * 2;
            }
            else if (angle > 0)
            {
                angle = Math.Abs(angle - prevAngle) < Math.Abs((angle - Math.PI * 2) - prevAngle) ? angle : angle - Math.PI * 2;
            }
            angle = MathHelper.Clamp((float)angle, -((float)Math.PI * 2), (float)Math.PI * 2);
            prevAngle = (float)angle;
            return (float)angle;
        }

        public void GetBatteryStatus(out float batteryCapacity, out float totalPowerConsumption, out float chargeRate, out bool isCharged)
        {
            GetAvailablePower(out float batteryCharge, out batteryCapacity);

            float ammoPowerConsumptionMultiplier = GetActiveTurretAmmo()?.PowerConsumptionMultiplier ?? 1.0f;
            totalPowerConsumption = 
                item.GetComponents<Powered>()
                    .Sum(p => p == this ? p.PowerConsumption * ammoPowerConsumptionMultiplier : p.PowerConsumption);

            chargeRate =
                totalPowerConsumption <= 0.0f ?
                1.0f :
                batteryCapacity > 0.0f ? batteryCharge / batteryCapacity : 0.0f;
            isCharged = batteryCharge * 3600.0f > totalPowerConsumption;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            base.DrawHUD(spriteBatch, character);

            if (HudTint.A > 0)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    new Color(HudTint.R, HudTint.G, HudTint.B) * (HudTint.A / 255.0f), true);
            }

            List<Item> availableAmmo = [];
            AddAmmoFromContainer(item.GetComponent<ItemContainer>());
            foreach (MapEntity e in item.linkedTo)
            {
                if (e is not Item linkedItem) { continue; }
                AddAmmoFromContainer(linkedItem.GetComponent<ItemContainer>());
            }

            void AddAmmoFromContainer(ItemContainer itemContainer)
            {
                if (itemContainer == null) { return; }
                availableAmmo.AddRange(itemContainer.Inventory.AllItems);
                //add empty slots too
                for (int i = 0; i < itemContainer.Inventory.Capacity - itemContainer.Inventory.AllItems.Count(); i++)
                {
                    availableAmmo.Add(null);
                }
            }

            GetBatteryStatus(out float batteryCapacity, out float totalPowerConsumption, out float chargeRate, out bool isCharged);

            if (ShowChargeIndicator && totalPowerConsumption > 0.0f && powerIndicator != null)
            {
                powerIndicator.Color = isCharged ? 
                    (HasPowerToShoot() ? GUIStyle.Green : GUIStyle.Orange) : 
                    GUIStyle.Red;
                if (flashLowPower)
                {
                    powerIndicator.BarSize = 1;
                    powerIndicator.Color *= (float)Math.Sin(flashTimer * 12);
                    powerIndicator.RectTransform.ChangeScale(Vector2.Lerp(Vector2.One, Vector2.One * 1.01f, 2 * (float)Math.Sin(flashTimer * 15)));
                }
                else
                {
                    powerIndicator.BarSize = chargeRate;
                }
                powerIndicator.DrawManually(spriteBatch, true);

                Rectangle sliderRect = powerIndicator.GetSliderRect(1.0f);
                int requiredChargeIndicatorPos = (int)(powerConsumption / (batteryCapacity * 3600.0f) * sliderRect.Width);
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(sliderRect.X + requiredChargeIndicatorPos, sliderRect.Y, 2, sliderRect.Height),
                    Color.White * 0.5f, true);
            }

            if (ShowProjectileIndicator && powerIndicator != null)
            {
                Point slotSize = (Inventory.SlotSpriteSmall.size * Inventory.UIScale).ToPoint();
                Point spacing = new Point(GUI.IntScale(5), GUI.IntScale(20));
                int slotsPerRow = Math.Min(availableAmmo.Count, 6);
                int totalWidth = slotSize.X * slotsPerRow + spacing.X * (slotsPerRow - 1);
                int rows = (int)Math.Ceiling(availableAmmo.Count / (float)slotsPerRow);
                Point invSlotPos = new Point(GameMain.GraphicsWidth / 2 - totalWidth / 2, powerIndicator.Rect.Y - (slotSize.Y + spacing.Y) * rows);
                for (int i = 0; i < availableAmmo.Count; i++)
                {
                    // TODO: Optimize? Creates multiple new objects per frame?
                    Inventory.DrawSlot(spriteBatch, null,
                        new VisualSlot(new Rectangle(invSlotPos + new Point((i % slotsPerRow) * (slotSize.X + spacing.X), (int)Math.Floor(i / (float)slotsPerRow) * (slotSize.Y + spacing.Y)), slotSize)),
                        availableAmmo[i], -1, true);
                }
                Rectangle rect = new Rectangle(invSlotPos.X, invSlotPos.Y, totalWidth, slotSize.Y);
                float inflate = MathHelper.Lerp(3, 8, (float)Math.Abs(Math.Sin(flashTimer * 5)));
                rect.Inflate(inflate, inflate);
                Color color = GUIStyle.Red * Math.Max(0.5f, (float)Math.Sin(flashTimer * 12));
                if (flashNoAmmo)
                {
                    GUI.DrawRectangle(spriteBatch, rect, color, thickness: 3);
                }
                else if (flashLoaderBroken)
                {
                    GUI.DrawRectangle(spriteBatch, rect, color, thickness: 3);
                    GUIStyle.BrokenIcon.Value.Sprite.Draw(spriteBatch, rect.Center.ToVector2(), color, scale: rect.Height / GUIStyle.BrokenIcon.Value.Sprite.size.Y);
                    GUIComponent.DrawToolTip(spriteBatch, TextManager.Get("turretloaderbroken"), new Rectangle(invSlotPos.X + totalWidth + GUI.IntScale(10), invSlotPos.Y + slotSize.Y / 2 - GUI.IntScale(9), 0, 0));
                }
            }

            float zoom = cam == null ? 1.0f : (float)Math.Sqrt(cam.Zoom);

            GUI.HideCursor = (crosshairSprite != null || crosshairPointerSprite != null) && GUI.MouseOn == null && !GameMain.Instance.Paused;
            if (GUI.HideCursor)
            {
                bool readyToFire = reload <= 0.0f && isCharged && !MaxActiveProjectilesReached && availableAmmo.Any(p => p != null);
                crosshairSprite?.Draw(spriteBatch, crosshairPos, readyToFire ? Color.White : Color.White * 0.2f, 0, zoom);
                crosshairPointerSprite?.Draw(spriteBatch, crosshairPointerPos, 0, zoom);
            }
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            UInt16 projectileID = msg.ReadUInt16();
            float newTargetRotation = msg.ReadRangedSingle(minRotation, maxRotation, 16);

            if (Character.Controlled == null || User != Character.Controlled)
            {
                targetRotation = newTargetRotation;
            }

            //projectile removed, do nothing
            if (projectileID == 0) { return; }

            //ID ushort.MaxValue = launched without a projectile
            if (projectileID == LaunchWithoutProjectileId)
            {
                Launch(null, User);
            }
            else
            {
                if (!(Entity.FindEntityByID(projectileID) is Item projectile))
                {
                    DebugConsole.ThrowError("Failed to launch a projectile - item with the ID \"" + projectileID + " not found");
                    return;
                }
                Launch(projectile, User, launchRotation: newTargetRotation);
            }
        }
    }
}
