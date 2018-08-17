using Barotrauma.Sounds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    partial class Client
    {
        public VoipSound VoipSound
        {
            get;
            private set;
        }

        partial void InitVoipProjSpecific()
        {
            if (GameMain.Client != null)
            {
                GameMain.Client.VoipClient.RegisterQueue(VoipQueue);
            }
            VoipSound = new VoipSound(GameMain.SoundManager,VoipQueue);
        }

        partial void DisposeProjSpecific()
        {
            if (GameMain.Client != null)
            {
                GameMain.Client.VoipClient.UnregisterQueue(VoipQueue);
            }
            if (VoipSound != null)
            {
                VoipSound.Dispose();
                VoipSound = null;
            }
        }
    }
}
