using Barotrauma.Networking;
using Barotrauma.Particles;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class Engine : Powered, IDrawableComponent
    {
        private float spriteIndex;

        private SpriteSheet propellerSprite;

        private GUITickBox powerIndicator;
        private GUIScrollBar forceSlider;
        private GUITickBox autoControlIndicator;

        [Obsolete("Use particleEmitters instead.")]
        private int particlesPerSec = 60;
        [Obsolete("Use particleEmitters instead.")]
        private float particleTimer;

        private readonly List<ParticleEmitter> particleEmitters = new List<ParticleEmitter>();

        public float AnimSpeed
        {
            get;
            private set;
        }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "particleemitter":
                        var emitter = new ParticleEmitter(subElement);
                        particleEmitters.Add(emitter);
                        TryAddParticleEmitterByID(emitter);
                        break;
                }
            }

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.85f, 0.65f), GuiFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.04f)
            }, style: null);

            var lightsArea = new GUIFrame(new RectTransform(new Vector2(1, 0.38f), paddedFrame.RectTransform, Anchor.TopLeft), style: null);
            powerIndicator = new GUITickBox(new RectTransform(new Vector2(0.45f, 0.8f), lightsArea.RectTransform, Anchor.Center, Pivot.CenterRight)
            {
                RelativeOffset = new Vector2(-0.05f, 0)
            }, TextManager.Get("EnginePowered"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightGreen")
            {
                CanBeFocused = false
            };
            autoControlIndicator = new GUITickBox(new RectTransform(new Vector2(0.45f, 0.8f), lightsArea.RectTransform, Anchor.Center, Pivot.CenterLeft)
            {
                RelativeOffset = new Vector2(0.05f, 0)
            }, TextManager.Get("PumpAutoControl", "ReactorAutoControl"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightYellow")
            {
                Selected = false,
                Enabled = false,
                ToolTip = TextManager.Get("AutoControlTip")
            };
            powerIndicator.TextBlock.Wrap = autoControlIndicator.TextBlock.Wrap = true;
            powerIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            autoControlIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            GUITextBlock.AutoScaleAndNormalize(powerIndicator.TextBlock, autoControlIndicator.TextBlock);

            var sliderArea = new GUIFrame(new RectTransform(new Vector2(1, 0.6f), paddedFrame.RectTransform, Anchor.BottomLeft), style: null);
            LocalizedString powerLabel = TextManager.Get("EngineForce");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), sliderArea.RectTransform, Anchor.TopCenter), "", textColor: GUIStyle.TextColorNormal, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center)
            {
                AutoScaleHorizontal = true,
                TextGetter = () => 
                { 
                    return TextManager.AddPunctuation(':', powerLabel, 
                        TextManager.GetWithVariable("percentageformat", "[value]", ((int)MathF.Round(targetForce)).ToString())); 
                }
            };
            forceSlider = new GUIScrollBar(new RectTransform(new Vector2(0.95f, 0.45f), sliderArea.RectTransform, Anchor.Center), barSize: 0.1f, style: "DeviceSlider")
            {
                Step = 0.05f,
                Range = new Vector2(MinThrust, MaxThrust),
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    lastReceivedTargetForce = null;
                    float newTargetForce = scrollBar.BarScrollValue;
                    if (Math.Abs(newTargetForce - targetForce) < 0.01) { return false; }

                    targetForce = newTargetForce;
                    User = Character.Controlled;

                    if (GameMain.Client != null)
                    {
                        correctionTimer = CorrectionDelay;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };

            var textsArea = new GUIFrame(new RectTransform(new Vector2(1, 0.25f), sliderArea.RectTransform, Anchor.BottomCenter), style: null);
            var backwardsLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1.0f), textsArea.RectTransform, Anchor.CenterLeft), TextManager.Get( AllowBackwardsThrust ? "EngineBackwards" : "EngineOff"),
                textColor: GUIStyle.TextColorNormal, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);
            var forwardsLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1.0f), textsArea.RectTransform, Anchor.CenterRight), TextManager.Get("EngineForwards"),
                textColor: GUIStyle.TextColorNormal, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterRight);
            GUITextBlock.AutoScaleAndNormalize(backwardsLabel, forwardsLabel);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "propellersprite":
                        propellerSprite = new SpriteSheet(subElement);
                        AnimSpeed = subElement.GetAttributeFloat("animspeed", 1.0f);
                        break;
                }
            }
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            powerIndicator.Selected = hasPower && IsActive;
            autoControlIndicator.Selected = controlLockTimer > 0.0f;
            forceSlider.Enabled = controlLockTimer <= 0.0f;

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                if (Math.Abs(targetForce - forceSlider.BarScrollValue) > 0.01f)
                {
                    forceSlider.BarScrollValue = targetForce;
                }
            }
        }

        partial void UpdateAnimation(float deltaTime)
        {
            if (propellerSprite == null) { return; }
            spriteIndex += (force / 100.0f) * AnimSpeed * deltaTime;
            if (spriteIndex < 0)
            {
                spriteIndex = propellerSprite.FrameCount - Math.Abs(spriteIndex) % propellerSprite.FrameCount;
            }
            else
            {
                spriteIndex = spriteIndex % propellerSprite.FrameCount;
            }
        }

        partial void EmitParticles(float deltaTime, Vector2 forceVector)
        {
            if (DisablePropellerParticles) { return; }

            Vector2 particlePos =
                turret == null ?
                item.WorldPosition :
                new Vector2(item.WorldRect.X + turret.TransformedBarrelPos.X, item.WorldRect.Y - turret.TransformedBarrelPos.Y);                
            particlePos += GetRotatedVector(PropellerPos, onlyIfRotationAffectsThrustDirection: false) * item.Scale;
            foreach (var particleEmitter in particleEmitters)
            {
                particleEmitter.Emit(deltaTime, particlePos, angle: -Rotation + MathHelper.Pi, particleRotation: Rotation + MathHelper.Pi, velocityMultiplier: Math.Abs(force) / 100.0f);
            }

            float particleInterval = 1.0f / particlesPerSec;
            particleTimer += deltaTime;
            while (particleTimer > particleInterval)
            {
                Vector2 particleVel = -forceVector.ClampLength(5000.0f) / 5.0f;
                GameMain.ParticleManager.CreateParticle("bubbles", particlePos,
                    particleVel * Rand.Range(0.8f, 1.1f),
                    0.0f, item.CurrentHull);
                particleTimer -= particleInterval;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1, Color? overrideColor = null)
        {
            if (propellerSprite != null)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += GetRotatedVector(PropellerPos, onlyIfRotationAffectsThrustDirection: false) * item.Scale;
                drawPos.Y = -drawPos.Y;
                propellerSprite.Draw(spriteBatch, (int)Math.Floor(spriteIndex), drawPos, overrideColor ?? Color.White, propellerSprite.Origin, 0.0f, Vector2.One);
            }

            if (editing && !DisablePropellerDamage && propellerDamage != null && !GUI.DisableHUD)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += GetRotatedVector(PropellerPos, onlyIfRotationAffectsThrustDirection: false) * item.Scale;
                drawPos.Y = -drawPos.Y;
                spriteBatch.DrawCircle(drawPos, propellerDamage.DamageRange * item.Scale, 16, GUIStyle.Red, thickness: 2);
            }
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            //targetForce can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger((int)(targetForce / 10.0f), -10, 10);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(msg.ExtractBits(5 + 16), sendingTime);
                return;
            }

            targetForce = msg.ReadRangedInteger(-10, 10) * 10.0f;
            UInt16 userID = msg.ReadUInt16();
            if (userID != Entity.NullEntityID)
            {
                User = Entity.FindEntityByID(userID) as Character;
            }
        }
    }
}
