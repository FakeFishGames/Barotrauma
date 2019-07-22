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

        private float pingState;

        private const float MinZoom = 1.0f, MaxZoom = 4.0f;
        private float zoom = 1.0f;

        private bool useDirectionalPing = false;
        private Vector2 lastPingDirection = new Vector2(1.0f, 0.0f);
        private Vector2 pingDirection = new Vector2(1.0f, 0.0f);

        //was the last ping sent with directional pinging
        private bool isLastPingDirectional;

        private Sprite pingCircle, directionalPingCircle, screenOverlay, screenBackground;
        private Sprite sonarBlip;
        private Sprite lineSprite;

        private bool aiPingCheckPending;

        //the float value is a timer used for disconnecting the transducer if no signal is received from it for 1 second
        private List<ConnectedTransducer> connectedTransducers;

        public IEnumerable<SonarTransducer> ConnectedTransducers
        {
            get { return connectedTransducers.Select(t => t.Transducer); }
        }

        [Serialize(DefaultSonarRange, false)]
        public float Range
        {
            get { return range; }
            set { range = MathHelper.Clamp(value, 0.0f, 100000.0f); }
        }

        [Serialize(false, false)]
        public bool DetectSubmarineWalls
        {
            get;
            set;
        }

        [Serialize(false, false), Editable(ToolTip = "Does the sonar have to be connected to external transducers to work.")]
        public bool UseTransducers
        {
            get;
            set;
        }

        public float Zoom
        {
            get { return zoom; }
        }
        
        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }

            set
            {
                base.IsActive = value;
                if (!value && item.CurrentHull != null)
                {
                    item.CurrentHull.AiTarget.SectorDegrees = 360.0f;
                }
#if CLIENT
                if (activeTickBox != null) activeTickBox.Selected = value;
                if (passiveTickBox != null) passiveTickBox.Selected = !value;
#endif
            }
        }

        public Sonar(Item item, XElement element)
            : base(item, element)
        {
            connectedTransducers = new List<ConnectedTransducer>();
                        
            IsActive = false;
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
            
            if ((voltage >= minVoltage || powerConsumption <= 0.0f) &&
                (!UseTransducers || connectedTransducers.Count > 0))
            {
                pingState = pingState + deltaTime * 0.5f;
                if (pingState > 1.0f)
                {
                    if (item.CurrentHull != null)
                    {
                        item.CurrentHull.AiTarget.SoundRange = Math.Max(Range * pingState / zoom, item.CurrentHull.AiTarget.SoundRange);
                        item.CurrentHull.AiTarget.SectorDegrees = isLastPingDirectional ? DirectionalPingSector : 360.0f;
                        item.CurrentHull.AiTarget.SectorDir = new Vector2(pingDirection.X, -pingDirection.Y);
                    }
                    if (item.AiTarget != null)
                    {
                        item.AiTarget.SoundRange = Math.Max(Range * pingState / zoom, item.AiTarget.SoundRange);
                        item.AiTarget.SectorDegrees = isLastPingDirectional ? DirectionalPingSector : 360.0f;
                        item.AiTarget.SectorDir = new Vector2(pingDirection.X, -pingDirection.Y);
                    }
                    aiPingCheckPending = true;
                    isLastPingDirectional = useDirectionalPing;
                    lastPingDirection = pingDirection;
                    item.Use(deltaTime);
                    pingState = 0.0f;
                }
            }
            else
            {
                if (item.CurrentHull != null)
                {
                    item.CurrentHull.AiTarget.SectorDegrees = 360.0f;
                }
                aiPingCheckPending = false;
                pingState = 0.0f;
            }

            Voltage -= deltaTime;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            return pingState > 1.0f;
        }

        protected override void RemoveComponentSpecific()
        {
            sonarBlip?.Remove();
            pingCircle?.Remove();
            directionalPingCircle?.Remove();
            screenOverlay?.Remove();
            screenBackground?.Remove();
            lineSprite?.Remove();
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (!IsActive || !aiPingCheckPending) return false;

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

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Client c)
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

            IsActive = isActive;

            //TODO: cleanup
#if CLIENT
            activeTickBox.Selected = IsActive;
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

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsActive);
            if (IsActive)
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
