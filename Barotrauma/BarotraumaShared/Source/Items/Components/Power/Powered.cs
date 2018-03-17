using System;
using System.Xml.Linq;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;

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

        [Serialize(0.5f, true), Editable(ToolTip = "The minimum voltage required for the device to function. "+
            "The voltage is calculated as power / powerconsumption, meaning that a device "+
            "with a power consumption of 1000 kW would need at least 500 kW of power to work if the minimum voltage is set to 0.5.")]
        public float MinVoltage
        {
            get { return minVoltage; }
            set { minVoltage = value; }
        }

        [Editable(ToolTip = "How much power the device draws (or attempts to draw) from the electrical grid."), Serialize(0.0f, true)]
        public float PowerConsumption
        {
            get { return powerConsumption; }
            set { powerConsumption = value; }
        }


        [Serialize(false, true)]
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
                powerOnSound = Submarine.LoadRoundSound("Content/Items/Electricity/powerOn.ogg", false);
            }

            if (sparkSounds == null)
            {
                sparkSounds = new Sound[4];
                for (int i = 0; i < 4; i++)
                {
                    sparkSounds[i] = Submarine.LoadRoundSound("Content/Items/Electricity/zap" + (i + 1) + ".ogg", false);
                }
            }
#endif
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0)
        {
            if (currPowerConsumption == 0.0f) voltage = 0.0f;
            if (connection.IsPower) voltage = power;                
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
                if (!powerOnSoundPlayed)
                {
                    if (Vector3.DistanceSquared(GameMain.SoundManager.ListenerPosition, new Vector3(item.WorldPosition.X, item.WorldPosition.Y, 0.0f)) < 360000.0f)
                    {
                        powerOnSound.Play(1.0f, 600.0f, item.WorldPosition);
                    }
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

            voltage = 0.0f;
        }


    }
}
