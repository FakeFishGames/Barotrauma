using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class Holdable : Pickable, IServerSerializable, IClientSerializable
    {
        public override void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            base.ServerWrite(msg, c, extraData);
            if (!attachable || body == null) { return; }

            msg.Write(Attached);
            msg.Write(body.SimPosition.X);
            msg.Write(body.SimPosition.Y);
            msg.Write(item.Submarine?.ID ?? Entity.NullEntityID);
        }

        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
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
            GameServer.Log(GameServer.CharacterLogName(c.Character) + " attached " + item.Name + " to a wall", ServerLog.MessageType.ItemInteraction);
        }
    }
}
