using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        private readonly Dictionary<Identifier, float> prevSentSkill = new Dictionary<Identifier, float>();

        partial void OnSkillChanged(Identifier skillIdentifier, float prevLevel, float newLevel)
        {
            if (Character == null || Character.Removed) { return; }
            if (!prevSentSkill.ContainsKey(skillIdentifier))
            {
                prevSentSkill[skillIdentifier] = prevLevel;
            }
            if (Math.Abs(prevSentSkill[skillIdentifier] - newLevel) > 0.01f)
            {
                GameMain.NetworkMember.CreateEntityEvent(Character, new Character.UpdateSkillsEventData());
                prevSentSkill[skillIdentifier] = newLevel;
            }            
        }

        partial void OnExperienceChanged(int prevAmount, int newAmount)
        {
            if (Character == null || Character.Removed) { return; }
            if (prevAmount != newAmount)
            {
                GameServer.Log($"{GameServer.CharacterLogName(Character)} has gained {newAmount - prevAmount} experience ({prevAmount} -> {newAmount})", ServerLog.MessageType.Talent);
                GameMain.NetworkMember.CreateEntityEvent(Character, new Character.UpdateExperienceEventData());
            }
        }

        partial void OnPermanentStatChanged(StatTypes statType)
        {
            if (Character == null || Character.Removed) { return; }
            GameMain.NetworkMember.CreateEntityEvent(Character, new Character.UpdatePermanentStatsEventData(statType));
        }

        public void ServerWrite(IWriteMessage msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write(OriginalName);
            msg.Write((byte)Head.Preset.TagSet.Count);
            foreach (Identifier tag in Head.Preset.TagSet)
            {
                msg.Write(tag);
            }
            msg.Write((byte)Head.HairIndex);
            msg.Write((byte)Head.BeardIndex);
            msg.Write((byte)Head.MoustacheIndex);
            msg.Write((byte)Head.FaceAttachmentIndex);
            msg.WriteColorR8G8B8(Head.SkinColor);
            msg.WriteColorR8G8B8(Head.HairColor);
            msg.WriteColorR8G8B8(Head.FacialHairColor);
            msg.Write(ragdollFileName);

            if (Job != null)
            {
                msg.Write(Job.Prefab.Identifier);
                msg.Write((byte)Job.Variant);
                var skills = Job.GetSkills();
                msg.Write((byte)skills.Count());
                foreach (Skill skill in skills)
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

            msg.Write((ushort)ExperiencePoints);
            msg.WriteRangedInteger(AdditionalTalentPoints, 0, MaxAdditionalTalentPoints);
        }
    }
}
