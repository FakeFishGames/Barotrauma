using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenGenerator : Powered
    {
        private float powerDownTimer;
        
        private float generatedAmount;

        private List<Vent> ventList;

        private float totalHullVolume;
        
        public float CurrFlow
        {
            get;
            private set;
        }

        [Editable, Serialize(400.0f, true, description: "How much oxygen the machine generates when operating at full power.")]
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
            currPowerConsumption *= MathHelper.Lerp(2.0f, 1.0f, item.Condition / item.MaxCondition);

            if (powerConsumption <= 0.0f)
            {
                Voltage = 1.0f;
            }

            if (item.CurrentHull == null) return;

            if (Voltage < minVoltage)
            {
                powerDownTimer += deltaTime;
                return;
            }
            else
            {
                powerDownTimer = 0.0f;
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
            powerDownTimer += deltaTime;
            CurrFlow = 0.0f;
        }

        private void GetVents()
        {
            ventList.Clear();

            foreach (MapEntity entity in item.linkedTo)
            {
                Item linkedItem = entity as Item;
                if (linkedItem == null) continue;

                Vent vent = linkedItem.GetComponent<Vent>();
                if (vent == null) continue;

                ventList.Add(vent);
                if (linkedItem.CurrentHull != null) totalHullVolume += linkedItem.CurrentHull.Volume;
            }
        }
        
        private void UpdateVents(float deltaOxygen)
        {
            if (ventList == null)
            {
                ventList = new List<Vent>();
                GetVents();
            }

            if (!ventList.Any() || totalHullVolume <= 0.0f) return;

            foreach (Vent v in ventList)
            {
                if (v.Item.CurrentHull == null) continue;

                v.OxygenFlow = deltaOxygen * (v.Item.CurrentHull.Volume / totalHullVolume);
                v.IsActive = true;
            }
        }
    }
}
