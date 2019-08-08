using Barotrauma.Networking;
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

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void InitProjSpecific(XElement element)
        {
            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), GuiFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            int indicatorSize = (int)(30 * GUI.Scale);

            powerIndicator = new GUITickBox(new RectTransform(new Point(indicatorSize, indicatorSize), content.RectTransform), 
                TextManager.Get("EnginePowered"), style: "IndicatorLightGreen")
            {
                CanBeFocused = false
            };

            string powerLabel = TextManager.Get("EngineForce");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform), "", textAlignment: Alignment.Center)
            {
                TextGetter = () => { return TextManager.AddPunctuation(':', powerLabel, (int)(targetForce) + " %"); }
            };

            forceSlider = new GUIScrollBar(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform), barSize: 0.2f, style: "GUISlider")
            {
                Step = 0.05f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    float newTargetForce = barScroll * 200.0f - 100.0f;
                    if (Math.Abs(newTargetForce - targetForce) < 0.01) return false;

                    targetForce = newTargetForce;

                    if (GameMain.Client != null)
                    {
                        correctionTimer = CorrectionDelay;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };

            var textArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), content.RectTransform), isHorizontal: true)
            {
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), textArea.RectTransform), TextManager.Get("EngineBackwards"), 
                font: GUI.SmallFont, textAlignment: Alignment.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), textArea.RectTransform), TextManager.Get("EngineForwards"),
                font: GUI.SmallFont, textAlignment: Alignment.CenterRight);
            
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

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            //targetForce can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedIntegerDeprecated(-10, 10, (int)(targetForce / 10.0f));
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
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
