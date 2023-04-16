using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    readonly struct TerminalMessage
    {
        public readonly string Text;
        public readonly Color Color;

        public TerminalMessage(string text, Color color)
        {
            Text = text;
            Color = color;
        }

        public void Deconstruct(out string text, out Color color)
        {
            text = Text;
            color = Color;
        }
    }

    partial class Terminal : ItemComponent
    {
        private const int MaxMessageLength = ChatMessage.MaxLength;

        private const int MaxMessages = 60;

        private readonly List<TerminalMessage> messageHistory = new List<TerminalMessage>(MaxMessages);

        public LocalizedString DisplayedWelcomeMessage
        {
            get;
            private set;
        }

        private string welcomeMessage;
        [InGameEditable, Serialize("", IsPropertySaveable.Yes, "Message to be displayed on the terminal display when it is first opened.", translationTextTag: "terminalwelcomemsg.", alwaysUseInstanceValues: true)]
        public string WelcomeMessage
        {
            get { return welcomeMessage; }
            set
            {
                if (welcomeMessage == value) { return; }
                welcomeMessage = value;
                DisplayedWelcomeMessage = TextManager.Get(welcomeMessage).Fallback(welcomeMessage.Replace("\\n", "\n"));
            }
        }

        /// <summary>
        /// Can be used to display messages on the terminal via status effects
        /// </summary>
        public string ShowMessage
        {
            get { return messageHistory.Count == 0 ? string.Empty : messageHistory.Last().Text; }
            set
            {
                if (string.IsNullOrEmpty(value)) { return; }
                ShowOnDisplay(value, addToHistory: true, TextColor);
            }
        }

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "The terminal will use a monospace font if this box is ticked.", alwaysUseInstanceValues: true)]
        public bool UseMonospaceFont { get; set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool AutoHideScrollbar { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, alwaysUseInstanceValues: true)]
        public bool WelcomeMessageDisplayed { get; set; }

        private Color textColor = Color.LimeGreen;

        [Editable, Serialize("50,205,50,255", IsPropertySaveable.Yes, description: "Color of the terminal text.", alwaysUseInstanceValues: true)]
        public Color TextColor
        {
            get => textColor;
            set
            {
                textColor = value;
#if CLIENT
                if (inputBox is { } input)
                {
                    input.TextColor = value;
                }
#endif
            }
        }

        [Editable, Serialize("> ", IsPropertySaveable.Yes)]
        public string LineStartSymbol { get; set; }

        [Editable, Serialize(false, IsPropertySaveable.No)]
        public bool Readonly { get; set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool AutoScrollToBottom { get; set; }

        private string OutputValue { get; set; }

        private string prevColorSignal;

        public Terminal(Item item, ContentXElement element)
            : base(item, element)
        {
            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        partial void ShowOnDisplay(string input, bool addToHistory, Color color);

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "set_text":
                case "signal_in":
                    if (string.IsNullOrEmpty(signal.value)) { return; }
                    if (signal.value.Length > MaxMessageLength)
                    {
                        signal.value = signal.value.Substring(0, MaxMessageLength);
                    }
                    string inputSignal = signal.value.Replace("\\n", "\n");
                    ShowOnDisplay(inputSignal, addToHistory: true, TextColor);
                    break;
                case "set_text_color":
                    if (signal.value != prevColorSignal)
                    {
                        TextColor = XMLExtensions.ParseColor(signal.value, false);
                        prevColorSignal = signal.value;
                    }
                    break;
                case "clear_text" when signal.value != "0":
                    messageHistory.Clear();
#if CLIENT
                    if (historyBox?.Content is { } history)
                    {
                        history.ClearChildren();
                    }

                    CreateFillerBlock();
#endif
                    break;
            }
        }

        public override void OnItemLoaded()
        {
            bool isSubEditor = false;
#if CLIENT
            isSubEditor = Screen.Selected == GameMain.SubEditorScreen || GameMain.GameSession?.GameMode is TestGameMode;
#endif

            base.OnItemLoaded();
            if (!DisplayedWelcomeMessage.IsNullOrEmpty() && !WelcomeMessageDisplayed)
            {
                ShowOnDisplay(DisplayedWelcomeMessage.Value, addToHistory: !isSubEditor, TextColor);
                DisplayedWelcomeMessage = "";
                //disable welcome message if a game session is running so it doesn't reappear on successive rounds
                if (GameMain.GameSession != null && !isSubEditor)
                {
                    WelcomeMessageDisplayed = true;
                }
            }
        }

        public override XElement Save(XElement parentElement)
        {
            var componentElement = base.Save(parentElement);
            for (int i = 0; i < messageHistory.Count; i++)
            {
                componentElement.Add(new XAttribute("msg" + i, messageHistory[i].Text));
                componentElement.Add(new XAttribute("color" + i, messageHistory[i].Color.ToStringHex()));
            }
            return componentElement;
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            for (int i = 0; i < MaxMessages; i++)
            {
                string msg = componentElement.GetAttributeString("msg" + i, null);
                if (msg is null) { break; }
                Color color = componentElement.GetAttributeColor("color" + i, TextColor);
                ShowOnDisplay(msg, addToHistory: true, color);
            }
        }
    }
}