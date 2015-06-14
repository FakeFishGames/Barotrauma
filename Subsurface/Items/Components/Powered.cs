using System;
using System.Globalization;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Powered : ItemComponent
    {
        //the amount of power CURRENTLY consumed by the item
        //negative values mean that the item is providing power to connected items
        protected float currPowerConsumption;

        //the amount of power available for the item through connected items
        protected float voltage;

        //the amount of power required for the item to work
        protected float minVoltage;

        //the maximum amount of power the item can draw from connected items
        protected float powerConsumption;

        [Editable, HasDefaultValue(0.5f, true)]
        public float MinVoltage
        {
            get { return minVoltage; }
            set { minVoltage = value; }
        }

        [Editable, HasDefaultValue(0.0f, true)]
        public float PowerConsumption
        {
            get { return powerConsumption; }
            set { powerConsumption = value; }
        }


        [HasDefaultValue(false,true)]
        public override bool IsActive
        {
            get { return isActive; }
            set 
            { 
                isActive = value;
                if (!isActive) currPowerConsumption = 0.0f;
            }
        }

        [HasDefaultValue(0.0f, true)]
        public float CurrPowerConsumption
        {
            get {return currPowerConsumption; }
            set { currPowerConsumption = value; }
        }

        [HasDefaultValue(0.0f, true)]
        public float Voltage
        {
            get { return voltage; }
            set { voltage = Math.Max(0.0f, value); }
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender)
        {
            if (connection.name=="power_in")
            {
                if (!float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out voltage))
                {
                    voltage = 0.0f;
                }
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (currPowerConsumption == 0.0f) return;
            if (voltage > minVoltage) ApplyStatusEffects(ActionType.OnActive, deltaTime);
        }

        public Powered(Item item, XElement element)
            : base(item, element)
        {
            //minVoltage = ToolBox.GetAttributeFloat(element, "minvoltage", 10.0f);
            //powerConsumption = ToolBox.GetAttributeFloat(element, "powerconsumption", 15.0f);
        }
    }
}
