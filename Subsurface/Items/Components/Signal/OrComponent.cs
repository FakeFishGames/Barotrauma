using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class OrComponent : AndComponent
    {
        public OrComponent(Item item, XElement element)
            : base (item, element)
        {
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = true;
            for (int i = 0; i<timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] > timeFrame) sendOutput = false;
                timeSinceReceived[i] += deltaTime;
            }

            if (sendOutput)
            {
                item.SendSignal(output, "signal_out");
            }
        }
    }
}
