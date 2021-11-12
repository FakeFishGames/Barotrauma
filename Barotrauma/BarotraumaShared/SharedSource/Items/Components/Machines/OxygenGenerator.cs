using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenGenerator : Powered
    {
        private float generatedAmount;

        //key = vent, float = total volume of the hull the vent is in and the hulls connected to it
        private List<(Vent vent, float hullVolume)> ventList;

        private float totalHullVolume;

        private float ventUpdateTimer;
        const float VentUpdateInterval = 5.0f;
        
        public float CurrFlow
        {
            get;
            private set;
        }

        [Editable, Serialize(400.0f, true, description: "How much oxygen the machine generates when operating at full power.", alwaysUseInstanceValues: true)]
        public float GeneratedAmount
        {
            get { return generatedAmount; }
            set { generatedAmount = MathHelper.Clamp(value, -10000.0f, 10000.0f); }
        }

        public OxygenGenerator(Item item, XElement element)
            : base(item, element)
        {
            //randomize update timer so all oxygen generators don't update at the same time
            ventUpdateTimer = Rand.Range(0.0f, VentUpdateInterval);
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateOnActiveEffects(deltaTime);

            CurrFlow = 0.0f;
            currPowerConsumption = powerConsumption;
            //consume more power when in a bad condition
            item.GetComponent<Repairable>()?.AdjustPowerConsumption(ref currPowerConsumption);

            if (powerConsumption <= 0.0f)
            {
                Voltage = 1.0f;
            }

            if (item.CurrentHull == null) { return; }

            if (Voltage < MinVoltage)
            {
                return;
            }
            
            CurrFlow = Math.Min(Voltage, 1.0f) * generatedAmount * 100.0f;

            //less effective when in bad condition
            float conditionMult = item.Condition / item.MaxCondition;
            //100% condition = 100% oxygen
            //50% condition = 25% oxygen
            //20% condition = 4%
            CurrFlow *= conditionMult * conditionMult;

            UpdateVents(CurrFlow, deltaTime);
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            CurrFlow = 0.0f;
        }

        private void GetVents()
        {
            totalHullVolume = 0.0f;
            ventList ??= new List<(Vent vent, float hullVolume)>();
            ventList.Clear();
            foreach (MapEntity entity in item.linkedTo)
            {
                if (!(entity is Item linkedItem)) { continue; }

                Vent vent = linkedItem.GetComponent<Vent>();
                if (vent?.Item.CurrentHull == null) { continue; }

                totalHullVolume += vent.Item.CurrentHull.Volume;
                ventList.Add((vent, vent.Item.CurrentHull.Volume));
            }

            for (int i = 0; i < ventList.Count; i++)
            {
                Vent vent = ventList[i].vent;
                foreach (Hull connectedHull in vent.Item.CurrentHull.GetConnectedHulls(includingThis: false, searchDepth: 3, ignoreClosedGaps: true))
                {
                    //another vent in the connected hull -> don't add it to this vent's total hull volume
                    if (ventList.Any(v => v.vent != vent && v.vent.Item.CurrentHull == connectedHull)) { continue; }
                    totalHullVolume += connectedHull.Volume;
                    ventList[i] = (ventList[i].vent, ventList[i].hullVolume + connectedHull.Volume);
                }
            }
        }

        private void UpdateVents(float deltaOxygen, float deltaTime)
        {
            if (ventList == null || ventUpdateTimer < 0.0f)
            {
                GetVents();
                ventUpdateTimer = VentUpdateInterval;
            }
            ventUpdateTimer -= deltaTime;

            if (!ventList.Any() || totalHullVolume <= 0.0f) { return; }

            foreach ((Vent vent, float hullVolume) in ventList)
            {
                if (vent.Item.CurrentHull == null) { continue; }

                vent.OxygenFlow = deltaOxygen * (hullVolume / totalHullVolume);
                vent.IsActive = true;
            }
        }
    }
}
