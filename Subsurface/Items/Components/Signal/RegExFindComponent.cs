using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Subsurface.Items.Components
{
    class RegExFindComponent : ItemComponent
    {
        private string output;

        private string expression;

        [InGameEditable, HasDefaultValue("1", true)]
        public string Output
        {
            get { return output; }
            set { output = value; }
        }

        [InGameEditable, HasDefaultValue("", true)]
        public string Expression
        {
            get { return expression; }
            set { expression = value; }
        }

        public RegExFindComponent(Item item, XElement element)
            : base(item, element)
        {
        }

        public override void ReceiveSignal(string signal, Connection connection, Item sender, float power=0.0f)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (string.IsNullOrWhiteSpace(expression)) return;

                    bool success = false;
                    try
                    {
                        Regex regex = new Regex(@expression);
                        Match match = regex.Match(signal);
                        success = match.Success;
                    }
                    catch
                    {
                        item.SendSignal("ERROR", "signal_out");
                        return;
                    }

                    item.SendSignal(success ? output : "0", "signal_out");

                    break;
                case "set_output":
                    output = signal;
                    break;
            }
        }
    }
}
