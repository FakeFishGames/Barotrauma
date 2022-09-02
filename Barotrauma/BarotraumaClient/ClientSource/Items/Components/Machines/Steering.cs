using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private GUIButton steeringModeSwitch;
        private GUITickBox autopilotIndicator, manualPilotIndicator;

        enum Destination
        {
            MaintainPos,
            LevelEnd,
            LevelStart
        };

        private GUITickBox maintainPosTickBox, levelEndTickBox, levelStartTickBox;

        private GUIComponent statusContainer, dockingContainer;

        public GUIComponent ControlContainer { get; private set; }

        private bool dockingNetworkMessagePending;

        private GUIButton dockingButton;
        private LocalizedString dockText, undockText;

        private GUIComponent steerArea;

        private GUITextBlock pressureWarningText, iceSpireWarningText;

        private GUITextBlock tipContainer;

        private LocalizedString noPowerTip, autoPilotMaintainPosTip, autoPilotLevelStartTip, autoPilotLevelEndTip;

        private Sprite maintainPosIndicator, maintainPosOriginIndicator;
        private Sprite steeringIndicator;

        private List<DockingPort> connectedPorts = new List<DockingPort>();
        private float checkConnectedPortsTimer;
        private const float CheckConnectedPortsInterval = 1.0f;

        public DockingPort ActiveDockingSource, DockingTarget;

        private Vector2 keyboardInput = Vector2.Zero;
        private float inputCumulation;

        private bool? swapDestinationOrder;

        private GUIMessageBox enterOutpostPrompt;

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

        private float steerRadius;
        public float? SteerRadius
        {
            get
            {
                return steerRadius;
            }
            set
            {
                steerRadius = value ?? (steerArea.Rect.Width / 2);
            }
        }

        private bool disableControls;
        /// <summary>
        /// Can be used by status effects to disable all the UI controls
        /// </summary>
        public bool DisableControls
        {
            get { return disableControls; }
            set 
            {
                if (disableControls == value) { return; }
                disableControls = value; 
                UpdateGUIElements();
            }
        }

        public override bool RecreateGUIOnResolutionChange => true;


        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
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
            CreateGUI();
        }

        protected override void CreateGUI()
        {
            ControlContainer = new GUIFrame(new RectTransform(new Vector2(Sonar.controlBoxSize.X, 1 - Sonar.controlBoxSize.Y * 2), GuiFrame.RectTransform, Anchor.CenterRight), "ItemUI");
            var paddedControlContainer = new GUIFrame(new RectTransform(ControlContainer.Rect.Size - GUIStyle.ItemFrameMargin, ControlContainer.RectTransform, Anchor.Center)
            {
                AbsoluteOffset = GUIStyle.ItemFrameOffset
            }, style: null);

            var steeringModeArea = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), paddedControlContainer.RectTransform, Anchor.TopLeft), style: null);
            steeringModeSwitch = new GUIButton(new RectTransform(new Vector2(0.2f, 1), steeringModeArea.RectTransform), string.Empty, style: "SwitchVertical")
            {
                Selected = autoPilot,
                Enabled = true,
                ClickSound = GUISoundType.UISwitch,
                OnClicked = (button, data) =>
                {
                    button.Selected = !button.Selected;
                    AutoPilot = button.Selected;
                    if (GameMain.Client != null)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                    }
                    return true;
                }
            };
            var steeringModeRightSide = new GUIFrame(new RectTransform(new Vector2(1.0f - steeringModeSwitch.RectTransform.RelativeSize.X, 0.8f), steeringModeArea.RectTransform, Anchor.CenterLeft)
            {
                RelativeOffset = new Vector2(steeringModeSwitch.RectTransform.RelativeSize.X, 0)
            }, style: null);
            manualPilotIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.45f), steeringModeRightSide.RectTransform, Anchor.TopLeft),
                TextManager.Get("SteeringManual"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRedSmall")
            {
                Selected = !autoPilot,
                Enabled = false
            };
            autopilotIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.45f), steeringModeRightSide.RectTransform, Anchor.BottomLeft),
                TextManager.Get("SteeringAutoPilot"), font: GUIStyle.SubHeadingFont, style: "IndicatorLightRedSmall")
            {
                Selected = autoPilot,
                Enabled = false
            };
            manualPilotIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            autopilotIndicator.TextBlock.OverrideTextColor(GUIStyle.TextColorNormal);
            GUITextBlock.AutoScaleAndNormalize(manualPilotIndicator.TextBlock, autopilotIndicator.TextBlock);

            var autoPilotControls = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.62f), paddedControlContainer.RectTransform, Anchor.BottomCenter), "OutlineFrame");
            var paddedAutoPilotControls = new GUIFrame(new RectTransform(new Vector2(0.92f, 0.88f), autoPilotControls.RectTransform, Anchor.Center), style: null);

            maintainPosTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.333f), paddedAutoPilotControls.RectTransform, Anchor.TopCenter),
                TextManager.Get("SteeringMaintainPos"), font: GUIStyle.SmallFont, style: "GUIRadioButton")
            {
                Enabled = autoPilot,
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
            int textLimit = (int)(paddedAutoPilotControls.Rect.Width * 0.75f);
            levelStartTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.333f), paddedAutoPilotControls.RectTransform, Anchor.Center),
                GameMain.GameSession?.StartLocation == null ? "" : ToolBox.LimitString(GameMain.GameSession.StartLocation.Name, GUIStyle.SmallFont, textLimit),
                font: GUIStyle.SmallFont, style: "GUIRadioButton")
            {
                Enabled = autoPilot,
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

            levelEndTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.333f), paddedAutoPilotControls.RectTransform, Anchor.BottomCenter),
                (GameMain.GameSession?.EndLocation == null || Level.IsLoadedOutpost) ? "" : ToolBox.LimitString(GameMain.GameSession.EndLocation.Name, GUIStyle.SmallFont, textLimit),
                font: GUIStyle.SmallFont, style: "GUIRadioButton")
            {
                Enabled = autoPilot,
                Selected = levelEndSelected,
                Visible = GameMain.GameSession?.EndLocation != null,
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
            maintainPosTickBox.RectTransform.IsFixedSize = levelStartTickBox.RectTransform.IsFixedSize = levelEndTickBox.RectTransform.IsFixedSize = false;
            maintainPosTickBox.RectTransform.MaxSize = levelStartTickBox.RectTransform.MaxSize = levelEndTickBox.RectTransform.MaxSize =
                new Point(int.MaxValue, paddedAutoPilotControls.Rect.Height / 3);
            maintainPosTickBox.RectTransform.MinSize = levelStartTickBox.RectTransform.MinSize = levelEndTickBox.RectTransform.MinSize =
                Point.Zero;

            GUITextBlock.AutoScaleAndNormalize(scaleHorizontal: false, scaleVertical: true, maintainPosTickBox.TextBlock, levelStartTickBox.TextBlock, levelEndTickBox.TextBlock);

            GUIRadioButtonGroup destinations = new GUIRadioButtonGroup();
            destinations.AddRadioButton((int)Destination.MaintainPos, maintainPosTickBox);
            destinations.AddRadioButton((int)Destination.LevelStart, levelStartTickBox);
            destinations.AddRadioButton((int)Destination.LevelEnd, levelEndTickBox);
            destinations.Selected = (int)(maintainPos ? Destination.MaintainPos :
                                          levelStartSelected ? Destination.LevelStart : Destination.LevelEnd);

            // Status ->
            statusContainer = new GUIFrame(new RectTransform(Sonar.controlBoxSize, GuiFrame.RectTransform, Anchor.BottomRight)
            {
                RelativeOffset = Sonar.controlBoxOffset
            }, "ItemUI");
            var paddedStatusContainer = new GUIFrame(new RectTransform(statusContainer.Rect.Size - GUIStyle.ItemFrameMargin, statusContainer.RectTransform, Anchor.Center, isFixedSize: false)
            {
                AbsoluteOffset = GUIStyle.ItemFrameOffset
            }, style: null);

            var elements = GUI.CreateElements(3, new Vector2(1f, 0.333f), paddedStatusContainer.RectTransform, rt => new GUIFrame(rt, style: null), Anchor.TopCenter, relativeSpacing: 0.01f);
            List<GUIComponent> leftElements = new List<GUIComponent>(), centerElements = new List<GUIComponent>(), rightElements = new List<GUIComponent>();
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                var group = new GUILayoutGroup(new RectTransform(Vector2.One, e.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    RelativeSpacing = 0.01f,
                    Stretch = true
                };
                var left = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), group.RectTransform), style: null);
                var center = new GUIFrame(new RectTransform(new Vector2(0.15f, 1), group.RectTransform), style: null);
                var right = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.8f), group.RectTransform), style: null);
                leftElements.Add(left);
                centerElements.Add(center);
                rightElements.Add(right);
                LocalizedString leftText = string.Empty, centerText = string.Empty;
                GUITextBlock.TextGetterHandler rightTextGetter = null;
                switch (i)
                {
                    case 0:
                        leftText = TextManager.Get("DescentVelocity");
                        centerText = $"({TextManager.Get("KilometersPerHour")})";
                        rightTextGetter = () =>
                        {
                            Vector2 vel = controlledSub == null ? Vector2.Zero : controlledSub.Velocity;
                            var realWorldVel = ConvertUnits.ToDisplayUnits(vel.Y * Physics.DisplayToRealWorldRatio) * 3.6f;
                            return ((int)(-realWorldVel)).ToString();
                        };
                        break;
                    case 1:
                        leftText = TextManager.Get("Velocity");
                        centerText = $"({TextManager.Get("KilometersPerHour")})";
                        rightTextGetter = () =>
                        {
                            Vector2 vel = controlledSub == null ? Vector2.Zero : controlledSub.Velocity;
                            var realWorldVel = ConvertUnits.ToDisplayUnits(vel.X * Physics.DisplayToRealWorldRatio) * 3.6f;
                            if (controlledSub != null && controlledSub.FlippedX) { realWorldVel *= -1; }
                            return ((int)realWorldVel).ToString();
                        };
                        break;
                    case 2:
                        leftText = TextManager.Get("Depth");
                        centerText = $"({TextManager.Get("Meter")})";
                        rightTextGetter = () =>
                        {
                            float realWorldDepth = controlledSub == null ? -1000.0f : controlledSub.RealWorldDepth;
                            return ((int)realWorldDepth).ToString();
                        };
                        break;
                }
                new GUITextBlock(new RectTransform(Vector2.One, left.RectTransform), leftText, font: GUIStyle.SubHeadingFont, wrap: leftText.Contains(" "), textAlignment: Alignment.CenterRight);
                new GUITextBlock(new RectTransform(Vector2.One, center.RectTransform), centerText, font: GUIStyle.Font, textAlignment: Alignment.Center) { Padding = Vector4.Zero };
                var digitalFrame = new GUIFrame(new RectTransform(Vector2.One, right.RectTransform), style: "DigitalFrameDark");
                new GUITextBlock(new RectTransform(Vector2.One * 0.85f, digitalFrame.RectTransform, Anchor.Center), "12345", GUIStyle.TextColorDark, GUIStyle.DigitalFont, Alignment.CenterRight)
                {
                    TextGetter = rightTextGetter
                };
            }
            GUITextBlock.AutoScaleAndNormalize(leftElements.SelectMany(e => e.GetAllChildren<GUITextBlock>()));
            GUITextBlock.AutoScaleAndNormalize(centerElements.SelectMany(e => e.GetAllChildren<GUITextBlock>()));
            GUITextBlock.AutoScaleAndNormalize(rightElements.SelectMany(e => e.GetAllChildren<GUITextBlock>()));

            //docking interface ----------------------------------------------------
            float dockingButtonSize = 1.1f;
            float elementScale = 0.6f;
            dockingContainer = new GUIFrame(new RectTransform(Sonar.controlBoxSize, GuiFrame.RectTransform, Anchor.BottomRight, scaleBasis: ScaleBasis.Smallest)
            {
                RelativeOffset = new Vector2(Sonar.controlBoxOffset.X + 0.05f, -0.05f)
            }, style: null);

            dockText = TextManager.Get("label.navterminaldock", "captain.dock");
            undockText = TextManager.Get("label.navterminalundock", "captain.undock");
            dockingButton = new GUIButton(new RectTransform(new Vector2(elementScale), dockingContainer.RectTransform, Anchor.Center), dockText, style: "PowerButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    if (GameMain.GameSession?.Missions.Any(m => !m.AllowUndocking) ?? false)
                    {
                        new GUIMessageBox("", TextManager.Get("undockingdisabledbymission"));
                        return false;
                    }
                    else if (GameMain.GameSession?.Campaign is CampaignMode campaign)
                    {
                        if (Level.IsLoadedOutpost &&
                            DockingSources.Any(d => d.Docked && (d.DockingTarget?.Item.Submarine?.Info?.IsOutpost ?? false)))
                        {
                            // Undocking from an outpost
                            campaign.ShowCampaignUI = true;
                            campaign.CampaignUI.SelectTab(CampaignMode.InteractionType.Map); 
                            return false;
                        }
                        else if (!Level.IsLoadedOutpost && DockingModeEnabled && ActiveDockingSource != null &&
                                !ActiveDockingSource.Docked && DockingTarget?.Item?.Submarine == Level.Loaded.StartOutpost && (DockingTarget?.Item?.Submarine?.Info.IsOutpost ?? false))
                        {
                            // Docking to an outpost
                            var subsToLeaveBehind = campaign.GetSubsToLeaveBehind(Item.Submarine);
                            if (subsToLeaveBehind.Any())
                            {
                                enterOutpostPrompt = new GUIMessageBox(
                                    TextManager.GetWithVariable("enterlocation", "[locationname]", DockingTarget.Item.Submarine.Info.Name),
                                    TextManager.Get(subsToLeaveBehind.Count == 1 ? "LeaveSubBehind" : "LeaveSubsBehind"),
                                    new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") });
                            }
                            else
                            {
                                enterOutpostPrompt = new GUIMessageBox("",
                                    TextManager.GetWithVariable("campaignenteroutpostprompt", "[locationname]", DockingTarget.Item.Submarine.Info.Name),
                                    new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") });
                            }
                            enterOutpostPrompt.Buttons[0].OnClicked += (btn, userdata) =>
                            {
                                SendDockingSignal();
                                enterOutpostPrompt.Close();
                                return true;
                            };
                            enterOutpostPrompt.Buttons[1].OnClicked += enterOutpostPrompt.Close;
                            return false;
                        }
                    }
                    SendDockingSignal();

                    return true;
                }
            };
            void SendDockingSignal()
            {
                if (GameMain.Client == null)
                {
                    item.SendSignal(new Signal("1", sender: Character.Controlled), "toggle_docking");
                }
                else
                {
                    dockingNetworkMessagePending = true;
                    item.CreateClientEvent(this);
                }
            }
            dockingButton.Font = GUIStyle.SubHeadingFont;
            dockingButton.TextBlock.RectTransform.MaxSize = new Point((int)(dockingButton.Rect.Width * 0.7f), int.MaxValue);
            dockingButton.TextBlock.AutoScaleHorizontal = true;

            var style = GUIStyle.GetComponentStyle("DockingButtonUp");
            Sprite buttonSprite = style.Sprites.FirstOrDefault().Value.FirstOrDefault()?.Sprite;
            Point buttonSize = buttonSprite != null ? buttonSprite.size.ToPoint() : new Point(149, 52);
            Point horizontalButtonSize = buttonSize.Multiply(elementScale * GUI.Scale * dockingButtonSize);
            Point verticalButtonSize = horizontalButtonSize.Flip();
            var leftButton = new GUIButton(new RectTransform(verticalButtonSize, dockingContainer.RectTransform, Anchor.CenterLeft), "", style: "DockingButtonLeft")
            {
                OnClicked = NudgeButtonClicked,
                UserData = -Vector2.UnitX
            };
            var rightButton = new GUIButton(new RectTransform(verticalButtonSize, dockingContainer.RectTransform, Anchor.CenterRight), "", style: "DockingButtonRight")
            {
                OnClicked = NudgeButtonClicked,
                UserData = Vector2.UnitX
            };
            var upButton = new GUIButton(new RectTransform(horizontalButtonSize, dockingContainer.RectTransform, Anchor.TopCenter), "", style: "DockingButtonUp")
            {
                OnClicked = NudgeButtonClicked,
                UserData = Vector2.UnitY
            };
            var downButton = new GUIButton(new RectTransform(horizontalButtonSize, dockingContainer.RectTransform, Anchor.BottomCenter), "", style: "DockingButtonDown")
            {
                OnClicked = NudgeButtonClicked,
                UserData = -Vector2.UnitY
            };

            // Sonar area
            steerArea = new GUICustomComponent(new RectTransform(Sonar.GUISizeCalculation, GuiFrame.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.Smallest),
                (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); }, null);
            steerRadius = steerArea.Rect.Width / 2;

            iceSpireWarningText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.25f), steerArea.RectTransform, Anchor.Center, Pivot.TopCenter),
                TextManager.Get("NavTerminalIceSpireWarning"), GUIStyle.Red, GUIStyle.SubHeadingFont, Alignment.Center, color: Color.Black * 0.8f, wrap: true)
            {
                Visible = false
            };
            pressureWarningText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.25f), steerArea.RectTransform, Anchor.Center, Pivot.TopCenter), 
                TextManager.Get("SteeringDepthWarning"), GUIStyle.Red, GUIStyle.SubHeadingFont, Alignment.Center, color: Color.Black * 0.8f)
            {
                Visible = false
            };
            // Tooltip/helper text
            tipContainer = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.1f), steerArea.RectTransform, Anchor.BottomCenter, Pivot.TopCenter)
                , "", font: GUIStyle.Font, wrap: true, style: "GUIToolTip", textAlignment: Alignment.Center)
            {
                AutoScaleHorizontal = true
            };
            noPowerTip = TextManager.Get("SteeringNoPowerTip");
            autoPilotMaintainPosTip = TextManager.Get("SteeringAutoPilotMaintainPosTip");
            autoPilotLevelStartTip = TextManager.GetWithVariable("SteeringAutoPilotLocationTip", "[locationname]",
                GameMain.GameSession?.StartLocation == null ? "Start" : GameMain.GameSession.StartLocation.Name);
            autoPilotLevelEndTip = TextManager.GetWithVariable("SteeringAutoPilotLocationTip", "[locationname]",
                GameMain.GameSession?.EndLocation == null ? "End" : GameMain.GameSession.EndLocation.Name);
        }

        protected override void OnResolutionChanged()
        {
            UpdateGUIElements();
        }

        /// <summary>
        /// Makes the sonar view CustomComponent render the steering HUD, preventing it from being drawn behing the sonar
        /// </summary>
        public void AttachToSonarHUD(GUICustomComponent sonarView)
        {
            steerArea.Visible = false;
            sonarView.OnDraw += (spriteBatch, guiCustomComponent) => 
            { 
                DrawHUD(spriteBatch, guiCustomComponent.Rect);
                steerArea.DrawChildren(spriteBatch, recursive: true);
            };
        }

        public void DrawHUD(SpriteBatch spriteBatch, Rectangle rect)
        {
            int width = rect.Width, height = rect.Height;
            int x = rect.X;
            int y = rect.Y;

            if (Voltage < MinVoltage) { return; }

            Rectangle velRect = new Rectangle(x + 20, y + 20, width - 40, height - 40);
            Vector2 steeringOrigin = steerArea.Rect.Center.ToVector2();

            if (!AutoPilot)
            {
                Vector2 unitSteeringInput = steeringInput / 100.0f;
                //map input from rectangle to circle
                Vector2 steeringInputPos = new Vector2(
                    steeringInput.X * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.Y * unitSteeringInput.Y),
                    -steeringInput.Y * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.X * unitSteeringInput.X));
                steeringInputPos += steeringOrigin;

                if (steeringIndicator != null)
                {
                    Vector2 dir = steeringInputPos - steeringOrigin;
                    float angle = (float)Math.Atan2(dir.Y, dir.X);
                    steeringIndicator.Draw(spriteBatch, steeringOrigin, Color.White, origin: steeringIndicator.Origin, rotate: angle,
                        scale: new Vector2(dir.Length() / steeringIndicator.size.X, 1.0f));
                }
                else
                {
                    GUI.DrawLine(spriteBatch, steeringOrigin, steeringInputPos, Color.LightGray);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 5, (int)steeringInputPos.Y - 5, 10, 10), Color.White);
                }

                if (velRect.Contains(PlayerInput.MousePosition))
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 4, (int)steeringInputPos.Y - 4, 8, 8), GUIStyle.Red, thickness: 2);
                }
            }
            else if (posToMaintain.HasValue && !LevelStartSelected && !LevelEndSelected)
            {
                Sonar sonar = item.GetComponent<Sonar>();
                if (sonar != null && controlledSub != null)
                {
                    Vector2 displayPosToMaintain = ((posToMaintain.Value - controlledSub.WorldPosition)) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
                    displayPosToMaintain.Y = -displayPosToMaintain.Y;
                    displayPosToMaintain = displayPosToMaintain.ClampLength(velRect.Width / 2);
                    displayPosToMaintain = steerArea.Rect.Center.ToVector2() + displayPosToMaintain;

                    Color crosshairColor = GUIStyle.Orange * (0.5f + ((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 4.0f);
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
                        maintainPosOriginIndicator.Draw(spriteBatch, steeringOrigin, GUIStyle.Orange, scale: 0.5f * sonar.Zoom);
                    }
                    else
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringOrigin.X - 5, (int)steeringOrigin.Y - 5, 10, 10), GUIStyle.Orange);
                    }
                }
            }
            
            //map velocity from rectangle to circle
            Vector2 unitTargetVel = targetVelocity / 100.0f;
            Vector2 steeringPos = new Vector2(
                targetVelocity.X * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.Y * unitTargetVel.Y),
                -targetVelocity.Y * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.X * unitTargetVel.X));
            steeringPos += steeringOrigin;

            if (steeringIndicator != null)
            {
                Vector2 dir = steeringPos - steeringOrigin;
                float angle = (float)Math.Atan2(dir.Y, dir.X);
                steeringIndicator.Draw(spriteBatch, steeringOrigin, Color.Gray, origin: steeringIndicator.Origin, rotate: angle,
                    scale: new Vector2(dir.Length() / steeringIndicator.size.X, 0.7f));
            }
            else
            {
                GUI.DrawLine(spriteBatch,
                    steeringOrigin,
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

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 3 / 2, (int)pos.Y - 3, 6, 6), (SteeringPath.CurrentNode == wp) ? Color.LightGreen : GUIStyle.Green, false);

                if (prevPos != Vector2.Zero)
                {
                    GUI.DrawLine(spriteBatch, pos, prevPos, GUIStyle.Green);
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
                    GUIStyle.Red * 0.6f, width: 3);

                if (obstacle.Intersection.HasValue)
                {
                    Vector2 intersectionPos = (obstacle.Intersection.Value - transducerCenter) *displayScale;
                    intersectionPos.Y = -intersectionPos.Y;
                    intersectionPos += center;
                    GUI.DrawRectangle(spriteBatch, intersectionPos - Vector2.One * 2, Vector2.One * 4, GUIStyle.Red);
                }

                Vector2 obstacleCenter = (pos1 + pos2) / 2;
                if (obstacle.AvoidStrength.LengthSquared() > 0.01f)
                {
                    GUI.DrawLine(spriteBatch,
                        obstacleCenter,
                        obstacleCenter + new Vector2(obstacle.AvoidStrength.X, -obstacle.AvoidStrength.Y) * 100,
                        Color.Lerp(GUIStyle.Green, GUIStyle.Orange, obstacle.Dot), width: 2);
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

            if (steerArea.Rect.Contains(PlayerInput.MousePosition))
            {
                if (!PlayerInput.KeyDown(InputType.Deselect) && !PlayerInput.KeyHit(InputType.Deselect))
                {
                    Character.DisableControls = true;
                }
            }

            if (DisableControls)
            {
                dockingModeEnabled = false;
            }

            dockingContainer.Visible = DockingModeEnabled;
            statusContainer.Visible = !DockingModeEnabled;
            if (!DockingModeEnabled)
            {
                enterOutpostPrompt?.Close();
            }

            if (DockingModeEnabled && ActiveDockingSource != null)
            {
                if (Math.Abs(ActiveDockingSource.Item.WorldPosition.X - DockingTarget.Item.WorldPosition.X) < ActiveDockingSource.DistanceTolerance.X &&
                    Math.Abs(ActiveDockingSource.Item.WorldPosition.Y - DockingTarget.Item.WorldPosition.Y) < ActiveDockingSource.DistanceTolerance.Y)
                {
                    dockingButton.Text = dockText;
                    if (dockingButton.FlashTimer <= 0.0f)
                    {
                        dockingButton.Flash(GUIStyle.Blue, 0.5f, useCircularFlash: true);
                        dockingButton.Pulsate(Vector2.One, Vector2.One * 1.2f, dockingButton.FlashTimer);
                    }
                }
                else
                {
                    enterOutpostPrompt?.Close();
                }
            }
            else if (connectedPorts.Any(d => d.Docked))
            {
                dockingButton.Text = undockText;
                dockingContainer.Visible = true;
                statusContainer.Visible = false;
                if (dockingButton.FlashTimer <= 0.0f)
                {
                    dockingButton.Flash(GUIStyle.Orange, useCircularFlash: true);
                    dockingButton.Pulsate(Vector2.One, Vector2.One * 1.2f, dockingButton.FlashTimer);
                }
            }
            else
            {
                dockingButton.Text = dockText;
            }

            if (Voltage < MinVoltage)
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

            pressureWarningText.Visible = item.Submarine != null && Timing.TotalTime % 1.0f < 0.8f;
            float depthEffectThreshold = 500.0f;
            if (Level.Loaded != null && pressureWarningText.Visible && 
                item.Submarine.RealWorldDepth > Level.Loaded.RealWorldCrushDepth - depthEffectThreshold && item.Submarine.RealWorldDepth > item.Submarine.RealWorldCrushDepth - depthEffectThreshold)
            {
                pressureWarningText.Visible = true;
                pressureWarningText.Text =
                    item.Submarine.AtDamageDepth ?
                    TextManager.Get("SteeringDepthWarning") :
                    TextManager.GetWithVariable("SteeringDepthWarningLow", "[crushdepth]", ((int)item.Submarine.RealWorldCrushDepth).ToString());
            }
            else
            {
                pressureWarningText.Visible = false;
            }

            iceSpireWarningText.Visible = item.Submarine != null && !pressureWarningText.Visible && showIceSpireWarning && Timing.TotalTime % 1.0f < 0.8f;

            if (!disableControls && Vector2.DistanceSquared(PlayerInput.MousePosition, steerArea.Rect.Center.ToVector2()) < steerRadius * steerRadius)
            {
                if (PlayerInput.PrimaryMouseButtonHeld() && !CrewManager.IsCommandInterfaceOpen && !GameSession.IsTabMenuOpen && 
                    (!GameMain.GameSession?.Campaign?.ShowCampaignUI ?? true) && !GUIMessageBox.MessageBoxes.Any(msgBox => msgBox is GUIMessageBox { MessageBoxType: GUIMessageBox.Type.Default }))
                {
                    Vector2 inputPos = PlayerInput.MousePosition - steerArea.Rect.Center.ToVector2();
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
                steeringAdjustSpeed = character == null ? DefaultSteeringAdjustSpeed : MathHelper.Lerp(0.2f, 1.0f, character.GetSkillLevel("helm") / 100.0f);
                Vector2 input = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left)) { input -= Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Right)) { input += Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Up)) { input += Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Down)) { input -= Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Run))
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

            if (!UseAutoDocking || DisableControls) { return; }

            if (checkConnectedPortsTimer <= 0.0f)
            {
                Connection dockingConnection = item.Connections?.FirstOrDefault(c => c.Name == "toggle_docking");
                if (dockingConnection != null)
                {
                    connectedPorts = item.GetConnectedComponentsRecursive<DockingPort>(dockingConnection, ignoreInactiveRelays: true, allowTraversingBackwards: false);
                }
                checkConnectedPortsTimer = CheckConnectedPortsInterval;
            }
            else
            {
                checkConnectedPortsTimer -= deltaTime;
            }
            
            DockingModeEnabled = false;

            if (connectedPorts.None()) { return; }

            float closestDist = DockingAssistThreshold * DockingAssistThreshold;
            foreach (DockingPort sourcePort in connectedPorts)
            {
                if (sourcePort.Docked || sourcePort.Item.Submarine == null) { continue; }
                if (sourcePort.Item.Submarine != controlledSub) { continue; }

                int sourceDir = sourcePort.GetDir();

                foreach (DockingPort targetPort in DockingPort.List)
                {
                    if (targetPort.Docked || targetPort.Item.Submarine == null) { continue; }
                    if (targetPort.Item.Submarine == controlledSub || targetPort.IsHorizontal != sourcePort.IsHorizontal) { continue; }
                    if (targetPort.Item.Submarine.DockedTo?.Contains(sourcePort.Item.Submarine) ?? false) { continue; }
                    if (Level.Loaded != null && targetPort.Item.Submarine.WorldPosition.Y > Level.Loaded.Size.Y) { continue; }
                    if (sourceDir == targetPort.GetDir()) { continue; }

                    float dist = Vector2.DistanceSquared(sourcePort.Item.WorldPosition, targetPort.Item.WorldPosition);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
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
            if (userdata is Vector2 nudgeAmount)
            {
                if (item.GetComponent<Sonar>() is Sonar sonar)
                {
                    nudgeAmount *= 500.0f / sonar.Zoom;
                }
                PosToMaintain += nudgeAmount;
            }
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            maintainPosIndicator?.Remove();
            maintainPosOriginIndicator?.Remove();
            steeringIndicator?.Remove();
            enterOutpostPrompt?.Close();
            pathFinder = null;
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(AutoPilot);
            msg.WriteBoolean(dockingNetworkMessagePending);
            dockingNetworkMessagePending = false;

            if (!AutoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.WriteSingle(steeringInput.X);
                msg.WriteSingle(steeringInput.Y);
            }
            else
            {
                msg.WriteBoolean(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.WriteSingle(((Vector2)posToMaintain).X);
                    msg.WriteSingle(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.WriteBoolean(LevelStartSelected);
                }
            }
        }
        
        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            int msgStartPos = msg.BitPosition;

            bool autoPilot                  = msg.ReadBoolean();
            bool dockingButtonClicked       = msg.ReadBoolean();
            ushort userID                   = msg.ReadUInt16();

            Vector2 newSteeringInput        = steeringInput;
            Vector2 newTargetVelocity       = targetVelocity;
            float newSteeringAdjustSpeed    = steeringAdjustSpeed;
            Vector2? newPosToMaintain       = null;
            bool headingToStart             = false;

            if (dockingButtonClicked)
            {
                item.SendSignal(new Signal("1", sender: Entity.FindEntityByID(userID) as Character), "toggle_docking");
            }

            if (autoPilot)
            {
                if (msg.ReadBoolean())
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadSingle(),
                        msg.ReadSingle());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newSteeringInput = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                newTargetVelocity = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                newSteeringAdjustSpeed = msg.ReadSingle();
            }

            if (correctionTimer > 0.0f)
            {
                int msgLength = (int)(msg.BitPosition - msgStartPos);
                msg.BitPosition = msgStartPos;
                StartDelayedCorrection(msg.ExtractBits(msgLength), sendingTime);
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

        private void UpdateGUIElements()
        {
            steeringModeSwitch.Selected = AutoPilot;
            autopilotIndicator.Selected = AutoPilot;
            manualPilotIndicator.Selected = !AutoPilot;
            if (DisableControls)
            {
                steeringModeSwitch.Enabled = false;
                maintainPosTickBox.Enabled = false;
                levelEndTickBox.Enabled = false;
                levelStartTickBox.Enabled = false;
            }
            else
            {
                steeringModeSwitch.Enabled = true;
                maintainPosTickBox.Enabled = AutoPilot;
                levelEndTickBox.Enabled = AutoPilot;
                levelStartTickBox.Enabled = AutoPilot;
            }
        }
    }
}
