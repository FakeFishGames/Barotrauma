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

        [Editable(ToolTip = "How much oxygen the machine generates when operating at full power."), Serialize(100.0f, true)]
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
            currPowerConsumption *= MathHelper.Lerp(2.0f, 1.0f, item.Condition / 100.0f);

            if (powerConsumption <= 0.0f)
            {
                voltage = 1.0f;
            }

            if (item.CurrentHull == null) return;

            if (voltage < minVoltage)
            {
                powerDownTimer += deltaTime;
                return;
            }
            else
            {
                powerDownTimer = 0.0f;
            }
            
            CurrFlow = Math.Min(voltage, 1.0f) * generatedAmount * 100.0f;
            //less effective when in bad condition
            CurrFlow *= MathHelper.Lerp(0.5f, 1.0f, item.Condition / 100.0f);

            UpdateVents(CurrFlow);
            
            voltage -= deltaTime;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            powerDownTimer += deltaTime;
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
