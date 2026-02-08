using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Client-side networking for collaborative submarine editor.
    /// Handles joining sessions, receiving updates, and sending local changes.
    /// </summary>
    internal class SubEditorNetworkingClient : SubEditorNetworkingShared
    {
        /// <summary>
        /// Singleton instance for the collaborative editor client.
        /// </summary>
        public static SubEditorNetworkingClient Instance { get; private set; }

        // Cursor rendering constants
        private const float MinCursorMovementDistanceSquared = 100f;
        private const int CursorSize = 10;
        private const int CursorHalfSize = 5;
        private const int UsernameOffsetX = 12;
        private const int UsernameOffsetY = -8;

        private float cursorSyncTimer;
        private Vector2 lastSentCursorPos;
        private byte localSessionId;
        private byte lastKnownHostSessionId;
        private bool wasInitiallyHost;
        private bool isRequestingSubmarineFile;
        private string requestedSubmarineName = "";

        /// <summary>
        /// The local user's session ID.
        /// </summary>
        public byte LocalSessionId => localSessionId;

        /// <summary>
        /// Event fired when a submarine sync is received from the host.
        /// </summary>
        public event Action<string> OnSubmarineReceived;

        /// <summary>
        /// Event fired when test mode starts.
        /// </summary>
        public event Action OnTestModeStarted;

        /// <summary>
        /// Event fired when test mode ends.
        /// </summary>
        public event Action OnTestModeEnded;

        /// <summary>
        /// Event fired when the client list is updated.
        /// </summary>
        public event Action OnClientListUpdated;

        /// <summary>
        /// Event fired when host notifies us of which submarine is being edited.
        /// Arg1: submarine name, Arg2: submarine hash
        /// </summary>
        public event Action<string, string> OnSubmarineInfoReceived;

        /// <summary>
        /// Event fired when another user places an entity.
        /// Args: senderSessionId, entityXml (full XML element with all properties)
        /// </summary>
        public event Action<byte, string> OnEntityPlaced;

        /// <summary>
        /// Event fired when another user removes an entity.
        /// Args: senderSessionId, entityId
        /// </summary>
        public event Action<byte, ushort> OnEntityRemoved;

        /// <summary>
        /// Event fired when another user moves an entity.
        /// Args: senderSessionId, entityId, x, y
        /// </summary>
        public event Action<byte, ushort, float, float> OnEntityMoved;

        /// <summary>
        /// Event fired when another user changes an entity property.
        /// Args: senderSessionId, entityId, propertyName, propertyValue
        /// </summary>
        public event Action<byte, ushort, string, string> OnEntityPropertyChanged;

        /// <summary>
        /// Event fired when another user sends a full entity state update (absolute sync).
        /// Args: senderSessionId, entityId, entityXml
        /// </summary>
        public event Action<byte, ushort, string> OnEntityUpdated;

        /// <summary>
        /// Event fired when another user moves their cursor.
        /// Args: senderSessionId, x, y
        /// </summary>
        public event Action<byte, float, float> OnCursorMoved;

        /// <summary>
        /// Initialize the client networking instance.
        /// </summary>
        public static void Initialize()
        {
            Instance ??= new SubEditorNetworkingClient();
        }

        /// <summary>
        /// Join a collaborative editing session.
        /// </summary>
        public void JoinSession(byte sessionId, string playerName, byte colorIndex)
        {
            localSessionId = sessionId;
            IsActive = true;
            IsHost = false;
            wasInitiallyHost = false;
            
            var localUser = new SubEditorUser(sessionId, playerName, colorIndex);
            AddUser(localUser);
            
            DebugConsole.Log($"[SubEditor] Joined collaborative session as {playerName} (color {colorIndex})");
        }

        /// <summary>
        /// Host a collaborative editing session.
        /// </summary>
        public void HostSession(byte sessionId, string playerName)
        {
            localSessionId = sessionId;
            IsActive = true;
            IsHost = true;
            wasInitiallyHost = true;
            
            var localUser = new SubEditorUser(sessionId, playerName, 0);
            AddUser(localUser);
            
            DebugConsole.Log($"[SubEditor] Hosting collaborative session as {playerName}");
        }

        /// <summary>
        /// Update the local session ID to match what the server assigned.
        /// Called when host receives their actual session ID from the server.
        /// </summary>
        public void UpdateLocalSessionId(byte newSessionId)
        {
            if (localSessionId == newSessionId) return;
            
            byte oldId = localSessionId;
            
            if (ConnectedEditors.TryGetValue(localSessionId, out var localUser))
            {
                ConnectedEditors.Remove(localSessionId);
                CursorPositions.Remove(localSessionId);
                
                var updatedUser = new SubEditorUser(newSessionId, localUser.Name, localUser.ColorIndex);
                ConnectedEditors[newSessionId] = updatedUser;
                CursorPositions[newSessionId] = Vector2.Zero;
            }
            
            localSessionId = newSessionId;
            
            if (wasInitiallyHost)
            {
                IsHost = true;
                lastKnownHostSessionId = newSessionId;
            }
            else
            {
                IsHost = (localSessionId == lastKnownHostSessionId);
            }
            DebugConsole.Log($"[SubEditor] Updated local session ID from {oldId} to {newSessionId}. Host ID: {lastKnownHostSessionId}, IsHost: {IsHost}, wasInitiallyHost: {wasInitiallyHost}");
            
            OnClientListUpdated?.Invoke();
        }

        /// <summary>
        /// Leave the current session.
        /// </summary>
        public void LeaveSession()
        {
            if (!IsActive) return;
            
            DebugConsole.Log("[SubEditor] Left collaborative session");
            Clear();
        }

        /// <summary>
        /// Update cursor position and sync if needed.
        /// </summary>
        public void UpdateCursor(Vector2 worldPosition, float deltaTime)
        {
            if (!IsActive) return;

            cursorSyncTimer -= deltaTime;
            
            // Only sync if position changed significantly and timer expired
            if (cursorSyncTimer <= 0 && Vector2.DistanceSquared(lastSentCursorPos, worldPosition) > MinCursorMovementDistanceSquared)
            {
                SendCursorPosition(worldPosition);
                lastSentCursorPos = worldPosition;
                cursorSyncTimer = CursorSyncInterval;
            }
        }

        /// <summary>
        /// Send local cursor position to others.
        /// </summary>
        private void SendCursorPosition(Vector2 worldPos)
        {
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            // Update local state
            CursorPositions[localSessionId] = worldPos;

            // Send to server for relay
            var cursorData = new SubEditorCursorData(localSessionId, worldPos.X, worldPos.Y);
            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.CursorPosition);
            msg.WriteNetSerializableStruct(cursorData);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Unreliable);
        }

        /// <summary>
        /// Notify that local user selected an entity.
        /// </summary>
        public void NotifyEntitySelected(MapEntity entity)
        {
            if (!IsActive || entity == null) return;

            // Try to lock locally first
            if (TryLockEntity(entity.ID, localSessionId))
            {
                // Send selection to server for relay
                if (GameMain.Client?.ClientPeer != null && GameMain.Client.ClientPeer.IsActive)
                {
                    var selectionData = new SubEditorSelectionData(localSessionId, entity.ID);
                    IWriteMessage msg = new WriteOnlyMessage();
                    msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
                    msg.WriteByte((byte)SubEditorPacketHeader.EntitySelection);
                    msg.WriteNetSerializableStruct(selectionData);
                    GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
                }
                DebugConsole.Log($"[SubEditor] Selected entity {entity.ID}");
            }
            else
            {
                DebugConsole.Log($"[SubEditor] Cannot select entity {entity.ID} - locked by another user");
            }
        }

        /// <summary>
        /// Notify that local user deselected an entity.
        /// </summary>
        public void NotifyEntityDeselected(MapEntity entity)
        {
            if (!IsActive || entity == null) return;

            UnlockEntity(entity.ID, localSessionId);

            // Send deselection to server for relay
            if (GameMain.Client?.ClientPeer != null && GameMain.Client.ClientPeer.IsActive)
            {
                var selectionData = new SubEditorSelectionData(localSessionId, entity.ID);
                IWriteMessage msg = new WriteOnlyMessage();
                msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
                msg.WriteByte((byte)SubEditorPacketHeader.EntityDeselection);
                msg.WriteNetSerializableStruct(selectionData);
                GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
            }
        }

        /// <summary>
        /// Check if local user can edit an entity.
        /// </summary>
        public bool CanEditEntity(MapEntity entity)
        {
            if (!IsActive) return true;
            if (entity == null) return false;
            
            return !IsEntityLockedByOther(entity.ID, localSessionId);
        }

        /// <summary>
        /// Draw other users' cursors in the editor.
        /// </summary>
        public void DrawCursors(SpriteBatch spriteBatch, Camera cam)
        {
            if (!IsActive) return;

            foreach (var kvp in CursorPositions)
            {
                if (kvp.Key == localSessionId) continue;
                
                if (ConnectedEditors.TryGetValue(kvp.Key, out var user))
                {
                    Vector2 screenPos = cam.WorldToScreen(kvp.Value);
                    Color cursorColor = user.GetColor();
                    
                    // Draw a simple cursor indicator
                    // Using GUI primitives since we don't have a custom sprite
                    GUI.DrawRectangle(spriteBatch, 
                        new Rectangle((int)screenPos.X - CursorHalfSize, (int)screenPos.Y - CursorHalfSize, CursorSize, CursorSize), 
                        cursorColor, 
                        isFilled: true);
                    
                    // Draw username near cursor
                    if (GUIStyle.SmallFont != null)
                    {
                        GUI.DrawString(spriteBatch, 
                            screenPos + new Vector2(UsernameOffsetX, UsernameOffsetY), 
                            user.Name, 
                            cursorColor, 
                            font: GUIStyle.SmallFont);
                    }
                }
            }
        }

        /// <summary>
        /// Handle receiving a cursor position update from another user.
        /// </summary>
        public void ReceiveCursorPosition(SubEditorCursorData data)
        {
            CursorPositions[data.SessionId] = new Vector2(data.WorldX, data.WorldY);
        }

        /// <summary>
        /// Handle receiving an entity selection update.
        /// </summary>
        public void ReceiveEntitySelection(SubEditorSelectionData data)
        {
            EntityLocks[data.EntityId] = data.SessionId;
        }

        /// <summary>
        /// Handle receiving an entity deselection update.
        /// </summary>
        public void ReceiveEntityDeselection(SubEditorSelectionData data)
        {
            if (EntityLocks.TryGetValue(data.EntityId, out byte owner) && owner == data.SessionId)
            {
                EntityLocks.Remove(data.EntityId);
            }
        }

        /// <summary>
        /// Handle receiving a full submarine sync from host.
        /// </summary>
        public void ReceiveSubmarineSync(string submarineXml)
        {
            OnSubmarineReceived?.Invoke(submarineXml);
        }

        /// <summary>
        /// Handle receiving client list update.
        /// </summary>
        public void ReceiveClientList(List<SubEditorUser> users, byte hostSessionId)
        {
            ConnectedEditors.Clear();
            foreach (var user in users)
            {
                AddUser(user);
            }
            
            lastKnownHostSessionId = hostSessionId;
            IsHost = (localSessionId == hostSessionId);
            DebugConsole.Log($"[SubEditor] Client list updated. Local ID: {localSessionId}, Host ID: {hostSessionId}, IsHost: {IsHost}");
            
            OnClientListUpdated?.Invoke();
        }

        /// <summary>
        /// Send submarine info to server when host loads a submarine.
        /// This allows clients to request the file if they don't have it.
        /// </summary>
        public void NotifySubmarineLoaded(SubmarineInfo subInfo)
        {
            if (!IsActive || !IsHost) return;
            if (subInfo == null) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.SubmarineInfo);
            msg.WriteString(subInfo.Name);
            msg.WriteString(subInfo.MD5Hash.StringRepresentation);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);

            DebugConsole.Log($"[SubEditor] Notified server of submarine: {subInfo.Name}");
        }

        /// <summary>
        /// Handle receiving submarine info from host (via server).
        /// If we don't have the submarine, request it via FileSender.
        /// </summary>
        public void ReceiveSubmarineInfo(string subName, string subHash)
        {
            // Prevent spam - don't request if we're already requesting this sub
            if (isRequestingSubmarineFile && requestedSubmarineName == subName)
            {
                return;
            }

            DebugConsole.Log($"[SubEditor] Host is editing submarine: {subName} (hash: {subHash})");

            // Check if we have this submarine locally
            var localSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.StringRepresentation == subHash);
            
            if (localSub != null)
            {
                DebugConsole.Log($"[SubEditor] Already have submarine {subName}, loading it...");
                isRequestingSubmarineFile = false;
                requestedSubmarineName = "";
                OnSubmarineInfoReceived?.Invoke(subName, subHash);
            }
            else
            {
                DebugConsole.Log($"[SubEditor] Don't have submarine {subName}, requesting file...");
                isRequestingSubmarineFile = true;
                requestedSubmarineName = subName;
                // Request the file via existing file transfer system
                GameMain.Client?.RequestFile(FileTransferType.Submarine, subName, subHash);
                OnSubmarineInfoReceived?.Invoke(subName, subHash);
            }
        }

        /// <summary>
        /// Called when submarine file transfer completes.
        /// </summary>
        public void OnSubmarineFileTransferComplete()
        {
            isRequestingSubmarineFile = false;
            requestedSubmarineName = "";
        }

        /// <summary>
        /// Handle receiving test mode start command.
        /// </summary>
        public void ReceiveTestModeStart()
        {
            OnTestModeStarted?.Invoke();
        }

        /// <summary>
        /// Handle receiving test mode end command.
        /// </summary>
        public void ReceiveTestModeEnd()
        {
            OnTestModeEnded?.Invoke();
        }

        /// <summary>
        /// Request the server to start test mode for all clients.
        /// Only the host can do this.
        /// </summary>
        public void RequestStartTestMode()
        {
            if (!IsActive || !IsHost) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.StartTestMode);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);

            DebugConsole.Log("[SubEditor] Requested server to start test mode");
        }

        /// <summary>
        /// Request the server to end test mode and return all clients to SubEditor.
        /// </summary>
        public void RequestEndTestMode()
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EndTestMode);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);

            DebugConsole.Log("[SubEditor] Requested server to end test mode");
        }

        public override void Clear()
        {
            base.Clear();
            cursorSyncTimer = 0;
            lastSentCursorPos = Vector2.Zero;
            localSessionId = 0;
            isRequestingSubmarineFile = false;
            requestedSubmarineName = "";
        }

        /// <summary>
        /// Notify server that an entity was placed.
        /// Sends full XML element so all properties (scale, sprite offset, color, etc.) are synced.
        /// </summary>
        public void NotifyEntityPlaced(string entityXml)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EntityPlaced);
            msg.WriteString(entityXml);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Notify server that an entity was removed.
        /// </summary>
        public void NotifyEntityRemoved(ushort entityId)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EntityRemoved);
            msg.WriteUInt16(entityId);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Notify server that an entity was moved.
        /// </summary>
        public void NotifyEntityMoved(ushort entityId, float x, float y)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EntityMoved);
            msg.WriteUInt16(entityId);
            msg.WriteSingle(x);
            msg.WriteSingle(y);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Notify server that an entity property was changed.
        /// </summary>
        public void NotifyEntityPropertyChanged(ushort entityId, string propertyName, string value)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EntityPropertyChanged);
            msg.WriteUInt16(entityId);
            msg.WriteString(propertyName);
            msg.WriteString(value);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Notify server of a full entity state update (absolute sync).
        /// Sends the complete entity XML like a .sub file entry.
        /// Use this after property changes, moves, or any modification that should be authoritative.
        /// </summary>
        public void NotifyEntityUpdated(ushort entityId, string entityXml)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EntityUpdated);
            msg.WriteUInt16(entityId);
            msg.WriteString(entityXml);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Request submarine file from server (when we don't have it locally).
        /// </summary>
        public void RequestSubmarineFile(string subName)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.RequestSubmarineFile);
            msg.WriteString(subName);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);

            DebugConsole.Log($"[SubEditor] Requested submarine file: {subName}");
        }

        /// <summary>
        /// Handle receiving an entity placed event from another user.
        /// </summary>
        public void ReceiveEntityPlaced(byte senderSessionId, string entityXml)
        {
            OnEntityPlaced?.Invoke(senderSessionId, entityXml);
        }

        /// <summary>
        /// Handle receiving an entity removed event from another user.
        /// </summary>
        public void ReceiveEntityRemoved(byte senderSessionId, ushort entityId)
        {
            OnEntityRemoved?.Invoke(senderSessionId, entityId);
        }

        /// <summary>
        /// Handle receiving an entity moved event from another user.
        /// </summary>
        public void ReceiveEntityMoved(byte senderSessionId, ushort entityId, float x, float y)
        {
            OnEntityMoved?.Invoke(senderSessionId, entityId, x, y);
        }

        /// <summary>
        /// Handle receiving an entity property changed event from another user.
        /// </summary>
        public void ReceiveEntityPropertyChanged(byte senderSessionId, ushort entityId, string propName, string propValue)
        {
            OnEntityPropertyChanged?.Invoke(senderSessionId, entityId, propName, propValue);
        }

        /// <summary>
        /// Handle receiving a full entity state update from another user (absolute sync).
        /// </summary>
        public void ReceiveEntityUpdated(byte senderSessionId, ushort entityId, string entityXml)
        {
            OnEntityUpdated?.Invoke(senderSessionId, entityId, entityXml);
        }

        /// <summary>
        /// Handle receiving a cursor moved event from another user.
        /// </summary>
        public void ReceiveCursorMoved(byte senderSessionId, float x, float y)
        {
            CursorPositions[senderSessionId] = new Vector2(x, y);
            OnCursorMoved?.Invoke(senderSessionId, x, y);
        }
    }
}
