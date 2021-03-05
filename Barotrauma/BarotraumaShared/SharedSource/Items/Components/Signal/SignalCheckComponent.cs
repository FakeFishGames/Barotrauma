using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SignalCheckComponent : ItemComponent
    {
        private string output;
        [InGameEditable, Serialize("1", true, description: "The signal this item outputs when the received signal matches the target signal.", alwaysUseInstanceValues: true)]
        public string Output
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength)
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        private string falseOutput;
        [InGameEditable, Serialize("0", true, description: "The signal this item outputs when the received signal does not match the target signal.", alwaysUseInstanceValues: true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set
            {
                if (value == null) { return; }
                falseOutput = value;
                if (falseOutput.Length > MaxOutputLength)
                {
                    falseOutput = falseOutput.Substring(0, MaxOutputLength);
                }
            }
        }

        [InGameEditable, Serialize("", true, description: "The value to compare the received signals against.", alwaysUseInstanceValues: true)]
        public string TargetSignal { get; set; }

        private int maxOutputLength;
        [Editable, Serialize(200, false, description: "The maximum length of the output strings. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        public SignalCheckComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    string signalOut = (signal == TargetSignal) ? Output : FalseOutput;

                    if (string.IsNullOrWhiteSpace(signalOut)) return;
                    item.SendSignal(stepsTaken, signalOut, "signal_out", sender, signalStrength, source);

                    break;
                case "set_output":
                    Output = signal;
                    break;
                case "set_targetsignal":
                    TargetSignal = signal;
                    break;
            }
        }
    }
}
