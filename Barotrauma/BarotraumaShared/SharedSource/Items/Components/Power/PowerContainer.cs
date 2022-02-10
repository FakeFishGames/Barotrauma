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
            
            if (chargeRatio > 0.0f)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }

            float loadReading = 0;
            if (powerOut != null && powerOut.Grid != null)
            {
                loadReading = powerOut.Grid.Load;
            }

            item.SendSignal(((int)Math.Round(-CurrPowerOutput)).ToString(), "power_value_out");
            item.SendSignal(((int)Math.Round(loadReading)).ToString(), "load_value_out");
            item.SendSignal(((int)Math.Round(Charge)).ToString(), "charge");
            item.SendSignal(((int)Math.Round(Charge / capacity * 100)).ToString(), "charge_%");
            item.SendSignal(((int)Math.Round(RechargeSpeed / maxRechargeSpeed * 100)).ToString(), "charge_rate");
        }

        /// <summary>
        /// Container power draw and flag that output can provide power.
        /// Power consumption is proportional to set recharge speed and if there is
        /// less than max charge.
        /// </summary>
        /// <param name="conn"></param>
        /// <returns></returns>
        public override float ConnCurrConsumption(Connection conn = null)
        {
            if (conn == this.powerIn)
            {
                //Don't draw power if fully charged
                if (charge >= capacity)
                {
                    charge = capacity;
                    return 0;
                }
                else
                {
                    float missingCharge = capacity - charge;
                    float targetRechargeSpeed = rechargeSpeed;

                    //For the last kwMin scale the recharge rate linearly to prevent over charging and have a smooth cutoff
                    if (missingCharge < 1.0f)
                    {
                        targetRechargeSpeed *= missingCharge;
                    }

                    return MathHelper.Clamp(targetRechargeSpeed, 0, MaxRechargeSpeed);
                }
            }
            else
            {
                //Reset PowerOut
                CurrPowerOutput = 0;

                // Flag that power can be provided if their is any charge
                return charge > 0 ? -1 : 0;
            }

        }

        /// <summary>
        /// Minimum and maximum output for the queried pin.
        /// Powerin min max equals CurrPowerConsumption as its abnormal for there to be power out.
        /// PowerOut min power out is zero and max is the maxout unless below 10% charge where
        /// the output is scaled relative to the 10% charge.
        /// </summary>
        /// <param name="conn">Connection being queried</param>
        /// <param name="load">Current grid load</param>
        /// <returns>Minimum and maximum power output for the pin</returns>
        public override Vector3 MinMaxPowerOut(Connection conn, float load = 0)
        {
            Vector3 minMaxPower = new Vector3();

            if (conn == powerIn)
            {
                minMaxPower.X = CurrPowerConsumption;
                minMaxPower.Y = CurrPowerConsumption;
            }
            else
            {
                float chargeRatio = charge / capacity;
                if (chargeRatio < 0.1f)
                {
                    minMaxPower.Y = Math.Max(chargeRatio * 10.0f, 0.0f) * -MaxOutPut;
                }
                else
                {
                    minMaxPower.Y = -MaxOutPut;
                }

                //Limit max power out to not exceed the charge of the container
                minMaxPower.Y = Math.Max(minMaxPower.Y, -charge * 60 / UpdateInterval);
            }

            return minMaxPower;
        }

        /// <summary>
        /// Finalized power out from the container for the pin, provided the given grid information
        /// Output power based on the maxpower all batteries can output. So all batteries can
        /// equally share powerout based on their output capabilities.
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="power"></param>
        /// <param name="minMaxPower"></param>
        /// <param name="load"></param>
        /// <returns></returns>
        public override float ConnPowerOut(Connection conn, float power, Vector3 minMaxPower, float load)
        {
            if (conn == powerOut)
            {
                //Calculate the max power the container can output
                float maxPowerOutput = -MaxOutPut;
                float chargeRatio = charge / capacity;
                if (chargeRatio < 0.1f)
                {
                    maxPowerOutput *= Math.Max(chargeRatio * 10.0f, 0.0f);
                }

                //Set power output based on the relative max power output capabilities and load demand
                CurrPowerOutput = MathHelper.Clamp((load - power) / minMaxPower.Y, 0, 1) * maxPowerOutput;
                return CurrPowerOutput;
            }
            else
            {
                //If powerin pin just output the CurrPowerConsumption
                return CurrPowerConsumption;
            }
        }

        /// <summary>
        /// When the corresponding grid pin is resolved adjust the container's charge.
        /// </summary>
        /// <param name="conn"></param>
        public override void GridResolved(Connection conn)
        {
            if (conn == powerIn)
            {
                //Increase charge based on how much power came in from the grid
                Charge += (CurrPowerConsumption * Voltage) / 60 * UpdateInterval;
            }
            else
            {
                //Decrease charge based on how much power is leaving the device
                Charge = Math.Clamp(Charge + CurrPowerOutput / 60 * UpdateInterval, 0, Capacity);
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
