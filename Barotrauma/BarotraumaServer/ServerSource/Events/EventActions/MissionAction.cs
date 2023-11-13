using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MissionAction : EventAction
    {
        private static readonly HashSet<Mission> missionsUnlockedThisRound = new HashSet<Mission>();

        public static void ResetMissionsUnlockedThisRound()
        {
            missionsUnlockedThisRound.Clear();
        }

        public static void NotifyMissionsUnlockedThisRound(Client client)
        {
            foreach (Mission mission in missionsUnlockedThisRound)
            {
                NotifyMissionUnlock(mission, client);
            }
        }

        private static void NotifyMissionUnlock(Mission mission)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                NotifyMissionUnlock(mission, client);
            }
        }

        private static void NotifyMissionUnlock(Mission mission, Client client)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.MISSION);
            outmsg.WriteIdentifier(mission.Prefab.Identifier);
            outmsg.WriteInt32(GameMain.GameSession?.Map?.Locations.IndexOf(mission.Locations[0]) ?? -1);
            outmsg.WriteInt32(GameMain.GameSession?.Map?.Locations.IndexOf(mission.Locations[1]) ?? -1);
            outmsg.WriteString(mission.Name.Value);
            GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);            
        }
    }
}