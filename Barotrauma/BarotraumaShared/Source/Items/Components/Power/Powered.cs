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
        private static List<Powered> poweredList = new List<Powered>();

        //the amount of power CURRENTLY consumed by the item
        //negative values mean that the item is providing power to connected items
        protected float currPowerConsumption;

        //current voltage of the item (load / power)
        private float voltage;

        //the minimum voltage required for the item to work
        protected float minVoltage;

        //the maximum amount of power the item can draw from connected items
        protected float powerConsumption;

        [Editable, Serialize(0.5f, true, description: "The minimum voltage required for the device to function. " +
            "The voltage is calculated as power / powerconsumption, meaning that a device " +
            "with a power consumption of 1000 kW would need at least 500 kW of power to work if the minimum voltage is set to 0.5.")]
        public float MinVoltage
        {
            get { return minVoltage; }
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

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1.0f)
        {
            if (updatingPower) { return; }

            if (currPowerConsumption == 0.0f) { voltage = 0.0f; }
            if (connection.IsPower){ voltage = Math.Max(0.0f, power); }              
        }

        protected void UpdateOnActiveEffects(float deltaTime)
        {
            if (currPowerConsumption == 0.0f)
            {
                //if the item consumes no power, ignore the voltage requirement and
                //apply OnActive statuseffects as long as this component is active
                if (powerConsumption == 0.0f)
                {
                    ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                }
                return;
            }

#if CLIENT
            if (voltage > minVoltage)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                if (!powerOnSoundPlayed && powerOnSound != null)
                {
                    SoundPlayer.PlaySound(powerOnSound.Sound, item.WorldPosition, powerOnSound.Volume, powerOnSound.Range, item.CurrentHull);                    
                    powerOnSoundPlayed = true;
                }
            }
            else if (voltage < 0.1f)
            {
                powerOnSoundPlayed = false;
            }
#else
            if (voltage > minVoltage)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);
        }

        public static void UpdatePower()
        {
            //reset power first
            foreach (Powered powered in poweredList)
            {
                powered.updatingPower = true;
                powered.voltage = 0.0f;
                if (powered is PowerTransfer pt)
                {
                    powered.CurrPowerConsumption = 0.0f;
                    pt.PowerLoad = 0.0f;
                }
            }

            foreach (Powered powered in poweredList)
            {
                if (powered is PowerTransfer pt) { continue; }
                if (powered.currPowerConsumption > 0.0f)
                {
                    //consuming power
                    powered.item.SendSignal(0, "", "power_in", null, -powered.currPowerConsumption, source: powered.Item);
                    powered.item.SendSignal(0, "", "power", null, -powered.currPowerConsumption, source: powered.Item);
                }
                else if (powered.currPowerConsumption < 0.0f)
                {
                    //providing power
                    powered.item.SendSignal(0, "", "power_out", null, -powered.currPowerConsumption, source: powered.Item);
                    powered.item.SendSignal(0, "", "power", null, -powered.currPowerConsumption, source: powered.Item);
                }
                if (powered is PowerContainer pc)
                {
                    powered.item.SendSignal(0, "", "power_out", null, pc.CurrPowerOutput, source: powered.Item);
                    powered.item.SendSignal(0, "", "power", null, pc.CurrPowerOutput, source: powered.Item);
                }
            }
            foreach (Powered powered in poweredList)
            {
                powered.updatingPower = false;                
            }
            foreach (Powered powered in poweredList)
            {
                if (powered is PowerTransfer pt)
                {
                    float voltageOut = -pt.CurrPowerConsumption / Math.Max(pt.PowerLoad, 1.0f);                    
                    powered.item.SendSignal(0, "", "power_out", null, voltageOut, source: powered.Item);
                    powered.item.SendSignal(0, "", "power", null, voltageOut, source: powered.Item);
                }
                else if (powered is PowerContainer pc)
                {
                    //powered.item.SendSignal(0, "", "power_out", null, pc., source: powered.Item);
                }
            }
        }

        protected bool updatingPower;

        protected override void RemoveComponentSpecific()
        {
            poweredList.Remove(this);
        }
    }
}
