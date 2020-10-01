using Barotrauma.Networking;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MemoryComponent : ItemComponent, IServerSerializable
    {
        const int MaxValueLength = ChatMessage.MaxLength;


        private string value;

        [InGameEditable, Serialize("", true, description: "The currently stored signal the item outputs.", alwaysUseInstanceValues: true)]
        public string Value
        {
            get { return value; }
            set
            {
                if (value == null) { return; }
                this.value = value.Length <= MaxValueLength ? value : value.Substring(0, MaxValueLength);
            }
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

        partial void OnStateChanged();

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (writeable) 
                    {
                        if (Value == signal) { return; }
                        Value = signal;
                        OnStateChanged();
                    }
                    break;
                case "signal_store":
                    writeable = signal == "1";
                    break;
            }
        }
    }
}
