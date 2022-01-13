using Barotrauma.Networking;
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

        //private float rechargeVoltage;

        //how fast the battery can be recharged
        private float maxRechargeSpeed;

        //how fast it's currently being recharged (can be changed, so that
        //charging can be slowed down or disabled if there's a shortage of power)
        private float rechargeSpeed;
        private float lastSentCharge;

        //charge indicator description
        protected Vector2 indicatorPosition, indicatorSize;

        protected bool isHorizontal;
        
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
                    if (GameMain.Server != null && (!item.Submarine?.Loading ?? true)) { item.CreateServerEvent(this); }
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

        [Serialize(false, true, description: "If true, the recharge speed (and power consumption) of the device goes up exponentially as the recharge rate is increased.")]
        public bool ExponentialRechargeSpeed { get; set; }

        private float efficiency;
        [Editable(minValue: 0.0f, maxValue: 1.0f, decimals: 2), Serialize(0.95f, true, description: "The amount of power you can get out of a item relative to the amount of power that's put into it.")]
        public float Efficiency
        {
            get { return efficiency; }
            set { efficiency = MathHelper.Clamp(value, 0.0f, 1.0f); }
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
            if (item.Connections == null) 
            {
                IsActive = false;
                return; 
            }

            isRunning = true;
            float chargeRatio = charge / capacity;
            float gridPower = 0.0f;
            float gridLoad = 0.0f;
            foreach (Connection c in item.Connections)
            {
                if (!c.IsPower || !c.IsOutput) { continue; }
                foreach (Connection c2 in c.Recipients)
                {
                    if (c2.Item.Condition <= 0.0f) { continue; }

                    PowerTransfer pt = c2.Item.GetComponent<PowerTransfer>();
                    if (pt == null)
                    {
                        foreach (Powered powered in c2.Item.GetComponents<Powered>())
                        {
                            if (!powered.IsActive) continue;
                            gridLoad += powered.CurrPowerConsumption;
                        }
                        continue;
                    }
                    if (!pt.IsActive || !pt.CanTransfer) { continue; }
                    gridPower -= pt.CurrPowerConsumption;
                    gridLoad += pt.PowerLoad;
                }
            }
            
            if (chargeRatio > 0.0f)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }

            if (charge >= capacity)
            {
                //rechargeVoltage = 0.0f;
                charge = capacity;
                CurrPowerConsumption = 0.0f;
            }
            else
            {
                float missingCharge = capacity - charge;
                float targetRechargeSpeed = rechargeSpeed;
                if (ExponentialRechargeSpeed)
                {
                    targetRechargeSpeed = MathF.Pow(rechargeSpeed / maxRechargeSpeed, 2) * maxRechargeSpeed;
                }
                if (missingCharge < 1.0f)
                {
                    targetRechargeSpeed *= missingCharge;
                }
                currPowerConsumption = MathHelper.Lerp(currPowerConsumption, targetRechargeSpeed, 0.05f);
                Charge += currPowerConsumption * Math.Min(Voltage, 1.0f) / 3600.0f * efficiency;
            }

            if (charge <= 0.0f)
            {
                CurrPowerOutput = 0.0f;
                charge = 0.0f;
                return;
            }
            else
            {
                //output starts dropping when the charge is less than 10%
                float maxOutputRatio = 1.0f;
                if (chargeRatio < 0.1f)
                {
                    maxOutputRatio = Math.Max(chargeRatio * 10.0f, 0.0f);
                }

                CurrPowerOutput += (gridLoad - gridPower) * deltaTime;

                float maxOutput = Math.Min(MaxOutPut * maxOutputRatio, gridLoad);
                CurrPowerOutput = MathHelper.Clamp(CurrPowerOutput, 0.0f, maxOutput);
                Charge -= CurrPowerOutput / 3600.0f;            
            }

            item.SendSignal(((int)Math.Round(Charge)).ToString(), "charge");
            item.SendSignal(((int)Math.Round(Charge / capacity * 100)).ToString(), "charge_%");
            item.SendSignal(((int)Math.Round(RechargeSpeed / maxRechargeSpeed * 100)).ToString(), "charge_rate");
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }

            if (objective.Override)
            {
                HasBeenTuned = false;
            }
            if (HasBeenTuned) { return true; }

            float targetRatio = string.IsNullOrEmpty(objective.Option) || objective.Option.Equals("charge", StringComparison.OrdinalIgnoreCase) ? aiRechargeTargetRatio : -1;
            if (targetRatio > 0 || float.TryParse(objective.Option, out targetRatio))
            {
                if (Math.Abs(rechargeSpeed - maxRechargeSpeed * targetRatio) > 0.05f)
                {
#if SERVER
                    item.CreateServerEvent(this);
#endif
                    RechargeSpeed = maxRechargeSpeed * targetRatio;
#if CLIENT
                    if (rechargeSpeedSlider != null)
                    {
                        rechargeSpeedSlider.BarScroll = RechargeSpeed / Math.Max(maxRechargeSpeed, 1.0f);
                    }
#endif                   
                    if (character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.GetWithVariables("DialogChargeBatteries", new string[2] { "[itemname]", "[rate]" },
                            new string[2] { item.Name, ((int)(rechargeSpeed / maxRechargeSpeed * 100.0f)).ToString() },
                            new bool[2] { true, false }), null, 1.0f, "chargebattery", 10.0f);
                    }
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
                    if (character.IsOnPlayerTeam)
                    {
                        character.Speak(TextManager.GetWithVariables("DialogStopChargingBatteries", new string[2] { "[itemname]", "[rate]" },
                            new string[2] { item.Name, ((int)(rechargeSpeed / maxRechargeSpeed * 100.0f)).ToString() },
                            new bool[2] { true, false }), null, 1.0f, "chargebattery", 10.0f);
                    }
                }
            }

            return true;
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            if (connection.IsPower) { return; }

            if (connection.Name == "set_rate")
            {
                if (float.TryParse(signal.value, NumberStyles.Any, CultureInfo.InvariantCulture, out float tempSpeed))
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
        }
    }
}
