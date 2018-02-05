using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MiniMap : Powered
    {
        class HullData
        {
            public float? Oxygen;
            public float? Water;
        }
        
        private DateTime resetDataTime;

        bool hasPower;

        [Editable(ToolTip = "Does the machine require inputs from water detectors in order to show the water levels inside rooms."), Serialize(false, true)]
        public bool RequireWaterDetectors
        {
            get;
            set;
        }

        [Editable(ToolTip = "Does the machine require inputs from oxygen detectors in order to show the oxygen levels inside rooms."), Serialize(true, true)]
        public bool RequireOxygenDetectors
        {
            get;
            set;
        }

        [Editable(ToolTip = "Should damaged walls be displayed by the machine."), Serialize(false, true)]
        public bool ShowHullIntegrity
        {
            get;
            set;
        }


        private Dictionary<Hull, HullData> hullDatas;

        public MiniMap(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            hullDatas = new Dictionary<Hull, HullData>();
        }
        
        public override void Update(float deltaTime, Camera cam) 
        {
            //periodically reset all hull data
            //(so that outdated hull info won't be shown if detectors stop sending signals)
            if (DateTime.Now > resetDataTime)
            {
                hullDatas.Clear();
                resetDataTime = DateTime.Now + new TimeSpan(0, 0, 1);
            }

            currPowerConsumption = powerConsumption;
            
            if (voltage > minVoltage)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }

            voltage = 0.0f;
        }
        
        public override bool Pick(Character picker)
        {
            if (picker == null) return false;

            //picker.SelectedConstruction = item;

            return true;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0)
        {
            base.ReceiveSignal(stepsTaken, signal, connection, source, sender, power);

            if (sender == null || sender.CurrentHull == null) return;

            Hull senderHull = sender.CurrentHull;

            HullData hullData;
            if (!hullDatas.TryGetValue(senderHull, out hullData))
            {
                hullData = new HullData();
                hullDatas.Add(senderHull, hullData);
            }

            switch (connection.Name)
            {
                case "water_data_in":
                    //cheating a bit because water detectors don't actually send the water level
                    if (source.GetComponent<WaterDetector>() == null)
                    {
                        hullData.Water = Rand.Range(0.0f, 1.0f);
                    }
                    else
                    {
                        hullData.Water = Math.Min(senderHull.WaterVolume / senderHull.Volume, 1.0f);
                    }
                    break;
                case "oxygen_data_in":
                    float oxy;

                    if (!float.TryParse(signal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out oxy))
                    {
                        oxy = Rand.Range(0.0f, 100.0f);
                    }

                    hullData.Oxygen = oxy;
                    break;
            }
        }

    }
}
