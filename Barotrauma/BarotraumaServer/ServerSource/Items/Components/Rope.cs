using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(Snapped);

            if (!Snapped)
            {
                msg.WriteUInt16(target?.ID ?? Entity.NullEntityID);
                if (source is Entity entity && !entity.Removed)
                {
                    msg.WriteUInt16(entity?.ID ?? Entity.NullEntityID);
                    msg.WriteByte((byte)0);
                }
                else if (source is Limb limb && limb.character != null && !limb.character.Removed)
                {
                    msg.WriteUInt16(limb.character?.ID ?? Entity.NullEntityID);
                    msg.WriteByte((byte)limb.character.AnimController.Limbs.IndexOf(limb));
                }
                else
                {
                    msg.WriteUInt16(Entity.NullEntityID);
                    msg.WriteByte((byte)0);
                }
            }
        }
    }
}
