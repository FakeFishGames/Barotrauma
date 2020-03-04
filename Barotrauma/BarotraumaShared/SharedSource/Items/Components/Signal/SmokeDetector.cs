using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SmokeDetector : ItemComponent
    {
        [Serialize(50.0f, false, description: "How large the fire has to be for the detector to react to it.")]
        public float FireSizeThreshold
        {
            get; set;
        }

        public SmokeDetector(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            item.SendSignal(0, item.CurrentHull != null && item.CurrentHull.FireSources.Any(fs => fs.Size.X > FireSizeThreshold) ? "1" : "0", "signal_out", null);            
        }
    }
}
