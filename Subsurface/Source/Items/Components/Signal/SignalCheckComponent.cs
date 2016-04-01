using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class SignalCheckComponent : ItemComponent
    {
        private string output;

        private string targetSignal;

        [InGameEditable, HasDefaultValue("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, HasDefaultValue("", true)]
        public string TargetSignal
        {
            get { return targetSignal; }
            set { targetSignal = value; }
        }

        public SignalCheckComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    item.SendSignal(stepsTaken, (signal == targetSignal) ? output : "0", "signal_out");

                    break;
                case "set_output":
                    output = signal;
                    break;
                case "set_targetsignal":
                    targetSignal = signal;
                    break;
            }
        }
    }
}
