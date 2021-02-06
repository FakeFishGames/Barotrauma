using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class NotComponent : ItemComponent
    {
        public NotComponent(Item item, XElement element)
            : base (item, element)
        {
        }

        public override void ReceiveSignal([NotNull] Signal signal)
        {
            if (signal.connection.Name != "signal_in") return;

            // signal.value = signal.value == "0" ? "1" : "0";

            item.SendSignal(signal.stepsTaken, signal.value == "0" ? "1" : "0", "signal_out", signal.sender, 0.0f, signal.source, signal.strength);
        }
    }
}
