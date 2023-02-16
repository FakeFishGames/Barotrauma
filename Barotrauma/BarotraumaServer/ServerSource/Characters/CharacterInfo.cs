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

        public void ApplyDeathEffects()
        {
            RespawnManager.ReduceCharacterSkills(this);
            RemoveSavedStatValuesOnDeath();
            CauseOfDeath = null;
        }

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
            msg.WriteUInt16(ID);
            msg.WriteString(Name);
            msg.WriteString(OriginalName);
            msg.WriteByte((byte)Head.Preset.TagSet.Count);
            foreach (Identifier tag in Head.Preset.TagSet)
            {
                msg.WriteIdentifier(tag);
            }
            msg.WriteByte((byte)Head.HairIndex);
            msg.WriteByte((byte)Head.BeardIndex);
            msg.WriteByte((byte)Head.MoustacheIndex);
            msg.WriteByte((byte)Head.FaceAttachmentIndex);
            msg.WriteColorR8G8B8(Head.SkinColor);
            msg.WriteColorR8G8B8(Head.HairColor);
            msg.WriteColorR8G8B8(Head.FacialHairColor);

            msg.WriteString(ragdollFileName);
            msg.WriteIdentifier(HumanPrefabIds.NpcIdentifier);
            msg.WriteIdentifier(MinReputationToHire.factionId);
            if (MinReputationToHire.factionId != default)
            {
                msg.WriteSingle(MinReputationToHire.reputation);
            }
            if (Job != null)
            {
                msg.WriteUInt32(Job.Prefab.UintIdentifier);
                msg.WriteByte((byte)Job.Variant);
                foreach (SkillPrefab skillPrefab in Job.Prefab.Skills.OrderBy(s => s.Identifier))
                {
                    msg.WriteSingle(Job.GetSkill(skillPrefab.Identifier)?.Level ?? 0.0f);
                }
            }
            else
            {
                msg.WriteUInt32((uint)0);
                msg.WriteByte((byte)0);
            }

            msg.WriteUInt16((ushort)ExperiencePoints);
            msg.WriteRangedInteger(AdditionalTalentPoints, 0, MaxAdditionalTalentPoints);
        }
    }
}
