using Barotrauma.Sounds;

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        protected static Sound[] sparkSounds;

        private bool powerOnSoundPlayed;

        private static Sound powerOnSound;

        public static void ClearSounds()
        {
            for (int i = 0; i < 4; i++)
            {
                sparkSounds[i].Dispose();
            }
            sparkSounds = null;

            powerOnSound.Dispose(); powerOnSound = null;
        }
    }
}
