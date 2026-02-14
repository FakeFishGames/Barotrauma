using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    internal class SubEditorNetworkingClient : SubEditorNetworkingShared
    {
        public static SubEditorNetworkingClient Instance { get; private set; }

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

        public byte LocalSessionId => localSessionId;

        public event Action<string> OnSubmarineReceived;
        public event Action OnTestModeStarted;
        public event Action OnTestModeEnded;
        public event Action OnClientListUpdated;
        public event Action<string, string> OnSubmarineInfoReceived;
        public event Action<byte, string> OnEntityPlaced;
        public event Action<byte, ushort> OnEntityRemoved;
        public event Action<byte, ushort, float, float> OnEntityMoved;
        public event Action<byte, ushort, string, string> OnEntityPropertyChanged;
        public event Action<byte, ushort, string> OnEntityUpdated;
        public event Action<byte, float, float> OnCursorMoved;

        public static void Initialize()
        {
            Instance ??= new SubEditorNetworkingClient();
        }

        public SubEditorUser? GetUserBySessionId(string sessionIdStr)
        {
            if (byte.TryParse(sessionIdStr, out byte sessionId) && ConnectedEditors.TryGetValue(sessionId, out var user))
            {
                return user;
            }
            return null;
        }

        public IEnumerable<SubEditorUser> GetAllUsers()
        {
            return ConnectedEditors.Values;
        }

        public void JoinSession(byte sessionId, string playerName, byte colorIndex)
        {
            localSessionId = sessionId;
            IsActive = true;
            IsHost = false;
            wasInitiallyHost = false;
            
            var localUser = new SubEditorUser(sessionId, playerName, colorIndex);
            AddUser(localUser);
        }

        public void HostSession(byte sessionId, string playerName)
        {
            localSessionId = sessionId;
            IsActive = true;
            IsHost = true;
            wasInitiallyHost = true;
            
            var localUser = new SubEditorUser(sessionId, playerName, 0);
            AddUser(localUser);
        }

        public void UpdateLocalSessionId(byte newSessionId)
        {
            if (localSessionId == newSessionId) return;
            
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
            
            OnClientListUpdated?.Invoke();
        }

        public void LeaveSession()
        {
            if (!IsActive) return;
            
            Clear();
        }

        public void UpdateCursor(Vector2 worldPosition, float deltaTime)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            cursorSyncTimer -= deltaTime;
            
            if (cursorSyncTimer <= 0 && Vector2.DistanceSquared(lastSentCursorPos, worldPosition) > MinCursorMovementDistanceSquared)
            {
                SendCursorPosition(worldPosition);
                lastSentCursorPos = worldPosition;
                cursorSyncTimer = CursorSyncInterval;
            }
        }

        private void SendCursorPosition(Vector2 worldPos)
        {
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            CursorPositions[localSessionId] = worldPos;

            var cursorData = new SubEditorCursorData(localSessionId, worldPos.X, worldPos.Y);
            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.CursorPosition);
            msg.WriteNetSerializableStruct(cursorData);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Unreliable);
        }

        public void NotifyEntitySelected(MapEntity entity)
        {
            if (!IsActive || entity == null) return;

            if (TryLockEntity(entity.ID, localSessionId))
            {
                if (GameMain.Client?.ClientPeer != null && GameMain.Client.ClientPeer.IsActive)
                {
                    var selectionData = new SubEditorSelectionData(localSessionId, entity.ID);
                    IWriteMessage msg = new WriteOnlyMessage();
                    msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
                    msg.WriteByte((byte)SubEditorPacketHeader.EntitySelection);
                    msg.WriteNetSerializableStruct(selectionData);
                    GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
                }
            }
        }

        public void NotifyEntityDeselected(MapEntity entity)
        {
            if (!IsActive || entity == null) return;

            UnlockEntity(entity.ID, localSessionId);

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

        public bool HasPermission(SubEditorPermissions perm)
        {
            if (!IsActive) return true;
            return GetPermissions(localSessionId).HasFlag(perm);
        }

        private string GetLocalAccountId()
        {
            var myClient = GameMain.Client?.MyClient;
            if (myClient != null && myClient.AccountId.TryUnwrap(out var accountId))
            {
                return accountId.StringRepresentation;
            }
            return GameMain.Client?.Name ?? "";
        }

        public bool CanEditEntity(MapEntity entity)
        {
            if (!IsActive) return true;
            if (entity == null) return false;
            if (IsEntityLockedByOther(entity.ID, localSessionId)) return false;

            return CanUserEditEntity(localSessionId, entity.ID, GetLocalAccountId());
        }

        public bool CanDeleteEntity(MapEntity entity)
        {
            if (!IsActive) return true;
            if (entity == null) return false;

            return CanUserDeleteEntity(localSessionId, entity.ID, GetLocalAccountId());
        }

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
                    
                    GUI.DrawRectangle(spriteBatch, 
                        new Rectangle((int)screenPos.X - CursorHalfSize, (int)screenPos.Y - CursorHalfSize, CursorSize, CursorSize), 
                        cursorColor, 
                        isFilled: true);
                    
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

        public void ReceiveCursorPosition(SubEditorCursorData data)
        {
            CursorPositions[data.SessionId] = new Vector2(data.WorldX, data.WorldY);
        }

        public void ReceiveEntitySelection(SubEditorSelectionData data)
        {
            EntityLocks[data.EntityId] = data.SessionId;
        }

        public void ReceiveEntityDeselection(SubEditorSelectionData data)
        {
            if (EntityLocks.TryGetValue(data.EntityId, out byte owner) && owner == data.SessionId)
            {
                EntityLocks.Remove(data.EntityId);
            }
        }

        public void ReceiveSubmarineSync(string submarineXml)
        {
            OnSubmarineReceived?.Invoke(submarineXml);
        }

        public void ReceiveClientList(List<SubEditorUser> users, byte hostSessionId)
        {
            ConnectedEditors.Clear();
            foreach (var user in users)
            {
                AddUser(user);
            }
            
            lastKnownHostSessionId = hostSessionId;
            HostSessionId = hostSessionId;
            IsHost = (localSessionId == hostSessionId);

            DebugConsole.NewMessage($"[SubEditor] ClientList received: {users.Count} users, hostSession={hostSessionId}, mySession={localSessionId}, IsHost={IsHost}", Color.Cyan);

            // Apply default permissions for all non-host users if not already set
            foreach (var user in users)
            {
                if (user.SessionId != hostSessionId && !UserPermissions.ContainsKey(user.SessionId))
                {
                    SetPermissions(user.SessionId, DefaultClientPermissions);
                    DebugConsole.NewMessage($"[SubEditor] Default perms for {user.Name} (session={user.SessionId}): {DefaultClientPermissions}", Color.Cyan);
                }
            }
            
            OnClientListUpdated?.Invoke();
        }

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
        }

        public void ReceiveSubmarineInfo(string subName, string subHash)
        {
            // State is synced via XML packets, not file transfers
            OnSubmarineInfoReceived?.Invoke(subName, subHash);
        }

        public void OnSubmarineFileTransferComplete()
        {
            isRequestingSubmarineFile = false;
            requestedSubmarineName = "";
        }

        public void ReceiveTestModeStart()
        {
            OnTestModeStarted?.Invoke();
        }

        public void ReceiveTestModeEnd()
        {
            OnTestModeEnded?.Invoke();
        }

        public void RequestStartTestMode()
        {
            if (!IsActive || !IsHost) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.StartTestMode);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void RequestEndTestMode()
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EndTestMode);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
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

        public bool HasLocalPermission(SubEditorPermissions flag)
        {
            return (GetPermissions(localSessionId) & flag) != 0;
        }

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

        /// <summary>Batched move to avoid DoS rate limit kicks.</summary>
        public void NotifyEntitiesMovedBatch(List<(ushort entityId, float dx, float dy)> moves)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;
            if (moves.Count == 0) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.EntitiesMovedBatch);
            int count = Math.Min(moves.Count, ushort.MaxValue);
            msg.WriteUInt16((ushort)count);
            for (int i = 0; i < count; i++)
            {
                var (entityId, dx, dy) = moves[i];
                msg.WriteUInt16(entityId);
                msg.WriteSingle(dx);
                msg.WriteSingle(dy);
            }
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

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

        public void RequestSubmarineFile(string subName)
        {
            if (!IsActive) return;
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.RequestSubmarineFile);
            msg.WriteString(subName);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SendPermissionUpdate(byte targetSessionId, SubEditorPermissions permissions)
        {
            if (!IsActive || !IsHost)
            {
                DebugConsole.NewMessage($"[SubEditor] SendPermissionUpdate BLOCKED: IsActive={IsActive}, IsHost={IsHost}", Color.Red);
                return;
            }
            if (GameMain.Client?.ClientPeer == null || !GameMain.Client.ClientPeer.IsActive) return;

            SetPermissions(targetSessionId, permissions);
            DebugConsole.NewMessage($"[SubEditor] SendPermissionUpdate: target={targetSessionId}, perms={permissions} (bits={(uint)permissions}), mySession={localSessionId}", Color.Yellow);

            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ClientPacketHeader.SUBEDITOR);
            msg.WriteByte((byte)SubEditorPacketHeader.SetPermissions);
            msg.WriteByte(targetSessionId);
            msg.WriteUInt32((uint)permissions);
            GameMain.Client.ClientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void ReceivePermissionUpdate(byte targetSessionId, SubEditorPermissions permissions)
        {
            SetPermissions(targetSessionId, permissions);
            DebugConsole.NewMessage($"[SubEditor] ReceivePermissionUpdate: target={targetSessionId}, perms={permissions}, mySession={localSessionId}", Color.Cyan);
            if (targetSessionId == localSessionId)
            {
                DebugConsole.NewMessage($"[SubEditor] YOUR permissions changed to: {permissions}", Color.Yellow);
            }
        }

        public void ReceiveEntityPlaced(byte senderSessionId, string entityXml)
        {
            OnEntityPlaced?.Invoke(senderSessionId, entityXml);
        }

        public void ReceiveEntityRemoved(byte senderSessionId, ushort entityId)
        {
            OnEntityRemoved?.Invoke(senderSessionId, entityId);
        }

        public void ReceiveEntityMoved(byte senderSessionId, ushort entityId, float x, float y)
        {
            OnEntityMoved?.Invoke(senderSessionId, entityId, x, y);
        }

        public void ReceiveEntityPropertyChanged(byte senderSessionId, ushort entityId, string propName, string propValue)
        {
            OnEntityPropertyChanged?.Invoke(senderSessionId, entityId, propName, propValue);
        }

        public void ReceiveEntityUpdated(byte senderSessionId, ushort entityId, string entityXml)
        {
            OnEntityUpdated?.Invoke(senderSessionId, entityId, entityXml);
        }

        public void ReceiveCursorMoved(byte senderSessionId, float x, float y)
        {
            CursorPositions[senderSessionId] = new Vector2(x, y);
            OnCursorMoved?.Invoke(senderSessionId, x, y);
        }
    }
}
