using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    static class NetConfig
    {
        public const int DefaultPort = 14242;

        //UpdateEntity networkevents aren't sent to clients if they're further than this from the entity
        public const float UpdateEntityDistance = 2500.0f;

        public const int MaxPlayers = 16;

        public static string MasterServerUrl = GameMain.Config.MasterServerUrl;

        //if a Character is further than this from the sub, the server will ignore it
        //(in sim units)
        public const float CharacterIgnoreDistance = 300.0f;

        //if a ragdoll is further than this from the correct position, teleport it there
        //(in sim units)
        public const float ResetRagdollDistance = 2.0f;

        //if the ragdoll is closer than this, don't try to correct its position
        public const float AllowedRagdollDistance = 0.1f;

        public const float LargeCharacterUpdateInterval = 5.0f;

        public const float DeleteDisconnectedTime = 10.0f;

        public const float IdSendInterval = 0.2f;
        public const float RerequestInterval = 0.2f;

        public const int ReliableMessageBufferSize = 500;
        public const int ResendAttempts = 10;
    }
}
