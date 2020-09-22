using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
#if CLIENT
using Barotrauma.Sounds;
#endif

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        private static float updateTimer;
        protected static float UpdateInterval = 0.2f;

        /// <summary>
        /// List of all powered ItemComponents
        /// </summary>
        private static readonly List<Powered> poweredList = new List<Powered>();
        public static IEnumerable<Powered> PoweredList
        {
            get { return poweredList; }
        }

        /// <summary>
        /// Items that have already received the "probe signal" that's used to distribute power and load across the grid
        /// </summary>
        protected static HashSet<PowerTransfer> lastPowerProbeRecipients = new HashSet<PowerTransfer>();

        /// <summary>
        /// The amount of power currently consumed by the item. Negative values mean that the item is providing power to connected items
        /// </summary>
        protected float currPowerConsumption;

        /// <summary>
        /// Current voltage of the item (load / power)
        /// </summary>
        private float voltage;

        /// <summary>
        /// The minimum voltage required for the item to work
        /// </summary>
        private float minVoltage;

        /// <summary>
        /// The maximum amount of power the item can draw from connected items
        /// </summary>
        protected float powerConsumption;

        protected Connection powerIn, powerOut;

        [Editable, Serialize(0.5f, true, description: "The minimum voltage required for the device to function. " +
            "The voltage is calculated as power / powerconsumption, meaning that a device " +
            "with a power consumption of 1000 kW would need at least 500 kW of power to work if the minimum voltage is set to 0.5.")]
        public float MinVoltage
        {
            get { return powerConsumption <= 0.0f ? 0.0f : minVoltage; }
            set { minVoltage = value; }
        }

        [Editable, Serialize(0.0f, true, description: "How much power the device draws (or attempts to draw) from the electrical grid when active.")]
        public float PowerConsumption
        {
            get { return powerConsumption; }
            set { powerConsumption = value; }
        }
        
        [Serialize(false, true, description: "Is the device currently active. Inactive devices don't consume power.")]
        public override bool IsActive
        {
            get { return base.IsActive; }
            set
            {
                base.IsActive = value;
                if (!value)
                {
                    currPowerConsumption = 0.0f;
                }
            }
        }

        [Serialize(0.0f, true, description: "The current power consumption of the device. Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public float CurrPowerConsumption
        {
            get {return currPowerConsumption; }
            set { currPowerConsumption = value; }
        }

        [Serialize(0.0f, true, description: "The current voltage of the item (calculated as power consumption / available power). Intended to be used by StatusEffect conditionals (setting the value from XML is not recommended).")]
        public float Voltage
        {
            get { return voltage; }
            set { voltage = Math.Max(0.0f, value); }
        }

        [Editable, Serialize(true, true, description: "Can the item be damaged by electomagnetic pulses.")]
        public bool VulnerableToEMP
        {
            get;
            set;
        }

        public Powered(Item item, XElement element)
            : base(item, element)
        {
            poweredList.Add(this);
            InitProjectSpecific(element);
        }

        partial void InitProjectSpecific(XElement element);

        protected void UpdateOnActiveEffects(float deltaTime)
        {
            if (currPowerConsumption <= 0.0f)
            {
                //if the item consumes no power, ignore the voltage requirement and
                //apply OnActive statuseffects as long as this component is active
                if (powerConsumption <= 0.0f)
                {
                    ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                }
                return;
            }

            if (voltage > minVoltage)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }
#if CLIENT
            if (voltage > minVoltage)
            {
                if (!powerOnSoundPlayed && powerOnSound != null)
                {
                    SoundPlayer.PlaySound(powerOnSound.Sound, item.WorldPosition, powerOnSound.Volume, powerOnSound.Range, hullGuess: item.CurrentHull);                    
                    powerOnSoundPlayed = true;
                }
            }
            else if (voltage < 0.1f)
            {
                powerOnSoundPlayed = false;
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            currPowerConsumption = powerConsumption;
            UpdateOnActiveEffects(deltaTime);
        }

        public override void OnItemLoaded()
        {
            if (item.Connections == null) { return; }
            foreach (Connection c in item.Connections)
            {
                if (!c.IsPower) { continue; }
                if (this is PowerTransfer pt)
                {
                    if (c.Name == "power_in")
                    {
                        powerIn = c;
                    }
                    else if (c.Name == "power_out")
                    {
                        powerOut = c;
                    }
                    else if (c.Name == "power")
                    {
                        powerIn = powerOut = c;
                    }
                }
                else
                {
                    if (c.IsOutput)
                    {
                        if (c.Name == "power_in")
                        {
#if DEBUG
                            DebugConsole.ThrowError($"Item \"{item.Name}\" has a power output connection called power_in. If the item is supposed to receive power through the connection, change it to an input connection.");
#else
                            DebugConsole.NewMessage($"Item \"{item.Name}\" has a power output connection called power_in. If the item is supposed to receive power through the connection, change it to an input connection.", Color.Orange);
#endif
                        }
                        powerOut = c;
                    }
                    else
                    {
                        if (c.Name == "power_out")
                        {
#if DEBUG
                            DebugConsole.ThrowError($"Item \"{item.Name}\" has a power input connection called power_out. If the item is supposed to output power through the connection, change it to an output connection.");
#else
                            DebugConsole.NewMessage($"Item \"{item.Name}\" has a power input connection called power_out. If the item is supposed to output power through the connection, change it to an output connection.", Color.Orange);
#endif
                        }
                        powerIn = c;
                    }
                }
            }
        }
        
        public virtual void ReceivePowerProbeSignal(Connection connection, Item source, float power) { }

        public static void UpdatePower(float deltaTime)
        {
            if (updateTimer > 0.0f)
            {
                updateTimer -= deltaTime;
                return;
            }
            updateTimer = UpdateInterval;

            //reset power first
            foreach (Powered powered in poweredList)
            {
                if (powered is PowerTransfer pt)
                {
                    powered.CurrPowerConsumption = 0.0f;
                    pt.PowerLoad = 0.0f;
                    if (pt is RelayComponent relay)
                    {
                        relay.DisplayLoad = 0.0f;
                    }
                }
                //only reset voltage if the item has a power connector
                //(other items, such as handheld devices, get power through other means and shouldn't be updated here)
                if (powered.powerIn != null || powered.powerOut != null) { powered.voltage = 0.0f; }                
            }

            //go through all the devices that are consuming/providing power
            //and send out a "probe signal" which the PowerTransfer components use to add up the grid power/load
            foreach (Powered powered in poweredList)
            {
                if (powered is PowerTransfer) { continue; }
                if (powered.currPowerConsumption > 0.0f)
                {
                    //consuming power
                    lastPowerProbeRecipients.Clear();
                    powered.powerIn?.SendPowerProbeSignal(powered.item, -powered.currPowerConsumption);
                }
            }
            foreach (Powered powered in poweredList)
            {
                if (powered is PowerTransfer) { continue; }
                else if (powered.currPowerConsumption < 0.0f)
                {
                    //providing power
                    lastPowerProbeRecipients.Clear();
                    powered.powerOut?.SendPowerProbeSignal(powered.item, -powered.currPowerConsumption);
                }
                if (powered is PowerContainer pc)
                {
                    if (pc.CurrPowerOutput <= 0.0f || pc.item.Condition <= 0.0f) { continue; }
                    //providing power
                    lastPowerProbeRecipients.Clear();
                    powered.powerOut?.SendPowerProbeSignal(powered.item, pc.CurrPowerOutput);
                }
            }
            //go through powered items and calculate their current voltage
            foreach (Powered powered in poweredList)
            {
                if (powered is PowerTransfer pt1 || (pt1 = powered.Item.GetComponent<PowerTransfer>()) != null) 
				{
                    powered.voltage = -pt1.CurrPowerConsumption / Math.Max(pt1.PowerLoad, 1.0f);
					continue; 
				}
                if (powered.powerConsumption <= 0.0f && !(powered is PowerContainer))
                {
                    powered.voltage = 1.0f;
                    continue;
                }
                if (powered.powerIn == null) { continue; }

                foreach (Connection powerSource in powered.powerIn.Recipients)
                {
                    if (!powerSource.IsPower || !powerSource.IsOutput) { continue; }
                    var pt = powerSource.Item.GetComponent<PowerTransfer>();
                    if (pt != null)
                    {
                        float voltage = -pt.CurrPowerConsumption / Math.Max(pt.PowerLoad, 1.0f);
                        powered.voltage = Math.Max(powered.voltage, voltage);
                        continue;
                    }
                    var pc = powerSource.Item.GetComponent<PowerContainer>();
                    if (pc != null && pc.item.Condition > 0.0f)
                    {
                        float voltage = pc.CurrPowerOutput / Math.Max(powered.CurrPowerConsumption, 1.0f);
                        powered.voltage += voltage;
                    }
                }
            }
        }

        /// <summary>
        /// Returns the amount of power that can be supplied by batteries directly connected to the item
        /// </summary>
        protected float GetAvailableBatteryPower()
        {
            var batteries = item.GetConnectedComponents<PowerContainer>();

            float availablePower = 0.0f;
            foreach (PowerContainer battery in batteries)
            {
                float batteryPower = Math.Min(battery.Charge * 3600.0f, battery.MaxOutPut);
                availablePower += batteryPower;
            }

            return availablePower;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            poweredList.Remove(this);
        }
    }
}
