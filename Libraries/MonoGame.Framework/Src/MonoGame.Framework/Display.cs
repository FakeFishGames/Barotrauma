namespace Microsoft.Xna.Framework
{
    public static class Display
    {
        public static int GetNumberOfDisplays()
            => Sdl.Display.GetNumVideoDisplays();

        public static string GetDisplayName(int displayIndex)
            => Sdl.Display.GetDisplayName(displayIndex);
    }
}
