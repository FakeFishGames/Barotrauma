using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenDetector : ItemComponent
    {
        public const int LowOxygenPercentage = 35;

        private int prevSentOxygenValue;
        private string oxygenSignal;

        public OxygenDetector(Item item, ContentXElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.CurrentHull == null) { return; }

            int currOxygenPercentage = (int)item.CurrentHull.OxygenPercentage;
            if (prevSentOxygenValue != currOxygenPercentage || oxygenSignal == null)
            {
                prevSentOxygenValue = currOxygenPercentage;
                oxygenSignal = prevSentOxygenValue.ToString();
            }

            item.SendSignal(oxygenSignal, "signal_out");
            item.SendSignal(currOxygenPercentage <= LowOxygenPercentage ? "1" : "0", "low_oxygen");
        }

    }
}
