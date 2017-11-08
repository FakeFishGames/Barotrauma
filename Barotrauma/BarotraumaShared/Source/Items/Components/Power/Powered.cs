using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        //the amount of power CURRENTLY consumed by the item
        //negative values mean that the item is providing power to connected items
        protected float currPowerConsumption;

        //current voltage of the item (load / power)
        protected float voltage;

        //the minimum voltage required for the item to work
        protected float minVoltage;

        //the maximum amount of power the item can draw from connected items
        protected float powerConsumption;

        [Editable, Serialize(0.5f, true)]
        public float MinVoltage
        {
            get { return minVoltage; }
            set { minVoltage = value; }
        }

        [Editable, Serialize(0.0f, true)]
        public float PowerConsumption
        {
            get { return powerConsumption; }
            set { powerConsumption = value; }
        }


        [Serialize(false,true)]
        public override bool IsActive
        {
            get { return base.IsActive; }
            set 
            { 
                base.IsActive = value;
                if (!value) currPowerConsumption = 0.0f;
            }
        }

        [Serialize(0.0f, true)]
        public float CurrPowerConsumption
        {
            get {return currPowerConsumption; }
            set { currPowerConsumption = value; }
        }

        [Serialize(0.0f, true)]
        public float Voltage
        {
            get { return voltage; }
            set { voltage = Math.Max(0.0f, value); }
        }

        public Powered(Item item, XElement element)
            : base(item, element)
        {
#if CLIENT
            if (powerOnSound == null)
            {
                powerOnSound = Sound.Load("Content/Items/Electricity/powerOn.ogg", false);
            }

            if (sparkSounds == null)
            {
                sparkSounds = new Sound[4];
                for (int i = 0; i < 4; i++)
                {
                    sparkSounds[i] = Sound.Load("Content/Items/Electricity/zap" + (i + 1) + ".ogg", false);
                }
            }
#endif
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0)
        {
            if (currPowerConsumption == 0.0f) voltage = 0.0f;
            if (connection.IsPower) voltage = power;                
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (currPowerConsumption == 0.0f) return;

#if CLIENT
            if (voltage > minVoltage)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                if (!powerOnSoundPlayed)
                {
                    powerOnSound.Play(1.0f, 600.0f, item.WorldPosition);
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


    }
}
