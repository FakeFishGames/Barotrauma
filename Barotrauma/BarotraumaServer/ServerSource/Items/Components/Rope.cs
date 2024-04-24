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
                switch (source)
                {
                    case Entity { Removed: false } entity:
                        msg.WriteUInt16(entity.ID);
                        msg.WriteByte((byte)0);
                        break;
                    case Limb { character.Removed: false } limb:
                        msg.WriteUInt16(limb.character.ID);
                        msg.WriteByte((byte)limb.character.AnimController.Limbs.IndexOf(limb));
                        break;
                    default:
                        msg.WriteUInt16(Entity.NullEntityID);
                        msg.WriteByte((byte)0);
                        break;
                }
            }
        }
    }
}
