using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class GreaterComponent : EqualsComponent
    {
        public GreaterComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = false;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame) sendOutput = true;
                timeSinceReceived[i] += deltaTime;
            }

            if (sendOutput)
            {
                string signalOut = receivedSignal[0] > receivedSignal[1] ? output : falseOutput;
                if (string.IsNullOrEmpty(signalOut)) return;

                item.SendSignal(0, signalOut, "signal_out", null);
            }
        }
    }
}
