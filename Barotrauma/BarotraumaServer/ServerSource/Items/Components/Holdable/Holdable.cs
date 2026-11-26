using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Items.Components
{
    partial class Holdable : Pickable, IServerSerializable, IClientSerializable
    {
        public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            base.ServerEventWrite(msg, c, extraData);

            bool writeAttachData = attachable && originalBody != null;
            msg.WriteBoolean(writeAttachData);
            if (!writeAttachData) { return; }

            UInt16 attacherId = Entity.NullEntityID;
            if (TryExtractEventData(extraData, out AttachEventData attachEventData) &&
                attachEventData.Attacher != null)
            {
                attacherId = attachEventData.Attacher.ID;
            }

            msg.WriteBoolean(Attached);
            msg.WriteSingle(originalBody.SimPosition.X);
            msg.WriteSingle(originalBody.SimPosition.Y);
            msg.WriteUInt16(item.Submarine?.ID ?? Entity.NullEntityID);
            msg.WriteUInt16(attacherId);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            Vector2 simPosition = new Vector2(msg.ReadSingle(), msg.ReadSingle());
            float newSpriteDepth = msg.ReadSingle();
            bool newHorizontalFlipState = msg.ReadBoolean();
                
            if (!item.CanClientAccess(c) || !Attachable || attached || !MathUtils.IsValid(simPosition)) { return; }

            if (AllowInGameSpriteDepthEditing)
            {
                item.SpriteDepth = MathHelper.Clamp(newSpriteDepth, MinSpriteDepth, MaxSpriteDepth);
            }
            if (AllowInGameFlipping)
            {
                currentHorizontalFlipState = newHorizontalFlipState;
            }

            Vector2 offset = ConvertUnits.ToDisplayUnits(simPosition - c.Character.SimPosition);
            if (!IsWithinAttachReach(offset))
            {
                DebugConsole.AddWarning($"Client {c.Name} attempted to attach {item.Name} too far away from its character ({(int)offset.Length()} units). Ignoring the request.");
                return;
            }

            Drop(false, null);
            item.SetTransform(simPosition, 0.0f, findNewHull: false);
            //don't find the new hull in SetTransform, because that'd also potentially change the submarine (teleport the item outside if it's attached outside)
            //instead just find the hull, so the item is considered to be in the right hull
            item.CurrentHull = Hull.FindHull(item.WorldPosition, item.CurrentHull);
            
            // AttachToWall may attach a different item than this one (eg. with catalog blueprints), and that's the one we need to sync the attach state for
            Item attachedItem = AttachToWall();

            OnUsed.Invoke(new ItemUseInfo(item, c.Character));

            if (attachedItem.GetComponent<Holdable>() is { } attachedHoldable)
            {
                attachedItem.CreateServerEvent(attachedHoldable, new AttachEventData(attachedItem.SimPosition, c.Character));
            }
            c.Character.Inventory?.CreateNetworkEvent();

            GameServer.Log(GameServer.CharacterLogName(c.Character) + " attached " + item.Name + " to a wall", ServerLog.MessageType.ItemInteraction);
        }
    }
}
