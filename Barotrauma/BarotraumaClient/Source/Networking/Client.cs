using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    struct TempClient
    {
        public string Name;
        public byte ID;
        public UInt16 CharacterID;
    }

    partial class Client : IDisposable
    {
        public VoipSound VoipSound
        {
            get;
            set;
        }
        
        public void UpdateSoundPosition()
        {
            if (VoipSound != null)
            {
                if (!VoipSound.IsPlaying)
                {
                    DebugConsole.NewMessage("destroying voipsound", Color.Lime);
                    VoipSound.Dispose();
                    VoipSound = null;
                    return;
                }

                if (character != null)
                {
                    VoipSound.SetPosition(new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0.0f));
                }
                else
                {
                    VoipSound.SetPosition(null);
                }
            }
        }

        partial void InitProjSpecific()
        {
            VoipQueue = null; VoipSound = null;
            if (ID == GameMain.Client.ID) return;
            VoipQueue = new VoipQueue(ID, false, true);
            GameMain.Client.VoipClient.RegisterQueue(VoipQueue);
            VoipSound = null;
        }

        partial void DisposeProjSpecific()
        {
            if (VoipQueue != null)
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
