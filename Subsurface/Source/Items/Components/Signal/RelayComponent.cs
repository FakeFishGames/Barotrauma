using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RelayComponent : ItemComponent
    {
        private float maxPower;

        [Editable, HasDefaultValue(1000.0f, true)]
        public float MaxPower
        {
            get { return maxPower; }
            set
            {
                maxPower = Math.Max(0.0f, value);
            }
        }

        [Editable, HasDefaultValue(false, true)]
        public bool IsOn
        {
            get; set; 
        }

        public RelayComponent(Item item, XElement element)
            : base (item, element)
        {
            IsActive = true;
            IsOn = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {

            item.SendSignal(0, IsOn ? "1" : "0", "state_out");
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (item.Condition <= 0.0f) return;

            if (power > maxPower) item.Condition = 0.0f;
            
            if (connection.Name.Contains("_in"))
            {
                if (!IsOn) return;

                string outConnection = connection.Name.Contains("power_in") ? "power_out" : "signal_out";

                int connectionNumber = -1;
                int.TryParse(connection.Name.Substring(connection.Name.Length - 1, 1), out connectionNumber);

                if (connectionNumber > 0) outConnection += connectionNumber;

                item.SendSignal(stepsTaken, signal, outConnection, power);
            }
            else if (connection.Name == "toggle")
            {
                IsOn = !IsOn;
            }
            else if (connection.Name == "set_state")
            {
                IsOn = signal != "0";
            }

        }
    }
}
