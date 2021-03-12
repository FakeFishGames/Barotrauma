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
            item.SendSignal(Value, "signal_out");
        }

        partial void OnStateChanged();

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (writeable) 
                    {
                        if (Value == signal.value) { return; }
                        Value = signal.value;
                        OnStateChanged();
                    }
                    break;
                case "signal_store":
                case "lock_state":
                    writeable = signal.value == "1";
                    break;
            }
        }
    }
}
