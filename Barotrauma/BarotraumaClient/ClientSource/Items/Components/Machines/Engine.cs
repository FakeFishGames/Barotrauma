﻿using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Engine : Powered, IDrawableComponent
    {
        private float spriteIndex;

        private SpriteSheet propellerSprite;

        private GUITickBox powerIndicator;
        private GUIScrollBar forceSlider;
        private GUITickBox autoControlIndicator;

        private int particlesPerSec = 60;
        private float particleTimer;


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
                TextGetter = () => { return TextManager.AddPunctuation(':', powerLabel, (int)(targetForce) + " %"); }
            };
            forceSlider = new GUIScrollBar(new RectTransform(new Vector2(0.95f, 0.45f), sliderArea.RectTransform, Anchor.Center), barSize: 0.1f, style: "DeviceSlider")
            {
                Step = 0.05f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    lastReceivedTargetForce = null;
                    float newTargetForce = barScroll * 200.0f - 100.0f;
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
            var backwardsLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 1.0f), textsArea.RectTransform, Anchor.CenterLeft), TextManager.Get("EngineBackwards"),
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

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            powerIndicator.Selected = hasPower && IsActive;
            autoControlIndicator.Selected = controlLockTimer > 0.0f;
            forceSlider.Enabled = controlLockTimer <= 0.0f;

            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                float newScroll = (targetForce + 100.0f) / 200.0f;
                if (Math.Abs(newScroll - forceSlider.BarScroll) > 0.01f)
                {
                    forceSlider.BarScroll = newScroll;
                }
            }
        }

        partial void UpdateAnimation(float deltaTime)
        {
            if (propellerSprite == null) { return; }
            spriteIndex += (force / 100.0f) * AnimSpeed * deltaTime;
            if (spriteIndex < 0)
            {
                spriteIndex = propellerSprite.FrameCount;
            }
            if (spriteIndex >= propellerSprite.FrameCount)
            {
                spriteIndex = 0.0f;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (propellerSprite != null)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += PropellerPos;
                drawPos.Y = -drawPos.Y;
                propellerSprite.Draw(spriteBatch, (int)Math.Floor(spriteIndex), drawPos, Color.White, propellerSprite.Origin, 0.0f, Vector2.One);
            }

            if (editing && !DisablePropellerDamage && propellerDamage != null && !GUI.DisableHUD)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += PropellerPos * item.Scale;
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
