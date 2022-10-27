using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        private CoroutineHandle logPropertyChangeCoroutine;

        public Inventory PreviousParentInventory;

        public override Sprite Sprite
        {
            get { return base.Prefab?.Sprite; }
        }

        partial void AssignCampaignInteractionTypeProjSpecific(CampaignMode.InteractionType interactionType)
        {
            GameMain.NetworkMember.CreateEntityEvent(this, new AssignCampaignInteractionEventData());
        }

        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            Exception error(string reason)
            {
                string errorMsg = $"Failed to write a network event for the item \"{Name}\" - {reason}";
                GameAnalyticsManager.AddErrorEventOnce($"Item.ServerWrite:{Name}", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return new Exception(errorMsg);
            }
            
            if (extraData is null) { throw error("event data was null"); }
            if (!(extraData is IEventData itemEventData)) { throw error($"event data was of the wrong type (\"{extraData.GetType().Name}\")"); }

            msg.WriteRangedInteger((int)itemEventData.EventType, (int)EventType.MinValue, (int)EventType.MaxValue);
            switch (itemEventData)
            {
                case ComponentStateEventData componentStateEventData:
                    int componentIndex = components.IndexOf(componentStateEventData.Component);
                    if (componentIndex < 0)
                    {
                        throw error($"component index out of range ({componentIndex})");
                    }
                    if (!(components[componentIndex] is IServerSerializable serializableComponent))
                    {
                        throw error($"component \"{components[componentIndex]}\" is not server serializable");
                    }
                    msg.WriteRangedInteger(componentIndex, 0, components.Count - 1);
                    serializableComponent.ServerEventWrite(msg, c, extraData);
                    break;
                case InventoryStateEventData inventoryStateEventData:
                    int containerIndex = components.IndexOf(inventoryStateEventData.Component);
                    if (containerIndex < 0)
                    {
                        throw error($"container index out of range ({containerIndex})");
                    }
                    if (!(components[containerIndex] is ItemContainer itemContainer))
                    {
                        throw error("component \"" + components[containerIndex] + "\" is not server serializable");
                    }
                    msg.WriteRangedInteger(containerIndex, 0, components.Count - 1);
                    msg.WriteUInt16(GameMain.Server.EntityEventManager.Events.Last()?.ID ?? (ushort)0);
                    itemContainer.Inventory.ServerEventWrite(msg, c);
                    break;
                case ItemStatusEventData _:
                    msg.WriteSingle(condition);
                    break;
                case AssignCampaignInteractionEventData _:
                    msg.WriteByte((byte)CampaignInteractionType);
                    break;
                case ApplyStatusEffectEventData applyStatusEffectEventData:
                    {
                        ActionType actionType = applyStatusEffectEventData.ActionType;
                        ItemComponent targetComponent = applyStatusEffectEventData.TargetItemComponent;
                        Limb targetLimb = applyStatusEffectEventData.TargetLimb;
                        Vector2? worldPosition = applyStatusEffectEventData.WorldPosition;

                        Character targetCharacter = applyStatusEffectEventData.TargetCharacter;
                        if (targetCharacter != null && targetCharacter.Removed) { targetCharacter = null; }
                        byte targetLimbIndex = targetLimb != null && targetCharacter != null ? (byte)Array.IndexOf(targetCharacter.AnimController.Limbs, targetLimb) : (byte)255;

                        msg.WriteRangedInteger((int)actionType, 0, Enum.GetValues(typeof(ActionType)).Length - 1);
                        msg.WriteByte((byte)(targetComponent == null ? 255 : components.IndexOf(targetComponent)));
                        msg.WriteUInt16(applyStatusEffectEventData.TargetCharacter?.ID ?? (ushort)0);
                        msg.WriteByte(targetLimbIndex);
                        msg.WriteUInt16(applyStatusEffectEventData.UseTarget?.ID ?? (ushort)0);
                        msg.WriteBoolean(worldPosition.HasValue);
                        if (worldPosition.HasValue)
                        {
                            msg.WriteSingle(worldPosition.Value.X);
                            msg.WriteSingle(worldPosition.Value.Y);
                        }
                    }
                    break;
                case ChangePropertyEventData changePropertyEventData:
                    try
                    {
                        WritePropertyChange(msg, changePropertyEventData, inGameEditableOnly: !GameMain.NetworkMember.IsServer);
                    }
                    catch (Exception e)
                    {
                        throw new Exception(
                            $"Failed to write a ChangeProperty network event for the item \"{Name}\" ({e.Message})");
                    }
                    break;
                case SetItemStatEventData setItemStatEventData:
                    msg.WriteByte((byte)setItemStatEventData.Stats.Count);
                    foreach (var (key, value) in setItemStatEventData.Stats)
                    {
                        msg.WriteNetSerializableStruct(key);
                        msg.WriteSingle(value);
                    }
                    break;
                case UpgradeEventData upgradeEventData:
                    var upgrade = upgradeEventData.Upgrade;
                    var upgradeTargets = upgrade.TargetComponents;
                    msg.WriteIdentifier(upgrade.Identifier);
                    msg.WriteByte((byte)upgrade.Level);
                    msg.WriteByte((byte)upgradeTargets.Count);
                    foreach (var (_, value) in upgrade.TargetComponents)
                    {
                        msg.WriteByte((byte)value.Length);
                        foreach (var propertyReference in value)
                        {
                            object originalValue = propertyReference.OriginalValue;
                            msg.WriteSingle((float)(originalValue ?? -1));
                        }
                    }
                    break;
                default:
                    throw error($"Unsupported event type {itemEventData.GetType().Name}");
            }
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            EventType eventType =
                (EventType)msg.ReadRangedInteger((int)EventType.MinValue, (int)EventType.MaxValue);

            c.KickAFKTimer = 0.0f;

            switch (eventType)
            {
                case EventType.ComponentState:
                    int componentIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[componentIndex] as IClientSerializable).ServerEventRead(msg, c);
                    break;
                case EventType.InventoryState:
                    int containerIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[containerIndex] as ItemContainer).Inventory.ServerEventRead(msg, c);
                    break;
                case EventType.Treatment:
                    if (c.Character == null || !c.Character.CanInteractWith(this)) return;

                    UInt16 characterID = msg.ReadUInt16();
                    byte limbIndex = msg.ReadByte();

                    Character targetCharacter = FindEntityByID(characterID) as Character;
                    if (targetCharacter == null) break;
                    if (targetCharacter != c.Character && c.Character.SelectedCharacter != targetCharacter) break;

                    Limb targetLimb = limbIndex < targetCharacter.AnimController.Limbs.Length ? targetCharacter.AnimController.Limbs[limbIndex] : null;

                    if (ContainedItems == null || ContainedItems.All(i => i == null))
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " used item " + Name, ServerLog.MessageType.ItemInteraction);
                    }
                    else
                    {
                        GameServer.Log(
                            GameServer.CharacterLogName(c.Character) + " used item " + Name + " (contained items: " + string.Join(", ", ContainedItems.Select(i => i.Name)) + ")",
                            ServerLog.MessageType.ItemInteraction);
                    }

                    ApplyTreatment(c.Character, targetCharacter, targetLimb);

                    break;
                case EventType.ChangeProperty:
                    ReadPropertyChange(msg, inGameEditableOnly: GameMain.NetworkMember.IsServer, sender: c);
                    break;
                case EventType.Combine:
                    UInt16 combineTargetID = msg.ReadUInt16();
                    Item combineTarget = FindEntityByID(combineTargetID) as Item;
                    if (combineTarget == null || !c.Character.CanInteractWith(this) || !c.Character.CanInteractWith(combineTarget))
                    {
                        return;
                    }
                    Combine(combineTarget, c.Character);
                    break;
            }
        }

        public void WriteSpawnData(IWriteMessage msg, UInt16 entityID, UInt16 originalInventoryID, byte originalItemContainerIndex, int originalSlotIndex)
        {
            if (GameMain.Server == null) { return; }

            msg.WriteString(Prefab.OriginalName);
            msg.WriteIdentifier(Prefab.Identifier);
            msg.WriteBoolean(Description != base.Prefab.Description);
            if (Description != base.Prefab.Description)
            {
                msg.WriteString(Description);
            }

            msg.WriteUInt16(entityID);

            if (ParentInventory == null || ParentInventory.Owner == null || originalInventoryID == 0)
            {
                msg.WriteUInt16((ushort)0);

                msg.WriteSingle(Position.X);
                msg.WriteSingle(Position.Y);
                msg.WriteRangedSingle(body == null ? 0.0f : MathUtils.WrapAngleTwoPi(body.Rotation), 0.0f, MathHelper.TwoPi, 8);
                msg.WriteUInt16(Submarine != null ? Submarine.ID : (ushort)0);
            }
            else
            {
                msg.WriteUInt16(originalInventoryID);
                msg.WriteByte(originalItemContainerIndex);
                msg.WriteByte(originalSlotIndex < 0 ? (byte)255 : (byte)originalSlotIndex);
            }

            msg.WriteByte(body == null ? (byte)0 : (byte)body.BodyType);
            msg.WriteBoolean(SpawnedInCurrentOutpost);
            msg.WriteBoolean(AllowStealing);
            msg.WriteRangedInteger(Quality, 0, Items.Components.Quality.MaxQuality);

            byte teamID = 0;
            IdCard idCardComponent = null;
            foreach (WifiComponent wifiComponent in GetComponents<WifiComponent>())
            {
                teamID = (byte)wifiComponent.TeamID;
                break;
            }
            if (teamID == 0)
            {
                foreach (IdCard idCard in GetComponents<IdCard>())
                {
                    teamID = (byte)idCard.TeamID;
                    idCardComponent = idCard;
                    break;
                }
            }

            msg.WriteByte(teamID);

            bool hasIdCard = idCardComponent != null;
            msg.WriteBoolean(hasIdCard);
            if (hasIdCard)
            {
                msg.WriteString(idCardComponent.OwnerName);
                msg.WriteString(idCardComponent.OwnerTags);
                msg.WriteByte((byte)Math.Max(0, idCardComponent.OwnerBeardIndex+1));
                msg.WriteByte((byte)Math.Max(0, idCardComponent.OwnerHairIndex+1));
                msg.WriteByte((byte)Math.Max(0, idCardComponent.OwnerMoustacheIndex+1));
                msg.WriteByte((byte)Math.Max(0, idCardComponent.OwnerFaceAttachmentIndex+1));
                msg.WriteColorR8G8B8(idCardComponent.OwnerHairColor);
                msg.WriteColorR8G8B8(idCardComponent.OwnerFacialHairColor);
                msg.WriteColorR8G8B8(idCardComponent.OwnerSkinColor);
                msg.WriteIdentifier(idCardComponent.OwnerJobId);
                msg.WriteByte((byte)idCardComponent.OwnerSheetIndex.X);
                msg.WriteByte((byte)idCardComponent.OwnerSheetIndex.Y);
            }
            
            bool tagsChanged = tags.Count != base.Prefab.Tags.Count || !tags.All(t => base.Prefab.Tags.Contains(t));
            msg.WriteBoolean(tagsChanged);
            if (tagsChanged)
            {
                IEnumerable<Identifier> splitTags = Tags.Split(',').ToIdentifiers();
                msg.WriteString(string.Join(',', splitTags.Where(t => !base.Prefab.Tags.Contains(t))));
                msg.WriteString(string.Join(',', base.Prefab.Tags.Where(t => !splitTags.Contains(t))));
            }
            var nameTag = GetComponent<NameTag>();
            msg.WriteBoolean(nameTag != null);
            if (nameTag != null)
            {
                msg.WriteString(nameTag.WrittenName ?? "");
            }
        }

        partial void UpdateNetPosition(float deltaTime)
        {
            if (parentInventory != null || body == null || !body.Enabled || Removed)
            {
                PositionUpdateInterval = float.PositiveInfinity;
                return;
            }
            
            //gradually increase the interval of position updates
            PositionUpdateInterval += deltaTime;

            float maxInterval = 30.0f;

            float velSqr = body.LinearVelocity.LengthSquared();
            if (velSqr > 10.0f * 10.0f)
            {
                //over 10 m/s (projectile, thrown item or similar) -> send updates very frequently
                maxInterval = 0.1f;
            }
            else if (velSqr > 1.0f)
            {
                //over 1 m/s
                maxInterval = 0.25f;
            }
            else if (velSqr > 0.05f * 0.05f)
            {
                //over 0.05 m/s
                maxInterval = 1.0f;
            }

            PositionUpdateInterval = Math.Min(PositionUpdateInterval, maxInterval);
        }

        public float GetPositionUpdateInterval(Client recipient)
        {
            if (PositionUpdateInterval == float.PositiveInfinity || body == null || parentInventory != null)
            {
                return float.PositiveInfinity;
            }

            if (recipient.Character == null || recipient.Character.IsDead)
            {
                //less frequent updates for clients who aren't controlling a character (max 2 updates/sec)
                return Math.Max(PositionUpdateInterval, 0.5f);
            }
            else
            {
                float distSqr = Vector2.DistanceSquared(recipient.Character.WorldPosition, WorldPosition);
                if (distSqr > 20000.0f * 20000.0f)
                {
                    //don't send position updates at all if >20 000 units away
                    return float.PositiveInfinity;
                }
                else if (distSqr > 10000.0f * 10000.0f)
                {
                    //drop the update rate to 10% if too far to see the item
                    return PositionUpdateInterval * 10;
                }
                else if (distSqr > 1000.0f * 1000.0f)
                {
                    //halve the update rate if the client is far away (but still close enough to possibly see the item)
                    return PositionUpdateInterval * 2;
                }
                return PositionUpdateInterval;
            }
        }

        public void ServerWritePosition(IWriteMessage msg, Client c)
        {
            msg.WriteUInt16(ID);

            IWriteMessage tempBuffer = new WriteOnlyMessage();
            body.ServerWrite(tempBuffer);
            msg.WriteVariableUInt32((uint)tempBuffer.LengthBytes);
            msg.WriteBytes(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            msg.WritePadBits();
        }

        public void CreateServerEvent<T>(T ic) where T : ItemComponent, IServerSerializable
            => CreateServerEvent(ic, ic.ServerGetEventData());

        public void CreateServerEvent<T>(T ic, ItemComponent.IEventData extraData) where T : ItemComponent, IServerSerializable
        {
            if (GameMain.Server == null) { return; }

            if (!ItemList.Contains(this))
            {
                string errorMsg = "Attempted to create a network event for an item (" + Name + ") that hasn't been fully initialized yet.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.CreateServerEvent:EventForUninitializedItem" + Name + ID, GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }

            #warning TODO: this should throw an exception
            if (!components.Contains(ic)) { return; }

            var eventData = new ComponentStateEventData(ic, extraData);
            if (!ic.ValidateEventData(eventData)) { throw new Exception($"Component event creation failed: {typeof(T).Name}.{nameof(ItemComponent.ValidateEventData)} returned false"); }
            GameMain.Server.CreateEntityEvent(this, eventData);
        }

#if DEBUG
        public void TryCreateServerEventSpam()
        {
            if (GameMain.Server == null) { return; }

            foreach (ItemComponent ic in components)
            {
                if (!(ic is IServerSerializable)) { continue; }
                var eventData = new ComponentStateEventData(ic, ic.ServerGetEventData());
                if (!ic.ValidateEventData(eventData)) { continue; }
                GameMain.Server.CreateEntityEvent(this, eventData);
            }
        }
#endif
    }
}
