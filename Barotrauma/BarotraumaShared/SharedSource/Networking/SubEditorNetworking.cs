using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    /// <summary>
    /// Packet headers for collaborative submarine editor messages.
    /// These are used within the existing network infrastructure.
    /// </summary>
    public enum SubEditorPacketHeader : byte
    {
        /// <summary>Server tells client to enter SubEditor mode</summary>
        EnterSubEditor,
        /// <summary>Host sends full submarine XML to clients when loading</summary>
        SyncSubmarine,
        /// <summary>Client/Host sends cursor position updates</summary>
        CursorPosition,
        /// <summary>Client/Host sends entity selection change</summary>
        EntitySelection,
        /// <summary>Client/Host sends entity deselection</summary>
        EntityDeselection,
        /// <summary>Host broadcasts entity creation</summary>
        EntityCreated,
        /// <summary>Host broadcasts entity deletion</summary>
        EntityDeleted,
        /// <summary>Host broadcasts entity transform (move/resize)</summary>
        EntityTransform,
        /// <summary>Host broadcasts entity property change</summary>
        EntityProperty,
        /// <summary>Host starts test mode, clients should follow</summary>
        StartTestMode,
        /// <summary>Host ends test mode, return to editor</summary>
        EndTestMode,
        /// <summary>Client list update with colors</summary>
        ClientList,
        /// <summary>Request to perform an edit operation (client to host)</summary>
        EditRequest,
        /// <summary>Host confirms edit is allowed</summary>
        EditConfirm,
        /// <summary>Host denies edit (entity locked by another user)</summary>
        EditDeny,
        /// <summary>Host notifies clients of which submarine is being edited (name + hash)</summary>
        SubmarineInfo,
        /// <summary>Server tells client to return to SubEditor from test mode</summary>
        ReturnToEditor,
        /// <summary>Entity was placed by a user</summary>
        EntityPlaced,
        /// <summary>Entity was removed by a user</summary>
        EntityRemoved,
        /// <summary>Entity was moved by a user</summary>
        EntityMoved,
        /// <summary>Entity property was changed</summary>
        EntityPropertyChanged,
        /// <summary>Cursor position update</summary>
        CursorMoved,
        /// <summary>Request submarine file transfer</summary>
        RequestSubmarineFile,
        /// <summary>Full entity state update (absolute sync, like .sub file format)</summary>
        EntityUpdated,
        /// <summary>Full submarine state as XML (all entities, like loading a .sub file)</summary>
        FullState,
        /// <summary>Batch of entity moves (multiple entities moved in one packet)</summary>
        EntitiesMovedBatch
    }

    /// <summary>
    /// Represents a user in the collaborative submarine editor.
    /// </summary>
    [NetworkSerialize]
    public readonly record struct SubEditorUser(
        byte SessionId,
        string Name,
        byte ColorIndex) : INetSerializableStruct
    {
        /// <summary>
        /// 16 distinct colors for up to 16 users.
        /// </summary>
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

    /// <summary>
    /// Data for cursor position updates in the editor.
    /// </summary>
    [NetworkSerialize]
    public readonly record struct SubEditorCursorData(
        byte SessionId,
        float WorldX,
        float WorldY) : INetSerializableStruct;

    /// <summary>
    /// Data for entity selection/deselection.
    /// </summary>
    [NetworkSerialize]
    public readonly record struct SubEditorSelectionData(
        byte SessionId,
        UInt16 EntityId) : INetSerializableStruct;

    /// <summary>
    /// Data for entity transform changes (position/size).
    /// </summary>
    [NetworkSerialize]
    public readonly record struct SubEditorTransformData(
        UInt16 EntityId,
        int RectX,
        int RectY,
        int RectWidth,
        int RectHeight) : INetSerializableStruct;

    /// <summary>
    /// Core shared logic for collaborative submarine editor networking.
    /// Tracks connected users, entity locks, and cursor positions.
    /// </summary>
    public class SubEditorNetworkingShared
    {
        /// <summary>
        /// Maximum number of simultaneous editors.
        /// </summary>
        public const int MaxEditors = 16;

        /// <summary>
        /// How often cursor positions are synced (in seconds).
        /// </summary>
        public const float CursorSyncInterval = 0.05f;

        /// <summary>
        /// Connected editor users.
        /// </summary>
        public Dictionary<byte, SubEditorUser> ConnectedEditors { get; } = new Dictionary<byte, SubEditorUser>();

        /// <summary>
        /// Maps entity IDs to the session ID of the user who has them selected/locked.
        /// </summary>
        public Dictionary<UInt16, byte> EntityLocks { get; } = new Dictionary<UInt16, byte>();

        /// <summary>
        /// Current cursor positions of all users.
        /// </summary>
        public Dictionary<byte, Vector2> CursorPositions { get; } = new Dictionary<byte, Vector2>();

        /// <summary>
        /// Get the color for a user by color index.
        /// </summary>
        public static Color GetUserColor(byte colorIndex)
        {
            return colorIndex < SubEditorUser.UserColors.Length
                ? SubEditorUser.UserColors[colorIndex]
                : Color.White;
        }

        /// <summary>
        /// Whether we are currently in a collaborative editing session.
        /// </summary>
        public bool IsActive { get; protected set; }

        /// <summary>
        /// Whether the local user is the host of the session.
        /// </summary>
        public bool IsHost { get; protected set; }

        /// <summary>
        /// Check if an entity is locked by another user.
        /// </summary>
        public bool IsEntityLockedByOther(UInt16 entityId, byte currentSessionId)
        {
            return EntityLocks.TryGetValue(entityId, out byte lockOwner) && lockOwner != currentSessionId;
        }

        /// <summary>
        /// Try to lock an entity for a user.
        /// </summary>
        public bool TryLockEntity(UInt16 entityId, byte sessionId)
        {
            if (EntityLocks.ContainsKey(entityId))
            {
                return EntityLocks[entityId] == sessionId;
            }
            EntityLocks[entityId] = sessionId;
            return true;
        }

        /// <summary>
        /// Unlock an entity.
        /// </summary>
        public void UnlockEntity(UInt16 entityId, byte sessionId)
        {
            if (EntityLocks.TryGetValue(entityId, out byte owner) && owner == sessionId)
            {
                EntityLocks.Remove(entityId);
            }
        }

        /// <summary>
        /// Unlock all entities owned by a specific user (e.g., when they disconnect).
        /// </summary>
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

        /// <summary>
        /// Get the color for an entity's selection box based on who has it selected.
        /// </summary>
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

        /// <summary>
        /// Add a user to the session.
        /// </summary>
        public void AddUser(SubEditorUser user)
        {
            ConnectedEditors[user.SessionId] = user;
            CursorPositions[user.SessionId] = Vector2.Zero;
        }

        /// <summary>
        /// Remove a user from the session.
        /// </summary>
        public void RemoveUser(byte sessionId)
        {
            ConnectedEditors.Remove(sessionId);
            CursorPositions.Remove(sessionId);
            UnlockAllEntitiesForUser(sessionId);
        }

        /// <summary>
        /// Maps entity IDs to the AccountId of the player who placed them (for ownership/blame).
        /// Uses AccountId (not username) because usernames can change.
        /// </summary>
        public Dictionary<UInt16, string> EntityOwnership { get; } = new Dictionary<UInt16, string>();

        /// <summary>
        /// Maps session IDs to their SubEditor-specific permissions.
        /// Host always has all permissions.
        /// </summary>
        public Dictionary<byte, SubEditorPermissions> UserPermissions { get; } = new Dictionary<byte, SubEditorPermissions>();

        /// <summary>
        /// The mass edit threshold — how many entities must be affected for an action to count as "mass edit."
        /// </summary>
        public int MassEditThreshold { get; set; } = 20;

        /// <summary>
        /// Set entity ownership. Associates an entity ID with a player's account identifier.
        /// </summary>
        public void SetEntityOwner(UInt16 entityId, string accountId)
        {
            if (!string.IsNullOrEmpty(accountId))
            {
                EntityOwnership[entityId] = accountId;
            }
        }

        /// <summary>
        /// Get the account ID of the player who owns an entity.
        /// </summary>
        public string GetEntityOwner(UInt16 entityId)
        {
            return EntityOwnership.TryGetValue(entityId, out string owner) ? owner : null;
        }

        /// <summary>
        /// Remove ownership tracking for an entity (e.g., when deleted).
        /// </summary>
        public void RemoveEntityOwnership(UInt16 entityId)
        {
            EntityOwnership.Remove(entityId);
        }

        /// <summary>
        /// Get the permissions for a user. Host always has all permissions.
        /// </summary>
        public SubEditorPermissions GetPermissions(byte sessionId)
        {
            if (ConnectedEditors.TryGetValue(sessionId, out _) && sessionId == 0)
            {
                return SubEditorPermissions.All; // Host always has all permissions
            }
            return UserPermissions.TryGetValue(sessionId, out var perms) ? perms : SubEditorPermissions.None;
        }

        /// <summary>
        /// Set permissions for a user.
        /// </summary>
        public void SetPermissions(byte sessionId, SubEditorPermissions permissions)
        {
            UserPermissions[sessionId] = permissions;
        }

        /// <summary>
        /// Check if a user can perform an action on an entity, considering ownership and permissions.
        /// </summary>
        public bool CanUserEditEntity(byte sessionId, UInt16 entityId, string userAccountId)
        {
            var perms = GetPermissions(sessionId);
            string owner = GetEntityOwner(entityId);
            
            // No owner means anyone with edit permission can edit
            if (string.IsNullOrEmpty(owner) || owner == userAccountId)
            {
                return perms.HasFlag(SubEditorPermissions.CanEditOwn);
            }
            return perms.HasFlag(SubEditorPermissions.CanEditOthers);
        }

        /// <summary>
        /// Check if a user can delete an entity, considering ownership and permissions.
        /// </summary>
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

        /// <summary>
        /// Check if an action counts as a mass edit based on the number of affected entities.
        /// </summary>
        public bool IsMassEdit(int entityCount)
        {
            return entityCount >= MassEditThreshold;
        }

        /// <summary>
        /// Clear all session data.
        /// </summary>
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

    /// <summary>
    /// Permissions specific to the collaborative SubEditor.
    /// These are separate from the standard ClientPermissions enum.
    /// </summary>
    [Flags]
    public enum SubEditorPermissions : uint
    {
        None = 0x0,
        /// <summary>Can add wires to own objects, edit/delete own wires</summary>
        CanWireOwnInEditor = 0x1,
        /// <summary>Can edit other players' wires and wire other players' devices</summary>
        CanWireOthersInEditor = 0x2,
        /// <summary>Can place and edit own objects</summary>
        CanEditOwn = 0x4,
        /// <summary>Can delete own objects</summary>
        CanDeleteOwn = 0x8,
        /// <summary>Can edit other players' objects</summary>
        CanEditOthers = 0x10,
        /// <summary>Can delete other players' objects</summary>
        CanDeleteOthers = 0x20,
        /// <summary>Can assign permissions, view/edit others' undo lists</summary>
        CanManageOthers = 0x40,
        /// <summary>Can undo own actions</summary>
        CanUndoSelf = 0x80,
        /// <summary>Can perform mass edits (select-all + delete, etc.)</summary>
        CanMassEdit = 0x100,
        /// <summary>All permissions</summary>
        All = 0x1FF
    }
}
