using System;
using System.Globalization;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class MemoryComponent : ItemComponent
    {
        [InGameEditable(MinValueFloat = -999999.0f, MaxValueFloat = 999999.0f), Serialize(1.0f, true)]
        public float Value
        {
            get;
            set;
        }

        public float TempValue
        {
            get;
            set;
        }

        protected bool writeable;

        public MemoryComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            item.SendSignal(0, Value.ToString(), "signal_out", null);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if ( writeable )
                    {
                        float tempValue;
                        if (float.TryParse(signal, NumberStyles.Any, CultureInfo.InvariantCulture, out tempValue))
                        {
                            if (!MathUtils.IsValid(tempValue)) return;
                            Value = tempValue;
                        }
                    }
                    break;
                case "signal_store":
                    writeable = (signal == "1");
                    break;
            }
        }
    }
}
