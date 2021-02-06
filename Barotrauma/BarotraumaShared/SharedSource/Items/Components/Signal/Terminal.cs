using Barotrauma.Networking;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Terminal : ItemComponent
    {
        private const int MaxMessageLength = ChatMessage.MaxLength;

        private const int MaxMessages = 60;

        private List<string> messageHistory = new List<string>(MaxMessages);

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

        public override void ReceiveSignal(Signal signal)
        {
            if (signal.connection.Name != "signal_in") { return; }
            if (signal.value.Length > MaxMessageLength)
            {
                signal.value = signal.value.Substring(0, MaxMessageLength);
            }

            string inputSignal = signal.value.Replace("\\n", "\n");
            ShowOnDisplay(inputSignal);
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            if (!string.IsNullOrEmpty(DisplayedWelcomeMessage))
            {
                ShowOnDisplay(DisplayedWelcomeMessage);
                DisplayedWelcomeMessage = "";
                //remove welcome message if a game session is running so it doesn't reappear on successive rounds
                if (GameMain.GameSession != null)
                {
                    welcomeMessage = null;
                }
            }
        }

        public override XElement Save(XElement parentElement)
        {
            var componentElement = base.Save(parentElement);
            for (int i = 0; i < messageHistory.Count; i++)
            {
                componentElement.Add(new XAttribute("msg" + i, messageHistory[i]));
            }
            return componentElement;
        }

        public override void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            for (int i = 0; i < MaxMessages; i++)
            {
                string msg = componentElement.GetAttributeString("msg" + i, null);
                if (msg == null) { break; }
                ShowOnDisplay(msg);
            }
        }
    }
}