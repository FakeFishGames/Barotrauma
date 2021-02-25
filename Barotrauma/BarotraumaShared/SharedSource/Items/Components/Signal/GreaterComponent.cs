using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class GreaterComponent : EqualsComponent
    {
        private float val1, val2;

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
                string signalOut = val1 > val2 ? output : falseOutput;
                if (string.IsNullOrEmpty(signalOut)) return;

                item.SendSignal(signalOut, "signal_out");
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            base.ReceiveSignal(signal, connection);
            float.TryParse(receivedSignal[0], NumberStyles.Float, CultureInfo.InvariantCulture, out val1);
            float.TryParse(receivedSignal[1], NumberStyles.Float, CultureInfo.InvariantCulture, out val2);
        }
    }
}
