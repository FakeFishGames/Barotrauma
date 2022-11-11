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

#if !WINDOWS
        /// Windows build uses ImeSharp instead because SDL2's IME implementation is broken on Windows (https://github.com/libsdl-org/SDL/issues/2243)
        void ReceiveEditingInput(string text, int start);
#endif

        bool Selected { get; set; } //or Focused
    }

    public class KeyboardDispatcher
    {
        public KeyboardDispatcher(GameWindow window)
        {
            EventInput.Initialize(window);
            EventInput.CharEntered += EventInput_CharEntered;
            EventInput.KeyDown += EventInput_KeyDown;
#if !WINDOWS
            EventInput.EditingText += EventInput_TextEditing;
#endif

            /*
             * SDL by default starts in a state where it accepts IME inputs
             * this is bad because this blocks keybinds since the IME thinks
             * it's typing in a text box and not forwarding keybinds to the game.
             */
            TextInput.StopTextInput();
        }
#if !WINDOWS
        public void EventInput_TextEditing(object sender, TextEditingEventArgs e)
        {
            _subscriber?.ReceiveEditingInput(e.Text, e.Start);
        }
#endif
        public void EventInput_KeyDown(object sender, KeyEventArgs e)
        {
            _subscriber?.ReceiveSpecialInput(e.KeyCode);
        }

        void EventInput_CharEntered(object sender, CharacterEventArgs e)
        {
            if (_subscriber == null)
                return;
            if (char.IsControl(e.Character))
            {
                _subscriber.ReceiveCommandInput(e.Character);
                // Doesn't work as expected. Not sure why this should be run in a separate thread.
                //#if WINDOWS
                //                //ctrl-v
                //                if (e.Character == 0x16) // 22
                //                {
                //                    //XNA runs in Multiple Thread Apartment state, which cannot recieve clipboard
                //                    Thread thread = new Thread(PasteThread);
                //                    thread.SetApartmentState(ApartmentState.STA);
                //                    thread.Start();
                //                    thread.Join();
                //                    _subscriber.ReceiveTextInput(_pasteResult);
                //                }
                //                else
                //                {
                //                    _subscriber.ReceiveCommandInput(e.Character);
                //                }
                //#else
                //                _subscriber.ReceiveCommandInput(e.Character);
                //#endif
            }
            else
            {
                _subscriber.ReceiveTextInput(e.Character);
            }
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
                    TextInput.SetTextInputRect(box.Rect);
                    TextInput.StartTextInput();
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
