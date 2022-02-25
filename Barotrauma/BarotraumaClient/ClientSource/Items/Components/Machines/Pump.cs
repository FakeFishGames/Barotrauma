using Barotrauma.Networking;
using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Pump : Powered, IServerSerializable, IClientSerializable
    {
        public GUIButton PowerButton { get; private set; }

        private GUIScrollBar pumpSpeedSlider;
        private GUITickBox powerLight;
        private GUITickBox autoControlIndicator;

        private readonly List<(Vector2 position, ParticleEmitter emitter)> pumpOutEmitters = new List<(Vector2 position, ParticleEmitter emitter)>(); 
        private readonly List<(Vector2 position, ParticleEmitter emitter)> pumpInEmitters = new List<(Vector2 position, ParticleEmitter emitter)>();

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "pumpoutemitter":
                        pumpOutEmitters.Add((subElement.GetAttributeVector2("position", Vector2.Zero), new ParticleEmitter(subElement)));
                        break;
                    case "pumpinemitter":
                        pumpInEmitters.Add((subElement.GetAttributeVector2("position", Vector2.Zero), new ParticleEmitter(subElement)));
                        break;
                }
            }

            if (GuiFrame == null) { return; }

            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.85f, 0.65f), GuiFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.04f)
            }, style: null);

            // Power button
            float powerButtonSize = 1f;
            var powerArea = new GUIFrame(new RectTransform(new Vector2(0.3f, 1) * powerButtonSize, paddedFrame.RectTransform, Anchor.CenterLeft), style: null);
            var paddedPowerArea = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.8f), powerArea.RectTransform, Anchor.Center), style: "PowerButtonFrame");
            var powerLightArea = new GUIFrame(new RectTransform(new Vector2(0.87f, 0.2f), powerArea.RectTransform, Anchor.TopRight), style: null);
            powerLight = new GUITickBox(new RectTransform(Vector2.One, powerLightArea.RectTransform, Anchor.Center),
                TextManager.Get("PowerLabel"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightPower")
            {
                CanBeFocused = false
            };
            powerLight.TextBlock.AutoScaleHorizontal = true;
            powerLight.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            PowerButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.75f), paddedPowerArea.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, style: "PowerButton")
            {
                OnClicked = (button, data) =>
                {
                    TargetLevel = null;
                    IsActive = !IsActive;
                    if (GameMain.Client != null)
                    {
                        correctionTimer = CorrectionDelay;
                        item.CreateClientEvent(this);
                    }
                    powerLight.Selected = IsActive;
                    return true;
                }
            };

            var rightArea = new GUIFrame(new RectTransform(new Vector2(0.65f, 1), paddedFrame.RectTransform, Anchor.CenterRight), style: null);
            
            autoControlIndicator = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.25f), rightArea.RectTransform, Anchor.TopLeft), 
                TextManager.Get("PumpAutoControl", "ReactorAutoControl"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightYellow")
            {
                Selected = false,
                Enabled = false,
                ToolTip = TextManager.Get("AutoControlTip")
            };
            autoControlIndicator.TextBlock.AutoScaleHorizontal = true;
            autoControlIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);

            var sliderArea = new GUIFrame(new RectTransform(new Vector2(1, 0.65f), rightArea.RectTransform, Anchor.BottomLeft), style: null);
            var pumpSpeedText = new GUITextBlock(new RectTransform(new Vector2(1, 0.3f), sliderArea.RectTransform, Anchor.TopLeft), "", 
                textColor: GUIStyle.TextColorNormal, textAlignment: Alignment.CenterLeft, wrap: false, font: GUIStyle.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };
            LocalizedString pumpSpeedStr = TextManager.Get("PumpSpeed");
            pumpSpeedText.TextGetter = () => { return TextManager.AddPunctuation(':', pumpSpeedStr, (int)Math.Round(flowPercentage) + " %"); };
            pumpSpeedSlider = new GUIScrollBar(new RectTransform(new Vector2(1, 0.35f), sliderArea.RectTransform, Anchor.Center), barSize: 0.1f, style: "DeviceSlider")
            {
                Step = 0.05f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    if (pumpSpeedLockTimer <= 0.0f)
                    {
                        TargetLevel = null;
                    }
                    float newValue = barScroll * 200.0f - 100.0f;
                    if (Math.Abs(newValue - FlowPercentage) < 0.1f) { return false; }

                    FlowPercentage = newValue;

                    if (GameMain.Client != null)
                    {
                        correctionTimer = CorrectionDelay;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };
            var textsArea = new GUIFrame(new RectTransform(new Vector2(1, 0.25f), sliderArea.RectTransform, Anchor.BottomCenter), style: null);
            var outLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), textsArea.RectTransform, Anchor.CenterLeft), TextManager.Get("PumpOut"), 
                textColor: GUIStyle.TextColorNormal, textAlignment: Alignment.CenterLeft, wrap: false, font: GUIStyle.SubHeadingFont);
            var inLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), textsArea.RectTransform, Anchor.CenterRight), TextManager.Get("PumpIn"), 
                textColor: GUIStyle.TextColorNormal, textAlignment: Alignment.CenterRight, wrap: false, font: GUIStyle.SubHeadingFont);
            GUITextBlock.AutoScaleAndNormalize(outLabel, inLabel);
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            if (pumpSpeedSlider != null)
            {
                pumpSpeedSlider.BarScroll = (flowPercentage + 100.0f) / 200.0f;
            }
        }
        
        partial void UpdateProjSpecific(float deltaTime)
        {
            float rotationRad = MathHelper.ToRadians(item.Rotation);
            if (FlowPercentage < 0.0f)
            {
                foreach (var (position, emitter) in pumpOutEmitters)
                {
                    if (item.CurrentHull != null && item.CurrentHull.Surface < item.Rect.Location.Y + position.Y) { continue; }

                    //only emit "pump out" particles when underwater
                    Vector2 relativeParticlePos = (item.WorldRect.Location.ToVector2() + position * item.Scale) - item.WorldPosition;
                    relativeParticlePos = MathUtils.RotatePoint(relativeParticlePos, item.FlippedX ? rotationRad : -rotationRad);
                    float angle = -rotationRad;
                    if (item.FlippedX)
                    {
                        relativeParticlePos.X = -relativeParticlePos.X;
                        angle += MathHelper.Pi;
                    }
                    if (item.FlippedY)
                    {
                        relativeParticlePos.Y = -relativeParticlePos.Y;
                    }

                    emitter.Emit(deltaTime, item.WorldPosition + relativeParticlePos, item.CurrentHull, angle,
                        velocityMultiplier: MathHelper.Lerp(0.5f, 1.0f, -FlowPercentage / 100.0f));
                }
            }
            else if (FlowPercentage > 0.0f)
            {
                foreach (var (position, emitter) in pumpInEmitters)
                {
                    Vector2 relativeParticlePos = (item.WorldRect.Location.ToVector2() + position * item.Scale) - item.WorldPosition;
                    relativeParticlePos = MathUtils.RotatePoint(relativeParticlePos, item.FlippedX ? rotationRad : -rotationRad);
                    float angle = -rotationRad;
                    if (item.FlippedX)
                    {
                        relativeParticlePos.X = -relativeParticlePos.X;
                        angle += MathHelper.Pi;
                    }
                    if (item.FlippedY)
                    {
                        relativeParticlePos.Y = -relativeParticlePos.Y;
                    }
                    emitter.Emit(deltaTime, item.WorldPosition + relativeParticlePos, item.CurrentHull, angle,
                        velocityMultiplier: MathHelper.Lerp(0.5f, 1.0f, FlowPercentage / 100.0f));
                }
            }
        }

        private float flickerTimer;
        private readonly float flickerFrequency = 1;
        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            autoControlIndicator.Selected = IsAutoControlled;
            PowerButton.Enabled = isActiveLockTimer <= 0.0f;
            if (HasPower)
            {
                flickerTimer = 0;
                powerLight.Selected = IsActive;
            }
            else if (IsActive)
            {
                flickerTimer += deltaTime;
                if (flickerTimer > flickerFrequency)
                {
                    flickerTimer = 0;
                    powerLight.Selected = !powerLight.Selected;
                }
            }
            else
            {
                flickerTimer = 0;
                powerLight.Selected = false;
            }
            pumpSpeedSlider.Enabled = pumpSpeedLockTimer <= 0.0f && IsActive;
            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                float pumpSpeedScroll = (FlowPercentage + 100.0f) / 200.0f;
                if (Math.Abs(pumpSpeedScroll - pumpSpeedSlider.BarScroll) > 0.01f)
                {
                    pumpSpeedSlider.BarScroll = pumpSpeedScroll;
                }
            }
        }
        
        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            //flowpercentage can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger((int)(flowPercentage / 10.0f), -10, 10);
            msg.Write(IsActive);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            int msgStartPos = msg.BitPosition;

            float flowPercentage = msg.ReadRangedInteger(-10, 10) * 10.0f;
            bool isActive = msg.ReadBoolean();
            bool hijacked = msg.ReadBoolean();
            float? targetLevel;
            if (msg.ReadBoolean())
            {
                targetLevel = msg.ReadSingle();
            }
            else
            {
                targetLevel = null;
            }

            if (correctionTimer > 0.0f)
            {
                int msgLength = msg.BitPosition - msgStartPos;
                msg.BitPosition = msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime);
                return;
            }

            FlowPercentage = flowPercentage;
            IsActive = isActive;
            Hijacked = hijacked;
            TargetLevel = targetLevel;
        }
    }
}
