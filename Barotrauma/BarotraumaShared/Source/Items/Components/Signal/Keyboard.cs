using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Keyboard : ItemComponent
    {
        [InGameEditable, Serialize("", true, "Message to be displayed on the keyboard display when it is first opened.")]
        public string WelcomeMessage { get; set; }
        private string OutputValue { get; set; }

        public Keyboard(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        partial void ShowOnDisplay(string input);

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            if (connection.Name != "signal_in") return;
            ShowOnDisplay(signal);
        }
    }
}