using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class Holdable : Pickable, IServerSerializable, IClientSerializable
    {
        public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            base.ServerEventWrite(msg, c, extraData);

            bool writeAttachData = attachable && body != null;
            msg.WriteBoolean(writeAttachData);
            if (!writeAttachData) { return; }

            msg.WriteBoolean(Attached);
            msg.WriteSingle(body.SimPosition.X);
            msg.WriteSingle(body.SimPosition.Y);
            msg.WriteUInt16(item.Submarine?.ID ?? Entity.NullEntityID);
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
            AttachToWall();

            item.CreateServerEvent(this);
            c.Character.Inventory?.CreateNetworkEvent();

            GameServer.Log(GameServer.CharacterLogName(c.Character) + " attached " + item.Name + " to a wall", ServerLog.MessageType.ItemInteraction);
        }
    }
}
