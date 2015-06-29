using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class NotComponent : ItemComponent
    {
        public NotComponent(Item item, XElement element)
            : base (item, element)
        {
        }
        
        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (connection.name != "signal_in") return;
            
            item.SendSignal(signal=="0" ? "1" : "0", "signal_out");
        }
    }
}
