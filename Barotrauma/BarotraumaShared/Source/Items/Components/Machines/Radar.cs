using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Radar : Powered, IServerSerializable, IClientSerializable
    {
        private float range;

        private float pingState;

        private readonly Sprite pingCircle, screenOverlay;

        private readonly Sprite radarBlip;

        private bool aiPingCheckPending;

        //the float value is a timer used for disconnecting the transducer if no signal is received from it for 1 second
        private Dictionary<SonarTransducer, float> connectedTransducers;

        public IEnumerable<SonarTransducer> ConnectedTransducers
        {
            get { return connectedTransducers.Keys; }
        }

        [Serialize(10000.0f, false)]
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

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }

            set
            {
                base.IsActive = value;
#if CLIENT
                if (isActiveTickBox != null) isActiveTickBox.Selected = value;
#endif
            }
        }

        public Radar(Item item, XElement element)
            : base(item, element)
        {
            connectedTransducers = new Dictionary<SonarTransducer, float>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "pingcircle":
                        pingCircle = new Sprite(subElement);
                        break;
                    case "screenoverlay":
                        screenOverlay = new Sprite(subElement);
                        break;
                    case "blip":
                        radarBlip = new Sprite(subElement);
                        break;
                }
            }
            
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
                List<SonarTransducer> transducers = new List<SonarTransducer>(connectedTransducers.Keys);
                foreach (SonarTransducer transducer in transducers)
                {
                    connectedTransducers[transducer] -= deltaTime;
                    if (connectedTransducers[transducer] <= 0.0f) connectedTransducers.Remove(transducer);
                }
            }
            
            if ((voltage >= minVoltage || powerConsumption <= 0.0f) &&
                (!UseTransducers || connectedTransducers.Count > 0))
            {
                pingState = pingState + deltaTime * 0.5f;
                if (pingState > 1.0f)
                {
                    if (item.CurrentHull != null) item.CurrentHull.AiTarget.SoundRange = Math.Max(Range * pingState, item.CurrentHull.AiTarget.SoundRange);
                    if (item.AiTarget != null) item.AiTarget.SoundRange = Math.Max(Range * pingState, item.AiTarget.SoundRange);
                    aiPingCheckPending = true;
                    item.Use(deltaTime);
                    pingState = 0.0f;
                }
            }
            else
            {
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
            if (pingCircle != null) pingCircle.Remove();
            if (screenOverlay != null) screenOverlay.Remove();
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
                character.Speak(TextManager.Get(dialogTag).Replace("[direction]", targetGroup.Key).Replace("[count]", targetGroup.Value.Count.ToString()),
                    null, 0, "sonartarget" + targetGroup.Value[0].ID, 30);

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

            return TextManager.Get("SubDirOClock").Replace("[dir]", clockDir.ToString());
        }

        private Vector2 GetTransducerCenter()
        {
            if (!UseTransducers || connectedTransducers.Count == 0) return Vector2.Zero;
            Vector2 transducerPosSum = Vector2.Zero;
            foreach (SonarTransducer transducer in connectedTransducers.Keys)
            {
                transducerPosSum += transducer.Item.WorldPosition;
            }
            return transducerPosSum / connectedTransducers.Count;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);

            if (connection.Name == "transducer_in")
            {
                var transducer = source.GetComponent<SonarTransducer>();
                if (transducer == null) return;
                connectedTransducers[transducer] = 1.0f;
            }
        }

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Client c)
        {
            bool isActive = msg.ReadBoolean();

            if (!item.CanClientAccess(c)) return; 

            IsActive = isActive;
#if CLIENT
            isActiveTickBox.Selected = IsActive;
#endif

            item.CreateServerEvent(this);
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(IsActive);
        }
    }
}
