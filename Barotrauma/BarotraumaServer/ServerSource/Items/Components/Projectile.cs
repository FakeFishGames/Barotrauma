using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class Projectile : ItemComponent
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(StickTarget != null);
            if (StickTarget != null)
            {
                msg.Write(item.body.SimPosition.X);
                msg.Write(item.body.SimPosition.Y);
                msg.Write(stickJoint.Axis.X);
                msg.Write(stickJoint.Axis.Y);
                if (StickTarget.UserData is Structure structure)
                {
                    msg.Write(structure.ID);
                    msg.Write((byte)structure.Bodies.IndexOf(StickTarget));
                }
                else if (StickTarget.UserData is Entity entity)
                {
                    msg.Write(entity.ID);
                }
                else if (StickTarget.UserData is Limb limb)
                {
                    msg.Write(limb.character.ID);
                    msg.Write((byte)Array.IndexOf(limb.character.AnimController.Limbs, limb));
                }
                else
                {
                    throw new NotImplementedException(StickTarget.UserData?.ToString() ?? "null" + " is not a valid projectile stick target.");
                }
            }
        }
    }
}
