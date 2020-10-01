using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent
    {
        private const int MaxMessageLength = 150;

        public string DisplayedWelcomeMessage
        {
            get;
            private set;
        }

        private string welcomeMessage;
        [InGameEditable, Serialize("", true, "Message to be displayed on the terminal display when it is first opened.", translationTextTag = "terminalwelcomemsg.", AlwaysUseInstanceValues = true)]
        public string WelcomeMessage
        {
            get { return welcomeMessage; }
            set
            {
                if (welcomeMessage == value) { return; }
                welcomeMessage = value;
                DisplayedWelcomeMessage = TextManager.Get(welcomeMessage, returnNull: true) ?? welcomeMessage.Replace("\\n", "\n");
            }
        }

        private string OutputValue { get; set; }

        public Terminal(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        partial void ShowOnDisplay(string input);

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0, float signalStrength = 1)
        {
            if (connection.Name != "signal_in") { return; }
            if (signal.Length > MaxMessageLength)
            {
                signal = signal.Substring(0, MaxMessageLength);
            }

            string inputSignal = signal.Replace("\\n", "\n");
            ShowOnDisplay(inputSignal);
        }
    }
}