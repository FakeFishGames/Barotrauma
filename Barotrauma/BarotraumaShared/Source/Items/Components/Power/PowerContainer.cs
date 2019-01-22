using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        //[power/min]        
        private float capacity;

        private float charge;

        private float rechargeVoltage, outputVoltage;

        //how fast the battery can be recharged
        private float maxRechargeSpeed;

        //how fast it's currently being recharged (can be changed, so that
        //charging can be slowed down or disabled if there's a shortage of power)
        private float rechargeSpeed;

        private float maxOutput;

        private float lastSentCharge;

        public float CurrPowerOutput
        {
            get;
            private set;
        }

        [Editable(ToolTip = "Maximum output of the device when fully charged (kW)."), Serialize(10.0f, true)]
        public float MaxOutPut
        {
            set { maxOutput = value; }
            get { return maxOutput; }
        }

        [Serialize(10.0f, true), Editable(ToolTip = "The maximum capacity of the device (kW * min). "+
            "For example, a value of 1000 means the device can output 100 kilowatts of power for 10 minutes, or 1000 kilowatts for 1 minute.")]
        public float Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1.0f); }
        }

        [Editable, Serialize(0.0f, true)]
        public float Charge
        {
            get { return charge; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                charge = MathHelper.Clamp(value, 0.0f, capacity); 

                if (Math.Abs(charge - lastSentCharge) / capacity > 1.0f)
                {
                    if (GameMain.Server != null) item.CreateServerEvent(this);
                    lastSentCharge = charge;
                }
            }
        }
        
        [Serialize(10.0f, true), Editable(ToolTip = "How fast the device can be recharged. "+
            "For example, a recharge speed of 100 kW and a capacity of 1000 kW*min would mean it takes 10 minutes to fully charge the device.")]
        public float MaxRechargeSpeed
        {
            get { return maxRechargeSpeed; }
            set { maxRechargeSpeed = Math.Max(value, 1.0f); }
        }

        [Serialize(10.0f, true), Editable]
        public float RechargeSpeed
        {
            get { return rechargeSpeed; }
            set
            {
                if (!MathUtils.IsValid(value)) return;              
                rechargeSpeed = MathHelper.Clamp(value, 0.0f, maxRechargeSpeed);
                rechargeSpeed = MathUtils.RoundTowardsClosest(rechargeSpeed, Math.Max(maxRechargeSpeed * 0.1f, 1.0f));
            }
        }

        public PowerContainer(Item item, XElement element)
            : base(item, element)
        {
            //capacity = ToolBox.GetAttributeFloat(element, "capacity", 10.0f);
            //maxRechargeSpeed = ToolBox.GetAttributeFloat(element, "maxinput", 10.0f);
            //maxOutput = ToolBox.GetAttributeFloat(element, "maxoutput", 10.0f);
            
            IsActive = true;

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override bool Pick(Character picker)
        {
            if (picker == null) return false;

            //picker.SelectedConstruction = (picker.SelectedConstruction == item) ? null : item;
            
            return true;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            float chargeRatio = (float)(Math.Sqrt(charge / capacity));
            float gridPower = 0.0f;
            float gridLoad = 0.0f;
            
            foreach (Connection c in item.Connections)
            {
                if (c.Name == "power_in") continue;
                foreach (Connection c2 in c.Recipients)
                {
                    PowerTransfer pt = c2.Item.GetComponent<PowerTransfer>();
                    if (pt == null || !pt.IsActive) continue;

                    gridLoad += pt.PowerLoad;
                    gridPower -= pt.CurrPowerConsumption;
                }
            }
            
            if (chargeRatio > 0.0f)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }
            
            if (charge >= capacity)
            {
                rechargeVoltage = 0.0f;
                charge = capacity;

                CurrPowerConsumption = 0.0f;
            }
            else
            {
                currPowerConsumption = MathHelper.Lerp(currPowerConsumption, rechargeSpeed, 0.05f);
                Charge += currPowerConsumption * rechargeVoltage / 3600.0f;
            }
            
            //provide power to the grid
            if (gridLoad > 0.0f)
            {
                if (charge <= 0.0f)
                {
                    CurrPowerOutput = 0.0f;
                    charge = 0.0f;
                    return;
                }

                if (gridPower < gridLoad)
                {
                    CurrPowerOutput = MathHelper.Lerp(
                       CurrPowerOutput,
                       Math.Min(maxOutput * chargeRatio, gridLoad),
                       deltaTime);
                }
                else
                {
                    CurrPowerOutput = MathHelper.Lerp(CurrPowerOutput, 0.0f, deltaTime);
                }

                Charge -= CurrPowerOutput / 3600.0f;
            }
            item.SendSignal(0, Charge.ToString(), "charge", null);
            item.SendSignal(0, ((Charge / capacity) * 100).ToString(), "charge_%", null);
            item.SendSignal(0, ((RechargeSpeed / maxRechargeSpeed) * 100).ToString(), "charge_rate", null);

            rechargeVoltage = 0.0f;
            outputVoltage = 0.0f;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            RechargeSpeed = maxRechargeSpeed * 0.5f;

            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power)
        {
            if (connection.Name == "set_rate")
            {
                float tempSpeed;
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out tempSpeed))
                {
                    if (!MathUtils.IsValid(tempSpeed)) return;
                    RechargeSpeed = MathHelper.Clamp(tempSpeed / 100.0f, 0.0f, 1.0f) * MaxRechargeSpeed;
                }
            }
            if (!connection.IsPower) return;

            if (connection.Name == "power_in")
            {
                rechargeVoltage = Math.Min(power, 1.0f);
            }
            else
            {
                outputVoltage = power;
            }
        }
        
        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            float newRechargeSpeed = msg.ReadRangedInteger(0,10) / 10.0f * maxRechargeSpeed;

            if (item.CanClientAccess(c))
            {
                RechargeSpeed = newRechargeSpeed;
                GameServer.Log(c.Character.LogName + " set the recharge speed of "+item.Name+" to "+ (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", ServerLog.MessageType.ItemInteraction);
            }

            item.CreateServerEvent(this);
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.WriteRangedInteger(0, 10, (int)(rechargeSpeed / MaxRechargeSpeed * 10));

            float chargeRatio = MathHelper.Clamp(charge / capacity, 0.0f, 1.0f);
            msg.WriteRangedSingle(chargeRatio, 0.0f, 1.0f, 8);
        }
    }
}
