using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class OxygenDetector : ItemComponent
    {
        public OxygenDetector(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (item.CurrentHull == null) return;

            item.SendSignal(0, ((int)item.CurrentHull.OxygenPercentage).ToString(), "signal_out", null);            
        }

    }
}
