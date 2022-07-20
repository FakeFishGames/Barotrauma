using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        private readonly Dictionary<Identifier, float> prevSentSkill = new Dictionary<Identifier, float>();

        /// <summary>
        /// The client opted to create a new character and discard this one
        /// </summary>
        public bool Discarded;

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
                msg.Write(Job.Prefab.UintIdentifier);
                msg.Write((byte)Job.Variant);
                foreach (SkillPrefab skillPrefab in Job.Prefab.Skills.OrderBy(s => s.Identifier))
                {
                    msg.Write(Job.GetSkill(skillPrefab.Identifier).Level);
                }
            }
            else
            {
                msg.Write((uint)0);
                msg.Write((byte)0);
            }

            msg.Write((ushort)ExperiencePoints);
            msg.WriteRangedInteger(AdditionalTalentPoints, 0, MaxAdditionalTalentPoints);
        }
    }
}
