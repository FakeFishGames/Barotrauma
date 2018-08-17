using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SignalCheckComponent : ItemComponent
    {
        private string output, falseOutput;

        private string targetSignal;

        [InGameEditable, Serialize("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }
        [InGameEditable, Serialize("0", true)]
        public string FalseOutput
        {
            get { return falseOutput; }
            set { falseOutput = value; }
        }

        [InGameEditable, Serialize("", true)]
        public string TargetSignal
        {
            get { return targetSignal; }
            set { targetSignal = value; }
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
                    string signalOut = (signal == targetSignal) ? output : falseOutput;

                    if (string.IsNullOrWhiteSpace(signalOut)) return;
                    item.SendSignal(stepsTaken, signalOut, "signal_out", sender, signalStrength);

                    break;
                case "set_output":
                    output = signal;
                    break;
                case "set_targetsignal":
                    targetSignal = signal;
                    break;
            }
        }
    }
}
