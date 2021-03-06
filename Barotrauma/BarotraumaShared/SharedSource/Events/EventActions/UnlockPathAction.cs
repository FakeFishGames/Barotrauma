using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class UnlockPathAction : EventAction
    {
        public UnlockPathAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

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
            GameMain.GameSession?.Map?.CurrentLocation?.Connections.ForEach(c => c.Locked = false);
            if (GameMain.GameSession?.Map?.CurrentLocation?.Connections != null)
            {
                foreach (LocationConnection connection in GameMain.GameSession?.Map?.CurrentLocation?.Connections)
                {
                    if (!connection.Locked) { continue; }
#if SERVER
                    NotifyUnlock(connection);
#else
                    connection.Locked = false;
                    new GUIMessageBox(string.Empty, TextManager.Get("pathunlockedgeneric"),
                        new string[0], type: GUIMessageBox.Type.InGame, iconStyle: "UnlockPathIcon", relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128));
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
        private void NotifyUnlock(LocationConnection connection)
        {
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                IWriteMessage outmsg = new WriteOnlyMessage();
                outmsg.Write((byte)ServerPacketHeader.EVENTACTION);
                outmsg.Write((byte)EventManager.NetworkEventType.UNLOCKPATH);
                outmsg.Write((UInt16)GameMain.GameSession.Map.Connections.IndexOf(connection));
                GameMain.Server.ServerPeer.Send(outmsg, client.Connection, DeliveryMethod.Reliable);
            }
        }
#endif
    }
}