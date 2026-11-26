using Barotrauma;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor
{
    /// <summary>
    /// An UI that can be used to rotate and launch a turret, or an item that uses the turret component to rotate (such as a searchlight or a thruster).
    /// </summary>
    public partial class TurretControlPanel : ItemComponent
    {
        private readonly struct TurretControlPanelEventData : IEventData
        {
            public readonly bool ButtonPressed;
            /// <summary>
            /// Did the client rotate the knob? If not, the server shouldn't apply the rotation in the message
            /// (otherwise e.g. the "still pressing the button" updates could override a rotation set by someone else).
            /// </summary>
            public readonly bool RotationChanged;

            public TurretControlPanelEventData(bool buttonPressed, bool rotationChanged)
            {
                ButtonPressed = buttonPressed;
                RotationChanged = rotationChanged;
            }
        }

        /// <summary>
        /// While above zero, the rotation received from the server isn't applied immediately (because it may be outdated after we just rotated the knob),
        /// but stored in <see cref="pendingServerRotation"/> and applied when the timer runs out.
        /// </summary>
        private float rotationCorrectionTimer;
        private float? pendingServerRotation;

        /// <summary>
        /// While above zero, the firing state received from the server is ignored (because it may be outdated after we just pressed or released the button).
        /// </summary>
        private float firingStateCorrectionTimer;

        private Sprite? rotationKnob, rotationKnobBackground;

        private GUIProgressBar? powerIndicator;

        private GUIComponent.ComponentState rotationKnobState;

        private float rotationKnobAngle;
        private float lastSoundPlayedAngle;

        private bool pressingButton;
        private bool lastSentPressedState;
        private float sendUpdateTimer;

        private GUIButton? firingButton;

        private RoundSound? rotateKnobSound;

        private GUITextBlock? labelText;

        partial void InitProjSpecific(ContentXElement element)
        {
            var verticalContent = new GUILayoutGroup(new RectTransform(new Vector2(0.85f, 0.71f), GuiFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.04f),
            })
            {
                Stretch = true,
                CanBeFocused = true,
                RelativeSpacing = 0.05f
            };

            labelText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), verticalContent.RectTransform, Anchor.TopCenter),
                uiLabel.IsNullOrWhiteSpace() ? item.Prefab.Name : TextManager.Get(uiLabel).Fallback(uiLabel),
                font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft, wrap: true);
            labelText.RectTransform.MinSize = new Point(0, (int)(labelText.TextSize.Y * 1.2f));

            var content = new GUILayoutGroup(new RectTransform(Vector2.One, verticalContent.RectTransform, Anchor.Center), childAnchor: Anchor.CenterLeft, isHorizontal: true)
            {
                Stretch = true,
                CanBeFocused = true,
                RelativeSpacing = 0.05f
            };

            new GUICustomComponent(new RectTransform(new Vector2(0.4f, 1.0f), content.RectTransform, Anchor.Center), onDraw: DrawRotationWidget, onUpdate: UpdateRotationWidget);

            var rightContent = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 0.75f), content.RectTransform, Anchor.CenterRight), 
                isHorizontal: false, childAnchor: ShowBatteryState ? Anchor.TopCenter : Anchor.Center)
            {
                Stretch = true,
                RelativeSpacing = 0.1f
            };

            firingButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.5f), rightContent.RectTransform, Anchor.Center))
            {
                Text =
                    buttonText.IsNullOrWhiteSpace() ? TextManager.Get("tutorial.basics.fire") : TextManager.Get(ButtonText).Fallback(ButtonText),
                PlaySoundOnSelect = false,
                OnButtonDown = () =>
                {
                    SoundPlayer.PlayUISound(GUISoundType.Select);
                    return true;
                },
                OnPressed = () =>
                {
                    user = Character.Controlled;
                    sendFiringSignal = true;
                    pressingButton = true;
                    firingButtonPressedTimer = 0.1f;
                    //don't let the server's (possibly outdated) button state override ours
                    firingStateCorrectionTimer = CorrectionDelay;
                    return true;
                }
            };

            if (ShowBatteryState)
            {
                powerIndicator = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.3f), rightContent.RectTransform)
                {
                    MinSize = new Point(100, 20),
                    RelativeOffset = new Vector2(0.0f, 0.01f)
                },
                    barSize: 0.0f, style: "DeviceProgressBar")
                {
                    CanBeFocused = false
                };
            }

            foreach (var subElement in element.Elements())
            {
                var elementName = subElement.NameAsIdentifier();
                if (elementName == "rotationknob")
                {
                    rotationKnob = new Sprite(subElement);
                }
                else if (elementName == "rotationknobbackground")
                {
                    rotationKnobBackground = new Sprite(subElement);
                }
                else if (elementName == "rotateknobsound")
                {
                    rotateKnobSound = RoundSound.Load(subElement);
                }
            }
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            rotationCorrectionTimer -= deltaTime;
            firingStateCorrectionTimer -= deltaTime;
            if (rotationCorrectionTimer <= 0.0f && pendingServerRotation.HasValue)
            {
                RotationDeg = pendingServerRotation.Value;
                pendingServerRotation = null;
            }

            //notify server if we started or stopped pressing the firing button
            if (lastSentPressedState != pressingButton)
            {
                item.CreateClientEvent(this, new TurretControlPanelEventData(pressingButton, rotationChanged: false));
                lastSentPressedState = pressingButton;
            }
            else if (pressingButton)
            {
                //if we are still pressing the button, send an update every 0.1 seconds
                sendUpdateTimer += deltaTime;
                if (sendUpdateTimer > 0.1f)
                {
                    item.CreateClientEvent(this, new TurretControlPanelEventData(pressingButton, rotationChanged: false));
                    sendUpdateTimer = 0.0f;
                }
                pressingButton = false;
            }
        }

        private void DrawRotationWidget(SpriteBatch spriteBatch, GUICustomComponent component)
        {
            if (rotationKnobBackground != null)
            {
                float scale = Math.Min(component.Rect.Width / rotationKnobBackground.size.X, component.Rect.Height / rotationKnobBackground.size.Y);
                rotationKnobBackground.Draw(spriteBatch, component.Center, scale: scale);
            }
            if (rotationKnob != null)
            {
                Color color = rotationKnobState switch
                {
                    GUIComponent.ComponentState.Hover => Color.White,
                    GUIComponent.ComponentState.Pressed => new Color(150, 150, 150, 255),
                    _ => new Color(220, 220, 220, 255)
                };

                float scale = Math.Min(component.Rect.Width / rotationKnob.size.X, component.Rect.Height / rotationKnob.size.Y);
                rotationKnob.Draw(spriteBatch, component.Center, color, rotate: rotationKnobAngle, scale: scale);
            }
        }

        private void UpdateRotationWidget(float deltaTime, GUICustomComponent component)
        {
            if (GUI.MouseOn == component)
            {
                if (PlayerInput.PrimaryMouseButtonHeld())
                {
                    Vector2 mousePos = PlayerInput.MousePosition;
                    Vector2 center = component.Rect.Center.ToVector2();
                    float rotationRad = MathUtils.VectorToAngle(mousePos - center) + MathHelper.PiOver2;
                    rotationRad = MathUtils.WrapAngleTwoPi(rotationRad);
                    float prevRotationDeg = RotationDeg;
                    RotationDeg = MathHelper.ToDegrees(rotationRad);
                    if (!MathUtils.NearlyEqual(prevRotationDeg, RotationDeg))
                    {
                        //don't let the server's (possibly outdated) rotation override ours
                        rotationCorrectionTimer = CorrectionDelay;
                        pendingServerRotation = null;
                        //notify the server so the rotation gets synced to other clients
                        //use the last sent pressed state (pressingButton gets reset between the "still pressing" updates)
                        item.CreateClientEvent(this, new TurretControlPanelEventData(lastSentPressedState, rotationChanged: true));
                    }
                    rotationKnobState = GUIComponent.ComponentState.Pressed;
                    user = Character.Controlled;
                }
                else
                {
                    rotationKnobState = GUIComponent.ComponentState.Hover;
                }
            }
            else
            {
                rotationKnobState = GUIComponent.ComponentState.None;
            }

            rotationKnobAngle = MathUtils.CurveAngle(rotationKnobAngle, MathHelper.ToRadians(rotationDeg), 0.5f);
            if (Math.Abs(lastSoundPlayedAngle - rotationKnobAngle) > 0.1f)
            {
                rotateKnobSound?.Sound?.Play();
                lastSoundPlayedAngle = rotationKnobAngle;
            }
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            user = Character.Controlled;
            if (powerIndicator != null && targetTurrets.Any())
            {
                //hide power indicators if none of the turrets consume power
                if (targetTurrets.All(static t => t.Item.GetComponents<Powered>().Sum(static p => p.PowerConsumption) <= 0.0f))
                {
                    powerIndicator.Visible = false;
                    if (firingButton != null)
                    {
                        firingButton.Enabled = true;
                    }
                }
                else
                {
                    powerIndicator.Visible = true;
                    bool anyTurretCharged = false;
                    float avgChargeRate = 0.0f;
                    foreach (var targetTurret in targetTurrets)
                    {
                        targetTurret.GetBatteryStatus(out float _, out float _, out float chargeRate, out bool isCharged);
                        if (isCharged)
                        {
                            anyTurretCharged = true;
                        }
                        avgChargeRate += chargeRate / targetTurrets.Count;
                    }

                    powerIndicator.Color = anyTurretCharged ? GUIStyle.Green : GUIStyle.Red;
                    powerIndicator.BarSize = avgChargeRate;
                    if (DisableFiringButtonIfOutOfPower && firingButton != null)
                    {
                        firingButton.Enabled = anyTurretCharged;
                    }
                }
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            rotationKnob?.Remove();
            rotationKnob = null;
            rotationKnobBackground?.Remove();
            rotationKnobBackground = null;
        }

        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
        {
            msg.WriteSingle(rotationDeg);
            var eventData = extraData is Item.ComponentStateEventData { ComponentData: TurretControlPanelEventData data } ? data : default;
            msg.WriteBoolean(eventData.ButtonPressed);
            msg.WriteBoolean(eventData.RotationChanged);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            float serverRotation = msg.ReadSingle();
            bool serverFiring = msg.ReadBoolean();

            //rotation and firing state are handled separately: e.g. someone else rotating the knob
            //shouldn't be ignored just because we're pressing the firing button, and vice versa
            if (rotationCorrectionTimer > 0.0f)
            {
                //we just rotated the knob, the server's rotation may be outdated: apply it once the correction timer runs out
                pendingServerRotation = serverRotation;
            }
            else
            {
                RotationDeg = serverRotation;
            }

            //(the timer is refreshed every frame while we're holding the button, so this also covers the time we're pressing it)
            if (firingStateCorrectionTimer <= 0.0f)
            {
                //fire on our end too if someone else is pressing the button (so the turrets also flash on the status monitor)
                sendFiringSignal = serverFiring;
                firingButtonPressedTimer = 0.5f;
            }
        }
    }
}