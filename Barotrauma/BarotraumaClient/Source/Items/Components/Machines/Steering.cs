using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        enum Mode
        {
            AutoPilot,
            Manual
        };
        private GUITickBox autopilotTickBox, manualTickBox;

        enum Destination
        {
            MaintainPos,
            LevelEnd,
            LevelStart
        };
        private GUITickBox maintainPosTickBox, levelEndTickBox, levelStartTickBox;

        private GUIComponent statusContainer, dockingContainer, controlContainer;

        private bool dockingNetworkMessagePending;

        private GUIButton dockingButton;
        private string dockText, undockText;

        private GUIFrame autoPilotControlsDisabler;

        private GUIComponent steerArea;

        private GUITextBlock pressureWarningText;

        private GUITextBlock tipContainer;

        private string noPowerTip, autoPilotMaintainPosTip, autoPilotLevelStartTip, autoPilotLevelEndTip;

        private Sprite maintainPosIndicator, maintainPosOriginIndicator;
        private Sprite steeringIndicator;

        private Vector2 keyboardInput = Vector2.Zero;
        private float inputCumulation;

        private bool? swapDestinationOrder;

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

        public List<DockingPort> DockingSources = new List<DockingPort>();
        public DockingPort ActiveDockingSource, DockingTarget;

        private bool searchedConnectedDockingPort;

        partial void InitProjSpecific(XElement element)
        {
            controlContainer = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.35f), GuiFrame.RectTransform, Anchor.CenterLeft)
                { MinSize = new Point(150, 0) }, "SonarFrame");
            var paddedControlContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), controlContainer.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.03f,
                Stretch = true
            };

            statusContainer = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GuiFrame.RectTransform, Anchor.BottomLeft)
                { MinSize = new Point(150, 0) }, "SonarFrame");
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
                        posToMaintain = controlledSub != null ?
                            controlledSub.WorldPosition :
                            item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
                    }
                    unsentChanges = true;
                    user = Character.Controlled;

                    return true;
                }
            };

            GUIRadioButtonGroup modes = new GUIRadioButtonGroup();
            modes.AddRadioButton(Mode.AutoPilot, autopilotTickBox);
            modes.AddRadioButton(Mode.Manual, manualTickBox);
            modes.Selected = Mode.Manual;
            
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

            GUIRadioButtonGroup destinations = new GUIRadioButtonGroup();
            destinations.AddRadioButton(Destination.MaintainPos, maintainPosTickBox);
            destinations.AddRadioButton(Destination.LevelStart, levelStartTickBox);
            destinations.AddRadioButton(Destination.LevelEnd, levelEndTickBox);
            destinations.Selected = maintainPos        ? Destination.MaintainPos :
                                    levelStartSelected ? Destination.LevelStart  : Destination.LevelEnd;
            
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
            autoPilotLevelStartTip = TextManager.GetWithVariable("SteeringAutoPilotLocationTip", "[locationname]",
                GameMain.GameSession?.StartLocation == null ? "Start" : GameMain.GameSession.StartLocation.Name);
            autoPilotLevelEndTip = TextManager.GetWithVariable("SteeringAutoPilotLocationTip", "[locationname]",
                GameMain.GameSession?.EndLocation == null ? "End" : GameMain.GameSession.EndLocation.Name);

            steerArea = new GUICustomComponent(new RectTransform(Point.Zero, GuiFrame.RectTransform, Anchor.CenterLeft),
                (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); }, null);

            //docking interface ----------------------------------------------------
            dockingContainer = new GUIFrame(new RectTransform(new Vector2(0.3f, 0.25f), GuiFrame.RectTransform, Anchor.BottomLeft)
            { MinSize = new Point(150, 0) }, style: null);
            var paddedDockingContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), dockingContainer.RectTransform, Anchor.Center), style: null);

            //TODO: add new texts for these ("Dock" & "Undock")
            dockText = TextManager.Get("captain.dock");
            undockText = TextManager.Get("captain.undock");
            dockingButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), paddedDockingContainer.RectTransform, Anchor.Center), dockText, style: "GUIButtonLarge")
            {
                OnClicked = (btn, userdata) =>
                {
                    if (GameMain.Client == null)
                    {
                        item.SendSignal(0, "1", "toggle_docking", sender: Character.Controlled);
                    }
                    else
                    {
                        dockingNetworkMessagePending = true;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };
            dockingButton.Font = GUI.SmallFont;

            var leftButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.5f), paddedDockingContainer.RectTransform, Anchor.CenterLeft), "")
            {
                OnClicked = NudgeButtonClicked,
                UserData = -Vector2.UnitX
            };
            new GUIImage(new RectTransform(new Vector2(0.7f), leftButton.RectTransform, Anchor.Center), "GUIButtonHorizontalArrow").SpriteEffects = SpriteEffects.FlipHorizontally;
            var rightButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.5f), paddedDockingContainer.RectTransform, Anchor.CenterRight), "")
            {
                OnClicked = NudgeButtonClicked,
                UserData = Vector2.UnitX
            };
            new GUIImage(new RectTransform(new Vector2(0.7f), rightButton.RectTransform, Anchor.Center), "GUIButtonHorizontalArrow");
            var upButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.2f), paddedDockingContainer.RectTransform, Anchor.TopCenter), "")
            {
                OnClicked = NudgeButtonClicked,
                UserData = Vector2.UnitY
            };
            new GUIImage(new RectTransform(new Vector2(0.7f), upButton.RectTransform, Anchor.Center), "GUIButtonVerticalArrow");
            var downButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.2f), paddedDockingContainer.RectTransform, Anchor.BottomCenter), "")
            {
                OnClicked = NudgeButtonClicked,
                UserData = -Vector2.UnitY
            };
            new GUIImage(new RectTransform(new Vector2(0.7f), downButton.RectTransform, Anchor.Center), "GUIButtonVerticalArrow").SpriteEffects = SpriteEffects.FlipVertically;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "steeringindicator":
                        steeringIndicator = new Sprite(subElement);
                        break;
                    case "maintainposindicator":
                        maintainPosIndicator = new Sprite(subElement);
                        break;
                    case "maintainposoriginindicator":
                        maintainPosOriginIndicator = new Sprite(subElement);
                        break;
                }
            }

            SetUILayout();

            GameMain.Instance.OnResolutionChanged += SetUILayout;
            GameMain.Config.OnHUDScaleChanged += SetUILayout;
        }

        private void SetUILayout()
        {
            int viewSize = (int)Math.Min(GuiFrame.Rect.Width - 150, GuiFrame.Rect.Height * 0.9f);

            controlContainer.RectTransform.AbsoluteOffset = new Point((int)(viewSize * 0.99f), (int)(viewSize * 0.05f));
            statusContainer.RectTransform.AbsoluteOffset = new Point((int)(viewSize * 0.9f), 0);
            steerArea.RectTransform.NonScaledSize = new Point(viewSize);
            dockingContainer.RectTransform.AbsoluteOffset = new Point((int)(viewSize * 0.9f), 0);
        }

        private void FindConnectedDockingPort()
        {
           foreach (MapEntity linkedTo in item.linkedTo)
            {
                if (linkedTo is Item item)
                {
                    var port = item.GetComponent<DockingPort>();
                    if (port != null)
                    {
                        DockingSources.Add(port);
                    }
                }
            }

            var dockingConnection = item.Connections.FirstOrDefault(c => c.Name == "toggle_docking");
            if (dockingConnection != null)
            {
                var connectedPorts = item.GetConnectedComponentsRecursive<DockingPort>(dockingConnection);
                DockingSources.AddRange(connectedPorts.Where(p => p.Item.Submarine != null && !p.Item.Submarine.IsOutpost));
            }            
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
            Vector2 displaySubPos = (-sonar.DisplayOffset * sonar.Zoom) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
            displaySubPos.Y = -displaySubPos.Y;
            displaySubPos = displaySubPos.ClampLength(velRect.Width / 2);
            displaySubPos = steerArea.Rect.Center.ToVector2() + displaySubPos;
            
            if (!AutoPilot)
            {
                Vector2 unitSteeringInput = steeringInput / 100.0f;
                //map input from rectangle to circle
                Vector2 steeringInputPos = new Vector2(
                    steeringInput.X * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.Y * unitSteeringInput.Y),
                    -steeringInput.Y * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.X * unitSteeringInput.X));
                steeringInputPos += displaySubPos;

                if (steeringIndicator != null)
                {
                    Vector2 dir = steeringInputPos - displaySubPos;
                    float angle = (float)Math.Atan2(dir.Y, dir.X);
                    steeringIndicator.Draw(spriteBatch, displaySubPos, Color.White, origin: steeringIndicator.Origin, rotate: angle,
                        scale: new Vector2(dir.Length() / steeringIndicator.size.X, 1.0f));
                }
                else
                {
                    GUI.DrawLine(spriteBatch, displaySubPos, steeringInputPos, Color.LightGray);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 5, (int)steeringInputPos.Y - 5, 10, 10), Color.White);
                }

                if (velRect.Contains(PlayerInput.MousePosition))
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 4, (int)steeringInputPos.Y - 4, 8, 8), Color.Red, thickness: 2);
                }
            }
            else if (posToMaintain.HasValue && !LevelStartSelected && !LevelEndSelected)
            {
                Sonar sonar = item.GetComponent<Sonar>();
                if (sonar != null && controlledSub != null)
                {
                    Vector2 displayPosToMaintain = ((posToMaintain.Value - sonar.DisplayOffset * sonar.Zoom - controlledSub.WorldPosition)) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
                    displayPosToMaintain.Y = -displayPosToMaintain.Y;
                    displayPosToMaintain = displayPosToMaintain.ClampLength(velRect.Width / 2);
                    displayPosToMaintain = steerArea.Rect.Center.ToVector2() + displayPosToMaintain;

                    Color crosshairColor = Color.Orange * (0.5f + ((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 4.0f);
                    if (maintainPosIndicator != null)
                    {
                        maintainPosIndicator.Draw(spriteBatch, displayPosToMaintain, crosshairColor, scale: 0.5f * sonar.Zoom);
                    }
                    else
                    {
                        float crossHairSize = 8.0f;
                        GUI.DrawLine(spriteBatch, displayPosToMaintain + Vector2.UnitY * crossHairSize, displayPosToMaintain - Vector2.UnitY * crossHairSize, crosshairColor, width: 3);
                        GUI.DrawLine(spriteBatch, displayPosToMaintain + Vector2.UnitX * crossHairSize, displayPosToMaintain - Vector2.UnitX * crossHairSize, crosshairColor, width: 3);
                    }

                    if (maintainPosOriginIndicator != null)
                    {
                        maintainPosOriginIndicator.Draw(spriteBatch, displaySubPos, Color.Orange, scale: 0.5f * sonar.Zoom);
                    }
                    else
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle((int)displaySubPos.X - 5, (int)displaySubPos.Y - 5, 10, 10), Color.Orange);
                    }
                }
            }
            
            //map velocity from rectangle to circle
            Vector2 unitTargetVel = targetVelocity / 100.0f;
            Vector2 steeringPos = new Vector2(
                targetVelocity.X * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.Y * unitTargetVel.Y),
                -targetVelocity.Y * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.X * unitTargetVel.X));
            steeringPos += displaySubPos;


            if (steeringIndicator != null)
            {
                Vector2 dir = steeringPos - displaySubPos;
                float angle = (float)Math.Atan2(dir.Y, dir.X);
                steeringIndicator.Draw(spriteBatch, displaySubPos, Color.Gray, origin: steeringIndicator.Origin, rotate: angle,
                    scale: new Vector2(dir.Length() / steeringIndicator.size.X, 0.7f));
            }
            else
            {
                GUI.DrawLine(spriteBatch,
                    displaySubPos,
                    steeringPos,
                    Color.CadetBlue, 0, 2);
            }           
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
            if (swapDestinationOrder == null)
            {
                swapDestinationOrder = item.Submarine != null && item.Submarine.FlippedX;
                if (swapDestinationOrder.Value)
                {
                    levelStartTickBox.RectTransform.SetAsLastChild();
                }
            }

            if (!searchedConnectedDockingPort)
            {
                FindConnectedDockingPort();
                searchedConnectedDockingPort = true;
            }

            if (steerArea.Rect.Contains(PlayerInput.MousePosition))
            {
                if (!PlayerInput.KeyDown(InputType.Deselect) && !PlayerInput.KeyHit(InputType.Deselect))
                {
                    Character.DisableControls = true;
                }
            }

            dockingContainer.Visible = DockingModeEnabled;
            statusContainer.Visible = !DockingModeEnabled;

            if (DockingModeEnabled && ActiveDockingSource != null)
            {
                if (Math.Abs(ActiveDockingSource.Item.WorldPosition.X - DockingTarget.Item.WorldPosition.X) < ActiveDockingSource.DistanceTolerance.X &&
                    Math.Abs(ActiveDockingSource.Item.WorldPosition.Y - DockingTarget.Item.WorldPosition.Y) < ActiveDockingSource.DistanceTolerance.Y)
                {
                    dockingButton.Text = dockText;
                    if (dockingButton.FlashTimer <= 0.0f)
                    {
                        dockingButton.Flash(Color.LightGreen, 0.5f);
                        dockingButton.Pulsate(Vector2.One, Vector2.One * 1.2f, dockingButton.FlashTimer);
                    }
                }
            }
            else if (DockingSources.Any(d => d.Docked))
            {
                dockingButton.Text = undockText;
                dockingContainer.Visible = true;
                statusContainer.Visible = false;
                if (dockingButton.FlashTimer <= 0.0f)
                {
                    dockingButton.Flash(Color.OrangeRed);
                    dockingButton.Pulsate(Vector2.One, Vector2.One * 1.2f, dockingButton.FlashTimer);
                }
            }
            else
            {
                dockingButton.Text = dockText;
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

                if (DockingModeEnabled && DockingTarget != null)
                {
                    posToMaintain += ConvertUnits.ToDisplayUnits(DockingTarget.Item.Submarine.Velocity) * deltaTime;
                }
            }

            pressureWarningText.Visible = item.Submarine != null && item.Submarine.AtDamageDepth && Timing.TotalTime % 1.0f < 0.5f;

            if (Vector2.Distance(PlayerInput.MousePosition, steerArea.Rect.Center.ToVector2()) < steerArea.Rect.Width / 2)
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    Vector2 displaySubPos = (-sonar.DisplayOffset * sonar.Zoom) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
                    displaySubPos.Y = -displaySubPos.Y;
                    displaySubPos = steerArea.Rect.Center.ToVector2() + displaySubPos;

                    Vector2 inputPos = PlayerInput.MousePosition - displaySubPos;
                    inputPos.Y = -inputPos.Y;
                    if (AutoPilot && !LevelStartSelected && !LevelEndSelected)
                    {
                        posToMaintain = controlledSub != null ? 
                            controlledSub.WorldPosition + inputPos / sonar.DisplayRadius * sonar.Range / sonar.Zoom :
                            item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
                    }
                    else
                    {
                        SteeringInput = inputPos;
                    }
                    unsentChanges = true;
                    user = Character.Controlled;
                }
            }
            if (!AutoPilot && Character.DisableControls && GUI.KeyboardDispatcher.Subscriber == null)
            {
                steeringAdjustSpeed = character == null ? 0.2f : MathHelper.Lerp(0.2f, 1.0f, character.GetSkillLevel("helm") / 100.0f);
                Vector2 input = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left)) { input -= Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Right)) { input += Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Up)) { input += Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Down)) { input -= Vector2.UnitY; }
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
                    if (Level.Loaded != null && targetPort.Item.Submarine.WorldPosition.Y > Level.Loaded.Size.Y) { continue; }

                    int targetDir = targetPort.IsHorizontal ?
                        Math.Sign(targetPort.Item.WorldPosition.X - targetPort.Item.Submarine.WorldPosition.X) :
                        Math.Sign(targetPort.Item.WorldPosition.Y - targetPort.Item.Submarine.WorldPosition.Y);

                    if (sourceDir == targetDir) { continue; }

                    float dist = Vector2.DistanceSquared(sourcePort.Item.WorldPosition, targetPort.Item.WorldPosition);
                    if (dist < closestDist)
                    {
                        DockingModeEnabled = true;
                        ActiveDockingSource = sourcePort;
                        DockingTarget = targetPort;
                    }
                }
            }
        }

        private bool NudgeButtonClicked(GUIButton btn, object userdata)
        {
            if (!MaintainPos || !AutoPilot)
            {
                AutoPilot = true;
                posToMaintain = item.Submarine.WorldPosition;
            }
            MaintainPos = true;
            if (userdata is Vector2)
            {
                Sonar sonar = item.GetComponent<Sonar>();
                Vector2 nudgeAmount = (Vector2)userdata;
                if (sonar != null)
                {
                    nudgeAmount *= sonar == null ? 500.0f : 500.0f / sonar.Zoom;
                }
                PosToMaintain += nudgeAmount;
            }
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            maintainPosIndicator?.Remove();
            maintainPosOriginIndicator?.Remove();
            steeringIndicator?.Remove();
        }

        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            msg.Write(autoPilot);
            msg.Write(dockingNetworkMessagePending);
            dockingNetworkMessagePending = false;

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
