using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public bool MirrorLevel
        {
            get;
            protected set;
        }

        public override void ShowStartMessage()
        {
            if (Mission == null) return;

            GameServer.Log(TextManager.Get("Mission") + ": " + Mission.Name, Networking.ServerLog.MessageType.ServerMessage);
            GameServer.Log(Mission.Description, Networking.ServerLog.MessageType.ServerMessage);
        }
    }
}
