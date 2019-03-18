using Barotrauma.Networking;
using Lidgren.Network;
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

        public float AnimSpeed
        {
            get;
            private set;
        }

        partial void InitProjSpecific(XElement element)
        {
            powerIndicator = new GUITickBox(new RectTransform(new Point(30, 30), GuiFrame.RectTransform) { RelativeOffset = new Vector2(0.05f, 0.15f) }, 
                TextManager.Get("EnginePowered"), style: "IndicatorLightGreen")
            {
                CanBeFocused = false
            };

            string powerLabel = TextManager.Get("EngineForce");
            new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.3f), GuiFrame.RectTransform, Anchor.BottomCenter)
                { RelativeOffset = new Vector2(0.0f, 0.4f) }, "", textAlignment: Alignment.Center)
            {
                TextGetter = () => { return powerLabel + ": " + (int)(targetForce) + " %"; }
            };

            var sliderArea = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.25f), GuiFrame.RectTransform, Anchor.BottomCenter)
                { RelativeOffset = new Vector2(0.0f, 0.2f) }, isHorizontal: true);

            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), sliderArea.RectTransform), TextManager.Get("EngineBackwards"), 
                font: GUI.SmallFont, textAlignment: Alignment.Center);
            forceSlider = new GUIScrollBar(new RectTransform(new Vector2(0.6f, 1.0f), sliderArea.RectTransform), barSize: 0.25f, style: "GUISlider")
            {
                Step = 0.05f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    float newTargetForce = barScroll * 200.0f - 100.0f;
                    if (Math.Abs(newTargetForce - targetForce) < 0.01) return false;

                    targetForce = newTargetForce;
                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                        GameServer.Log(Character.Controlled.LogName + " set the force speed of " + item.Name + " to " + (int)(targetForce) + " %", ServerLog.MessageType.ItemInteraction);
                    }
                    else if (GameMain.Client != null)
                    {
                        correctionTimer = CorrectionDelay;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };
            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), sliderArea.RectTransform), TextManager.Get("EngineForwards"),
                font: GUI.SmallFont, textAlignment: Alignment.Center);
            
            foreach (XElement subElement in element.Elements())
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

            if (!PlayerInput.LeftButtonHeld())
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
            if (propellerSprite == null) return;

            spriteIndex += (force / 100.0f) * AnimSpeed * deltaTime;
            if (spriteIndex < 0) spriteIndex = propellerSprite.FrameCount;
            if (spriteIndex >= propellerSprite.FrameCount) spriteIndex = 0.0f;
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (propellerSprite != null)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += PropellerPos;
                drawPos.Y = -drawPos.Y;

                propellerSprite.Draw(spriteBatch, (int)Math.Floor(spriteIndex), drawPos, Color.White, propellerSprite.Origin, 0.0f, Vector2.One);
            }

            if (editing)
            {
                Vector2 drawPos = item.DrawPosition;
                drawPos += PropellerPos;
                drawPos.Y = -drawPos.Y;
                GUI.DrawRectangle(spriteBatch, drawPos - Vector2.One * 10, Vector2.One * 20, Color.Red);
            }
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            //targetForce can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger(-10, 10, (int)(targetForce / 10.0f));
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(5), sendingTime);
                return;
            }

            targetForce = msg.ReadRangedInteger(-10, 10) * 10.0f;
        }
    }
}
