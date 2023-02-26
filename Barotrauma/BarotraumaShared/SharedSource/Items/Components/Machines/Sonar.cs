using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

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

        public const float PassivePowerConsumption = 0.1f;

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
        private static readonly float DirectionalPingDotProduct;

        static Sonar()
        {
            DirectionalPingDotProduct = (float)Math.Cos(MathHelper.ToRadians(DirectionalPingSector) * 0.5f);
        }

        private float range;

        private const float PingFrequency = 0.5f;

        private Mode currentMode = Mode.Passive;

        private class ActivePing
        {
            public float State;
            public bool IsDirectional;
            public Vector2 Direction;
            public float PrevPingRadius;
        }
        // rotating list of currently active pings
        private ActivePing[] activePings = new ActivePing[8];
        // total number of currently active pings, range [0, activePings.Length[
        private int activePingsCount;
        // currently active ping index on the above list
        private int currentPingIndex = -1;

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

            for (var pingIndex = 0; pingIndex < activePingsCount; ++pingIndex)
            {
                activePings[pingIndex].State += deltaTime * PingFrequency;
            }

            if (currentMode == Mode.Active)
            {
                if ((Voltage >= MinVoltage) &&
                    (!UseTransducers || connectedTransducers.Count > 0))
                {
                    if (currentPingIndex != -1)
                    {
                        var activePing = activePings[currentPingIndex];
                        if (activePing.State > 1.0f)
                        {
                            aiPingCheckPending = true;
                            currentPingIndex = -1;
                        }
                    }
                    if (currentPingIndex == -1 && activePingsCount < activePings.Length)
                    {
                        currentPingIndex = activePingsCount++;
                        if (activePings[currentPingIndex] == null)
                        {
                            activePings[currentPingIndex] = new ActivePing();
                        }
                        activePings[currentPingIndex].IsDirectional = useDirectionalPing;
                        activePings[currentPingIndex].Direction = pingDirection;
                        activePings[currentPingIndex].State = 0.0f;
                        activePings[currentPingIndex].PrevPingRadius = 0.0f;
                        if (item.AiTarget != null)
                        {
                            item.AiTarget.SectorDegrees = useDirectionalPing ? DirectionalPingSector : 360.0f;
                            item.AiTarget.SectorDir = new Vector2(pingDirection.X, -pingDirection.Y);
                            if (UseTransducers)
                            {
                                // TODO: do something to get average position for more than one
                                item.AiTarget.EntityOveride = connectedTransducers.First().Transducer.Item;
                            }
                            else
                            {
                                item.AiTarget.EntityOveride = item.Submarine;
                            }
                        }
                        item.Use(deltaTime);
                    }
                }
                else
                {
                    aiPingCheckPending = false;
                }
            }

            for (var pingIndex = 0; pingIndex < activePingsCount;)
            {
                if (item.AiTarget != null)
                {
                    float range = MathUtils.InverseLerp(item.AiTarget.MinSoundRange, item.AiTarget.MaxSoundRange, Range * activePings[pingIndex].State / zoom);
                    item.AiTarget.SoundRange = Math.Max(item.AiTarget.SoundRange, MathHelper.Lerp(item.AiTarget.MinSoundRange, item.AiTarget.MaxSoundRange, range));
                }
                if (activePings[pingIndex].State > 1.0f)
                {
                    var lastIndex = --activePingsCount;
                    var oldActivePing = activePings[pingIndex];
                    activePings[pingIndex] = activePings[lastIndex];
                    activePings[lastIndex] = oldActivePing;
                    if (currentPingIndex == lastIndex)
                    {
                        currentPingIndex = pingIndex;
                    }
                }
                else
                {
                    ++pingIndex;
                }
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

            return (currentMode == Mode.Active) ? powerConsumption : powerConsumption * PassivePowerConsumption;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return currentPingIndex != -1 && (character == null || characterUsable);
        }

        private static readonly Dictionary<string, List<Character>> targetGroups = new Dictionary<string, List<Character>>();

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (currentMode == Mode.Passive || !aiPingCheckPending) { return false; }

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

        private LocalizedString GetDirectionName(Vector2 dir)
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
