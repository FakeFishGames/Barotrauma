using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        private readonly Dictionary<string, float> prevSentSkill = new Dictionary<string, float>();

        partial void OnSkillChanged(string skillIdentifier, float prevLevel, float newLevel, Vector2 textPopupPos)
        {
            if (Character == null || Character.Removed) { return; }
            if (!prevSentSkill.ContainsKey(skillIdentifier))
            {
                prevSentSkill[skillIdentifier] = prevLevel;
            }
            if (Math.Abs(prevSentSkill[skillIdentifier] - newLevel) > 0.01f)
            {
                GameMain.NetworkMember.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.UpdateSkills });
                prevSentSkill[skillIdentifier] = newLevel;
            }            
        }

        partial void OnExperienceChanged(int prevAmount, int newAmount, Vector2 textPopupPos)
        {
            if (Math.Abs(prevAmount - newAmount) > 0)
            {
                GameMain.NetworkMember.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.UpdateExperience });
            }
        }

        public void ServerWrite(IWriteMessage msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write(OriginalName);
            msg.Write((byte)Gender);
            msg.Write((byte)Race);
            msg.Write((byte)HeadSpriteId);
            msg.Write((byte)HairIndex);
            msg.Write((byte)BeardIndex);
            msg.Write((byte)MoustacheIndex);
            msg.Write((byte)FaceAttachmentIndex);
            msg.WriteColorR8G8B8(SkinColor);
            msg.WriteColorR8G8B8(HairColor);
            msg.WriteColorR8G8B8(FacialHairColor);
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
            msg.Write((byte)savedStatValues.SelectMany(s => s.Value).Count());
            foreach (var savedStatValuePair in savedStatValues)
            {
                foreach (var savedStatValue in savedStatValuePair.Value)
                {
                    msg.Write((byte)savedStatValuePair.Key);
                    msg.Write(savedStatValue.StatIdentifier);
                    msg.Write(savedStatValue.StatValue);
                    msg.Write(savedStatValue.RemoveOnDeath);
                }
            }
            msg.Write((ushort)ExperiencePoints);
            msg.Write((ushort)AdditionalTalentPoints);
        }
    }
}
