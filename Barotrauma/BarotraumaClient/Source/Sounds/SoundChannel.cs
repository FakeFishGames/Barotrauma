using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma
{
    class SoundChannel
    {
        private Sound sound;
        private int alSourceIndex;

        public bool Stream
        {
            get;
            private set;
        }

        private bool isPlaying;
        public bool IsPlaying
        {
            get { return isPlaying; }
            set { /* TODO: implement */ }
        }

        public SoundChannel(Sound sound,bool stream)
        {

            Stream = stream;
        }

        public void UpdateStream()
        {
            //TODO: implement
        }
    }
}
