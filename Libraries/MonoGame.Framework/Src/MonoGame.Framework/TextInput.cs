#nullable enable

namespace Microsoft.Xna.Framework
{
    public static class TextInput
    {
        public static bool IsTextInputShown()
        {
            return Sdl.SDL_IsTextInputShown();
        }

        public static void StartTextInput()
        {
            Sdl.SDL_StartTextInput();
        }

        public static void StopTextInput()
        {
            Sdl.SDL_StopTextInput();
        }

        public static void SetTextInputRect(Rectangle rectangle)
        {
            Sdl.Rectangle r = new Sdl.Rectangle
            {
                X = rectangle.X,
                Y = rectangle.Y,
                Width = rectangle.Width,
                Height = rectangle.Height
            };
            Sdl.SDL_SetTextInputRect(ref r);
        }
    }
}
