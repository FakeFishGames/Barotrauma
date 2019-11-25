using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            string errorMsg = "";
            if (extraData == null || extraData.Length == 0 || !(extraData[0] is NetEntityEvent.Type))
            {
                if (extraData == null)
                {
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - event data was null.";
                }
                else if (extraData.Length == 0)
                {
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - event data was empty.";
                }
                else
                {
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - event type not set.";
                }
                msg.WriteRangedInteger((int)NetEntityEvent.Type.Invalid, 0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);
                DebugConsole.Log(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.ServerWrite:InvalidData" + Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            int initialWritePos = msg.LengthBits;

            NetEntityEvent.Type eventType = (NetEntityEvent.Type)extraData[0];
            msg.WriteRangedInteger((int)eventType, 0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);
            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    if (extraData.Length < 2 || !(extraData[1] is int))
                    {
                        errorMsg = "Failed to write a component state event for the item \"" + Name + "\" - component index not given.";
                        break;
                    }
                    int componentIndex = (int)extraData[1];
                    if (componentIndex < 0 || componentIndex >= components.Count)
                    {
                        errorMsg = "Failed to write a component state event for the item \"" + Name + "\" - component index out of range (" + componentIndex + ").";
                        break;
                    }
                    else if (!(components[componentIndex] is IServerSerializable))
                    {
                        errorMsg = "Failed to write a component state event for the item \"" + Name + "\" - component \"" + components[componentIndex] + "\" is not server serializable.";
                        break;
                    }
                    msg.WriteRangedInteger(componentIndex, 0, components.Count - 1);
                    (components[componentIndex] as IServerSerializable).ServerWrite(msg, c, extraData);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    if (extraData.Length < 2 || !(extraData[1] is int))
                    {
                        errorMsg = "Failed to write an inventory state event for the item \"" + Name + "\" - component index not given.";
                        break;
                    }
                    int containerIndex = (int)extraData[1];
                    if (containerIndex < 0 || containerIndex >= components.Count)
                    {
                        errorMsg = "Failed to write an inventory state event for the item \"" + Name + "\" - container index out of range (" + containerIndex + ").";
                        break;
                    }
                    else if (!(components[containerIndex] is ItemContainer))
                    {
                        errorMsg = "Failed to write an inventory state event for the item \"" + Name + "\" - component \"" + components[containerIndex] + "\" is not server serializable.";
                        break;
                    }
                    msg.WriteRangedInteger(containerIndex, 0, components.Count - 1);
                    (components[containerIndex] as ItemContainer).Inventory.ServerWrite(msg, c);
                    break;
                case NetEntityEvent.Type.Status:
                    msg.Write(condition);
                    break;
                case NetEntityEvent.Type.Treatment:
                    {
                        ItemComponent targetComponent = (ItemComponent)extraData[1];
                        ActionType actionType = (ActionType)extraData[2];
                        ushort targetID = (ushort)extraData[3];
                        Limb targetLimb = (Limb)extraData[4];

                        Character targetCharacter = FindEntityByID(targetID) as Character;
                        byte targetLimbIndex = targetLimb != null && targetCharacter != null ? (byte)Array.IndexOf(targetCharacter.AnimController.Limbs, targetLimb) : (byte)255;

                        msg.Write((byte)components.IndexOf(targetComponent));
                        msg.WriteRangedInteger((int)actionType, 0, Enum.GetValues(typeof(ActionType)).Length - 1);
                        msg.Write(targetID);
                        msg.Write(targetLimbIndex);
                    }
                    break;
                case NetEntityEvent.Type.ApplyStatusEffect:
                    {
                        ActionType actionType = (ActionType)extraData[1];
                        ItemComponent targetComponent = extraData.Length > 2 ? (ItemComponent)extraData[2] : null;
                        ushort characterID = extraData.Length > 3 ? (ushort)extraData[3] : (ushort)0;
                        Limb targetLimb = extraData.Length > 4 ? (Limb)extraData[4] : null;
                        ushort useTargetID = extraData.Length > 5 ? (ushort)extraData[5] : (ushort)0;
                        Vector2? worldPosition = null;
                        if (extraData.Length > 6) { worldPosition = (Vector2)extraData[6]; }

                        Character targetCharacter = FindEntityByID(characterID) as Character;
                        byte targetLimbIndex = targetLimb != null && targetCharacter != null ? (byte)Array.IndexOf(targetCharacter.AnimController.Limbs, targetLimb) : (byte)255;

                        msg.WriteRangedInteger((int)actionType, 0, Enum.GetValues(typeof(ActionType)).Length - 1);
                        msg.Write((byte)(targetComponent == null ? 255 : components.IndexOf(targetComponent)));
                        msg.Write(characterID);
                        msg.Write(targetLimbIndex);
                        msg.Write(useTargetID);
                        msg.Write(worldPosition.HasValue);
                        if (worldPosition.HasValue)
                        {
                            msg.Write(worldPosition.Value.X);
                            msg.Write(worldPosition.Value.Y);
                        }
                    }
                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    try
                    {
                        WritePropertyChange(msg, extraData, false);
                    }
                    catch (Exception e)
                    {
                        errorMsg = "Failed to write a ChangeProperty network event for the item \"" + Name + "\" (" + e.Message + ")";
                    }
                    break;
                default:
                    errorMsg = "Failed to write a network event for the item \"" + Name + "\" - \"" + eventType + "\" is not a valid entity event type for items.";
                    break;
            }

            if (!string.IsNullOrEmpty(errorMsg))
            {
                //something went wrong - rewind the write position and write invalid event type to prevent creating an unreadable event
                msg.BitPosition = initialWritePos;
                msg.LengthBits = initialWritePos;
                msg.WriteRangedInteger((int)NetEntityEvent.Type.Invalid, 0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);
                DebugConsole.Log(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.ServerWrite:" + errorMsg, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
            }

        }

        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            NetEntityEvent.Type eventType =
                (NetEntityEvent.Type)msg.ReadRangedInteger(0, Enum.GetValues(typeof(NetEntityEvent.Type)).Length - 1);

            c.KickAFKTimer = 0.0f;

            switch (eventType)
            {
                case NetEntityEvent.Type.ComponentState:
                    int componentIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[componentIndex] as IClientSerializable).ServerRead(type, msg, c);
                    break;
                case NetEntityEvent.Type.InventoryState:
                    int containerIndex = msg.ReadRangedInteger(0, components.Count - 1);
                    (components[containerIndex] as ItemContainer).Inventory.ServerRead(type, msg, c);
                    break;
                case NetEntityEvent.Type.Treatment:
                    if (c.Character == null || !c.Character.CanInteractWith(this)) return;

                    UInt16 characterID = msg.ReadUInt16();
                    byte limbIndex = msg.ReadByte();

                    Character targetCharacter = FindEntityByID(characterID) as Character;
                    if (targetCharacter == null) break;
                    if (targetCharacter != c.Character && c.Character.SelectedCharacter != targetCharacter) break;

                    Limb targetLimb = limbIndex < targetCharacter.AnimController.Limbs.Length ? targetCharacter.AnimController.Limbs[limbIndex] : null;

                    if (ContainedItems == null || ContainedItems.All(i => i == null))
                    {
                        GameServer.Log(c.Character.LogName + " used item " + Name, ServerLog.MessageType.ItemInteraction);
                    }
                    else
                    {
                        GameServer.Log(
                            c.Character.LogName + " used item " + Name + " (contained items: " + string.Join(", ", ContainedItems.Select(i => i.Name)) + ")",
                            ServerLog.MessageType.ItemInteraction);
                    }

                    ApplyTreatment(c.Character, targetCharacter, targetLimb);

                    break;
                case NetEntityEvent.Type.ChangeProperty:
                    ReadPropertyChange(msg, true, c);
                    break;
                case NetEntityEvent.Type.Combine:
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

        public void WriteSpawnData(IWriteMessage msg)
        {
            if (GameMain.Server == null) return;

            msg.Write(Prefab.OriginalName);
            msg.Write(Prefab.Identifier);
            msg.Write(Description != prefab.Description);
            if (Description != prefab.Description)
            {
                msg.Write(Description);
            }

            msg.Write(ID);

            if (ParentInventory == null || ParentInventory.Owner == null)
            {
                msg.Write((ushort)0);

                msg.Write(Position.X);
                msg.Write(Position.Y);
                msg.Write(Submarine != null ? Submarine.ID : (ushort)0);
            }
            else
            {
                msg.Write(ParentInventory.Owner.ID);

                //find the index of the ItemContainer this item is inside to get the item to
                //spawn in the correct inventory in multi-inventory items like fabricators
                byte containerIndex = 0;
                if (Container != null)
                {
                    for (int i = 0; i < Container.components.Count; i++)
                    {
                        if (Container.components[i] is ItemContainer container &&
                            container.Inventory == ParentInventory)
                        {
                            containerIndex = (byte)i;
                            break;
                        }
                    }
                }
                msg.Write(containerIndex);

                int slotIndex = ParentInventory.FindIndex(this);
                msg.Write(slotIndex < 0 ? (byte)255 : (byte)slotIndex);
            }

            byte teamID = 0;
            foreach (WifiComponent wifiComponent in GetComponents<WifiComponent>())
            {
                teamID = (byte)wifiComponent.TeamID;
                break;
            }

            msg.Write(teamID);
            bool tagsChanged = tags.Count != prefab.Tags.Count || !tags.All(t => prefab.Tags.Contains(t));
            msg.Write(tagsChanged);
            if (tagsChanged)
            {
                msg.Write(Tags);
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

        public void ServerWritePosition(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(ID);

            IWriteMessage tempBuffer = new WriteOnlyMessage();
            body.ServerWrite(tempBuffer, c, extraData);
            msg.Write((byte)tempBuffer.LengthBytes);
            msg.Write(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            msg.WritePadBits();
        }

        public void CreateServerEvent<T>(T ic) where T : ItemComponent, IServerSerializable
        {
            if (GameMain.Server == null) return;

            if (!ItemList.Contains(this))
            {
                string errorMsg = "Attempted to create a network event for an item (" + Name + ") that hasn't been fully initialized yet.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.CreateServerEvent:EventForUninitializedItem" + Name + ID, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            int index = components.IndexOf(ic);
            if (index == -1) return;

            GameMain.Server.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ComponentState, index });
        }
    }
}
