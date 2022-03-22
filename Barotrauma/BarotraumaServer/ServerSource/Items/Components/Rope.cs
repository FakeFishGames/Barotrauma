using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent
    {
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(Snapped);

            if (!Snapped)
            {
                msg.Write(target?.ID ?? Entity.NullEntityID);
                if (source is Entity entity && !entity.Removed)
                {
                    msg.Write(entity?.ID ?? Entity.NullEntityID);
                    msg.Write((byte)0);
                }
                else if (source is Limb limb && limb.character != null && !limb.character.Removed)
                {
                    msg.Write(limb.character?.ID ?? Entity.NullEntityID);
                    msg.Write((byte)limb.character.AnimController.Limbs.IndexOf(limb));
                }
                else
                {
                    msg.Write(Entity.NullEntityID);
                    msg.Write((byte)0);
                }
            }
        }
    }
}
