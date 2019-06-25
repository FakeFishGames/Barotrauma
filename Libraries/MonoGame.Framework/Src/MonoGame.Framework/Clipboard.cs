//TODO: maybe make this compile even if SDL isn't available

namespace Microsoft.Xna.Framework
{
    public static class Clipboard
    {
        public static string GetText()
        {
            return Sdl.GetClipboardText();
        }

        public static void SetText(string text)
        {
            Sdl.SetClipboardText(text);
        }
    }
}
