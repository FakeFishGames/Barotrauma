using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ExponentiationComponent : AdderComponent
    {
        public ExponentiationComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            bool sendOutput = true;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] > timeFrame) sendOutput = false;
                timeSinceReceived[i] += deltaTime;
            }
            if (sendOutput)
            {
                item.SendSignal(0, MathUtils.Pow(receivedSignal[0], receivedSignal[1]).ToString("G", CultureInfo.InvariantCulture), "signal_out", null);
            }
        }
    }
}