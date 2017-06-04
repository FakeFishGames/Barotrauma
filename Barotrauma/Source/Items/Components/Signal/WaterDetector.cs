using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WaterDetector : ItemComponent
    {
        public WaterDetector(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            item.SendSignal(0, item.InWater ? "1" : "0", "signal_out", null);            
        }
    }
}
