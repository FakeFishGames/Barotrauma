using OpenTK.Audio.OpenAL;

namespace Barotrauma
{
    abstract class Sound
    {
        public SoundManager Owner
        {
            get;
            private set;
        }


    }
}

