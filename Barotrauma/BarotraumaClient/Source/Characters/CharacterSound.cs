using System;
using System.Xml.Linq;
using Barotrauma.Sounds;

namespace Barotrauma
{
    class CharacterSound
    {
        public enum SoundType
        {
            Idle, Attack, Die, Damage
        }

        private readonly RoundSound roundSound;
        public readonly CharacterSoundParams Params;

        public SoundType Type => Params.State;
        public Gender Gender => Params.Gender;
        public float Volume => roundSound.Volume;
        public float Range => roundSound.Range;
        public Sound Sound => roundSound?.Sound;

        public CharacterSound(CharacterSoundParams soundParams)
        {
            Params = soundParams;
            roundSound = Submarine.LoadRoundSound(soundParams.Element);
        }
    }
}
