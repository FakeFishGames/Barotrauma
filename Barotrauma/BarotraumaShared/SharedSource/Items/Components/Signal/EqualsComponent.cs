using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class EqualsComponent : ItemComponent
    {
        protected string output, falseOutput;

        //an array to keep track of how long ago a signal was received on both inputs
        protected float[] timeSinceReceived;

        protected string[] receivedSignal;

        private readonly Character[] signalSender = new Character[2];

        //the output is sent if both inputs have received a signal within the timeframe
        protected float timeFrame;

        private int maxOutputLength;
        [Editable, Serialize(200, IsPropertySaveable.No, description: "The maximum length of the output strings. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal sent when the condition is met.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        [InGameEditable, Serialize("0", IsPropertySaveable.Yes, description: "The signal sent when the condition is met (if empty, no signal is sent).", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null) { return; }
                falseOutput = value;
                if (falseOutput.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    falseOutput = falseOutput.Substring(0, MaxOutputLength);
                }
            }
        }

        private string value1, value2;

        //not virtual on purpose: overriding would change the declaring type of the property, which is used for the localization tags in the editor
        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "Used instead of the first input value if no value is received through wiring, or if the first signal input is not wired to anything.", alwaysUseInstanceValues: true)]
        public string Value1
        {
            get { return value1; }
            set { value1 = OnFixedValueChanged(0, value); }
        }

        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "Used instead of the second input value if no value is received through wiring, or if the second signal input is not wired to anything.", alwaysUseInstanceValues: true)]
        public string Value2
        {
            get { return value2; }
            set { value2 = OnFixedValueChanged(1, value); }
        }

        [InGameEditable(DecimalCount = 2), Serialize(0.0f, IsPropertySaveable.Yes, description: "The maximum amount of time between the received signals. If set to 0, the signals must be received at the same time.", alwaysUseInstanceValues: true)]
        public float TimeFrame
        {
            get { return timeFrame; }
            set
            {
                if (value > timeFrame)
                {
                    timeSinceReceived[0] = timeSinceReceived[1] = Math.Max(value * 2.0f, 0.1f);
                }
                timeFrame = Math.Max(0.0f, value);
            }
        }

        public EqualsComponent(Item item, ContentXElement element)
            : base(item, element)
        {
            timeSinceReceived = new float[] { Math.Max(timeFrame * 2.0f, 0.1f), Math.Max(timeFrame * 2.0f, 0.1f) };
            receivedSignal = new string[2];
            IsActive = true;
        }

        /// <summary>
        /// Called when <see cref="Value1"/> or <see cref="Value2"/> is set. Can be overridden to validate or cache the value.
        /// </summary>
        /// <param name="index">0 = Value1, 1 = Value2</param>
        /// <returns>The value that should be assigned to the property.</returns>
        protected virtual string OnFixedValueChanged(int index, string value)
        {
            return value;
        }

        public override void Update(float deltaTime, Camera cam)
        {
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
                            receivedSignal[0] = Value1;
                            sendOutput = true;
                        }
                    }
                    else if (i == 1)
                    {
                        if (!Value2.IsNullOrEmpty())
                        {
                            receivedSignal[1] = Value2;
                            sendOutput = true;
                        }
                    }
                }
                timeSinceReceived[i] += deltaTime;
            }

            if (sendOutput)
            {
                string signalOut = receivedSignal[0] == receivedSignal[1] ? output : falseOutput;
                if (string.IsNullOrEmpty(signalOut)) { return; }
                item.SendSignal(new Signal(signalOut, sender: signalSender[0] ?? signalSender[1]), "signal_out");
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "signal_in1":
                    receivedSignal[0] = signal.value;
                    timeSinceReceived[0] = 0.0f;
                    signalSender[0] = signal.sender;
                    break;
                case "signal_in2":
                    receivedSignal[1] = signal.value;
                    timeSinceReceived[1] = 0.0f;
                    signalSender[1] = signal.sender;
                    break;
                case "set_output":
                    output = signal.value;
                    break;
                case "set_falseoutput":
                    falseOutput = signal.value;
                    break;
            }
        }
    }
}
