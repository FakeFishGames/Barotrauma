using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MissionAction : EventAction
    {
        private record class MissionUnlock(Mission Mission, bool CreateUnlockNotification);

        private static readonly HashSet<MissionUnlock> missionsUnlockedThisRound = new HashSet<MissionUnlock>();

        public static void ResetMissionsUnlockedThisRound()
        {
            missionsUnlockedThisRound.Clear();
        }

        public static void WriteMissionsUnlockedThisRound(Client client)
        {
            foreach (MissionUnlock missionUnlock in missionsUnlockedThisRound)
            {
                WriteMissionUnlock(missionUnlock.Mission, client, missionUnlock.CreateUnlockNotification);
            }
        }

        private static void WriteMissionUnlock(Mission mission, bool createUnlockNotification)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                WriteMissionUnlock(mission, client, createUnlockNotification);
            }
        }

        private static void WriteMissionUnlock(Mission mission, Client client, bool createUnlockNotification)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.MISSION);
            outmsg.WriteIdentifier(mission.Prefab.Identifier);
            outmsg.WriteInt32(GameMain.GameSession?.Map?.Locations.IndexOf(mission.Locations[0]) ?? -1);
            outmsg.WriteInt32(GameMain.GameSession?.Map?.Locations.IndexOf(mission.Locations[1]) ?? -1);
            outmsg.WriteInt32(mission.Locations[0].GetAvailableMissionIndex(mission));
            outmsg.WriteBoolean(createUnlockNotification);
            outmsg.WriteString(mission.Name.Value);
            outmsg.WriteBoolean(mission.Locations[0].Discovered);
            GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);            
        }
    }
}