using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SignalCheckComponent : ItemComponent
    {
        [InGameEditable, Serialize("1", true, description: "The signal this item outputs when the received signal matches the target signal.", alwaysUseInstanceValues: true)]
        public string Output { get; set; }
        [InGameEditable, Serialize("0", true, description: "The signal this item outputs when the received signal does not match the target signal.", alwaysUseInstanceValues: true)]
        public string FalseOutput { get; set; }

        [InGameEditable, Serialize("", true, description: "The value to compare the received signals against.", alwaysUseInstanceValues: true)]
        public string TargetSignal { get; set; }

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
                    item.SendSignal(stepsTaken, signalOut, "signal_out", sender, signalStrength);

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
