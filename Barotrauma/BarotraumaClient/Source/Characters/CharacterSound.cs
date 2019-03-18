using System;
using System.Xml.Linq;
using Barotrauma.Sounds;

namespace Barotrauma
{
    class CharacterSound
    {
        public enum SoundType
        {
            Idle, Attack, Die
        }

        private readonly RoundSound roundSound;

        public readonly SoundType Type;

        public float Volume
        {
            get { return roundSound.Volume; }
        }
        public float Range
        {
            get { return roundSound.Range; }
        }
        public Sound Sound
        {
            get { return roundSound.Sound; }
        }

        public CharacterSound(XElement element)
        {
            roundSound = Submarine.LoadRoundSound(element);
            Enum.TryParse<SoundType>(element.GetAttributeString("state", "Idle"), true, out Type);
        }
    }
}
