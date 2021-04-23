using Barotrauma.Networking;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class MemoryComponent : ItemComponent, IServerSerializable
    {
        private string value;

        [InGameEditable, Serialize("", true, description: "The currently stored signal the item outputs.", alwaysUseInstanceValues: true)]
        public string Value
        {
            get { return value; }
            set
            {
                if (value == null) { return; }
                this.value = value;
                if (this.value.Length > MaxValueLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    this.value = this.value.Substring(0, MaxValueLength);
                }
            }
        }

        private int maxValueLength;
        [Editable, Serialize(200, false, description: "The maximum length of the stored value. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxValueLength
        {
            get { return maxValueLength; }
            set
            {
                maxValueLength = Math.Max(value, 0);
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
                        string prevValue = Value;
                        Value = signal.value;
                        if (Value != prevValue)
                        {
                            OnStateChanged();
                        }
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
