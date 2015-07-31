using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class OxygenGenerator : Powered
    {
        PropertyTask powerUpTask;

        float powerDownTimer;

        bool running;

        List<Vent> ventList;

        public bool IsRunning()
        {
            return (running && item.Condition>0.0f);
        }

        public OxygenGenerator(Item item, XElement element)
            : base(item, element)
        {
            isActive = true;

            ventList = new List<Vent>();

            item.linkedTo.CollectionChanged += delegate(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            { GetVents(); };
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            currPowerConsumption = powerConsumption;

            if (item.CurrentHull == null) return;

            if (voltage < minVoltage)
            {
                powerDownTimer += deltaTime;
                running = false;
                if ((powerUpTask==null || powerUpTask.IsFinished) && powerDownTimer>5.0f)
                {
                    powerUpTask = new PropertyTask(item, IsRunning, 50.0f, "Turn on the oxygen generator");
                }
                return;                
            }
            else
            {
                powerDownTimer = 0.0f;
            }

            running = true;
            
            float deltaOxygen = Math.Min(voltage, 1.0f) * 50000.0f;
            item.CurrentHull.Oxygen += deltaOxygen * deltaTime;

            UpdateVents(deltaOxygen);


            voltage = 0.0f;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            powerDownTimer += deltaTime;
        }

        private void GetVents()
        {            
            foreach (MapEntity entity in item.linkedTo)
            {
                Item linkedItem = entity as Item;
                if (linkedItem == null) continue;

                Vent vent = linkedItem.GetComponent<Vent>();
                if (vent != null) ventList.Add(vent);                
            }
        }
                
        private void UpdateVents(float deltaOxygen)
        {
            if (ventList.Count == 0) return;

            deltaOxygen = deltaOxygen / ventList.Count;
            foreach (Vent v in ventList)
            {
                v.OxygenFlow = deltaOxygen;
                v.IsActive = true;
            }
        }
    }
}
