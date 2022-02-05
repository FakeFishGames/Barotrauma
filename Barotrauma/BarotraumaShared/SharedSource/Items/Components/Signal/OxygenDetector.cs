using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenDetector : ItemComponent
    {
        public const int LowOxygenPercentage = 35;

        public OxygenDetector(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.CurrentHull == null) return;

            int oxygenPercentage = (int)item.CurrentHull.OxygenPercentage;
            item.SendSignal((oxygenPercentage).ToString(), "signal_out");
            item.SendSignal((oxygenPercentage <= LowOxygenPercentage ? "1" : "0"), "low_oxygen");
        }

    }
}
