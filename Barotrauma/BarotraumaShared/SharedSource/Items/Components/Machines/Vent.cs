using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Vent : ItemComponent
    {
        private float oxygenFlow;
        private float airAfflictionFlow;
        private String airAffliction;

        public float OxygenFlow
        {
            get { return oxygenFlow; }
            set { oxygenFlow = Math.Max(value, 0.0f); }
        }
        public float AirAfflictionFlow
        {
            get { return airAfflictionFlow; }
            set { airAfflictionFlow = Math.Max(value, 0.0f); }
        }

        [Serialize("", false)]
        public String AirAffliction
        {
            get { return airAffliction; }
            set { airAffliction = value; }
        }

        public Vent (Item item, XElement element)
            : base(item, element)
        {

        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.CurrentHull == null) return;

            if (item.InWater) return;
            //if(airAffliction != item.CurrentHull.AirAffliction)
            //{
            //    item.CurrentHull.AirAffliction = airAffliction;
            //    item.CurrentHull.AirAfflictionAmount = 0;
            //}
            //item.CurrentHull.AirAfflictionAmount += airAfflictionFlow * deltaTime;
            item.CurrentHull.Oxygen += oxygenFlow * deltaTime;
            OxygenFlow -= deltaTime * 1000.0f;
            //AirAfflictionFlow -= deltaTime * 1000.0f;
        }
    }
}
