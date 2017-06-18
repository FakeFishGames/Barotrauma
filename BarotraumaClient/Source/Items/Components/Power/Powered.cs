using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Powered : ItemComponent
    {
        protected static Sound[] sparkSounds;

        private bool powerOnSoundPlayed;

        private static Sound powerOnSound;
    }
}
