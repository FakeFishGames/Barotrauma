using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Pump : Powered, IServerSerializable, IClientSerializable
    {
        private float flowPercentage;
        private float maxFlow;

        private float? targetLevel;
        
        public Hull hull1;

        [HasDefaultValue(0.0f, true)]
        public float FlowPercentage
        {
            get { return flowPercentage; }
            set 
            {
                if (!MathUtils.IsValid(flowPercentage)) return;
                flowPercentage = MathHelper.Clamp(value,-100.0f,100.0f);
                flowPercentage = MathUtils.Round(flowPercentage, 1.0f);
            }
        }

        [HasDefaultValue(80.0f, false)]
        public float MaxFlow
        {
            get { return maxFlow; }
            set { maxFlow = value; } 
        }

        float currFlow;
        public float CurrFlow
        {
            get 
            {
                if (!IsActive) return 0.0f;
                return Math.Abs(currFlow); 
            }
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

        public Pump(Item item, XElement element)
            : base(item, element)
        {
            GetHull();

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            GetHull();
        }

        public override void OnMapLoaded()
        {
            GetHull();
        }

        public override void Update(float deltaTime, Camera cam)
        {
            currFlow = 0.0f;

            if (targetLevel != null)
            {
                float hullPercentage = 0.0f;
                if (hull1 != null) hullPercentage = (hull1.Volume / hull1.FullVolume) * 100.0f;
                FlowPercentage = ((float)targetLevel - hullPercentage) * 10.0f;
            }

            currPowerConsumption = powerConsumption * Math.Abs(flowPercentage / 100.0f);

            if (voltage < minVoltage) return;

            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            if (hull1 == null) return;
            
            float powerFactor = (currPowerConsumption==0.0f) ? 1.0f : voltage;
            //flowPercentage = maxFlow * powerFactor;

            currFlow = (flowPercentage / 100.0f) * maxFlow * powerFactor;

            hull1.Volume += currFlow;
            if (hull1.Volume > hull1.FullVolume) hull1.Pressure += 0.5f;

            //if (hull2 != null)
            //{
            //    hull2.Volume -= currFlow;
            //    if (hull2.Volume > hull1.FullVolume) hull2.Pressure += 0.5f;
            //}

            voltage = 0.0f;
        }

        private void GetHull()
        {
            hull1 = Hull.FindHull(item.WorldPosition, item.CurrentHull);
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);
            
            if (connection.Name == "toggle")
            {
                IsActive = !IsActive;
            }
            else if (connection.Name == "set_active")
            {
                IsActive = (signal != "0");                
            }
            else if (connection.Name == "set_speed")
            {
                float tempSpeed;
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out tempSpeed))
                {
                    flowPercentage = MathHelper.Clamp(tempSpeed, -100.0f, 100.0f);
                }
            }
            else if (connection.Name == "set_targetlevel")
            {
                float tempTarget;
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out tempTarget))
                {
                    targetLevel = MathHelper.Clamp((tempTarget+100.0f)/2.0f, 0.0f, 100.0f);
                }
            }

            if (!IsActive) currPowerConsumption = 0.0f;
        }

        public void ServerRead(ClientNetObject type, Lidgren.Network.NetBuffer msg, Client c)
        {
            float newFlowPercentage = msg.ReadRangedInteger(-10, 10) * 10.0f;
            bool newIsActive        = msg.ReadBoolean();

            if (item.CanClientAccess(c))
            {
                if (newFlowPercentage != FlowPercentage)
                {
                    GameServer.Log(c.Character + " set the pumping speed of " + item.Name + " to " + (int)(newFlowPercentage) + " %", ServerLog.MessageType.ItemInteraction);
                }
                if (newIsActive != IsActive)
                {
                    GameServer.Log(c.Character + (newIsActive ? " turned on " : " turned off ") + item.Name, ServerLog.MessageType.ItemInteraction);
                }

                FlowPercentage  = newFlowPercentage;
                IsActive        = newIsActive;
            } 
            
            //notify all clients of the changed state
            item.CreateServerEvent(this);
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Client c, object[] extraData = null)
        {
            //flowpercentage can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger(-10, 10, (int)(flowPercentage / 10.0f));
            msg.Write(IsActive);
        }

    }
}
