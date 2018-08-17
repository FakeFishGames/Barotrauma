using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RegExFindComponent : ItemComponent
    {
        private string output;

        private string expression;

        private string receivedSignal;
        private string previousReceivedSignal;

        bool previousResult;

        private Regex regex;

        [InGameEditable, Serialize("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, Serialize("", true)]
        public string Expression
        {
            get { return expression; }
            set 
            {
                if (expression == value) return;
                expression = value;
                previousReceivedSignal = "";

                try
                {
                    regex = new Regex(@expression);
                }

                catch
                {
                    item.SendSignal(0, "ERROR", "signal_out", null);
                    return;
                }
            }
        }

        public RegExFindComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (string.IsNullOrWhiteSpace(expression) || regex == null) return;

            if (receivedSignal != previousReceivedSignal && receivedSignal != null)
            {
                try
                {
                    Match match = regex.Match(receivedSignal);
                    previousResult =  match.Success;
                    previousReceivedSignal = receivedSignal;

                }
                catch
                {
                    item.SendSignal(0, "ERROR", "signal_out", null);
                    previousResult = false;
                    return;
                }
            }


            item.SendSignal(0, previousResult ? output : "0", "signal_out", null);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    receivedSignal = signal;
                    break;
                case "set_output":
                    output = signal;
                    break;
            }
        }
    }
}
