using Barotrauma.Networking;
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

            if (!item.CanClientAccess(c) || !Attachable || attached || !MathUtils.IsValid(simPosition)) { return; }

            Vector2 offset = simPosition - c.Character.SimPosition;
            offset = offset.ClampLength(MaxAttachDistance * 1.5f);
            simPosition = c.Character.SimPosition + offset;

            Drop(false, null);
            item.SetTransform(simPosition, 0.0f, findNewHull: false);
            //don't find the new hull in SetTransform, because that'd also potentially change the submarine (teleport the item outside if it's attached outside)
            //instead just find the hull, so the item is considered to be in the right hull
            item.CurrentHull = Hull.FindHull(item.WorldPosition, item.CurrentHull);
            AttachToWall();
            OnUsed.Invoke(new ItemUseInfo(item, c.Character));

            item.CreateServerEvent(this, new AttachEventData(simPosition, c.Character));
            c.Character.Inventory?.CreateNetworkEvent();

            GameServer.Log(GameServer.CharacterLogName(c.Character) + " attached " + item.Name + " to a wall", ServerLog.MessageType.ItemInteraction);
        }
    }
}
