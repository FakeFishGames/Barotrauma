using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class GreaterComponent : EqualsComponent
    {
        private float val1 = float.NegativeInfinity;
        private float val2 = float.PositiveInfinity;

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
                val1 = float.NegativeInfinity;
                val2 = float.PositiveInfinity;
                if (string.IsNullOrEmpty(signalOut)) return;

                item.SendSignal(signalOut, "signal_out");
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {           
            switch (connection.Name)
            {
                case "signal_in1":
                    float signal1 = float.NegativeInfinity;
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out signal1);
                    val1 = signal1 > val1 ? signal1 : val1;
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_in2":
                    float signal2 = float.PositiveInfinity;
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out signal2);
                    val2 = signal2 < val2 ? signal2 : val2;
                    timeSinceReceived[1] = 0.0f;
                    break;
                case "set_output":
                    output = signal.value;
                    break;
            }
        }
    }
}
