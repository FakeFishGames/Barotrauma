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
        public readonly CharacterParams.SoundParams Params;

        public SoundType Type => Params.State;
        public Gender Gender => Params.Gender;
        public float Volume => roundSound.Volume;
        public float Range => roundSound.Range;
        public Sound Sound => roundSound?.Sound;

        public CharacterSound(CharacterParams.SoundParams soundParams)
        {
            Params = soundParams;
            roundSound = Submarine.LoadRoundSound(soundParams.Element);
        }
    }
}
