using System.Diagnostics.CodeAnalysis;
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

        public override void ReceiveSignal(Signal signal)
        {
            switch (signal.connection.Name)
            {
                case "signal_in":
                    string signalOut = (signal.value == TargetSignal) ? Output : FalseOutput;

                    if (string.IsNullOrWhiteSpace(signalOut)) return;
                    signal.value = signalOut;
                    item.SendSignal(signal, "signal_out");

                    break;
                case "set_output":
                    Output = signal.value;
                    break;
                case "set_targetsignal":
                    TargetSignal = signal.value;
                    break;
            }
        }
    }
}
