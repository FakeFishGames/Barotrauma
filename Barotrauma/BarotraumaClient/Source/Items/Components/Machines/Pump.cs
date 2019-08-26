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
        public GUIScrollBar IsActiveSlider
        {
            get { return isActiveSlider; }
        }
        private GUIScrollBar isActiveSlider;
        private GUIScrollBar pumpSpeedSlider;
        private GUITickBox powerIndicator;
        private GUITickBox autoControlIndicator;

        private List<Pair<Vector2, ParticleEmitter>> pumpOutEmitters = new List<Pair<Vector2, ParticleEmitter>>(); 
        private List<Pair<Vector2, ParticleEmitter>> pumpInEmitters = new List<Pair<Vector2, ParticleEmitter>>(); 

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "pumpoutemitter":
                        pumpOutEmitters.Add(new Pair<Vector2, ParticleEmitter>(
                            subElement.GetAttributeVector2("position", Vector2.Zero), 
                            new ParticleEmitter(subElement)));
                        break;
                    case "pumpinemitter":
                        pumpInEmitters.Add(new Pair<Vector2, ParticleEmitter>(
                            subElement.GetAttributeVector2("position", Vector2.Zero),
                            new ParticleEmitter(subElement)));
                        break;
                }
            }

            if (GuiFrame == null) { return; }

            GUIFrame paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.8f), GuiFrame.RectTransform, Anchor.Center), style: null);

            isActiveSlider = new GUIScrollBar(new RectTransform(new Point((int)(50 * GUI.Scale), (int)(100 * GUI.Scale)), paddedFrame.RectTransform, Anchor.CenterLeft),
                barSize: 0.2f, style: "OnOffLever")
            {
                IsBooleanSwitch = true,
                MinValue = 0.25f,
                MaxValue = 0.75f
            };
            var sliderHandle = isActiveSlider.GetChild<GUIButton>();
            sliderHandle.RectTransform.NonScaledSize = new Point((int)(84 * GUI.Scale), sliderHandle.Rect.Height);
            isActiveSlider.OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
            {
                bool active = scrollBar.BarScroll < 0.5f;
                if (active == IsActive) return false;

                targetLevel = null;
                IsActive = active;
                if (!IsActive) currPowerConsumption = 0.0f;

                if (GameMain.Client != null)
                {
                    correctionTimer = CorrectionDelay;
                    item.CreateClientEvent(this);
                }

                return true;
            };

            var rightArea = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.95f), paddedFrame.RectTransform, Anchor.CenterRight))
            {
                RelativeSpacing = 0.1f,
                Stretch = true
            };

            powerIndicator = new GUITickBox(new RectTransform(new Point((int)(30 * GUI.Scale)), rightArea.RectTransform), TextManager.Get("PumpPowered"), style: "IndicatorLightGreen")
            {
                CanBeFocused = false
            };
            autoControlIndicator = new GUITickBox(new RectTransform(new Point((int)(30 * GUI.Scale)), rightArea.RectTransform), TextManager.Get("PumpAutoControl", fallBackTag: "ReactorAutoControl"), style: "IndicatorLightGreen")
            {
                CanBeFocused = false
            };

            var pumpSpeedText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), rightArea.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.0f) },
                "", textAlignment: Alignment.BottomLeft, wrap: true);
            string pumpSpeedStr = TextManager.Get("PumpSpeed");
            pumpSpeedText.TextGetter = () => { return TextManager.AddPunctuation(':', pumpSpeedStr, (int)flowPercentage + " %"); };

            var sliderArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), rightArea.RectTransform, Anchor.CenterLeft), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.01f
            };

            var outLabel = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), sliderArea.RectTransform),
                TextManager.Get("PumpOut"), textAlignment: Alignment.Center, wrap: false, font: GUI.SmallFont);
            pumpSpeedSlider = new GUIScrollBar(new RectTransform(new Vector2(0.5f, 1.0f), sliderArea.RectTransform), barSize: 0.25f, style: "GUISlider")
            {
                Step = 0.05f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    float newValue = barScroll * 200.0f - 100.0f;
                    if (Math.Abs(newValue - FlowPercentage) < 0.1f) return false;

                    FlowPercentage = newValue;

                    if (GameMain.Client != null)
                    {
                        correctionTimer = CorrectionDelay;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };
            var inLabel = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), sliderArea.RectTransform), 
                TextManager.Get("PumpIn"), textAlignment: Alignment.Center, wrap: false, font: GUI.SmallFont);

            rightArea.Recalculate();
            sliderArea.Recalculate();
            GUITextBlock.AutoScaleAndNormalize(outLabel, inLabel);
        }

        public override void OnItemLoaded()
        {
            if (pumpSpeedSlider != null)
            {
                pumpSpeedSlider.BarScroll = (flowPercentage + 100.0f) / 200.0f;
            }
        }
        
        partial void UpdateProjSpecific(float deltaTime)
        {
            if (FlowPercentage < 0.0f)
            {
                foreach (Pair<Vector2, ParticleEmitter> pumpOutEmitter in pumpOutEmitters)
                {
                    //only emit "pump out" particles when underwater
                    Vector2 particlePos = item.Rect.Location.ToVector2() + pumpOutEmitter.First;
                    if (item.CurrentHull != null && item.CurrentHull.Surface < particlePos.Y) continue;

                    pumpOutEmitter.Second.Emit(deltaTime, item.WorldRect.Location.ToVector2() + pumpOutEmitter.First * item.Scale, item.CurrentHull,
                        velocityMultiplier: MathHelper.Lerp(0.5f, 1.0f, -FlowPercentage / 100.0f));
                }
            }
            else if (FlowPercentage > 0.0f)
            {
                foreach (Pair<Vector2, ParticleEmitter> pumpInEmitter in pumpInEmitters)
                {
                    pumpInEmitter.Second.Emit(deltaTime, item.WorldRect.Location.ToVector2() + pumpInEmitter.First * item.Scale, item.CurrentHull,
                        velocityMultiplier: MathHelper.Lerp(0.5f, 1.0f, FlowPercentage / 100.0f));
                }
            }
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            powerIndicator.Selected = hasPower && IsActive;
            autoControlIndicator.Selected = controlLockTimer > 0.0f && IsActive;
            pumpSpeedSlider.Enabled = controlLockTimer <= 0.0f && IsActive;

            if (!PlayerInput.LeftButtonHeld())
            {
                isActiveSlider.BarScroll += (IsActive ? -10.0f : 10.0f) * deltaTime;

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
            msg.WriteRangedIntegerDeprecated(-10, 10, (int)(flowPercentage / 10.0f));
            msg.Write(IsActive);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(5 + 1), sendingTime);
                return;
            }

            FlowPercentage = msg.ReadRangedInteger(-10, 10) * 10.0f;
            IsActive = msg.ReadBoolean();
        }
    }
}
