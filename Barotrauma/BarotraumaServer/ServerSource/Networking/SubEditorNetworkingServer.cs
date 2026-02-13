using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    sealed partial class GameServer
    {
        private SubEditorNetworkingShared subEditorSession;
        private bool isSubEditorSessionActive;
        private Client subEditorHost;
        private string subEditorCurrentSubPath;
        private string subEditorCurrentSubName;
        private string subEditorStoredSubmarineXml;
        private byte[] subEditorStoredSubmarineCompressed;
        private int subEditorStoredSubmarineUncompressedLength;

        private const double SubEditorResyncCooldown = 0.5;
        private readonly Dictionary<byte, double> subEditorLastResyncTime = new Dictionary<byte, double>();

        public bool IsSubEditorSessionActive => isSubEditorSessionActive;
        
        public Client SubEditorHost => subEditorHost;

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
                case SubEditorPacketHeader.SubmarineInfo:
                    HandleSubmarineInfo(inc, sender);
                    break;
                case SubEditorPacketHeader.EntityPlaced:
                    HandleEntityPlaced(inc, sender);
                    break;
                case SubEditorPacketHeader.EntityRemoved:
                    HandleEntityRemoved(inc, sender);
                    break;
                case SubEditorPacketHeader.EntityMoved:
                    HandleEntityMoved(inc, sender);
                    break;
                case SubEditorPacketHeader.EntitiesMovedBatch:
                    HandleEntitiesMovedBatch(inc, sender);
                    break;
                case SubEditorPacketHeader.EntityPropertyChanged:
                    HandleEntityPropertyChanged(inc, sender);
                    break;
                case SubEditorPacketHeader.EntityUpdated:
                    HandleEntityUpdated(inc, sender);
                    break;
                case SubEditorPacketHeader.CursorMoved:
                    HandleCursorMoved(inc, sender);
                    break;
                case SubEditorPacketHeader.RequestSubmarineFile:
                    HandleRequestSubmarineFile(inc, sender);
                    break;
                case SubEditorPacketHeader.SetPermissions:
                    HandleSetPermissions(inc, sender);
                    break;
                default:
                    DebugConsole.AddWarning($"Unknown SubEditor packet header: {subHeader}");
                    break;
            }
        }

        private SubEditorPermissions GetSenderPermissions(Client sender)
        {
            if (subEditorSession == null) return SubEditorPermissions.None;
            var perms = subEditorSession.GetPermissions((byte)sender.SessionId);
            DebugConsole.Log($"[SubEditor] GetSenderPermissions({sender.Name}, session={sender.SessionId}): hostSession={subEditorSession.HostSessionId}, result={perms}");
            return perms;
        }

        private string GetSenderAccountId(Client sender)
        {
            return sender.AccountId.TryUnwrap(out var accountId) ? accountId.StringRepresentation : sender.Name;
        }

        private void ResyncClientState(Client sender)
        {
            if (subEditorStoredSubmarineCompressed == null || subEditorStoredSubmarineCompressed.Length == 0) return;

            byte sessionId = (byte)sender.SessionId;
            double now = Timing.TotalTime;
            if (subEditorLastResyncTime.TryGetValue(sessionId, out double lastTime) && now - lastTime < SubEditorResyncCooldown)
            {
                return;
            }
            subEditorLastResyncTime[sessionId] = now;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
            msg.WriteInt32(subEditorStoredSubmarineCompressed.Length);
            msg.WriteInt32(subEditorStoredSubmarineUncompressedLength);
            msg.WriteBytes(subEditorStoredSubmarineCompressed, 0, subEditorStoredSubmarineCompressed.Length);
            serverPeer.Send(msg, sender.Connection, DeliveryMethod.Reliable);
        }

        private void HandleEntityPlaced(IReadMessage inc, Client sender)
        {
            string entityXml = inc.ReadString();

            var perms = GetSenderPermissions(sender);
            DebugConsole.NewMessage($"[SubEditor] HandleEntityPlaced from {sender.Name} (session={sender.SessionId}), perms={perms}", Color.Cyan);
            if (!perms.HasFlag(SubEditorPermissions.CanEditOwn))
            {
                DebugConsole.NewMessage($"[SubEditor] DENIED entity placement from {sender.Name} — missing CanEditOwn", Color.Red);
                ResyncClientState(sender);
                return;
            }

            // Track ownership: try to extract entity ID from XML
            if (subEditorSession != null)
            {
                try
                {
                    var xElement = System.Xml.Linq.XElement.Parse(entityXml);
                    int id = xElement.GetAttributeInt("ID", 0);
                    if (id > 0)
                    {
                        subEditorSession.SetEntityOwner((ushort)id, GetSenderAccountId(sender));
                    }
                }
                catch { }
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityPlaced);
                msg.WriteByte((byte)sender.SessionId);
                msg.WriteString(entityXml);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleEntityRemoved(IReadMessage inc, Client sender)
        {
            ushort entityId = inc.ReadUInt16();

            if (subEditorSession != null)
            {
                string accountId = GetSenderAccountId(sender);
                if (!subEditorSession.CanUserDeleteEntity((byte)sender.SessionId, entityId, accountId))
                {
                    ResyncClientState(sender);
                    return;
                }
                subEditorSession.RemoveEntityOwnership(entityId);
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityRemoved);
                msg.WriteByte((byte)sender.SessionId);
                msg.WriteUInt16(entityId);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleEntityMoved(IReadMessage inc, Client sender)
        {
            ushort entityId = inc.ReadUInt16();
            float posX = inc.ReadSingle();
            float posY = inc.ReadSingle();

            if (subEditorSession != null)
            {
                string accountId = GetSenderAccountId(sender);
                if (!subEditorSession.CanUserEditEntity((byte)sender.SessionId, entityId, accountId))
                {
                    ResyncClientState(sender);
                    return;
                }
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityMoved);
                msg.WriteByte((byte)sender.SessionId);
                msg.WriteUInt16(entityId);
                msg.WriteSingle(posX);
                msg.WriteSingle(posY);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleEntitiesMovedBatch(IReadMessage inc, Client sender)
        {
            ushort count = inc.ReadUInt16();
            var moves = new List<(ushort entityId, float dx, float dy)>(count);
            for (int i = 0; i < count; i++)
            {
                ushort entityId = inc.ReadUInt16();
                float dx = inc.ReadSingle();
                float dy = inc.ReadSingle();
                moves.Add((entityId, dx, dy));
            }

            if (subEditorSession != null)
            {
                var perms = GetSenderPermissions(sender);
                if (subEditorSession.IsMassEdit(moves.Count) && !perms.HasFlag(SubEditorPermissions.CanMassEdit))
                {
                    ResyncClientState(sender);
                    return;
                }
                string accountId = GetSenderAccountId(sender);
                moves.RemoveAll(m => !subEditorSession.CanUserEditEntity((byte)sender.SessionId, m.entityId, accountId));
                if (moves.Count == 0)
                {
                    ResyncClientState(sender);
                    return;
                }
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntitiesMovedBatch);
                msg.WriteByte((byte)sender.SessionId);
                msg.WriteUInt16((ushort)moves.Count);
                foreach (var (entityId, dx, dy) in moves)
                {
                    msg.WriteUInt16(entityId);
                    msg.WriteSingle(dx);
                    msg.WriteSingle(dy);
                }
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleEntityPropertyChanged(IReadMessage inc, Client sender)
        {
            ushort entityId = inc.ReadUInt16();
            string propName = inc.ReadString();
            string propValue = inc.ReadString();

            if (subEditorSession != null)
            {
                string accountId = GetSenderAccountId(sender);
                if (!subEditorSession.CanUserEditEntity((byte)sender.SessionId, entityId, accountId))
                {
                    ResyncClientState(sender);
                    return;
                }
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityPropertyChanged);
                msg.WriteByte((byte)sender.SessionId);
                msg.WriteUInt16(entityId);
                msg.WriteString(propName);
                msg.WriteString(propValue);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleEntityUpdated(IReadMessage inc, Client sender)
        {
            ushort entityId = inc.ReadUInt16();
            string entityXml = inc.ReadString();

            if (subEditorSession != null)
            {
                string accountId = GetSenderAccountId(sender);
                if (!subEditorSession.CanUserEditEntity((byte)sender.SessionId, entityId, accountId))
                {
                    ResyncClientState(sender);
                    return;
                }
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityUpdated);
                msg.WriteByte((byte)sender.SessionId);
                msg.WriteUInt16(entityId);
                msg.WriteString(entityXml);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleCursorMoved(IReadMessage inc, Client sender)
        {
            float posX = inc.ReadSingle();
            float posY = inc.ReadSingle();

            byte senderSessionId = (byte)sender.SessionId;

            if (subEditorSession != null)
            {
                subEditorSession.CursorPositions[senderSessionId] = new Vector2(posX, posY);
            }

            foreach (var client in connectedClients.Where(c => c != sender))
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.CursorMoved);
                msg.WriteByte(senderSessionId);
                msg.WriteSingle(posX);
                msg.WriteSingle(posY);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Unreliable);
            }
        }

        private void HandleRequestSubmarineFile(IReadMessage inc, Client sender)
        {
            string subName = inc.ReadString();
            
            var subInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
            if (subInfo != null && System.IO.File.Exists(subInfo.FilePath))
            {
                FileSender.StartTransfer(sender.Connection, FileTransferType.Submarine, subInfo.FilePath);
            }
            else if (!string.IsNullOrEmpty(subEditorCurrentSubPath) && System.IO.File.Exists(subEditorCurrentSubPath))
            {
                FileSender.StartTransfer(sender.Connection, FileTransferType.Submarine, subEditorCurrentSubPath);
            }
            else
            {
                DebugConsole.AddWarning($"[SubEditor] Could not find submarine '{subName}' to send to {sender.Name}");
            }
        }

        private void HandleSetPermissions(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;
            if (sender != subEditorHost)
            {
                DebugConsole.NewMessage($"[SubEditor] SetPermissions REJECTED: sender {sender.Name} is not host ({subEditorHost?.Name})", Color.Red);
                return;
            }

            byte targetSessionId = inc.ReadByte();
            uint permBits = inc.ReadUInt32();
            var permissions = (SubEditorPermissions)permBits;

            DebugConsole.NewMessage($"[SubEditor] SetPermissions: target session={targetSessionId}, perms={permissions} (bits={permBits})", Color.Yellow);
            subEditorSession.SetPermissions(targetSessionId, permissions);

            // Verify it stuck
            var verify = subEditorSession.GetPermissions(targetSessionId);
            DebugConsole.NewMessage($"[SubEditor] Verified permissions for session {targetSessionId}: {verify}", Color.Yellow);

            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.SetPermissions);
                msg.WriteByte(targetSessionId);
                msg.WriteUInt32(permBits);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

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
            
            subEditorSession.HostSessionId = (byte)host.SessionId;
            ServerSettings.VoiceChatEnabled = true;

            byte hostColorIndex = 0;
            var hostUser = new SubEditorUser((byte)host.SessionId, host.Name, hostColorIndex);
            subEditorSession.AddUser(hostUser);

            DebugConsole.NewMessage($"[SubEditor] Session started by {host.Name} (session={host.SessionId}), hostSessionId set to {subEditorSession.HostSessionId}", Color.Green);

            SendSubEditorClientList();
        }

        public void EndSubEditorSession()
        {
            if (!isSubEditorSessionActive) return;

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

            DebugConsole.Log("[SubEditor] Session ended");
        }

        public void AddClientToSubEditorSession(Client client)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            byte sessionId = (byte)client.SessionId;
            
            if (subEditorSession.ConnectedEditors.ContainsKey(sessionId))
            {
                subEditorSession.RemoveUser(sessionId);
            }

            byte colorIndex = (byte)(subEditorSession.ConnectedEditors.Count % SubEditorUser.UserColors.Length);
            var user = new SubEditorUser(sessionId, client.Name, colorIndex);
            subEditorSession.AddUser(user);

            // Grant default permissions to new clients
            subEditorSession.SetPermissions(sessionId, SubEditorNetworkingShared.DefaultClientPermissions);

            DebugConsole.NewMessage($"[SubEditor] {client.Name} joined (session={sessionId}, hostSession={subEditorSession.HostSessionId}), defaultPerms={SubEditorNetworkingShared.DefaultClientPermissions}", Color.Green);

            SendSubEditorClientList();
            
            if (subEditorStoredSubmarineCompressed != null && subEditorStoredSubmarineCompressed.Length > 0)
            {
                IWriteMessage subMsg = new WriteOnlyMessage();
                subMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                subMsg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
                subMsg.WriteInt32(subEditorStoredSubmarineCompressed.Length);
                subMsg.WriteInt32(subEditorStoredSubmarineUncompressedLength);
                subMsg.WriteBytes(subEditorStoredSubmarineCompressed, 0, subEditorStoredSubmarineCompressed.Length);
                serverPeer?.Send(subMsg, client.Connection, DeliveryMethod.Reliable);
            }
            else
            {
                var selectedSub = GameMain.NetLobbyScreen?.SelectedSub;
                if (selectedSub != null)
                {
                    IWriteMessage subMsg = new WriteOnlyMessage();
                    subMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                    subMsg.WriteByte((byte)SubEditorPacketHeader.SubmarineInfo);
                    subMsg.WriteString(selectedSub.Name);
                    subMsg.WriteString(selectedSub.MD5Hash.StringRepresentation);
                    serverPeer?.Send(subMsg, client.Connection, DeliveryMethod.Reliable);
                }
            }
        }

        public void RemoveClientFromSubEditorSession(Client client)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            subEditorSession.RemoveUser((byte)client.SessionId);

            DebugConsole.Log($"[SubEditor] {client.Name} left");

            if (client == subEditorHost)
            {
                EndSubEditorSession();
            }
            else
            {
                SendSubEditorClientList();
            }
        }

        private void SendSubEditorClientList()
        {
            if (subEditorSession == null) return;

            var users = subEditorSession.ConnectedEditors.Values.ToList();
            byte hostSessionId = subEditorHost != null ? (byte)subEditorHost.SessionId : (byte)0;

            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.ClientList);
                msg.WriteByte((byte)users.Count);
                msg.WriteByte(hostSessionId);

                foreach (var user in users)
                {
                    msg.WriteNetSerializableStruct(user);
                }

                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleCursorPosition(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            var cursorData = INetSerializableStruct.Read<SubEditorCursorData>(inc);

            // Use server-assigned SessionId (authoritative)
            byte senderSessionId = (byte)sender.SessionId;

            subEditorSession.CursorPositions[senderSessionId] = 
                new Microsoft.Xna.Framework.Vector2(cursorData.WorldX, cursorData.WorldY);

            // Rebuild with authoritative SessionId
            var correctedCursorData = new SubEditorCursorData(senderSessionId, cursorData.WorldX, cursorData.WorldY);

            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.CursorPosition);
                msg.WriteNetSerializableStruct(correctedCursorData);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Unreliable);
            }
        }

        private void HandleEntitySelection(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            var selectionData = INetSerializableStruct.Read<SubEditorSelectionData>(inc);

            if (subEditorSession.TryLockEntity(selectionData.EntityId, (byte)sender.SessionId))
            {
                foreach (var client in connectedClients)
                {
                    if (client == sender) continue;

                    IWriteMessage msg = new WriteOnlyMessage();
                    msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                    msg.WriteByte((byte)SubEditorPacketHeader.EntitySelection);
                    msg.WriteNetSerializableStruct(selectionData);
                    serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
                }

                IWriteMessage confirmMsg = new WriteOnlyMessage();
                confirmMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                confirmMsg.WriteByte((byte)SubEditorPacketHeader.EditConfirm);
                confirmMsg.WriteUInt16(selectionData.EntityId);
                serverPeer?.Send(confirmMsg, sender.Connection, DeliveryMethod.Reliable);
            }
            else
            {
                IWriteMessage denyMsg = new WriteOnlyMessage();
                denyMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                denyMsg.WriteByte((byte)SubEditorPacketHeader.EditDeny);
                denyMsg.WriteUInt16(selectionData.EntityId);
                serverPeer?.Send(denyMsg, sender.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleEntityDeselection(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            var selectionData = INetSerializableStruct.Read<SubEditorSelectionData>(inc);

            subEditorSession.UnlockEntity(selectionData.EntityId, (byte)sender.SessionId);

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

        /// <summary>Force all clients to return from test mode to SubEditor.</summary>
        public void ReturnToSubEditor()
        {
            Level.IsSubEditorTestMode = false;
            if (!isSubEditorSessionActive)
            {
                DebugConsole.ThrowError("[SubEditor] No SubEditor session is active");
                return;
            }

            if (GameStarted)
            {
                string endMessage = "Returning to SubEditor";
                
                if (connectedClients.Count > 0)
                {
                    IWriteMessage endMsg = new WriteOnlyMessage();
                    endMsg.WriteByte((byte)ServerPacketHeader.ENDGAME);
                    endMsg.WriteByte((byte)CampaignMode.TransitionType.None);
                    endMsg.WriteBoolean(false);
                    endMsg.WriteString(endMessage);
                    endMsg.WriteByte(0);
                    endMsg.WriteByte(0);
                    endMsg.WriteBoolean(false);

                    foreach (Client c in connectedClients)
                    {
                        serverPeer.Send(endMsg, c.Connection, DeliveryMethod.Reliable);
                    }
                }
                
                GameMain.GameSession?.EndRound(endMessage);
                
                EndRoundTimer = 0.0f;
                entityEventManager.Clear();
                
                foreach (Client c in connectedClients)
                {
                    c.ResetSync();
                    c.Character = null;
                    c.HasSpawned = false;
                    c.InGame = false;
                }
                
                RespawnManager = null;
                GameStarted = false;
                
                entityEventManager.Clear();
                Submarine.Unload();
            }

            GameMain.IsSubEditorMode = true;

            foreach (var client in connectedClients)
            {
                SendSubEditorModeMessage(client);
            }
            
            SendSubEditorClientList();
            
            // Send stored submarine to non-host clients for state restoration
            if (subEditorStoredSubmarineCompressed != null && subEditorStoredSubmarineCompressed.Length > 0)
            {
                foreach (var client in connectedClients)
                {
                    if (client == subEditorHost) continue;
                    IWriteMessage subMsg = new WriteOnlyMessage();
                    subMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                    subMsg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
                    subMsg.WriteInt32(subEditorStoredSubmarineCompressed.Length);
                    subMsg.WriteInt32(subEditorStoredSubmarineUncompressedLength);
                    subMsg.WriteBytes(subEditorStoredSubmarineCompressed, 0, subEditorStoredSubmarineCompressed.Length);
                    serverPeer?.Send(subMsg, client.Connection, DeliveryMethod.Reliable);
                }
            }

            DebugConsole.Log("[SubEditor] All clients returned to SubEditor");
            Log("Returned to SubEditor.", ServerLog.MessageType.ServerMessage);
        }

        private void HandleStartTestMode(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to start test mode");
                return;
            }

            StartSubEditorTestRound();
        }

        private void StartSubEditorTestRound()
        {
            Level.IsSubEditorTestMode = true;

            // Notify clients before starting so they can snapshot submarine state
            foreach (var client in connectedClients)
            {
                IWriteMessage startTestMsg = new WriteOnlyMessage();
                startTestMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                startTestMsg.WriteByte((byte)SubEditorPacketHeader.StartTestMode);
                serverPeer?.Send(startTestMsg, client.Connection, DeliveryMethod.Reliable);
            }

            string tempPath = Path.Combine("Submarines", "_SubEditorTestMode.sub");
            if (!System.IO.File.Exists(tempPath))
            {
                DebugConsole.ThrowError($"[SubEditor] Temp sub file not found: {tempPath}");
                Level.IsSubEditorTestMode = false;
                return;
            }

            SubmarineInfo testSubInfo;
            try
            {
                testSubInfo = new SubmarineInfo(tempPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("[SubEditor] Failed to load temp sub for test mode", e);
                Level.IsSubEditorTestMode = false;
                return;
            }

            GameMain.NetLobbyScreen.SelectedSub = testSubInfo;
            SubmarineInfo.AddToSavedSubs(testSubInfo);

            GameMain.IsSubEditorMode = false;
            GameMain.NetLobbyScreen.SelectedModeIdentifier = GameModePreset.Sandbox.Identifier;
            // Keep reporting as SubEditor in server browser during test mode
            ServerSettings.GameModeIdentifier = "subeditor".ToIdentifier();
            ServerSettings.AllowSubVoting = false;
            ServerSettings.AllowModeVoting = false;
            ServerSettings.SelectedLevelDifficulty = 0;

            if (GameMain.NetLobbyScreen.SelectedShuttle == null)
            {
                GameMain.NetLobbyScreen.SelectedShuttle =
                    SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.HasTag(SubmarineTag.Shuttle))
                    ?? GameMain.NetLobbyScreen.SelectedSub;
            }

            foreach (var client in connectedClients)
            {
                client.SetVote(VoteType.Mode, null);
                client.SetVote(VoteType.Sub, null);
            }

            if (GameStarted)
            {
                EndGame(CampaignMode.TransitionType.None, wasSaved: false, missions: Enumerable.Empty<Mission>());
            }
            initiatedStartGame = false;

            var result = TryStartGame();
            if (result != TryStartGameResult.Success)
            {
                DebugConsole.ThrowError($"[SubEditor] Failed to start test mode: {result}");
                GameMain.IsSubEditorMode = true;
                Level.IsSubEditorTestMode = false;
                return;
            }

            DebugConsole.CheatsEnabled = true;
            AchievementManager.CheatsEnabled = true;
            UpdateCheatsEnabled();

            DebugConsole.Log("[SubEditor] Test mode started");
        }

        private void HandleEndTestMode(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to end test mode");
                return;
            }

            ReturnToSubEditor();
        }

        private void HandleSubmarineSync(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to sync submarine");
                return;
            }

            int compressedLength = inc.ReadInt32();
            int uncompressedLength = inc.ReadInt32();
            byte[] compressedBytes = inc.ReadBytes(compressedLength);

            // Store for newly joining clients
            subEditorStoredSubmarineCompressed = compressedBytes;
            subEditorStoredSubmarineUncompressedLength = uncompressedLength;
            // Decompress and store XML for backward compat
            try
            {
                using (var ms = new System.IO.MemoryStream(compressedBytes))
                using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
                using (var reader = new System.IO.StreamReader(gzip, System.Text.Encoding.UTF8))
                {
                    subEditorStoredSubmarineXml = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.AddWarning($"[SubEditor] Failed to decompress submarine XML for storage: {ex.Message}");
                subEditorStoredSubmarineXml = "";
            }

            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
                msg.WriteInt32(compressedLength);
                msg.WriteInt32(uncompressedLength);
                msg.WriteBytes(compressedBytes, 0, compressedBytes.Length);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        private void HandleSubmarineInfo(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to set submarine info");
                return;
            }

            string subName = inc.ReadString();
            string subHash = inc.ReadString();

            subEditorCurrentSubName = subName;

            var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.StringRepresentation == subHash);
            if (matchingSub != null)
            {
                subEditorCurrentSubPath = matchingSub.FilePath;
                GameMain.NetLobbyScreen.SelectedSub = matchingSub;
            }
            else
            {
                // Check temp file path the host may have saved to
                string tempPath = System.IO.Path.Combine(
                    SaveUtil.SubmarineDownloadFolder,
                    $"_SubEditorTemp_{subName}.sub");
                if (System.IO.File.Exists(tempPath))
                {
                    subEditorCurrentSubPath = tempPath;
                    // Register so file sender can find it
                    var tempSubInfo = new SubmarineInfo(tempPath);
                    SubmarineInfo.AddToSavedSubs(tempSubInfo);
                    GameMain.NetLobbyScreen.SelectedSub = tempSubInfo;
                }
                else
                {
                    // Fallback: match by name only
                    var fallback = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
                    if (fallback != null)
                    {
                        subEditorCurrentSubPath = fallback.FilePath;
                        GameMain.NetLobbyScreen.SelectedSub = fallback;
                    }
                }
            }

            foreach (var client in connectedClients)
            {
                if (client == sender) continue;

                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.SubmarineInfo);
                msg.WriteString(subName);
                msg.WriteString(subHash);
                serverPeer?.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        public void SetSubEditorHost(Client newHost)
        {
            if (!isSubEditorSessionActive)
            {
                StartSubEditorSession(newHost);
                return;
            }
            
            subEditorHost = newHost;
            DebugConsole.Log($"[SubEditor] Host changed to {newHost.Name}");
            SendSubEditorClientList();
        }

        public void ForceSubEditorTestMode()
        {
            if (!isSubEditorSessionActive)
            {
                DebugConsole.AddWarning("[SubEditor] Cannot start test mode - no active session");
                return;
            }

            DebugConsole.Log("[SubEditor] Forcing test mode");
            
            StartSubEditorTestRound();
        }
    }
}
