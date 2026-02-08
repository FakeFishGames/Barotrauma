using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

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
        private string subEditorCurrentSubPath;
        private string subEditorCurrentSubName;
        private string subEditorStoredSubmarineXml;

        /// <summary>
        /// Whether a collaborative SubEditor session is currently active.
        /// </summary>
        public bool IsSubEditorSessionActive => isSubEditorSessionActive;
        
        /// <summary>
        /// The client who is the host of the SubEditor session.
        /// </summary>
        public Client SubEditorHost => subEditorHost;

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
                default:
                    DebugConsole.AddWarning($"Unknown SubEditor packet header: {subHeader}");
                    break;
            }
        }

        private void HandleEntityPlaced(IReadMessage inc, Client sender)
        {
            string entityXml = inc.ReadString();
            DebugConsole.Log($"[SubEditor] Entity placed by {sender.Name}, XML length: {entityXml?.Length ?? 0}");

            // Relay to all other clients
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

        private void HandleEntityPropertyChanged(IReadMessage inc, Client sender)
        {
            ushort entityId = inc.ReadUInt16();
            string propName = inc.ReadString();
            string propValue = inc.ReadString();

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

            // Relay full entity state to all other clients (absolute sync)
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

            // Update stored cursor position
            if (subEditorSession != null)
            {
                subEditorSession.CursorPositions[senderSessionId] = new Vector2(posX, posY);
            }

            // Relay to all other clients
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
            
            // Find the submarine file — check saved subs, then stored temp path
            var subInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
            if (subInfo != null && System.IO.File.Exists(subInfo.FilePath))
            {
                FileSender.StartTransfer(sender.Connection, FileTransferType.Submarine, subInfo.FilePath);
                DebugConsole.Log($"[SubEditor] Sending submarine file '{subName}' to {sender.Name}");
            }
            else if (!string.IsNullOrEmpty(subEditorCurrentSubPath) && System.IO.File.Exists(subEditorCurrentSubPath))
            {
                // Use the stored temp file path from HandleSubmarineInfo
                FileSender.StartTransfer(sender.Connection, FileTransferType.Submarine, subEditorCurrentSubPath);
                DebugConsole.Log($"[SubEditor] Sending temp submarine file '{subEditorCurrentSubPath}' to {sender.Name}");
            }
            else
            {
                DebugConsole.AddWarning($"[SubEditor] Could not find submarine '{subName}' to send to {sender.Name}");
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
            
            // Ensure voice chat is enabled for SubEditor sessions
            ServerSettings.VoiceChatEnabled = true;

            // Add the host as the first editor
            byte hostColorIndex = 0;
            var hostUser = new SubEditorUser((byte)host.SessionId, host.Name, hostColorIndex);
            subEditorSession.AddUser(hostUser);

            DebugConsole.Log($"[SubEditor] Session started by {host.Name}");

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

            DebugConsole.Log("[SubEditor] Session ended");
        }

        /// <summary>
        /// Add a client to the editor session.
        /// </summary>
        public void AddClientToSubEditorSession(Client client)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            byte sessionId = (byte)client.SessionId;
            
            // Prevent duplicate entries
            if (subEditorSession.ConnectedEditors.ContainsKey(sessionId))
            {
                subEditorSession.RemoveUser(sessionId);
            }

            // Assign next available color
            byte colorIndex = (byte)(subEditorSession.ConnectedEditors.Count % SubEditorUser.UserColors.Length);
            var user = new SubEditorUser(sessionId, client.Name, colorIndex);
            subEditorSession.AddUser(user);

            DebugConsole.Log($"[SubEditor] {client.Name} joined the session");

            // Send updated client list to everyone
            SendSubEditorClientList();
            
            // Send current submarine state to the new client
            if (!string.IsNullOrEmpty(subEditorStoredSubmarineXml))
            {
                IWriteMessage subMsg = new WriteOnlyMessage();
                subMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                subMsg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
                subMsg.WriteString(subEditorStoredSubmarineXml);
                serverPeer?.Send(subMsg, client.Connection, DeliveryMethod.Reliable);
                DebugConsole.Log($"[SubEditor] Sent stored submarine XML ({subEditorStoredSubmarineXml.Length} chars) to new client {client.Name}");
            }
            else
            {
                // Fallback: send submarine file info
                var selectedSub = GameMain.NetLobbyScreen?.SelectedSub;
                if (selectedSub != null)
                {
                    IWriteMessage subMsg = new WriteOnlyMessage();
                    subMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                    subMsg.WriteByte((byte)SubEditorPacketHeader.SubmarineInfo);
                    subMsg.WriteString(selectedSub.Name);
                    subMsg.WriteString(selectedSub.MD5Hash.StringRepresentation);
                    serverPeer?.Send(subMsg, client.Connection, DeliveryMethod.Reliable);
                    DebugConsole.Log($"[SubEditor] Sent submarine info ({selectedSub.Name}) to new client {client.Name}");
                }
            }
        }

        /// <summary>
        /// Remove a client from the editor session.
        /// </summary>
        public void RemoveClientFromSubEditorSession(Client client)
        {
            if (!isSubEditorSessionActive || subEditorSession == null) return;

            subEditorSession.RemoveUser((byte)client.SessionId);

            DebugConsole.Log($"[SubEditor] {client.Name} left the session");

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
            byte hostSessionId = subEditorHost != null ? (byte)subEditorHost.SessionId : (byte)0;

            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.ClientList);
                msg.WriteByte((byte)users.Count);
                msg.WriteByte(hostSessionId);  // Tell clients who the host is

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

            // Use the sender's actual server-assigned SessionId
            byte senderSessionId = (byte)sender.SessionId;

            subEditorSession.CursorPositions[senderSessionId] = 
                new Microsoft.Xna.Framework.Vector2(cursorData.WorldX, cursorData.WorldY);

            // Rebuild with authoritative SessionId and relay
            var correctedCursorData = new SubEditorCursorData(senderSessionId, cursorData.WorldX, cursorData.WorldY);

            // Relay to other clients
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
        /// Force all clients to return from test mode to SubEditor.
        /// Called by the subeditor_endtest console command.
        /// </summary>
        public void ReturnToSubEditor()
        {
            Level.IsSubEditorTestMode = false;
            if (!isSubEditorSessionActive)
            {
                DebugConsole.ThrowError("[SubEditor] No SubEditor session is active");
                return;
            }

            DebugConsole.Log("[SubEditor] Forcing return to SubEditor...");

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
            
            // Send stored submarine XML to non-host clients for state restoration
            if (!string.IsNullOrEmpty(subEditorStoredSubmarineXml))
            {
                foreach (var client in connectedClients)
                {
                    if (client == subEditorHost) continue;
                    IWriteMessage subMsg = new WriteOnlyMessage();
                    subMsg.WriteByte((byte)ServerPacketHeader.SUBEDITOR);
                    subMsg.WriteByte((byte)SubEditorPacketHeader.SyncSubmarine);
                    subMsg.WriteString(subEditorStoredSubmarineXml);
                    serverPeer?.Send(subMsg, client.Connection, DeliveryMethod.Reliable);
                }
                DebugConsole.Log($"[SubEditor] Sent stored submarine XML to non-host clients ({subEditorStoredSubmarineXml.Length} chars)");
            }

            DebugConsole.Log("[SubEditor] All clients returned to SubEditor");
            Log("Returned to SubEditor.", ServerLog.MessageType.ServerMessage);
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

            DebugConsole.Log("[SubEditor] Host requested test mode - starting multiplayer test round");
            StartSubEditorTestRound();
        }

        /// <summary>
        /// Starts a SubEditor test round using the standard Sandbox game flow.
        /// </summary>
        private void StartSubEditorTestRound()
        {
            Level.IsSubEditorTestMode = true;

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

        /// <summary>
        /// Handle test mode end request (host only).
        /// Ends the current round and returns all clients to SubEditor mode.
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

            ReturnToSubEditor();
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

            // Store it so we can send to newly joining clients
            subEditorStoredSubmarineXml = submarineXml;

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

            DebugConsole.Log($"[SubEditor] Submarine synced from host ({submarineXml.Length} chars)");
        }

        /// <summary>
        /// Handle submarine info message from host.
        /// This tells clients which submarine is being edited so they can request it if needed.
        /// Uses the existing FileSender system for submarine file transfer.
        /// </summary>
        private void HandleSubmarineInfo(IReadMessage inc, Client sender)
        {
            if (!isSubEditorSessionActive) return;

            // Only host can announce submarine info
            if (sender != subEditorHost)
            {
                DebugConsole.AddWarning($"[SubEditor] Non-host {sender.Name} tried to set submarine info");
                return;
            }

            // Read submarine name and hash
            string subName = inc.ReadString();
            string subHash = inc.ReadString();

            DebugConsole.Log($"[SubEditor] Host is editing submarine: {subName} (hash: {subHash})");

            // Store the sub name for file requests
            subEditorCurrentSubName = subName;

            // Try to find in saved subs first, then check temp file path
            var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.StringRepresentation == subHash);
            if (matchingSub != null)
            {
                subEditorCurrentSubPath = matchingSub.FilePath;
                GameMain.NetLobbyScreen.SelectedSub = matchingSub;
                DebugConsole.Log($"[SubEditor] Updated lobby submarine to: {matchingSub.Name}");
            }
            else
            {
                // Host may have saved to a temp file — check common temp paths
                // The host saves to SubmarineDownloadFolder/_SubEditorTemp_{name}.sub
                string tempPath = System.IO.Path.Combine(
                    SaveUtil.SubmarineDownloadFolder,
                    $"_SubEditorTemp_{subName}.sub");
                if (System.IO.File.Exists(tempPath))
                {
                    subEditorCurrentSubPath = tempPath;
                    DebugConsole.Log($"[SubEditor] Found temp submarine file: {tempPath}");
                    // Register it so the file sender can find it
                    var tempSubInfo = new SubmarineInfo(tempPath);
                    SubmarineInfo.AddToSavedSubs(tempSubInfo);
                    GameMain.NetLobbyScreen.SelectedSub = tempSubInfo;
                }
                else
                {
                    // Also check without the temp prefix (host may have saved it normally)
                    var fallback = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName);
                    if (fallback != null)
                    {
                        subEditorCurrentSubPath = fallback.FilePath;
                        GameMain.NetLobbyScreen.SelectedSub = fallback;
                    }
                }
            }

            // Relay to all other clients - they will request the file if they don't have it
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

        /// <summary>
        /// Set a new host for the SubEditor session. Used by console command.
        /// </summary>
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

        /// <summary>
        /// Force all clients to start test mode. Used by console command when testing without a proper host.
        /// </summary>
        public void ForceSubEditorTestMode()
        {
            if (!isSubEditorSessionActive)
            {
                DebugConsole.AddWarning("[SubEditor] Cannot start test mode - no active session");
                return;
            }

            DebugConsole.Log("[SubEditor] Server forcing test mode for all clients");
            
            // Start the actual multiplayer test round
            StartSubEditorTestRound();
        }
    }
}
