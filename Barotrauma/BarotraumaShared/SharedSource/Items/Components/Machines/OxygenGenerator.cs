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
        private Dictionary<Vent, float> ventList;

        private float totalHullVolume;
        
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

            UpdateVents(CurrFlow);
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam);
            CurrFlow = 0.0f;
        }

        private void GetVents()
        {
            ventList = new Dictionary<Vent, float>();
            foreach (MapEntity entity in item.linkedTo)
            {
                if (!(entity is Item linkedItem)) { continue; }

                Vent vent = linkedItem.GetComponent<Vent>();
                if (vent?.Item.CurrentHull == null) { continue; }

                ventList.Add(vent, 0.0f);
                foreach (Hull connectedHull in vent.Item.CurrentHull.GetConnectedHulls(includingThis: true, searchDepth: 10, ignoreClosedGaps: true))
                { 
                    totalHullVolume += connectedHull.Volume;
                    ventList[vent] += connectedHull.Volume;
                }
            }
        }
        
        private void UpdateVents(float deltaOxygen)
        {
            if (ventList == null)
            {
                GetVents();
            }

            if (!ventList.Any() || totalHullVolume <= 0.0f) { return; }

            foreach (KeyValuePair<Vent, float> v in ventList)
            {
                if (v.Key?.Item.CurrentHull == null) { continue; }

                v.Key.OxygenFlow = deltaOxygen * (v.Value / totalHullVolume);
                v.Key.IsActive = true;
            }
        }
    }
}
