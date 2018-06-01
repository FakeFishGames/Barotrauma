using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private GUITickBox autopilotTickBox, maintainPosTickBox;
        private GUITickBox levelEndTickBox, levelStartTickBox;

        private GUIComponent steerArea;

        public bool LevelStartSelected
        {
            get { return levelStartTickBox.Selected; }
            set { levelStartTickBox.Selected = value; }
        }

        public bool LevelEndSelected
        {
            get { return levelEndTickBox.Selected; }
            set { levelEndTickBox.Selected = value; }
        }

        public bool MaintainPos
        {
            get { return maintainPosTickBox.Selected; }
            set { maintainPosTickBox.Selected = value; }
        }

        partial void InitProjSpecific()
        {
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), style: null)
            {
                CanBeFocused = false
            };
            var tickBoxContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.15f, 1.0f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(0, 30) })
            {
                AbsoluteSpacing = 5
            };

            autopilotTickBox = new GUITickBox(new RectTransform(new Point(20, 20), tickBoxContainer.RectTransform),
                TextManager.Get("SteeringAutoPilot"))
            {
                OnSelected = (GUITickBox box) =>
                {
                    AutoPilot = box.Selected;
                    unsentChanges = true;

                    return true;
                }
            };

            maintainPosTickBox = new GUITickBox(new RectTransform(new Point(20, 20), tickBoxContainer.RectTransform),
                TextManager.Get("SteeringMaintainPos"), font: GUI.SmallFont)
            {
                Enabled = false,
                OnSelected = ToggleMaintainPosition
            };

            levelStartTickBox = new GUITickBox(new RectTransform(new Point(20, 20), tickBoxContainer.RectTransform),
                GameMain.GameSession == null ? "" : ToolBox.LimitString(GameMain.GameSession.StartLocation.Name, 20),
                font: GUI.SmallFont)
            {
                Enabled = false,
                OnSelected = SelectDestination
            };

            levelEndTickBox = new GUITickBox(new RectTransform(new Point(20, 20), tickBoxContainer.RectTransform),
                GameMain.GameSession == null ? "" : ToolBox.LimitString(GameMain.GameSession.EndLocation.Name, 20),
                font: GUI.SmallFont)
            {
                Enabled = false,
                OnSelected = SelectDestination
            };

            var textContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.15f, 0.5f), paddedFrame.RectTransform, Anchor.BottomLeft), childAnchor: Anchor.BottomLeft)
            {
                AbsoluteSpacing = 5
            };

            string steeringVelX = TextManager.Get("SteeringVelocityX");
            string steeringVelY = TextManager.Get("SteeringVelocityY");
            string steeringDepth = TextManager.Get("SteeringDepth");
            new GUITextBlock(new RectTransform(new Point(100, 15), textContainer.RectTransform), "")
            {
                TextGetter = () =>
                {
                    var realWorldVel = ConvertUnits.ToDisplayUnits(item.Submarine.Velocity.Y * Physics.DisplayToRealWorldRatio) * 3.6f;
                    return steeringVelY.Replace("[kph]", ((int)realWorldVel).ToString());
                }
            };
            new GUITextBlock(new RectTransform(new Point(100, 15), textContainer.RectTransform), "")
            {
                TextGetter = () => 
                {
                    var realWorldVel = ConvertUnits.ToDisplayUnits(item.Submarine.Velocity.X * Physics.DisplayToRealWorldRatio) * 3.6f;
                    return steeringVelX.Replace("[kph]", ((int)realWorldVel).ToString());
                }
            };
            new GUITextBlock(new RectTransform(new Point(100, 15), textContainer.RectTransform), "")
            {
                TextGetter = () =>
                {
                    float realWorldDepth = Math.Abs(item.Submarine.Position.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                    return steeringDepth.Replace("[m]", ((int)realWorldDepth).ToString());
                }
            };

            steerArea = new GUICustomComponent(new RectTransform(new Point(GuiFrame.Rect.Height, GuiFrame.Rect.Width), GuiFrame.RectTransform, Anchor.CenterRight) { AbsoluteOffset = new Point(10, 0) },
                (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); }, null);
        }

        private bool ToggleMaintainPosition(GUITickBox tickBox)
        {
            unsentChanges = true;

            levelStartTickBox.Selected = false;
            levelEndTickBox.Selected = false;

            if (item.Submarine == null)
            {
                posToMaintain = null;
            }
            else
            {
                posToMaintain = item.Submarine.WorldPosition;
            }

            tickBox.Selected = true;

            return true;
        }

        public void DrawHUD(SpriteBatch spriteBatch, Rectangle rect)
        {
            int width = rect.Width, height = rect.Height;
            int x = rect.X;
            int y = rect.Y;
            
            if (voltage < minVoltage && currPowerConsumption > 0.0f) return;

            Rectangle velRect = new Rectangle(x + 20, y + 20, width - 40, height - 40);            
            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                new Vector2(velRect.Center.X + currVelocity.X, velRect.Center.Y - currVelocity.Y),
                Color.Gray);

            if (!AutoPilot)
            {
                Vector2 steeringInputPos = new Vector2(velRect.Center.X + steeringInput.X, velRect.Center.Y - steeringInput.Y);

                GUI.DrawLine(spriteBatch,
                    new Vector2(velRect.Center.X, velRect.Center.Y),
                    steeringInputPos,
                    Color.LightGray);

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 5, (int)steeringInputPos.Y - 5, 10, 10), Color.White);

                if (Vector2.Distance(PlayerInput.MousePosition, new Vector2(velRect.Center.X, velRect.Center.Y)) < 200.0f)
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 10, (int)steeringInputPos.Y - 10, 20, 20), Color.Red);
                }
            }

            Vector2 steeringPos = new Vector2(velRect.Center.X + targetVelocity.X * 0.9f, velRect.Center.Y - targetVelocity.Y * 0.9f);

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                steeringPos,
                Color.CadetBlue, 0, 2);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime)
        {
            if (voltage < minVoltage && currPowerConsumption > 0.0f) return;

            if (Vector2.Distance(PlayerInput.MousePosition, steerArea.Rect.Center.ToVector2()) < steerArea.Rect.Width / 2)
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    SteeringInput = PlayerInput.MousePosition - steerArea.Rect.Center.ToVector2();
                    steeringInput.Y = -steeringInput.Y;

                    steeringAdjustSpeed = character == null ? 
                        0.2f : MathHelper.Lerp(0.2f, 1.0f, character.GetSkillLevel("Helm") / 100.0f);

                    unsentChanges = true;
                }
            }
        }

        private bool SelectDestination(GUITickBox tickBox)
        {
            unsentChanges = true;

            if (tickBox == levelStartTickBox)
            {
                levelEndTickBox.Selected = false;
            }
            else
            {
                levelStartTickBox.Selected = false;
            }

            maintainPosTickBox.Selected = false;
            posToMaintain = null;
            tickBox.Selected = true;

            UpdatePath();

            return true;
        }

        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            msg.Write(autoPilot);

            if (!autoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.Write(steeringInput.X);
                msg.Write(steeringInput.Y);
            }
            else
            {
                msg.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.Write(((Vector2)posToMaintain).X);
                    msg.Write(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.Write(LevelStartSelected);
                }
            }
        }
        
        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            long msgStartPos = msg.Position;

            bool autoPilot                  = msg.ReadBoolean();
            Vector2 newSteeringInput        = steeringInput;
            Vector2 newTargetVelocity       = targetVelocity;
            float newSteeringAdjustSpeed    = steeringAdjustSpeed;
            bool maintainPos                = false;
            Vector2? newPosToMaintain       = null;
            bool headingToStart             = false;

            if (autoPilot)
            {
                maintainPos = msg.ReadBoolean();
                if (maintainPos)
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadFloat(),
                        msg.ReadFloat());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newSteeringInput = new Vector2(msg.ReadFloat(), msg.ReadFloat());
                newTargetVelocity = new Vector2(msg.ReadFloat(), msg.ReadFloat());
                newSteeringAdjustSpeed = msg.ReadFloat();
            }

            if (correctionTimer > 0.0f)
            {
                int msgLength = (int)(msg.Position - msgStartPos);
                msg.Position = msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime);
                return;
            }

            AutoPilot = autoPilot;

            if (!AutoPilot)
            {
                SteeringInput = newSteeringInput;
                TargetVelocity = newTargetVelocity;
                steeringAdjustSpeed = newSteeringAdjustSpeed;
            }
            else
            {
                MaintainPos = newPosToMaintain != null;
                posToMaintain = newPosToMaintain;

                if (posToMaintain == null)
                {
                    LevelStartSelected = headingToStart;
                    LevelEndSelected = !headingToStart;
                    UpdatePath();
                }
                else
                {
                    LevelStartSelected = false;
                    LevelEndSelected = false;
                }
            }
        }
    }
}
