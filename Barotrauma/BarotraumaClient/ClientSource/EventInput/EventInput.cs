using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace EventInput
{
    public class CharacterEventArgs : EventArgs
    {
        private readonly char character;
        private readonly long lParam;

        public CharacterEventArgs(char character, long lParam)
        {
            this.character = character;
            this.lParam = lParam;
        }

        public char Character
        {
            get { return character; }
        }

        public long Param
        {
            get { return lParam; }
        }

        public long RepeatCount
        {
            get { return lParam & 0xffff; }
        }

        public bool ExtendedKey
        {
            get { return (lParam & (1 << 24)) > 0; }
        }

        public bool AltPressed
        {
            get { return (lParam & (1 << 29)) > 0; }
        }

        public bool PreviousState
        {
            get { return (lParam & (1 << 30)) > 0; }
        }

        public bool TransitionState
        {
            get { return (lParam & (1 << 31)) > 0; }
        }
    }

    public class KeyEventArgs : EventArgs
    {
        private Keys keyCode;

        public KeyEventArgs(Keys keyCode)
        {
            this.keyCode = keyCode;
        }

        public Keys KeyCode
        {
            get { return keyCode; }
        }
    }

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


#if !WINDOWS
        /// <summary>
        /// Raised when the user is editing text and IME is in progress. 
        /// Windows build uses ImeSharp instead because SDL2's IME implementation is broken on Windows (https://github.com/libsdl-org/SDL/issues/2243)
        /// </summary>
        public static event EditingTextHandler EditingText;
#endif

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
#if !WINDOWS
            window.TextEditing += ReceiveTextEditing;
#endif

            initialized = true;
        }

        private static void ReceiveInput(object sender, TextInputEventArgs e)
        {
            OnCharEntered(e.Character);
            KeyDown?.Invoke(sender, new KeyEventArgs(e.Key));
        }

#if !WINDOWS
        private static void ReceiveTextEditing(object sender, TextEditingEventArgs e)
        {
            EditingText?.Invoke(sender, e);
        }
#endif

        public static void OnCharEntered(char character)
        {
            CharEntered?.Invoke(null, new CharacterEventArgs(character, 0));
        }
    }
}
