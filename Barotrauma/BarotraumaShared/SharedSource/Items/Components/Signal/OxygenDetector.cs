using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenDetector : ItemComponent
    {
        private int prevSentOxygenValue;
        private string oxygenSignal;

        public OxygenDetector(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.CurrentHull == null) { return; }

            if (prevSentOxygenValue != (int)item.CurrentHull.OxygenPercentage || oxygenSignal == null)
            {
                prevSentOxygenValue = (int)item.CurrentHull.OxygenPercentage;
                oxygenSignal = prevSentOxygenValue.ToString();
            }

            item.SendSignal(oxygenSignal, "signal_out");            
        }

    }
}
