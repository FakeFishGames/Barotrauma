using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Mission
    {
        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Length && messageIndex >= Messages.Length) { return; }
            if (messageIndex < 0) { return; }

            LocalizedString header = messageIndex < Headers.Length ? Headers[messageIndex] : "";
            LocalizedString message = messageIndex < Messages.Length ? Messages[messageIndex] : "";
            if (!message.IsNullOrEmpty())
            {
                message = ModifyMessage(message, color: false);
            }

            GameServer.Log($"{TextManager.Get("MissionInfo")}: {header} - {message}", ServerLog.MessageType.ServerMessage);
        }

        public static int DistributeRewardsToCrew(IEnumerable<Character> crew, int totalReward)
        {
            int remainingRewards = totalReward;
            float sum = GetRewardDistibutionSum(crew);
            if (MathUtils.NearlyEqual(sum, 0)) { return remainingRewards; }
            foreach (Character character in crew)
            {
                int rewardDistribution = character.Wallet.RewardDistribution;
                float rewardWeight = sum > 100 ? rewardDistribution / sum : rewardDistribution / 100f;
                int reward = Math.Min(remainingRewards, (int)(totalReward * rewardWeight));
                character.Wallet.Give(reward);
                remainingRewards -= reward;
                if (remainingRewards <= 0) { break; }
            }

            return remainingRewards;
        }

        partial void DistributeExperienceToCrew(IEnumerable<Character> crew, int experienceGain)
        {
            Dictionary<Character, float> traitorExpSteal = new Dictionary<Character, float>();
            float totalExpSteal = 0.0f;
            foreach (var traitorEvent in GameMain.Server.TraitorManager.ActiveEvents)
            {
                if (traitorEvent.TraitorEvent.CurrentState != TraitorEvent.State.Completed) { continue; }
                if (traitorEvent.Traitor?.Character == null || !GameMain.Server.ConnectedClients.Contains(traitorEvent.Traitor)) { continue; }

                float expSteal = Math.Max(traitorEvent.TraitorEvent.Prefab.StealPercentageOfExperience, 0.0f);
                AddTraitorExpSteal(traitorEvent.Traitor.Character, expSteal);
                foreach (var secondaryTraitor in traitorEvent.TraitorEvent.SecondaryTraitors)
                {
                    AddTraitorExpSteal(secondaryTraitor.Character, expSteal);
                }

                void AddTraitorExpSteal(Character traitorCharacter, float expSteal)
                {
                    if (traitorCharacter == null) { return; }
                    if (!traitorExpSteal.ContainsKey(traitorCharacter))
                    {
                        traitorExpSteal.Add(traitorCharacter, 0.0f);
                    }
                    traitorExpSteal[traitorCharacter] += expSteal;
                }
            }
            totalExpSteal = traitorExpSteal.Values.Sum();
            //if exp to steal exceeds 100%, normalize to get it back to 100%
            //(e.g. two traitors who both steal 75%, they'll share 50% of all the exp gains)
            if (totalExpSteal > 100.0f)
            {
                foreach (Character traitor in traitorExpSteal.Keys)
                {
                    traitorExpSteal[traitor] /= totalExpSteal;
                }
                totalExpSteal = 100.0f;
            }
            if (totalExpSteal > 0)
            {
                GameServer.Log($"Traitors stole {(int)totalExpSteal}% of the total experience.", ServerLog.MessageType.Traitors);
            }

            int nonTraitorCount = GameSession.GetSessionCrewCharacters(CharacterType.Both).Count(c => !traitorExpSteal.ContainsKey(c));
            foreach (Networking.Client c in GameMain.Server.ConnectedClients)
            {
                //give the experience to the stored characterinfo if the client isn't currently controlling a character
                GiveMissionExperience(c.Character?.Info ?? c.CharacterInfo);
            }
            foreach (Character bot in GameSession.GetSessionCrewCharacters(CharacterType.Bot))
            {
                GiveMissionExperience(bot.Info);
            }

            void GiveMissionExperience(CharacterInfo info)
            {
                if (info == null) { return; }
                var experienceGainMultiplierIndividual = new AbilityMissionExperienceGainMultiplier(this, 1f, info.Character);
                //check if anyone else in the crew has talents that could give a bonus to this one
                foreach (var c in crew)
                {
                    if (c == info.Character) { continue; }
                    c.CheckTalents(AbilityEffectType.OnAllyGainMissionExperience, experienceGainMultiplierIndividual);
                }
                info.Character?.CheckTalents(AbilityEffectType.OnGainMissionExperience, experienceGainMultiplierIndividual);

                int finalExperienceGain = (int)(experienceGain * experienceGainMultiplierIndividual.Value);
                if (info.Character != null && traitorExpSteal.TryGetValue(info.Character, out float expToSteal))
                {
                    int stealAmount = (int)(experienceGain * nonTraitorCount * expToSteal / 100.0f);
                    GameServer.Log($"Traitor {info.Character} stole {stealAmount} ({(int)expToSteal}%) of the total experience.", ServerLog.MessageType.Traitors);
                    finalExperienceGain += stealAmount;
                }
                else
                {
                    GameServer.Log($"{(int)(finalExperienceGain * totalExpSteal / 100.0f)} ({(int)totalExpSteal}%) was stolen from {info.Name}.", ServerLog.MessageType.Traitors);
                    finalExperienceGain -= (int)(finalExperienceGain * totalExpSteal / 100.0f);
                }
                info.GiveExperience(finalExperienceGain);
            }
        }

        public virtual void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.WriteUInt16((ushort)State);
        }

        public virtual void ServerWrite(IWriteMessage msg)
        {
            msg.WriteUInt16((ushort)State);
        }
    }
}