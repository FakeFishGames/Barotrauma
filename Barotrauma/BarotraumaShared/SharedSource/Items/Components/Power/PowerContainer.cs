using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Globalization;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        //[power/min]        
        private float capacity;

        private float charge, prevCharge;

        //how fast the battery can be recharged
        private float maxRechargeSpeed;

        //how fast it's currently being recharged (can be changed, so that
        //charging can be slowed down or disabled if there's a shortage of power)
        private float rechargeSpeed;
        private float lastSentCharge;

        //charge indicator description
        protected Vector2 indicatorPosition, indicatorSize;

        protected bool isHorizontal;

        protected override PowerPriority Priority { get { return PowerPriority.Battery; } }

        private float currPowerOutput;
        public float CurrPowerOutput
        {
            get { return currPowerOutput; }
            private set
            {
                System.Diagnostics.Debug.Assert(value >= 0.0f, $"Tried to set PowerContainer's output to a negative value ({value})");
                currPowerOutput = Math.Max(0, value);
            }
        }

        [Serialize("0,0", IsPropertySaveable.Yes, description: "The position of the progress bar indicating the charge of the item. In pixels as an offset from the upper left corner of the sprite.")]
        public Vector2 IndicatorPosition
        {
            get { return indicatorPosition; }
            set { indicatorPosition = value; }
        }

        [Serialize("0,0", IsPropertySaveable.Yes, description: "The size of the progress bar indicating the charge of the item (in pixels).")]
        public Vector2 IndicatorSize
        {
            get { return indicatorSize; }
            set { indicatorSize = value; }
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the progress bar indicating the charge of the item fill up horizontally or vertically.")]
        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set { isHorizontal = value; }
        }

        [Editable, Serialize(10.0f, IsPropertySaveable.Yes, description: "Maximum output of the device when fully charged (kW).")]
        public float MaxOutPut { set; get; }

        [Editable, Serialize(10.0f, IsPropertySaveable.Yes, description: "The maximum capacity of the device (kW * min). For example, a value of 1000 means the device can output 100 kilowatts of power for 10 minutes, or 1000 kilowatts for 1 minute.")]
        public float Capacity
        {
            get { return capacity; }
            set { capacity = Math.Max(value, 1.0f); }
        }

        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "The current charge of the device.")]
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
        
        [Editable, Serialize(10.0f, IsPropertySaveable.Yes, description: "How fast the device can be recharged. For example, a recharge speed of 100 kW and a capacity of 1000 kW*min would mean it takes 10 minutes to fully charge the device.")]
        public float MaxRechargeSpeed
        {
            get { return maxRechargeSpeed; }
            set { maxRechargeSpeed = Math.Max(value, 1.0f); }
        }

        [Editable, Serialize(0.0f, IsPropertySaveable.Yes, description: "The current recharge speed of the device.")]
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

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, the recharge speed (and power consumption) of the device goes up exponentially as the recharge rate is increased.")]
        public bool ExponentialRechargeSpeed { get; set; }

        private float efficiency;
        [Editable(minValue: 0.0f, maxValue: 1.0f, decimals: 2), Serialize(0.95f, IsPropertySaveable.Yes, description: "The amount of power you can get out of a item relative to the amount of power that's put into it.")]
        public float Efficiency
        {
            get { return efficiency; }
            set { efficiency = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        private bool flipIndicator;
        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "Should the progress bar indicating the charge be flipped to fill from the other side.")]
        public bool FlipIndicator
        {
            get { return flipIndicator; }
            set { flipIndicator = value; }
        }

        public float RechargeRatio => RechargeSpeed / MaxRechargeSpeed;

        public const float aiRechargeTargetRatio = 0.5f;
        private bool isRunning;
        public bool HasBeenTuned { get; private set; }

        public PowerContainer(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific();
            prevCharge = Charge;
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
            
            if (chargeRatio > 0.0f)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }

            float loadReading = 0;
            if (powerOut != null && powerOut.Grid != null)
            {
                loadReading = powerOut.Grid.Load;
            }

            item.SendSignal(((int)Math.Round(CurrPowerOutput)).ToString(), "power_value_out");
            item.SendSignal(((int)Math.Round(loadReading)).ToString(), "load_value_out");
            item.SendSignal(((int)Math.Round(Charge)).ToString(), "charge");
            item.SendSignal(((int)Math.Round(Charge / capacity * 100)).ToString(), "charge_%");
            item.SendSignal(((int)Math.Round(RechargeSpeed / maxRechargeSpeed * 100)).ToString(), "charge_rate");
        }

        /// <summary>
        /// Returns the power consumption if checking the powerIn connection, or a negative value if the output can provide power when checking powerOut.
        /// Power consumption is proportional to set recharge speed and if there is less than max charge.
        /// </summary>
        public override float GetCurrentPowerConsumption(Connection connection = null)
        {
            if (connection == powerIn)
            {
                //Don't draw power if fully charged
                if (charge >= capacity)
                {
                    charge = capacity;
                    return 0;
                }
                else
                {
                    if (item.Condition <= 0.0f) { return 0.0f; }

                    float missingCharge = capacity - charge;
                    float targetRechargeSpeed = rechargeSpeed;

                    if (ExponentialRechargeSpeed)
                    {
                        targetRechargeSpeed = MathF.Pow(rechargeSpeed / maxRechargeSpeed, 2) * maxRechargeSpeed;
                    }
                    //For the last kwMin scale the recharge rate linearly to prevent overcharging and to have a smooth cutoff
                    if (missingCharge < 1.0f)
                    {
                        targetRechargeSpeed *= missingCharge;
                    }

                    return MathHelper.Clamp(targetRechargeSpeed, 0, MaxRechargeSpeed);
                }
            }
            else
            {
                CurrPowerOutput = 0;
                return charge > 0 ? -1 : 0;
            }
        }

        /// <summary>
        /// Minimum and maximum output for the queried connection.
        /// Powerin min max equals CurrPowerConsumption as its abnormal for there to be power out.
        /// PowerOut min power out is zero and max is the maxout unless below 10% charge where
        /// the output is scaled relative to the 10% charge.
        /// </summary>
        /// <param name="connection">Connection being queried</param>
        /// <param name="load">Current grid load</param>
        /// <returns>Minimum and maximum power output for the connection</returns>
        public override PowerRange MinMaxPowerOut(Connection connection, float load = 0)
        {
            if (connection == powerOut)
            {
                float maxOutput;
                float chargeRatio = prevCharge / capacity;
                if (chargeRatio < 0.1f)
                {
                    maxOutput = Math.Max(chargeRatio * 10.0f, 0.0f) * MaxOutPut;
                }
                else
                {
                    maxOutput = MaxOutPut;
                }

                //Limit max power out to not exceed the charge of the container
                maxOutput = Math.Min(maxOutput, prevCharge * 60 / UpdateInterval);
                return new PowerRange(0.0f, maxOutput);
            }

            return PowerRange.Zero;
        }

        /// <summary>
        /// Finalized power out from the container for the connection, provided the given grid information
        /// Output power based on the maxpower all batteries can output. So all batteries can
        /// equally share powerout based on their output capabilities.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="power"></param>
        /// <param name="minMaxPower"></param>
        /// <param name="load"></param>
        /// <returns></returns>
        public override float GetConnectionPowerOut(Connection connection, float power, PowerRange minMaxPower, float load)
        {
            //Only power out connection can provide power and Max poweroutput can't be negative
            if (connection == powerOut && minMaxPower.Max > 0)
            {
                //Set power output based on the relative max power output capabilities and load demand
                CurrPowerOutput = MathHelper.Clamp((load - power) / minMaxPower.Max, 0, 1) * MinMaxPowerOut(connection, load).Max;
                return CurrPowerOutput;
            }
            return 0.0f;            
        }

        /// <summary>
        /// When the corresponding grid connection is resolved, adjust the container's charge.
        /// </summary>
        public override void GridResolved(Connection conn)
        {
            if (conn == powerIn)
            {
                //Increase charge based on how much power came in from the grid
                Charge += (CurrPowerConsumption * Voltage) / 60 * UpdateInterval * efficiency;
            }
            else
            {
                //Decrease charge based on how much power is leaving the device
                Charge = Math.Clamp(Charge - CurrPowerOutput / 60 * UpdateInterval, 0, Capacity);
                prevCharge = Charge;
            }
        }

        public override bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }

            if (objective.Override)
            {
                HasBeenTuned = false;
            }
            if (HasBeenTuned) { return true; }

            float targetRatio = objective.Option.IsEmpty || objective.Option == "charge" ? aiRechargeTargetRatio : -1;
            if (targetRatio > 0 || float.TryParse(objective.Option.Value, out targetRatio))
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
                        character.Speak(TextManager.GetWithVariables("DialogChargeBatteries",
                            ("[itemname]", item.Name, FormatCapitals.Yes),
                            ("[rate]", ((int)(rechargeSpeed / maxRechargeSpeed * 100.0f)).ToString(), FormatCapitals.No)).Value,
                            null, 1.0f, "chargebattery".ToIdentifier(), 10.0f);
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
                        character.Speak(TextManager.GetWithVariables("DialogStopChargingBatteries",
                            ("[itemname]", item.Name, FormatCapitals.Yes),
                            ("[rate]", ((int)(rechargeSpeed / maxRechargeSpeed * 100.0f)).ToString(), FormatCapitals.No)).Value,
                            null, 1.0f, "chargebattery".ToIdentifier(), 10.0f);
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
