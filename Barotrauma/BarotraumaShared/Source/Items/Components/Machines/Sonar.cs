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

        private bool useDirectionalPing = false;
        private Vector2 pingDirection = new Vector2(1.0f, 0.0f);

        private Sprite pingCircle, directionalPingCircle;
        private Sprite screenOverlay, screenBackground;

        private Sprite sonarBlip;
        private Sprite lineSprite;

        private bool aiPingCheckPending;

        //the float value is a timer used for disconnecting the transducer if no signal is received from it for 1 second
        private readonly List<ConnectedTransducer> connectedTransducers;

        public IEnumerable<SonarTransducer> ConnectedTransducers
        {
            get { return connectedTransducers.Select(t => t.Transducer); }
        }

        [Serialize(DefaultSonarRange, false, description: "The maximum range of the sonar.")]
        public float Range
        {
            get { return range; }
            set { range = MathHelper.Clamp(value, 0.0f, 100000.0f); }
        }

        [Serialize(false, false, description: "Should the sonar display the walls of the submarine it is inside.")]
        public bool DetectSubmarineWalls
        {
            get;
            set;
        }

        [Editable, Serialize(false, false, description: "Does the sonar have to be connected to external transducers to work.")]
        public bool UseTransducers
        {
            get;
            set;
        }

        public float Zoom
        {
            get { return zoom; }
        }

        public Mode CurrentMode
        {
            get => currentMode;
            set
            {
                currentMode = value;
                if (value == Mode.Passive)
                {
                    currentPingIndex = -1;
                    if (item.AiTarget != null)
                    {
                        item.AiTarget.SectorDegrees = 360.0f;
                    }
                }
#if CLIENT
                if (activeTickBox != null) activeTickBox.Selected = value == Mode.Active;
                if (passiveTickBox != null) passiveTickBox.Selected = value == Mode.Passive;
#endif
            }
        }

        public Sonar(Item item, XElement element)
            : base(item, element)
        {
            connectedTransducers = new List<ConnectedTransducer>();

            CurrentMode = Mode.Passive;
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam)
        {
            currPowerConsumption = powerConsumption;

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
                            if (item.AiTarget != null)
                            {
                                float range = MathUtils.InverseLerp(item.AiTarget.MinSoundRange, item.AiTarget.MaxSoundRange, Range * activePing.State / zoom);
                                item.AiTarget.SoundRange = MathHelper.Lerp(item.AiTarget.MinSoundRange, item.AiTarget.MaxSoundRange, range);
                                item.AiTarget.SectorDegrees = activePing.IsDirectional ? DirectionalPingSector : 360.0f;
                                item.AiTarget.SectorDir = new Vector2(pingDirection.X, -pingDirection.Y);
                            }
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
                        item.Use(deltaTime);
                    }
                }
                else
                {
                    if (item.AiTarget != null)
                    {
                        item.AiTarget.SectorDegrees = 360.0f;
                    }
                    currentPingIndex = -1;
                    aiPingCheckPending = false;
                }
            }

            for (var pingIndex = 0; pingIndex < activePingsCount;)
            {
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

            Voltage -= deltaTime;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return currentPingIndex != -1;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            sonarBlip?.Remove();
            pingCircle?.Remove();
            directionalPingCircle?.Remove();
            screenOverlay?.Remove();
            screenBackground?.Remove();
            lineSprite?.Remove();
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (currentMode == Mode.Passive || !aiPingCheckPending) return false;

            // TODO: Don't create new collections here
            Dictionary<string, List<Character>> targetGroups = new Dictionary<string, List<Character>>();

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.CurrentHull != null || !c.Enabled) continue;
                if (DetectSubmarineWalls && c.AnimController.CurrentHull == null && item.CurrentHull != null) continue;
                if (Vector2.DistanceSquared(c.WorldPosition, item.WorldPosition) > range * range) continue;

                string directionName = GetDirectionName(c.WorldPosition - item.WorldPosition);

                if (!targetGroups.ContainsKey(directionName))
                {
                    targetGroups.Add(directionName, new List<Character>());
                }
                targetGroups[directionName].Add(c);
            }

            foreach (KeyValuePair<string, List<Character>> targetGroup in targetGroups)
            {
                string dialogTag = "DialogSonarTarget";
                if (targetGroup.Value.Count > 1)
                {
                    dialogTag = "DialogSonarTargetMultiple";
                }
                else if (targetGroup.Value[0].Mass > 100.0f)
                {
                    dialogTag = "DialogSonarTargetLarge";
                }

                character.Speak(TextManager.GetWithVariables(dialogTag, new string[2] { "[direction]", "[count]" }, 
                    new string[2] { targetGroup.Key.ToString(), targetGroup.Value.Count.ToString() },
                    new bool[2] { true, false }), null, 0, "sonartarget" + targetGroup.Value[0].ID, 30);

                //prevent the character from reporting other targets in the group
                for (int i = 1; i < targetGroup.Value.Count; i++)
                {
                    character.DisableLine("sonartarget" + targetGroup.Value[i].ID);
                }
            }

            return true;
        }

        private string GetDirectionName(Vector2 dir)
        {
            float angle = MathUtils.WrapAngleTwoPi((float)-Math.Atan2(dir.Y, dir.X) + MathHelper.PiOver2);

            int clockDir = (int)Math.Round((angle / MathHelper.TwoPi) * 12);
            if (clockDir == 0) clockDir = 12;

            return TextManager.GetWithVariable("roomname.subdiroclock", "[dir]", clockDir.ToString());
        }

        private Vector2 GetTransducerPos()
        {
            if (!UseTransducers || connectedTransducers.Count == 0)
            {
                //use the position of the sub if the item is static (no body) and inside a sub
                return item.Submarine != null && item.body == null ? item.Submarine.WorldPosition : item.WorldPosition;
            }

            Vector2 transducerPosSum = Vector2.Zero;
            foreach (ConnectedTransducer transducer in connectedTransducers)
            {
                if (transducer.Transducer.Item.Submarine != null)
                {
                    return transducer.Transducer.Item.Submarine.WorldPosition;
                }
                transducerPosSum += transducer.Transducer.Item.WorldPosition;
            }
            return transducerPosSum / connectedTransducers.Count;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power, signalStrength);

            if (connection.Name == "transducer_in")
            {
                var transducer = source.GetComponent<SonarTransducer>();
                if (transducer == null) return;

                var connectedTransducer = connectedTransducers.Find(t => t.Transducer == transducer);
                if (connectedTransducer == null)
                {
                    connectedTransducers.Add(new ConnectedTransducer(transducer, signalStrength, 1.0f));
                }
                else
                {
                    connectedTransducer.SignalStrength = signalStrength;
                    connectedTransducer.DisconnectTimer = 1.0f;
                }
            }
        }

        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            bool isActive = msg.ReadBoolean();
            bool directionalPing = useDirectionalPing;
            float zoomT = zoom, pingDirectionT = 0.0f;
            if (isActive)
            {
                zoomT = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                directionalPing = msg.ReadBoolean();
                if (directionalPing)
                {
                    pingDirectionT = msg.ReadRangedSingle(0.0f, 1.0f, 8);
                }
            }

            if (!item.CanClientAccess(c)) return;

            CurrentMode = isActive ? Mode.Active : Mode.Passive;

            //TODO: cleanup
#if CLIENT
            activeTickBox.Selected = currentMode == Mode.Active;
#endif
            if (isActive)
            {
                zoom = MathHelper.Lerp(MinZoom, MaxZoom, zoomT);
                useDirectionalPing = directionalPing;
                if (useDirectionalPing)
                {
                    float pingAngle = MathHelper.Lerp(0.0f, MathHelper.TwoPi, pingDirectionT);
                    pingDirection = new Vector2((float)Math.Cos(pingAngle), (float)Math.Sin(pingAngle));
                }
#if CLIENT
                zoomSlider.BarScroll = zoomT;
                directionalTickBox.Selected = useDirectionalPing;
                directionalSlider.BarScroll = pingDirectionT;
#endif
            }
#if SERVER
            item.CreateServerEvent(this);
#endif
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(currentMode == Mode.Active);
            if (currentMode == Mode.Active)
            {
                msg.WriteRangedSingle(zoom, MinZoom, MaxZoom, 8);
                msg.Write(useDirectionalPing);
                if (useDirectionalPing)
                {
                    float pingAngle = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(pingDirection));
                    msg.WriteRangedSingle(MathUtils.InverseLerp(0.0f, MathHelper.TwoPi, pingAngle), 0.0f, 1.0f, 8);
                }
            }
        }
    }
}
