using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Server-side handler for collaborative submarine editor sessions.
    /// Relays messages between connected editors and manages session state.
    /// </summary>
    sealed partial class GameServer
    {
        private SubEditorNetworkingShared subEditorSession;
        private bool isSubEditorSessionActive;
        private Client subEditorHost;

        /// <summary>
        /// Handle incoming SUBEDITOR packets from clients.
        /// </summary>
        private void ReadSubEditorMessage(IReadMessage inc, Client sender)
        {
            if (sender == null) return;

            SubEditorPacketHeader subHeader = (SubEditorPacketHeader)inc.ReadByte();

            switch (subHeader)
            {
                case SubEditorPacketHeader.CursorPosition:
                    HandleCursorPosition(inc, sender);
                    break;
                case SubEditorPacketHeader.EntitySelection:
                    HandleEntitySelection(inc, sender);
                    break;
                case SubEditorPacketHeader.EntityDeselection:
                    HandleEntityDeselection(inc, sender);
                    break;
                case SubEditorPacketHeader.StartTestMode:
                    HandleStartTestMode(inc, sender);
                    break;
                case SubEditorPacketHeader.EndTestMode:
                    HandleEndTestMode(inc, sender);
                    break;
                case SubEditorPacketHeader.SyncSubmarine:
                    HandleSubmarineSync(inc, sender);
                    break;
                default:
                    DebugConsole.AddWarning($"Unknown SubEditor packet header: {subHeader}");
                    break;
            }
        }

        /// <summary>
        /// Start a collaborative editor session. Called when host starts hosting.
        /// </summary>
        public void StartSubEditorSession(Client host)
        {
            if (isSubEditorSessionActive)
            {
                DebugConsole.AddWarning("[SubEditor] Session already active");
                return;
            }

            subEditorSession = new SubEditorNetworkingShared();
            subEditorHost = host;
            isSubEditorSessionActive = true;

            // Add the host as the first editor
            byte hostColorIndex = 0;
            var hostUser = new SubEditorUser((byte)host.SessionId, host.Name, hostColorIndex);
            subEditorSession.AddUser(hostUser);

            DebugConsole.NewMessage($"[SubEditor] Session started by {host.Name}", Microsoft.Xna.Framework.Color.LightGreen);

            // Notify the host that session started
            SendSubEditorClientList();
        }

        /// <summary>
        /// End the collaborative editor session.
        /// </summary>
        public void EndSubEditorSession()
        {
            if (!isSubEditorSessionActive) return;

            // Notify all clients that session is ending
            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EndTestMode);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }

            subEditorSession?.Clear();
            subEditorSession = null;
            subEditorHost = null;
            isSubEditorSessionActive = false;

            DebugConsole.NewMessage("[SubEditor] Session ended", Microsoft.Xna.Framework.Color.Yellow);
        }

        /// <summary>
        /// Add a client to the editor session.
        /// </summary>
        public void AddClientToSubEditorSession(Client client)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            // Assign next available color
            byte colorIndex = (byte)(subEditorSession.ConnectedEditors.Count % SubEditorUser.UserColors.Length);
            var user = new SubEditorUser((byte)client.SessionId, client.Name, colorIndex);
            subEditorSession.AddUser(user);

            DebugConsole.NewMessage($"[SubEditor] {client.Name} joined the session", Microsoft.Xna.Framework.Color.LightGreen);

            // Send updated client list to everyone
            SendSubEditorClientList();
        }

        /// <summary>
        /// Remove a client from the editor session.
        /// </summary>
        public void RemoveClientFromSubEditorSession(Client client)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            subEditorSession.RemoveUser((byte)client.SessionId);

            DebugConsole.NewMessage($"[SubEditor] {client.Name} left the session", Microsoft.Xna.Framework.Color.Yellow);

            // If the host left, end the session
            if (client == subEditorHost)
            {
                EndSubEditorSession();
            }
            else
            {
                SendSubEditorClientList();
            }
        }

        /// <summary>
        /// Send the current client list to all connected editors.
        /// </summary>
        private void SendSubEditorClientList()
        {
            if (subEditorSession == null) return;

            var users = subEditorSession.ConnectedEditors.Values.ToList();

            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.ClientList);
                msg.WriteByte((byte)users.Count);

                foreach (var user in users)
                {
                    msg.WriteNetSerializableStruct(user);
                }

                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        /// <summary>
        /// Handle cursor position updates and relay to other clients.
        /// </summary>
        private void HandleCursorPosition(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            var cursorData = INetSerializableStruct.Read<SubEditorCursorData>(inc);

            // Update server's tracking
            subEditorSession.CursorPositions[(byte)sender.SessionId] = 
                new Microsoft.Xna.Framework.Vector2(cursorData.WorldX, cursorData.WorldY);

            // Relay to other clients
            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.CursorPosition);
                msg.WriteNetSerializableStruct(cursorData);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Unreliable);
            }
        }

        /// <summary>
        /// Handle entity selection and relay lock to other clients.
        /// </summary>
        private void HandleEntitySelection(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            var selectionData = INetSerializableStruct.Read<SubEditorSelectionData>(inc);

            // Try to lock the entity
            if (subEditorSession.TryLockEntity(selectionData.EntityId, (byte)sender.SessionId))
            {
                // Relay to other clients
                foreach (var client in connectedClients)
                {
                    if (client == sender) continue;

                    IWriteMessage msg = new WriteOnlyMessage();
                    msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                    msg.WriteByte((byte)SubEditorPacketHeader.EntitySelection);
                    msg.WriteNetSerializableStruct(selectionData);
                    serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
                }

                // Confirm to sender
                IWriteMessage confirmMsg = new WriteOnlyMessage();
                confirmMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                confirmMsg.WriteByte((byte)SubEditorPacketHeader.EditConfirm);
                confirmMsg.WriteUInt16(selectionData.EntityId);
                serverPeer?.Send(confirmMsg, sender.Connection, DeliveryMethod.Reliable);
            }
            else
            {
                // Deny - entity already locked
                IWriteMessage denyMsg = new WriteOnlyMessage();
                denyMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                denyMsg.WriteByte((byte)SubEditorPacketHeader.EditDeny);
                denyMsg.WriteUInt16(selectionData.EntityId);
                serverPeer?.Send(denyMsg, sender.Connection, DeliveryMethod.Reliable);
            }
        }

        /// <summary>
        /// Handle entity deselection and relay unlock to other clients.
        /// </summary>
        private void HandleEntityDeselection(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            var selectionData = INetSerializableStruct.Read<SubEditorSelectionData>(inc);

            // Unlock the entity
            subEditorSession.UnlockEntity(selectionData.EntityId, (byte)sender.SessionId);

            // Relay to other clients
            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityDeselection);
                msg.WriteNetSerializableStruct(selectionData);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        /// <summary>
        /// Handle test mode start request (host only).
        /// </summary>
        private void HandleStartTestMode(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            // Only host can start test mode
            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to start test mode");
                return;
            }

            // Notify all clients to enter test mode
            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.StartTestMode);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }

            DebugConsole.NewMessage("[SubEditor] Test mode started", Microsoft.Xna.Framework.Color.Cyan);
        }

        /// <summary>
        /// Handle test mode end request (host only).
        /// </summary>
        private void HandleEndTestMode(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            // Only host can end test mode
            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to end test mode");
                return;
            }

            // Notify all clients to return to editor
            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EndTestMode);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }

            DebugConsole.NewMessage("[SubEditor] Test mode ended", Microsoft.Xna.Framework.Color.Yellow);
        }

        /// <summary>
        /// Handle submarine sync from host - relay to all other clients.
        /// </summary>
        private void HandleSubmarineSync(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            // Only host can sync submarine
            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to sync submarine");
                return;
            }

            // Read the submarine XML
            string submarineXml = inc.ReadString();

            // Relay to all other clients
            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
                msg.WriteString(submarineXml);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }

            DebugConsole.NewMessage($"[SubEditor] Submarine synced from host ({submarineXml.Length} chars)", Microsoft.Xna.Framework.Color.LightGreen);
        }
    }
}
