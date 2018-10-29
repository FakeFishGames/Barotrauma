using Barotrauma.Networking;
using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    partial class BanList
    {
        public void ServerAdminWrite(NetBuffer outMsg, Client c)
        {
            if (!c.HasPermission(ClientPermissions.Ban))
            {
                outMsg.Write(false); outMsg.WritePadBits();
                return;
            }
            outMsg.Write(true);
            if (c.Connection == GameMain.Server.OwnerConnection)
            {
                outMsg.Write(true);
            }
            else
            {
                outMsg.Write(false);
            }

            outMsg.WritePadBits();
            outMsg.WriteVariableInt32(bannedPlayers.Count);
            for (int i=0;i<bannedPlayers.Count;i++)
            {
                BannedPlayer bannedPlayer = bannedPlayers[i];

                outMsg.Write(bannedPlayer.Name);
                if (c.Connection == GameMain.Server.OwnerConnection)
                {
                    outMsg.Write(bannedPlayer.IP);
                    outMsg.Write(bannedPlayer.SteamID);
                }
            }
        }
    }
}
