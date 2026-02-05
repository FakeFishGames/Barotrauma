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
            
            var localUser = new SubEditorUser(sessionId, playerName, colorIndex);
            AddUser(localUser);
            
            DebugConsole.NewMessage($"[SubEditor] Joined collaborative session as {playerName} (color {colorIndex})", Color.LightGreen);
        }

        /// <summary>
        /// Host a collaborative editing session.
        /// </summary>
        public void HostSession(byte sessionId, string playerName)
        {
            localSessionId = sessionId;
            IsActive = true;
            IsHost = true;
            
            var localUser = new SubEditorUser(sessionId, playerName, 0);
            AddUser(localUser);
            
            DebugConsole.NewMessage($"[SubEditor] Hosting collaborative session as {playerName}", Color.LightGreen);
        }

        /// <summary>
        /// Leave the current session.
        /// </summary>
        public void LeaveSession()
        {
            if (!IsActive) return;
            
            DebugConsole.NewMessage("[SubEditor] Left collaborative session", Color.Yellow);
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
            if (GameMain.Client == null) return;

            // TODO: Implement actual network message sending
            // For now, update local state
            CursorPositions[localSessionId] = worldPos;
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
                // Send selection to others
                // TODO: Implement network message
                DebugConsole.NewMessage($"[SubEditor] Selected entity {entity.ID}", Color.Gray);
            }
            else
            {
                DebugConsole.NewMessage($"[SubEditor] Cannot select entity {entity.ID} - locked by another user", Color.Orange);
            }
        }

        /// <summary>
        /// Notify that local user deselected an entity.
        /// </summary>
        public void NotifyEntityDeselected(MapEntity entity)
        {
            if (!IsActive || entity == null) return;

            UnlockEntity(entity.ID, localSessionId);
            // Send deselection to others
            // TODO: Implement network message
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
        public void ReceiveClientList(List<SubEditorUser> users)
        {
            ConnectedEditors.Clear();
            foreach (var user in users)
            {
                AddUser(user);
            }
            OnClientListUpdated?.Invoke();
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

        public override void Clear()
        {
            base.Clear();
            cursorSyncTimer = 0;
            lastSentCursorPos = Vector2.Zero;
            localSessionId = 0;
        }
    }
}
