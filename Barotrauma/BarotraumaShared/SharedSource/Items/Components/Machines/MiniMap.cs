using Microsoft.Xna.Framework;
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

            public bool Distort;
            public float DistortionTimer;

            public List<Hull> LinkedHulls = new List<Hull>();
        }

        private DateTime resetDataTime;

        private bool hasPower;

        private readonly Dictionary<Hull, HullData> hullDatas;

        [Editable, Serialize(false, true, description: "Does the machine require inputs from water detectors in order to show the water levels inside rooms.")]
        public bool RequireWaterDetectors
        {
            get;
            set;
        }

        [Editable, Serialize(true, true, description: "Does the machine require inputs from oxygen detectors in order to show the oxygen levels inside rooms.")]
        public bool RequireOxygenDetectors
        {
            get;
            set;
        }

        [Editable, Serialize(true, true, description: "Should damaged walls be displayed by the machine.")]
        public bool ShowHullIntegrity
        {
            get;
            set;
        }

        public MiniMap(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            hullDatas = new Dictionary<Hull, HullData>();
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Update(float deltaTime, Camera cam) 
        {
            //periodically reset all hull data
            //(so that outdated hull info won't be shown if detectors stop sending signals)
            if (DateTime.Now > resetDataTime)
            {
                foreach (HullData hullData in hullDatas.Values)
                {
                    if (!hullData.Distort)
                    {
                        hullData.Oxygen = null;
                        hullData.Water = null;
                    }
                }
                resetDataTime = DateTime.Now + new TimeSpan(0, 0, 1);
            }

            currPowerConsumption = powerConsumption;
            currPowerConsumption *= MathHelper.Lerp(2.0f, 1.0f, item.Condition / item.MaxCondition);

            hasPower = Voltage > MinVoltage;
            if (hasPower)
            {
                ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
            }
        }
        
        public override bool Pick(Character picker)
        {
            return picker != null;
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1.0f)
        {
            if (source == null || source.CurrentHull == null) { return; }

            Hull sourceHull = source.CurrentHull;
            if (!hullDatas.TryGetValue(sourceHull, out HullData hullData))
            {
                hullData = new HullData();
                hullDatas.Add(sourceHull, hullData);
            }

            if (hullData.Distort) return;

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
                        hullData.Water = Math.Min(sourceHull.WaterVolume / sourceHull.Volume, 1.0f);
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
