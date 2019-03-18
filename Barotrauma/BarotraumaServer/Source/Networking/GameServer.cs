using Lidgren.Network;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        void InitProjSpecific()
        {
            //do nothing
        }

        void InitUPnP()
        {
            server.UPnP.ForwardPort(NetPeerConfiguration.Port, "barotrauma");
            if (Steam.SteamManager.USE_STEAM)
            {
                server.UPnP.ForwardPort(QueryPort, "barotrauma");
            }
        }

        bool DiscoveringUPnP()
        {
            return server.UPnP.Status == UPnPStatus.Discovering;
        }

        void FinishUPnP()
        {
            //do nothing
        }
    }
}
