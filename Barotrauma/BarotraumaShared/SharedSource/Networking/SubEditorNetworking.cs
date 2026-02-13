using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    public enum SubEditorPacketHeader : byte
    {
        EnterSubEditor,
        SyncSubmarine,
        CursorPosition,
        EntitySelection,
        EntityDeselection,
        EntityCreated,
        EntityDeleted,
        EntityTransform,
        EntityProperty,
        StartTestMode,
        EndTestMode,
        ClientList,
        EditRequest,
        EditConfirm,
        EditDeny,
        SubmarineInfo,
        ReturnToEditor,
        EntityPlaced,
        EntityRemoved,
        EntityMoved,
        EntityPropertyChanged,
        CursorMoved,
        RequestSubmarineFile,
        EntityUpdated,
        FullState,
        EntitiesMovedBatch
    }

    [NetworkSerialize]
    public readonly record struct SubEditorUser(
        byte SessionId,
        string Name,
        byte ColorIndex) : INetSerializableStruct
    {
        public static readonly Color[] UserColors = new Color[]
        {
            new Color(255, 82, 82),    // Red
            new Color(255, 177, 66),   // Orange
            new Color(255, 241, 118),  // Yellow
            new Color(129, 199, 132),  // Light Green
            new Color(79, 195, 247),   // Light Blue
            new Color(149, 117, 205),  // Purple
            new Color(244, 143, 177),  // Pink
            new Color(77, 182, 172),   // Teal
            new Color(255, 138, 101),  // Deep Orange
            new Color(174, 213, 129),  // Lime
            new Color(100, 181, 246),  // Blue
            new Color(206, 147, 216),  // Light Purple
            new Color(240, 98, 146),   // Deep Pink
            new Color(128, 203, 196),  // Cyan
            new Color(255, 213, 79),   // Amber
            new Color(144, 164, 174)   // Blue Grey
        };

        public Color GetColor() => ColorIndex < UserColors.Length 
            ? UserColors[ColorIndex] 
            : Color.White;
    }

    [NetworkSerialize]
    public readonly record struct SubEditorCursorData(
        byte SessionId,
        float WorldX,
        float WorldY) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct SubEditorSelectionData(
        byte SessionId,
        UInt16 EntityId) : INetSerializableStruct;

    [NetworkSerialize]
    public readonly record struct SubEditorTransformData(
        UInt16 EntityId,
        int RectX,
        int RectY,
        int RectWidth,
        int RectHeight) : INetSerializableStruct;

    public class SubEditorNetworkingShared
    {
        public const int MaxEditors = 16;

        public const float CursorSyncInterval = 0.05f;

        public Dictionary<byte, SubEditorUser> ConnectedEditors { get; } = new Dictionary<byte, SubEditorUser>();

        public Dictionary<UInt16, byte> EntityLocks { get; } = new Dictionary<UInt16, byte>();

        public Dictionary<byte, Vector2> CursorPositions { get; } = new Dictionary<byte, Vector2>();

        public static Color GetUserColor(byte colorIndex)
        {
            return colorIndex < SubEditorUser.UserColors.Length
                ? SubEditorUser.UserColors[colorIndex]
                : Color.White;
        }

        public bool IsActive { get; protected set; }

        public bool IsHost { get; protected set; }

        public bool IsEntityLockedByOther(UInt16 entityId, byte currentSessionId)
        {
            return EntityLocks.TryGetValue(entityId, out byte lockOwner) && lockOwner != currentSessionId;
        }

        public bool TryLockEntity(UInt16 entityId, byte sessionId)
        {
            if (EntityLocks.ContainsKey(entityId))
            {
                return EntityLocks[entityId] == sessionId;
            }
            EntityLocks[entityId] = sessionId;
            return true;
        }

        public void UnlockEntity(UInt16 entityId, byte sessionId)
        {
            if (EntityLocks.TryGetValue(entityId, out byte owner) && owner == sessionId)
            {
                EntityLocks.Remove(entityId);
            }
        }

        public void UnlockAllEntitiesForUser(byte sessionId)
        {
            var toRemove = new List<UInt16>();
            foreach (var kvp in EntityLocks)
            {
                if (kvp.Value == sessionId)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var entityId in toRemove)
            {
                EntityLocks.Remove(entityId);
            }
        }

        public Color? GetEntitySelectionColor(UInt16 entityId)
        {
            if (EntityLocks.TryGetValue(entityId, out byte sessionId))
            {
                if (ConnectedEditors.TryGetValue(sessionId, out var user))
                {
                    return user.GetColor();
                }
            }
            return null;
        }

        public void AddUser(SubEditorUser user)
        {
            ConnectedEditors[user.SessionId] = user;
            CursorPositions[user.SessionId] = Vector2.Zero;
        }

        public void RemoveUser(byte sessionId)
        {
            ConnectedEditors.Remove(sessionId);
            CursorPositions.Remove(sessionId);
            UnlockAllEntitiesForUser(sessionId);
        }

        // Keyed by AccountId (not username) because usernames can change
        public Dictionary<UInt16, string> EntityOwnership { get; } = new Dictionary<UInt16, string>();

        public static SubEditorPermissions DefaultClientPermissions { get; set; } = SubEditorPermissions.All;

        // Host always has all permissions
        public Dictionary<byte, SubEditorPermissions> UserPermissions { get; } = new Dictionary<byte, SubEditorPermissions>();

        public static int MassEditThreshold { get; set; } = 20;

        public void SetEntityOwner(UInt16 entityId, string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
            {
                EntityOwnership[entityId] = accountId;
            }
        }

        public string GetEntityOwner(UInt16 entityId)
        {
            return EntityOwnership.TryGetValue(entityId, out string owner) ? owner : null;
        }

        public void RemoveEntityOwnership(UInt16 entityId)
        {
            EntityOwnership.Remove(entityId);
        }

        public byte HostSessionId { get; set; }

        public SubEditorPermissions GetPermissions(byte sessionId)
        {
            if (sessionId == HostSessionId)
            {
                return SubEditorPermissions.All;
            }
            return UserPermissions.TryGetValue(sessionId, out var perms) ? perms : SubEditorPermissions.None;
        }

        public void SetPermissions(byte sessionId, SubEditorPermissions permissions)
        {
            UserPermissions[sessionId] = permissions;
        }

        public bool CanUserEditEntity(byte sessionId, UInt16 entityId, string userAccountId)
        {
            var perms = GetPermissions(sessionId);
            string owner = GetEntityOwner(entityId);
            
            if (string.IsNullOrEmpty(owner) || owner == userAccountId)
            {
                return perms.HasFlag(SubEditorPermissions.CanEditOwn);
            }
            return perms.HasFlag(SubEditorPermissions.CanEditOthers);
        }

        public bool CanUserDeleteEntity(byte sessionId, UInt16 entityId, string userAccountId)
        {
            var perms = GetPermissions(sessionId);
            string owner = GetEntityOwner(entityId);
            
            if (string.IsNullOrEmpty(owner) || owner == userAccountId)
            {
                return perms.HasFlag(SubEditorPermissions.CanDeleteOwn);
            }
            return perms.HasFlag(SubEditorPermissions.CanDeleteOthers);
        }

        public bool IsMassEdit(int entityCount)
        {
            return entityCount >= MassEditThreshold;
        }

        public virtual void Clear()
        {
            ConnectedEditors.Clear();
            EntityLocks.Clear();
            CursorPositions.Clear();
            EntityOwnership.Clear();
            UserPermissions.Clear();
            IsActive = false;
            IsHost = false;
        }
    }

    // Separate from the standard ClientPermissions enum
    [Flags]
    public enum SubEditorPermissions : uint
    {
        None = 0x0,
        CanWireOwnInEditor = 0x1,
        CanWireOthersInEditor = 0x2,
        CanEditOwn = 0x4,
        CanDeleteOwn = 0x8,
        CanEditOthers = 0x10,
        CanDeleteOthers = 0x20,
        CanManageOthers = 0x40,
        CanUndoSelf = 0x80,
        CanMassEdit = 0x100,
        All = 0x1FF
    }
}
