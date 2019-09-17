using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        //[power/min]        
        private float capacity;

        private float charge;

        private float rechargeVoltage;

        //how fast the battery can be recharged
        private float maxRechargeSpeed;

        //how fast it's currently being recharged (can be changed, so that
        //charging can be slowed down or disabled if there's a shortage of power)
        private float rechargeSpeed;
        private float lastSentCharge;

        //charge indicator description
        protected Vector2 indicatorPosition, indicatorSize;

        protected bool isHorizontal;

        //a list of powered devices connected directly to this item
        private readonly List<Pair<Powered, Connection>> directlyConnected = new List<Pair<Powered, Connection>>(10);

        public float CurrPowerOutput
        {
            get;
            private set;
        }

        [Serialize("0,0", true, description: "The position of the progress bar indicating the charge of the item. In pixels as an offset from the upper left corner of the sprite.")]
        public Vector2 IndicatorPosition
        {
            get { return indicatorPosition; }
            set { indicatorPosition = value; }
        }

        [Serialize("0,0", true, description: "The size of the progress bar indicating the charge of the item (in pixels).")]
        public Vector2 IndicatorSize
        {
            get { return indicatorSize; }
            set { indicatorSize = value; }
        }

        [Serialize(false, true, description: "Should the progress bar indicating the charge of the item fill up horizontally or vertically.")]
        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set { isHorizontal = value; }
        }

        [Editable, Serialize(10.0f, true, description: "Maximum output of the device when fully charged (kW).")]
        public float MaxOutPut { set; get; }

        [Editable, Serialize(10.0f, true, description: "The maximum capacity of the device (kW * min). For example, a value of 1000 means the device can output 100 kilowatts of power for 10 minutes, or 1000 kilowatts for 1 minute.")]
        public float Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1.0f); }
        }

        [Editable, Serialize(0.0f, true, description: "The current charge of the device.")]
        public float Charge
        {
            get { return charge; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                charge = MathHelper.Clamp(value, 0.0f, capacity); 

                //send a network event if the charge has changed by more than 5%
                if (Math.Abs(charge - lastSentCharge) / capacity > 0.05f)
                {
#if SERVER
                    if (GameMain.Server != null) item.CreateServerEvent(this);
#endif
                    lastSentCharge = charge;
                }
            }
        }

        public float ChargePercentage => MathUtils.Percentage(Charge, Capacity);
        
        [Editable, Serialize(10.0f, true, description: "How fast the device can be recharged. For example, a recharge speed of 100 kW and a capacity of 1000 kW*min would mean it takes 10 minutes to fully charge the device.")]
        public float MaxRechargeSpeed
        {
            get { return maxRechargeSpeed; }
            set { maxRechargeSpeed = Math.Max(value, 1.0f); }
        }

        [Editable, Serialize(10.0f, true, description: "The current recharge speed of the device.")]
        public float RechargeSpeed
        {
            get { return rechargeSpeed; }
            set
            {
                if (!MathUtils.IsValid(value)) return;              
                rechargeSpeed = MathHelper.Clamp(value, 0.0f, maxRechargeSpeed);
                rechargeSpeed = MathUtils.RoundTowardsClosest(rechargeSpeed, Math.Max(maxRechargeSpeed * 0.1f, 1.0f));
                if (isRunning)
                {
                    HasBeenTuned = true;
                }
            }
        }

        public float RechargeRatio => RechargeSpeed / MaxRechargeSpeed;

        public const float aiRechargeTargetRatio = 0.5f;
        private bool isRunning;
        public bool HasBeenTuned { get; private set; }

        public PowerContainer(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            InitProjSpecific();
        }

        partial void InitProjSpecific();

        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        public override void Update(float deltaTime, Camera cam) 
        {
            isRunning = true;
            float chargeRatio = charge / capacity;
            float gridPower = 0.0f;
            float gridLoad = 0.0f;
            directlyConnected.Clear();

            foreach (Connection c in item.Connections)
            {
                if (c.Name == "power_in") continue;
                foreach (Connection c2 in c.Recipients)
                {
                    if (c2.Item.Condition <= 0.0f) { continue; }

                    PowerTransfer pt = c2.Item.GetComponent<PowerTransfer>();
                    if (pt == null)
                    {
                        foreach (Powered powered in c2.Item.GetComponents<Powered>())
                        {
                            if (!powered.IsActive) continue;
                            directlyConnected.Add(new Pair<Powered, Connection>(powered, c2));
                            gridLoad += powered.CurrPowerConsumption;
                        }
                        continue;
                    }
                    if (!pt.IsActive || !pt.CanTransfer) { continue; }

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
                    //output starts dropping when the charge is less than 10%
                    float maxOutputRatio = 1.0f;
                    if (chargeRatio < 0.1f)
                    {
                        maxOutputRatio = Math.Max(chargeRatio * 10.0f, 0.0f);
                    }

                    CurrPowerOutput = MathHelper.Lerp(
                       CurrPowerOutput,
                       Math.Min(MaxOutPut * maxOutputRatio, gridLoad),
                       deltaTime * 10.0f);
                }
                else
                {
                    CurrPowerOutput = MathHelper.Lerp(CurrPowerOutput, 0.0f, deltaTime * 10.0f);
                }

                Charge -= CurrPowerOutput / 3600.0f;
            }
            item.SendSignal(0, ((int)Charge).ToString(), "charge", null);
            item.SendSignal(0, ((int)((Charge / capacity) * 100)).ToString(), "charge_%", null);
            item.SendSignal(0, ((int)((RechargeSpeed / maxRechargeSpeed) * 100)).ToString(), "charge_rate", null);

            foreach (Pair<Powered, Connection> connected in directlyConnected)
            {
                connected.First.ReceiveSignal(0, "", connected.Second, source: item, sender: null, 
                    power: gridLoad <= 0.0f ? 1.0f : CurrPowerOutput / gridLoad);
            }

            rechargeVoltage = 0.0f;
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
#if CLIENT
            if (GameMain.Client != null) return false;
#endif
            if (objective.Override)
            {
                HasBeenTuned = false;
            }
            if (HasBeenTuned) { return true; }

            if (string.IsNullOrEmpty(objective.Option) || objective.Option.ToLowerInvariant() == "charge")
            {
                if (Math.Abs(rechargeSpeed - maxRechargeSpeed * aiRechargeTargetRatio) > 0.05f)
                {
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    RechargeSpeed = maxRechargeSpeed * aiRechargeTargetRatio;
#if CLIENT
                    if (rechargeSpeedSlider != null)
                    {
                        rechargeSpeedSlider.BarScroll = RechargeSpeed / Math.Max(maxRechargeSpeed, 1.0f);
                    }
#endif
                    
                    character.Speak(TextManager.GetWithVariables("DialogChargeBatteries", new string[2] { "[itemname]", "[rate]" }, 
                        new string[2] { item.Name, ((int)(rechargeSpeed / maxRechargeSpeed * 100.0f)).ToString() },
                        new bool[2] { true, false }), null, 1.0f, "chargebattery", 10.0f);
                }
            }
            else
            {
                if (rechargeSpeed > 0.0f)
                {
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    RechargeSpeed = 0.0f;
#if CLIENT
                    if (rechargeSpeedSlider != null)
                    {
                        rechargeSpeedSlider.BarScroll = RechargeSpeed / Math.Max(maxRechargeSpeed, 1.0f);
                    }
#endif
                    character.Speak(TextManager.GetWithVariables("DialogStopChargingBatteries", new string[2] { "[itemname]", "[rate]" },
                        new string[2] { item.Name, ((int)(rechargeSpeed / maxRechargeSpeed * 100.0f)).ToString() },
                        new bool[2] { true, false }), null, 1.0f, "chargebattery", 10.0f);
                }
            }

            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power, float signalStrength = 1.0f)
        {
            if (connection.Name == "set_rate")
            {
                if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out float tempSpeed))
                {
                    if (!MathUtils.IsValid(tempSpeed)) { return; }

                    float rechargeRate = MathHelper.Clamp(tempSpeed / 100.0f, 0.0f, 1.0f);
                    RechargeSpeed = rechargeRate * MaxRechargeSpeed;
#if CLIENT
                    if (rechargeSpeedSlider != null)
                    {
                        rechargeSpeedSlider.BarScroll = rechargeRate;
                    }
#endif
                }
            }
            if (!connection.IsPower) { return; }

            if (connection.Name == "power_in")
            {
                rechargeVoltage = Math.Min(power, 1.0f);
            }
        }
    }
}
