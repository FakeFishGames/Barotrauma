using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class NotComponent : ItemComponent
    {
        public NotComponent(Item item, XElement element)
            : base (item, element)
        {
        }
        
        public override void ReceiveSignal(string signal, Connection connection, Item sender)
        {
            if (connection.name != "signal_in") return;
            
            item.SendSignal(signal=="0" ? "1" : "0", "signal_out", item);
        }
    }
}
