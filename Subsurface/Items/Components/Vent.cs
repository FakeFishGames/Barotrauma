using System;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Vent : ItemComponent
    {
        private float oxygenFlow;

        public float OxygenFlow
        {
            get { return oxygenFlow; }
            set { oxygenFlow = Math.Max(value, 0.0f); }
        }

        public Vent (Item item, XElement element)
            : base(item, element)
        {

        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (item.currentHull == null) return;

            item.currentHull.Oxygen += oxygenFlow * deltaTime;
            OxygenFlow -= deltaTime * 1000.0f;
        }
    }
}
