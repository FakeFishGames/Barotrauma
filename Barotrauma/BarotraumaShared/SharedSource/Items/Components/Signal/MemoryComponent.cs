using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class MemoryComponent : ItemComponent
    {
        [InGameEditable, Serialize("", true, description: "The currently stored signal the item outputs.")]
        public string Value
        {
            get;
            set;
        }
        
        protected bool writeable = true;

        public MemoryComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            item.SendSignal(0, Value, "signal_out", null);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (writeable) { Value = signal; }
                    break;
                case "signal_store":
                    writeable = (signal == "1");
                    break;
            }
        }
    }
}
