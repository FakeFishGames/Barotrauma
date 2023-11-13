using System;
namespace Microsoft.Xna.Framework
{
    public sealed class TextEditingEventArgs : EventArgs
    {
        public readonly string Text;
        public readonly int Start;
        public readonly int Length;

        public TextEditingEventArgs(string text, int start, int length)
        {
            Text = text;
            Start = start;
            Length = length;
        }
    }
}
