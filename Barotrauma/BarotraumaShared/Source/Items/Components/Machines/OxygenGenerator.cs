using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenGenerator : Powered
    {
        float powerDownTimer;

        bool running;

        private float generatedAmount;

        List<Vent> ventList;

        private float totalHullVolume;

        public bool IsRunning()
        {
            return (running && item.Condition>0.0f);
        }

        public float CurrFlow
        {
            get;
            private set;
        }

        [Editable, Serialize(100.0f, true)]
        public float GeneratedAmount
        {
            get { return generatedAmount; }
            set { generatedAmount = MathHelper.Clamp(value, -10000.0f, 10000.0f); }
        }

        public OxygenGenerator(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;

            //item.linkedTo.CollectionChanged += delegate { GetVents(); };
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            CurrFlow = 0.0f;
            currPowerConsumption = powerConsumption;

            if (item.CurrentHull == null) return;

            if (voltage < minVoltage)
            {
                powerDownTimer += deltaTime;
                running = false;
                return;
            }
            else
            {
                powerDownTimer = 0.0f;
            }

            running = true;

            CurrFlow = Math.Min(voltage, 1.0f) * generatedAmount*100.0f;
            //item.CurrentHull.Oxygen += CurrFlow * deltaTime;

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
