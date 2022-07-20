using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class GreaterComponent : EqualsComponent
    {
        private float val1, val2;

        public GreaterComponent(Item item, ContentXElement element)
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
            //base.ReceiveSignal(signal, connection);
            switch (connection.Name)
            {
                case "signal_in1":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out val1);
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_in2":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out val2);
                    timeSinceReceived[1] = 0.0f;
                    break;
                case "set_output":
                    output = signal.value;
                    break;
            }
        }
    }
}
