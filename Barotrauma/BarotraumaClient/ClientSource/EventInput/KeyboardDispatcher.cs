using Barotrauma;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace EventInput
{
    public interface IKeyboardSubscriber
    {
        void ReceiveTextInput(char inputChar);
        void ReceiveTextInput(string text);
        void ReceiveCommandInput(char command);
        void ReceiveSpecialInput(Keys key);
        void ReceiveEditingInput(string text, int start, int length);

        bool Selected { get; set; } //or Focused
    }

    public class KeyboardDispatcher
    {
        public KeyboardDispatcher(GameWindow window)
        {
            EventInput.Initialize(window);
            EventInput.CharEntered += EventInput_CharEntered;
            EventInput.KeyDown += EventInput_KeyDown;
            EventInput.EditingText += EventInput_TextEditing;
            GameMain.ResetIMEWorkaround();
        }

        public void EventInput_TextEditing(object sender, TextEditingEventArgs e)
        {
            _subscriber?.ReceiveEditingInput(e.Text, e.Start, e.Length);
        }

        public void EventInput_KeyDown(object sender, KeyEventArgs e)
        {
            _subscriber?.ReceiveSpecialInput(e.KeyCode);
            if (char.IsControl(e.Character))
            {
                _subscriber?.ReceiveCommandInput(e.Character);
            }
        }

        void EventInput_CharEntered(object sender, CharacterEventArgs e)
        {
            _subscriber?.ReceiveTextInput(e.Character);
        }

        IKeyboardSubscriber _subscriber;
        public IKeyboardSubscriber Subscriber
        {
            get { return _subscriber; }
            set
            {
                if (_subscriber == value) { return; }

                if (_subscriber is GUITextBox)
                {
                    TextInput.StopTextInput();
                    _subscriber.Selected = false;
                }

                if (value is GUITextBox box)
                {
                    TextInput.SetTextInputRect(box.MouseRect);
                    TextInput.StartTextInput();
                    TextInput.SetTextInputRect(box.MouseRect);
                }

                _subscriber = value;
                if (value != null)
                {
                    value.Selected = true;
                }
            }
        }
    }
}
