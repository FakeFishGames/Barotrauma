using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Sonar : Powered, IServerSerializable, IClientSerializable
    {
        public enum Mode
        {
            Active,
            Passive
        };

        public const float DefaultSonarRange = 10000.0f;

        class ConnectedTransducer
        {
            public readonly SonarTransducer Transducer;
            public float SignalStrength;
            public float DisconnectTimer;

            public ConnectedTransducer(SonarTransducer transducer, float signalStrength, float disconnectTimer)
            {
                Transducer = transducer;
                SignalStrength = signalStrength;
                DisconnectTimer = disconnectTimer;
            }
        }

        private const float DirectionalPingSector = 30.0f;

        private float range;
        private float pingInterval;
        private float pingTimer;
        private float pingSpeed;

        private Mode currentMode = Mode.Passive;

        private class ActivePing
        {
            public float Radius;
            public float PrevRadius;
            public bool IsDirectional;
            public Vector2 Direction;
        }
        // List of currently active pings.
        private readonly List<ActivePing> activePings = new();

        private const float MinZoom = 1.0f, MaxZoom = 4.0f;
        private float zoom = 1.0f;

        /// <remarks>Accessed through event actions. Do not remove even if there are no references in code.</remarks>
        public bool UseDirectionalPing => useDirectionalPing;
        private bool useDirectionalPing = false;
        private Vector2 pingDirection = new Vector2(1.0f, 0.0f);
        private bool useMineralScanner;

        private bool aiPingCheckPending;

        //the float value is a timer used for disconnecting the transducer if no signal is received from it for 1 second
        private readonly List<ConnectedTransducer> connectedTransducers;

        public IEnumerable<SonarTransducer> ConnectedTransducers
        {
            get { return connectedTransducers.Select(t => t.Transducer); }
        }

        [Serialize(DefaultSonarRange, IsPropertySaveable.No, description: "The maximum range of the sonar.")]
        public float Range
        {
            get { return range; }
            set
            {
                range = MathHelper.Clamp(value, 0.0f, 100000.0f);
                if (item?.AiTarget != null && item.AiTarget.MaxSoundRange <= 0)
                {
                    item.AiTarget.MaxSoundRange = range;
                }
            }
        }

        [Serialize(2.0f, IsPropertySaveable.No, description: "The interval between active sonar pings.")]
        public float PingInterval
        {
            get { return pingInterval; }
            set { pingInterval = MathHelper.Max(value, 0.01f); }
        }

        [Serialize(5000f, IsPropertySaveable.No, description: "The speed at which sonar pings travel.")]
        public float PingSpeed
        {
            get { return pingSpeed; }
            set { pingSpeed = MathHelper.Max(value, 0.01f); }
        }

        [Serialize(0.1f, IsPropertySaveable.No, description: "The power consumption multiplier when using Passive mode.")]
        public float PassivePowerConsumptionMultiplier
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Should the sonar display the walls of the submarine it is inside.")]
        public bool DetectSubmarineWalls
        {
            get;
            set;
        }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Does the sonar have to be connected to external transducers to work.")]
        public bool UseTransducers
        {
            get;
            set;
        }

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Should the sonar view be centered on the transducers or the submarine's center of mass. Only has an effect if UseTransducers is enabled.")]
        public bool CenterOnTransducers
        {
            get;
            set;
        }

        private bool hasMineralScanner;

        [Editable, Serialize(false, IsPropertySaveable.No, description: "Does the sonar have mineral scanning mode. ")]
        public bool HasMineralScanner
        {
            get => hasMineralScanner;
            set
            {
#if CLIENT
                if (controlContainer != null && !hasMineralScanner && value)
                {
                    AddMineralScannerSwitchToGUI();
                }
#endif
                hasMineralScanner = value;
            }
        }

        public float Zoom
        {
            get { return zoom; }
            set 
            { 
                zoom = MathHelper.Clamp(value, MinZoom, MaxZoom);
#if CLIENT
                zoomSlider.BarScroll = MathUtils.InverseLerp(MinZoom, MaxZoom, zoom);
#endif
            }
        }

        public Mode CurrentMode
        {
            get => currentMode;
            set
            {
                bool changed = currentMode != value;
                currentMode = value;
#if CLIENT
                if (changed) { prevPassivePingRadius = float.MaxValue; }
                UpdateGUIElements();
#endif
            }
        }

        public Sonar(Item item, ContentXElement element)
            : base(item, element)
        {
            connectedTransducers = new List<ConnectedTransducer>();
            IsActive = true;
            InitProjSpecific(element);
            CurrentMode = Mode.Passive;
        }

        partial void InitProjSpecific(ContentXElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);

            if (UseTransducers)
            {
                foreach (ConnectedTransducer transducer in connectedTransducers)
                {
                    transducer.DisconnectTimer -= deltaTime;
                }
                connectedTransducers.RemoveAll(t => t.DisconnectTimer <= 0.0f);
            }

            if ((!UseTransducers || connectedTransducers.Count > 0) && (Voltage >= MinVoltage))
            {
                if (currentMode == Mode.Active)
                {
                    if (pingTimer >= 1.0f)
                    {
                        ActivePing newPing = new()
                        {
                            Radius = 0.0f,
                            PrevRadius = 0.0f,
                            IsDirectional = useDirectionalPing,
                            Direction = pingDirection
                        };
                        activePings.Add(newPing);

                        item.Use(deltaTime);
                        pingTimer = 0.0f;
                    }

                    aiPingCheckPending = true;
                    pingTimer += deltaTime * (1 / PingInterval);
                }
                else
                {
                    aiPingCheckPending = false;
                    pingTimer = 1.0f;
                }
            }
            else
            {
                aiPingCheckPending = false;
                activePings.Clear();
                pingTimer = 1.0f;
            }

            if (activePings.Any())
            {
                for (var pingIndex = 0; pingIndex < activePings.Count; pingIndex++)
                {
                    ActivePing activePing = activePings[pingIndex];
                    if (activePing.Radius > Range)
                    {
                        activePings.RemoveAt(pingIndex);
                    }
                    else
                    {
                        activePing.Radius += deltaTime * PingSpeed;
                    }

                    if (item.AiTarget != null)
                    {
                        item.AiTarget.SoundRange = Math.Max(item.AiTarget.SoundRange, MathHelper.Lerp(item.AiTarget.MinSoundRange, item.AiTarget.MaxSoundRange, activePing.Radius / Range));
                    }
                }

                // TODO: Allow multiple seperated sectors & iterate over each active ping to disallow pinging a sector then moving the ping direction to gain information without aggro'ing enemies.
                if (item.AiTarget != null)
                {
                    item.AiTarget.SectorDegrees = useDirectionalPing ? DirectionalPingSector : 360.0f;
                    item.AiTarget.SectorDir = new Vector2(pingDirection.X, -pingDirection.Y);
                }
            }
            else
            {
                item.AiTarget.SoundRange = 0.0f;
            }
        }

        /// <summary>
        /// Power consumption of the sonar. Only consume power when active and adjust the consumption based on the sonar mode.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            if (connection != powerIn || !IsActive)
            {
                return 0;
            }

            return CurrentMode == Mode.Active ? PowerConsumption : PowerConsumption * PassivePowerConsumptionMultiplier;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return activePings.Any() && (character == null || characterUsable);
        }

        private static readonly Dictionary<string, List<Character>> targetGroups = new Dictionary<string, List<Character>>();

        public override bool CrewAIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (!aiPingCheckPending) { return false; }

            foreach (List<Character> targetGroup in targetGroups.Values)
            {
                targetGroup.Clear();
            }
            foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead || c.Removed || !c.Enabled) { continue; }
                if (c.AnimController.CurrentHull != null || c.Params.HideInSonar) { continue; }
                if (DetectSubmarineWalls && c.AnimController.CurrentHull == null && item.CurrentHull != null) { continue; }
                if (Vector2.DistanceSquared(c.WorldPosition, item.WorldPosition) > range * range) { continue; }

                #warning This is not the best key for a dictionary.
                string directionName = GetDirectionName(c.WorldPosition - item.WorldPosition).Value;
                if (!targetGroups.ContainsKey(directionName))
                {
                    targetGroups.Add(directionName, new List<Character>());
                }
                targetGroups[directionName].Add(c);
            }

            foreach (KeyValuePair<string, List<Character>> targetGroup in targetGroups)
            {
                if (!targetGroup.Value.Any()) { continue; }
                string dialogTag = "DialogSonarTarget";
                if (targetGroup.Value.Count > 1)
                {
                    dialogTag = "DialogSonarTargetMultiple";
                }
                else if (targetGroup.Value[0].Mass > 100.0f)
                {
                    dialogTag = "DialogSonarTargetLarge";
                }

                if (character.IsOnPlayerTeam)
                {
                    character.Speak(TextManager.GetWithVariables(dialogTag,
                        ("[direction]", targetGroup.Key.ToString(), FormatCapitals.Yes),
                        ("[count]", targetGroup.Value.Count.ToString(), FormatCapitals.No)).Value,
                        null, 0, $"sonartarget{targetGroup.Value[0].ID}".ToIdentifier(), 60);
                }

                //prevent the character from reporting other targets in the group
                for (int i = 1; i < targetGroup.Value.Count; i++)
                {
                    character.DisableLine("sonartarget" + targetGroup.Value[i].ID);
                }
            }

            return true;
        }

        private static LocalizedString GetDirectionName(Vector2 dir)
        {
            float angle = MathUtils.WrapAngleTwoPi((float)-Math.Atan2(dir.Y, dir.X) + MathHelper.PiOver2);

            int clockDir = (int)Math.Round((angle / MathHelper.TwoPi) * 12);
            if (clockDir == 0) clockDir = 12;

            return TextManager.GetWithVariable("roomname.subdiroclock", "[dir]", clockDir.ToString());
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            base.ReceiveSignal(signal, connection);

            if (connection.Name == "transducer_in")
            {
                var transducer = signal.source.GetComponent<SonarTransducer>();
                if (transducer == null) { return; }

                transducer.ConnectedSonar = this;

                var connectedTransducer = connectedTransducers.Find(t => t.Transducer == transducer);
                if (connectedTransducer == null)
                {
                    connectedTransducers.Add(new ConnectedTransducer(transducer, signal.strength, 1.0f));
                }
                else
                {
                    connectedTransducer.SignalStrength = signal.strength;
                    connectedTransducer.DisconnectTimer = 1.0f;
                }
            }
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            bool isActive = msg.ReadBoolean();
            bool directionalPing = useDirectionalPing;
            float zoomT = zoom, pingDirectionT = 0.0f;
            bool mineralScanner = useMineralScanner;
            if (isActive)
            {
                zoomT = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                directionalPing = msg.ReadBoolean();
                if (directionalPing)
                {
                    pingDirectionT = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                }
                mineralScanner = msg.ReadBoolean();
            }

            if (!item.CanClientAccess(c)) { return; }

            CurrentMode = isActive ? Mode.Active : Mode.Passive;

            if (isActive)
            {
                zoom = MathHelper.Lerp(MinZoom, MaxZoom, zoomT);
                useDirectionalPing = directionalPing;
                if (useDirectionalPing)
                {
                    float pingAngle = MathHelper.Lerp(0.0f, MathHelper.TwoPi, pingDirectionT);
                    pingDirection = new Vector2((float)Math.Cos(pingAngle), (float)Math.Sin(pingAngle));
                }
                useMineralScanner = mineralScanner;
#if CLIENT
                zoomSlider.BarScroll = zoomT;
                directionalModeSwitch.Selected = useDirectionalPing;
                if (mineralScannerSwitch != null)
                {
                    mineralScannerSwitch.Selected = useMineralScanner;
                }
#endif
            }
#if SERVER
            item.CreateServerEvent(this);
#endif
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(currentMode == Mode.Active);
            if (currentMode == Mode.Active)
            {
                msg.WriteRangedSingle(zoom, MinZoom, MaxZoom, 8);
                msg.WriteBoolean(useDirectionalPing);
                if (useDirectionalPing)
                {
                    float pingAngle = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(pingDirection));
                    msg.WriteRangedSingle(MathUtils.InverseLerp(0.0f, MathHelper.TwoPi, pingAngle), 0.0f, 1.0f, 8);
                }
                msg.WriteBoolean(useMineralScanner);
            }
        }
    }
}
