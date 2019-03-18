using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class NotComponent : ItemComponent
    {
        public NotComponent(Item item, XElement element)
            : base (item, element)
        {
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection.Name != "signal_in") return;

            item.SendSignal(stepsTaken, signal == "0" ? "1" : "0", "signal_out", sender, 0.0f, source, signalStrength);
        }
    }
}
