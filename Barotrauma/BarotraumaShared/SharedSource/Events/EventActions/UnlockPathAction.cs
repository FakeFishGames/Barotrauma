using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    /// <summary>
    /// Unlocks a "locked" pathways between locations, if there are any such paths adjacent to the current location.
    /// </summary>
    class UnlockPathAction : EventAction
    {
        private static readonly HashSet<LocationConnection> pathsUnlockedThisRound = new HashSet<LocationConnection>();

        public static void ResetPathsUnlockedThisRound()
        {
            pathsUnlockedThisRound.Clear();
        }

        public UnlockPathAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            if (GameMain.GameSession?.Map?.CurrentLocation?.Connections != null)
            {
                foreach (LocationConnection connection in GameMain.GameSession?.Map?.CurrentLocation?.Connections)
                {
                    if (!connection.Locked) { continue; }
                    connection.Locked = false;
                    pathsUnlockedThisRound.Add(connection);
#if SERVER
                    NotifyUnlock(connection);
#else
                    new GUIMessageBox(string.Empty, TextManager.Get("pathunlockedgeneric"),
                        Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, iconStyle: "UnlockPathIcon", relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128));
#endif
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(UnlockPathAction)}";
        }

#if SERVER
        public static void NotifyPathsUnlockedThisRound(Client client)
        {
            foreach (LocationConnection connection in pathsUnlockedThisRound)
            {
                NotifyUnlock(connection, client);
            }
        }
        
        private static void NotifyUnlock(LocationConnection connection)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                NotifyUnlock(connection, client);
            }
        }

        private static void NotifyUnlock(LocationConnection connection, Client client)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.UNLOCKPATH);
            outmsg.WriteUInt16((UInt16)GameMain.GameSession.Map.Connections.IndexOf(connection));
            GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);            
        }
#endif
    }
}