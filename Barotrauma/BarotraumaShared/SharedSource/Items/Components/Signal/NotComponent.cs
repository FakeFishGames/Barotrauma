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

        public override void ReceiveSignal(Signal signal)
        {
            if (signal.connection.Name != "signal_in") return;

            signal.value = signal.value == "0" ? "1" : "0";

            item.SendSignal(signal, "signal_out");
        }
    }
}
