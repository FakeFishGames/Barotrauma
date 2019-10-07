using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Xna.Framework
{
    public static class MessageBox
    {
        [Flags]
        public enum Flags : uint
        {
            Error = 0x00000010,
            Warning = 0x00000020,
            Information = 0x00000040
        }

        public static void Show(Flags flags, string title, string message, GameWindow window = null)
        {
            Sdl.ShowSimpleMessageBox((uint)flags, title, message, window?.Handle ?? IntPtr.Zero);
        }

        public static void ShowWrapped(Flags flags, string title, string message, int charsPerLine = 60, GameWindow window = null)
        {
            string[] split = message.Split(' ');
            if (split.Length > 0)
            {
                message = split[0];
                string currLine = message;
                for (int i = 1; i < split.Length; i++)
                {
                    currLine += " " + split[i];
                    if (currLine.Length > charsPerLine)
                    {
                        currLine = split[i];
                        message += "\n" + split[i];
                    }
                    else
                    {
                        message += " " + split[i];
                    }
                }
            }

            Show(flags, title, message, window);
        }
    }
}
