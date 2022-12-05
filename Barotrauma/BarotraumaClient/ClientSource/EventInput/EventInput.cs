using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace EventInput
{
    public readonly record struct CharacterEventArgs(char Character, long Param)
    {
        public long RepeatCount => Param & 0xffff;
        public bool ExtendedKey => (Param & (1 << 24)) > 0;
        public bool AltPressed => (Param & (1 << 29)) > 0;
        public bool PreviousState => (Param & (1 << 30)) > 0;
        public bool TransitionState => (Param & (1 << 31)) > 0;
    }

    public readonly record struct KeyEventArgs(Keys KeyCode, char Character);

    public delegate void CharEnteredHandler(object sender, CharacterEventArgs e);
    public delegate void KeyEventHandler(object sender, KeyEventArgs e);
    public delegate void EditingTextHandler(object sender, TextEditingEventArgs e);

    public static class EventInput
    {
        /// <summary>
        /// Event raised when a Character has been entered.
        /// </summary>
        public static event CharEnteredHandler CharEntered;

        /// <summary>
        /// Event raised when a key has been pressed down. May fire multiple times due to keyboard repeat.
        /// </summary>
        public static event KeyEventHandler KeyDown;

        /// <summary>
        /// Event raised when a key has been released.
        /// </summary>
        public static event KeyEventHandler KeyUp;

        /// <summary>
        /// Raised when the user is editing text and IME is in progress. 
        /// </summary>
        public static event EditingTextHandler EditingText;


        static bool initialized;

        /// <summary>
        /// Initialize the TextInput with the given GameWindow.
        /// </summary>
        /// <param name="window">The XNA window to which text input should be linked.</param>
        public static void Initialize(GameWindow window)
        {
            if (initialized)
            {
                return;
            }

            window.TextInput += ReceiveInput;
            window.KeyDown += ReceiveKeyDown;
            window.TextEditing += ReceiveTextEditing;

            initialized = true;
        }

        private static void ReceiveInput(object sender, TextInputEventArgs e)
        {
            OnCharEntered(e.Character);
        }

        private static void ReceiveKeyDown(object sender, TextInputEventArgs e)
        {
            KeyDown?.Invoke(sender, new KeyEventArgs(e.Key, e.Character));
        }

        private static void ReceiveTextEditing(object sender, TextEditingEventArgs e)
        {
            EditingText?.Invoke(sender, e);
        }

        public static void OnCharEntered(char character)
        {
            CharEntered?.Invoke(null, new CharacterEventArgs(character, 0));
        }
    }
}
