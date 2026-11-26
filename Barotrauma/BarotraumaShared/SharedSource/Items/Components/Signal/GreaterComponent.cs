using System.Globalization;

namespace Barotrauma.Items.Components
{
    class GreaterComponent : EqualsComponent
    {
        //values received through wiring, cached as floats separately from receivedSignals to avoid constant parsing of the string signals
        private float receivedValue1, receivedValue2;

        //the static values set in the editor, cached as floats separately from Value1 and Value2 to avoid constant parsing of the string values
        private float fixedValue1, fixedValue2;

        public GreaterComponent(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        protected override string OnFixedValueChanged(int index, string value)
        {
            //only numeric values are valid, clear the value otherwise
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
            {
                floatValue = 0.0f;
                value = string.Empty;
            }
            if (index == 0)
            {
                fixedValue1 = floatValue;
            }
            else
            {
                fixedValue2 = floatValue;
            }
            return value;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            float value1 = receivedValue1;
            float value2 = receivedValue2;

            bool sendOutput = false;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame)
                {
                    sendOutput = true;
                }
                else
                {
                    if (i == 0)
                    {
                        if (!Value1.IsNullOrEmpty())
                        {
                            value1 = fixedValue1;
                            sendOutput = true;
                        }
                    }
                    else if (i == 1)
                    {
                        if (!Value2.IsNullOrEmpty())
                        {
                            value2 = fixedValue2;
                            sendOutput = true;
                        }
                    }
                }
                timeSinceReceived[i] += deltaTime;
            }

            if (sendOutput)
            {
                string signalOut = value1 > value2 ? output : falseOutput;
                if (string.IsNullOrEmpty(signalOut)) return;

                item.SendSignal(signalOut, "signal_out");
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {           
            switch (connection.Name)
            {
                case "signal_in1":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedValue1);
                    timeSinceReceived[0] = 0.0f;
                    break;
                case "signal_in2":
                    float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out receivedValue2);
                    timeSinceReceived[1] = 0.0f;
                    break;
                default:
                    base.ReceiveSignal(signal, connection); 
                    break;
            }
        }
    }
}
