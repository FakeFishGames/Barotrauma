using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        public void ServerWrite(IWriteMessage msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write((byte)Gender);
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
                msg.Write((byte)Job.Variant);
                msg.Write((byte)Job.Skills.Count);
                foreach (Skill skill in Job.Skills)
                {
                    msg.Write(skill.Identifier);
                    msg.Write(skill.Level);
                }
            }
            else
            {
                msg.Write("");
                msg.Write((byte)0);
            }
            // TODO: animations
        }
    }
}
