using Lidgren.Network;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        partial void SpawnInventoryItemProjSpecific(Item item)
        {
            Entity.Spawner.CreateNetworkEvent(item, false);
        }

        public void ServerWrite(NetBuffer msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write(Gender == Gender.Female);
            msg.Write((byte)Race);
            msg.Write((byte)HeadSpriteId);
            msg.Write((byte)Head.HairIndex);
            msg.Write((byte)Head.BeardIndex);
            msg.Write((byte)Head.MoustacheIndex);
            msg.Write((byte)Head.FaceAttachmentIndex);
            msg.Write(ragdollFileName);

            if (Job != null)
            {
                msg.Write(Job.Prefab.Identifier);
                foreach (Skill skill in Job.Skills)
                {
                    msg.Write(skill.Level);
                }
            }
            else
            {
                msg.Write("");
            }
            // TODO: animations
        }
    }
}
