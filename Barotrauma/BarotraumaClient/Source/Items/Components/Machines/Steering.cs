using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private GUITickBox autopilotTickBox, manualTickBox;
        private GUITickBox maintainPosTickBox, levelEndTickBox, levelStartTickBox;

        private GUIFrame autoPilotControlsDisabler;

        private GUIComponent steerArea;

        private GUITextBlock pressureWarningText;

        private GUITextBlock tipContainer;

        private string noPowerTip, autoPilotMaintainPosTip, autoPilotLevelStartTip, autoPilotLevelEndTip;

        private Vector2 keyboardInput = Vector2.Zero;
        private float inputCumulation;

        private bool levelStartSelected;
        public bool LevelStartSelected
        {
            get { return levelStartTickBox.Selected; }
            set { levelStartTickBox.Selected = value; }
        }

        private bool levelEndSelected;
        public bool LevelEndSelected
        {
            get { return levelEndTickBox.Selected; }
            set { levelEndTickBox.Selected = value; }
        }

        private bool maintainPos;
        public bool MaintainPos
        {
            get { return maintainPosTickBox.Selected; }
            set { maintainPosTickBox.Selected = value; }
        }

        public bool DockingModeEnabled
        {
            get;
            set;
        }

        public DockingPort DockingSource, DockingTarget;

        partial void InitProjSpecific()
        {
            int viewSize = (int)Math.Min(GuiFrame.Rect.Width - 150, GuiFrame.Rect.Height * 0.9f);
            var controlContainer = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.35f), GuiFrame.RectTransform, Anchor.CenterLeft)
                { MinSize = new Point(150, 0), AbsoluteOffset = new Point((int)(viewSize * 0.99f), (int)(viewSize * 0.05f)) }, "SonarFrame");
            var paddedControlContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), controlContainer.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.03f,
                Stretch = true
            };

            var statusContainer = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GuiFrame.RectTransform, Anchor.BottomLeft)
                { MinSize = new Point(150, 0), AbsoluteOffset = new Point((int)(viewSize * 0.9f), 0) }, "SonarFrame");
            var paddedStatusContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), statusContainer.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.03f,
                Stretch = true
            };

            manualTickBox = new GUITickBox(new RectTransform(new Vector2(0.3f, 0.3f), paddedControlContainer.RectTransform),
                TextManager.Get("SteeringManual"), style: "GUIRadioButton")
            {
                Selected = true,
                OnSelected = (GUITickBox box) =>
                {
                    AutoPilot = !box.Selected;
                    unsentChanges = true;
                    user = Character.Controlled;

                    return true;
                }
            };
            autopilotTickBox = new GUITickBox(new RectTransform(new Vector2(0.3f, 0.3f), paddedControlContainer.RectTransform),
                TextManager.Get("SteeringAutoPilot"), style: "GUIRadioButton")
            {
                OnSelected = (GUITickBox box) =>
                {
                    AutoPilot = box.Selected;
                    if (AutoPilot && MaintainPos)
                    {
                        posToMaintain = controlledSub == null ? item.WorldPosition : controlledSub.WorldPosition;
                    }
                    unsentChanges = true;
                    user = Character.Controlled;

                    return true;
                }
            };

            GUITickBox.CreateRadioButtonGroup(new List<GUITickBox> { manualTickBox, autopilotTickBox });

            var autoPilotControls = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.6f), paddedControlContainer.RectTransform), "InnerFrame");
            var paddedAutoPilotControls = new GUILayoutGroup(new RectTransform(new Vector2(0.8f), autoPilotControls.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            maintainPosTickBox = new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), paddedAutoPilotControls.RectTransform),
                TextManager.Get("SteeringMaintainPos"), font: GUI.SmallFont)
            {
                Enabled = false,
                Selected = maintainPos,
                OnSelected = tickBox =>
                {
                    if (maintainPos != tickBox.Selected)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                        maintainPos = tickBox.Selected;
                        if (maintainPos)
                        {
                            if (controlledSub == null)
                            {
                                posToMaintain = null;
                            }
                            else
                            {
                                posToMaintain = controlledSub.WorldPosition;
                            }
                        }
                        else if (!LevelEndSelected && !LevelStartSelected)
                        {
                            AutoPilot = false;
                        }
                        if (!maintainPos)
                        {
                            posToMaintain = null;
                        }
                    }
                    return true;
                }
            };

            levelStartTickBox = new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), paddedAutoPilotControls.RectTransform),
                GameMain.GameSession?.StartLocation == null ? "" : ToolBox.LimitString(GameMain.GameSession.StartLocation.Name, 20),
                font: GUI.SmallFont)
            {
                Enabled = false,
                Selected = levelStartSelected,
                OnSelected = tickBox =>
                {
                    if (levelStartSelected != tickBox.Selected)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                        levelStartSelected = tickBox.Selected;
                        levelEndSelected = !levelStartSelected;
                        if (levelStartSelected)
                        {
                            UpdatePath();
                        }
                        else if (!MaintainPos && !LevelEndSelected)
                        {
                            AutoPilot = false;
                        }
                    }
                    return true;
                }
            };

            levelEndTickBox = new GUITickBox(new RectTransform(new Vector2(0.2f, 0.2f), paddedAutoPilotControls.RectTransform),
                GameMain.GameSession?.EndLocation == null ? "" : ToolBox.LimitString(GameMain.GameSession.EndLocation.Name, 20),
                font: GUI.SmallFont)
            {
                Enabled = false,
                Selected = levelEndSelected,
                OnSelected = tickBox =>
                {
                    if (levelEndSelected != tickBox.Selected)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                        levelEndSelected = tickBox.Selected;
                        levelStartSelected = !levelEndSelected;
                        if (levelEndSelected)
                        {
                            UpdatePath();
                        }
                        else if (!MaintainPos && !LevelStartSelected)
                        {
                            AutoPilot = false;
                        }
                    }
                    return true;
                }
            };

            autoPilotControlsDisabler = new GUIFrame(new RectTransform(Vector2.One, autoPilotControls.RectTransform), "InnerFrame");

            GUITickBox.CreateRadioButtonGroup(new List<GUITickBox> { maintainPosTickBox, levelStartTickBox, levelEndTickBox });
            
            string steeringVelX = TextManager.Get("SteeringVelocityX");
            string steeringVelY = TextManager.Get("SteeringVelocityY");
            string steeringDepth = TextManager.Get("SteeringDepth");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), paddedStatusContainer.RectTransform), "")
            {
                TextGetter = () =>
                {
                    Vector2 vel = controlledSub == null ? Vector2.Zero : controlledSub.Velocity;
                    var realWorldVel = ConvertUnits.ToDisplayUnits(vel.Y * Physics.DisplayToRealWorldRatio) * 3.6f;
                    return steeringVelY.Replace("[kph]", ((int)-realWorldVel).ToString());
                }
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), paddedStatusContainer.RectTransform), "")
            {
                TextGetter = () =>
                {
                    Vector2 vel = controlledSub == null ? Vector2.Zero : controlledSub.Velocity;
                    var realWorldVel = ConvertUnits.ToDisplayUnits(vel.X * Physics.DisplayToRealWorldRatio) * 3.6f;
                    return steeringVelX.Replace("[kph]", ((int)realWorldVel).ToString());
                }
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), paddedStatusContainer.RectTransform), "")
            {
                TextGetter = () =>
                {
                    Vector2 pos = controlledSub == null ? Vector2.Zero : controlledSub.Position;
                    float realWorldDepth = Level.Loaded == null ? 0.0f : Math.Abs(pos.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                    return steeringDepth.Replace("[m]", ((int)realWorldDepth).ToString());
                }
            };

            pressureWarningText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), paddedStatusContainer.RectTransform), TextManager.Get("SteeringDepthWarning"), Color.Red)
            {
                Visible = false
            };

            tipContainer = new GUITextBlock(new RectTransform(new Vector2(0.25f, 0.12f), GuiFrame.RectTransform, Anchor.BottomLeft)
            { MinSize = new Point(150, 0), RelativeOffset = new Vector2(0.0f, -0.05f) }, "", wrap: true, style: "GUIToolTip")
            {
                AutoScale = true
            };

            noPowerTip = TextManager.Get("SteeringNoPowerTip");
            autoPilotMaintainPosTip = TextManager.Get("SteeringAutoPilotMaintainPosTip");
            autoPilotLevelStartTip = TextManager.Get("SteeringAutoPilotLocationTip").Replace("[locationname]", 
                GameMain.GameSession?.StartLocation == null ? "Start" : GameMain.GameSession.StartLocation.Name);
            autoPilotLevelEndTip = TextManager.Get("SteeringAutoPilotLocationTip").Replace("[locationname]", 
                GameMain.GameSession?.EndLocation == null ? "End" : GameMain.GameSession.EndLocation.Name);

            steerArea = new GUICustomComponent(new RectTransform(new Point(viewSize), GuiFrame.RectTransform, Anchor.CenterLeft),
                (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); }, null);
        }

        /// <summary>
        /// Makes the sonar view CustomComponent render the steering HUD, preventing it from being drawn behing the sonar
        /// </summary>
        public void AttachToSonarHUD(GUICustomComponent sonarView)
        {
            steerArea.Visible = false;
            sonarView.OnDraw += (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); };
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
                Vector2 unitSteeringInput = steeringInput / 100.0f;
                //map input from rectangle to circle
                Vector2 steeringInputPos = new Vector2(
                    steeringInput.X * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.Y * unitSteeringInput.Y),
                    -steeringInput.Y * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.X * unitSteeringInput.X));
                steeringInputPos.X += velRect.Center.X;
                steeringInputPos.Y += velRect.Center.Y;

                GUI.DrawLine(spriteBatch,
                    new Vector2(velRect.Center.X, velRect.Center.Y),
                    steeringInputPos,
                    Color.LightGray);

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 5, (int)steeringInputPos.Y - 5, 10, 10), Color.White);

                //if (keyboardInput.Length() > 0 || Vector2.Distance(PlayerInput.MousePosition, new Vector2(velRect.Center.X, velRect.Center.Y)) < 200.0f)
                if (velRect.Contains(PlayerInput.MousePosition))
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 10, (int)steeringInputPos.Y - 10, 20, 20), Color.Red);
                }
            }
            else if (posToMaintain.HasValue && !LevelStartSelected && !LevelEndSelected)
            {
                Sonar sonar = item.GetComponent<Sonar>();
                if (sonar != null && controlledSub != null)
                {
                    Vector2 displayPosToMaintain = (posToMaintain.Value - controlledSub.WorldPosition) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
                    displayPosToMaintain.Y = -displayPosToMaintain.Y;
                    displayPosToMaintain = displayPosToMaintain.ClampLength(velRect.Width / 2);

                    displayPosToMaintain = velRect.Center.ToVector2() + displayPosToMaintain;
                    
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)displayPosToMaintain.X - 5, (int)displayPosToMaintain.Y - 5, 10, 10), Color.Red);
                }
            }

            //map velocity from rectangle to circle
            Vector2 unitTargetVel = targetVelocity / 100.0f;
            Vector2 steeringPos = new Vector2(
                targetVelocity.X * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.Y * unitTargetVel.Y),
                -targetVelocity.Y * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.X * unitTargetVel.X));
            steeringPos.X += velRect.Center.X;
            steeringPos.Y += velRect.Center.Y;

            GUI.DrawLine(spriteBatch,
                new Vector2(velRect.Center.X, velRect.Center.Y),
                steeringPos,
                Color.CadetBlue, 0, 2);            
        }

        public void DebugDrawHUD(SpriteBatch spriteBatch, Vector2 transducerCenter, float displayScale, float displayRadius, Vector2 center)
        {
            if (SteeringPath == null) return;

            Vector2 prevPos = Vector2.Zero;
            foreach (WayPoint wp in SteeringPath.Nodes)
            {
                Vector2 pos = (wp.Position - transducerCenter) * displayScale;
                if (pos.Length() > displayRadius) continue;

                pos.Y = -pos.Y;
                pos += center;

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 3 / 2, (int)pos.Y - 3, 6, 6), (SteeringPath.CurrentNode == wp) ? Color.LightGreen : Color.Green, false);

                if (prevPos != Vector2.Zero)
                {
                    GUI.DrawLine(spriteBatch, pos, prevPos, Color.Green);
                }

                prevPos = pos;
            }

            foreach (ObstacleDebugInfo obstacle in debugDrawObstacles)
            {
                Vector2 pos1 = (obstacle.Point1 - transducerCenter) * displayScale;
                pos1.Y = -pos1.Y;
                pos1 += center;
                Vector2 pos2 = (obstacle.Point2 - transducerCenter) * displayScale;
                pos2.Y = -pos2.Y;
                pos2 += center;

                GUI.DrawLine(spriteBatch, 
                    pos1, 
                    pos2,
                    Color.Red * 0.6f, width: 3);

                if (obstacle.Intersection.HasValue)
                {
                    Vector2 intersectionPos = (obstacle.Intersection.Value - transducerCenter) *displayScale;
                    intersectionPos.Y = -intersectionPos.Y;
                    intersectionPos += center;
                    GUI.DrawRectangle(spriteBatch, intersectionPos - Vector2.One * 2, Vector2.One * 4, Color.Red);
                }

                Vector2 obstacleCenter = (pos1 + pos2) / 2;
                if (obstacle.AvoidStrength.LengthSquared() > 0.01f)
                {
                    GUI.DrawLine(spriteBatch,
                        obstacleCenter,
                        obstacleCenter + new Vector2(obstacle.AvoidStrength.X, -obstacle.AvoidStrength.Y) * 100,
                        Color.Lerp(Color.Green, Color.Orange, obstacle.Dot), width: 2);
                }
            }
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (steerArea.Rect.Contains(PlayerInput.MousePosition))
            {
                if (!PlayerInput.KeyDown(InputType.Select) && !PlayerInput.KeyHit(InputType.Select))
                {
                    Character.DisableControls = true;
                }
            }

            autoPilotControlsDisabler.Visible = !AutoPilot;

            if (voltage < minVoltage && currPowerConsumption > 0.0f)
            {
                tipContainer.Visible = true;
                tipContainer.Text = noPowerTip;
                return;
            }


            tipContainer.Visible = AutoPilot;
            if (AutoPilot)
            {
                if (maintainPos)
                {
                    tipContainer.Text = autoPilotMaintainPosTip;
                }
                else if (LevelStartSelected)
                {
                    tipContainer.Text = autoPilotLevelStartTip;
                }
                else if (LevelEndSelected)
                {
                    tipContainer.Text = autoPilotLevelEndTip;
                }
            }

            pressureWarningText.Visible = item.Submarine != null && item.Submarine.AtDamageDepth && Timing.TotalTime % 1.0f < 0.5f;

            if (Vector2.Distance(PlayerInput.MousePosition, steerArea.Rect.Center.ToVector2()) < steerArea.Rect.Width / 2)
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    Vector2 inputPos = PlayerInput.MousePosition - steerArea.Rect.Center.ToVector2();
                    inputPos.Y = -inputPos.Y;
                    if (AutoPilot && !LevelStartSelected && !LevelEndSelected)
                    {
                        posToMaintain = controlledSub == null ? item.WorldPosition : controlledSub.WorldPosition + inputPos / sonar.DisplayRadius * sonar.Range / sonar.Zoom;                        
                    }
                    else
                    {
                        SteeringInput = inputPos;
                    }
                    unsentChanges = true;
                    user = Character.Controlled;
                }
            }
            if (!AutoPilot && Character.DisableControls)
            {
                steeringAdjustSpeed = character == null ? 0.2f : MathHelper.Lerp(0.2f, 1.0f, character.GetSkillLevel("helm") / 100.0f);
                Vector2 input = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left))
                {
                    input -= Vector2.UnitX;
                }
                if (PlayerInput.KeyDown(InputType.Right))
                {
                    input += Vector2.UnitX;
                }
                if (PlayerInput.KeyDown(InputType.Up))
                {
                    input += Vector2.UnitY;
                }
                if (PlayerInput.KeyDown(InputType.Down))
                {
                    input -= Vector2.UnitY;
                }
                if (PlayerInput.KeyDown(Keys.LeftShift))
                {
                    SteeringInput += input * deltaTime * 200;
                    inputCumulation = 0;
                    keyboardInput = Vector2.Zero;
                    unsentChanges = true;
                }
                else
                {
                    float step = deltaTime * 5;
                    if (input.Length() > 0)
                    {
                        inputCumulation += step;
                    }
                    else
                    {
                        inputCumulation -= step;
                    }
                    float maxCumulation = 1;
                    inputCumulation = MathHelper.Clamp(inputCumulation, 0, maxCumulation);
                    float length = MathHelper.Lerp(0, 0.2f, MathUtils.InverseLerp(0, maxCumulation, inputCumulation));
                    var normalizedInput = Vector2.Normalize(input);
                    if (MathUtils.IsValid(normalizedInput))
                    {
                        keyboardInput += normalizedInput * length;
                    }
                    if (keyboardInput.LengthSquared() > 0.01f)
                    {
                        SteeringInput += keyboardInput;
                        unsentChanges = true;
                        user = Character.Controlled;
                        keyboardInput *= MathHelper.Clamp(1 - step, 0, 1);
                    }
                }
            }
            else
            {
                inputCumulation = 0;
                keyboardInput = Vector2.Zero;
            }


            float closestDist = DockingAssistThreshold * DockingAssistThreshold;
            DockingModeEnabled = false;
            DockingSource = null;
            DockingTarget = null;
            foreach (DockingPort sourcePort in DockingPort.List)
            {
                if (sourcePort.Docked || sourcePort.Item.Submarine == null) { continue; }
                if (sourcePort.Item.Submarine != controlledSub) { continue; }

                int sourceDir = sourcePort.IsHorizontal ?
                    Math.Sign(sourcePort.Item.WorldPosition.X - sourcePort.Item.Submarine.WorldPosition.X) :
                    Math.Sign(sourcePort.Item.WorldPosition.Y - sourcePort.Item.Submarine.WorldPosition.Y);

                foreach (DockingPort targetPort in DockingPort.List)
                {
                    if (targetPort.Docked || targetPort.Item.Submarine == null) { continue; }
                    if (targetPort.Item.Submarine == controlledSub || targetPort.IsHorizontal != sourcePort.IsHorizontal) { continue; }

                    int targetDir = targetPort.IsHorizontal ?
                        Math.Sign(targetPort.Item.WorldPosition.X - targetPort.Item.Submarine.WorldPosition.X) :
                        Math.Sign(targetPort.Item.WorldPosition.Y - targetPort.Item.Submarine.WorldPosition.Y);

                    if (sourceDir == targetDir) { continue; }

                    float dist = Vector2.DistanceSquared(sourcePort.Item.WorldPosition, targetPort.Item.WorldPosition);
                    if (dist < closestDist)
                    {
                        DockingModeEnabled = true;
                        DockingSource = sourcePort;
                        DockingTarget = targetPort;
                    }
                }
            }
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
