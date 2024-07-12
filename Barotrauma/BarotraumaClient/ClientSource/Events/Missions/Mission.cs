using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static Barotrauma.MissionPrefab;

namespace Barotrauma
{
    abstract partial class Mission
    {
        private readonly List<LocalizedString> shownMessages = new List<LocalizedString>();
        public IEnumerable<LocalizedString> ShownMessages => shownMessages;

        public bool DisplayTargetHudIcons => Prefab.DisplayTargetHudIcons;

        public virtual IEnumerable<Entity> HudIconTargets => Enumerable.Empty<Entity>();

        /// <summary>
        /// Is the mission at a state at which the only thing left to do is to reach the end of the level?
        /// </summary>
        public abstract bool DisplayAsCompleted { get; }
        /// <summary>
        /// Is the mission at a state at which the mission cannot be completed anymore?
        /// </summary>
        public abstract bool DisplayAsFailed { get; }

        public Color GetDifficultyColor()
        {
            return GetDifficultyColor(Difficulty ?? MissionPrefab.MinDifficulty);
        }
        public static Color GetDifficultyColor(int difficulty)
        {
            int v = difficulty;
            float t = MathUtils.InverseLerp(MissionPrefab.MinDifficulty, MissionPrefab.MaxDifficulty, v);
            return ToolBox.GradientLerp(t, GUIStyle.Green, GUIStyle.Orange, GUIStyle.Red);
        }

        /// <summary>
        /// Returns the amount of marks you get from the reward (e.g. "3,000 mk")
        /// </summary>
        protected LocalizedString GetRewardAmountText(Submarine sub)
        {
            int baseReward = GetReward(sub);
            int finalReward = GetFinalReward(sub);
            string rewardAmountText = string.Format(CultureInfo.InvariantCulture, "{0:N0}", baseReward);
            if (finalReward > baseReward)
            {
                rewardAmountText += $" + {string.Format(CultureInfo.InvariantCulture, "{0:N0}", finalReward - baseReward)}";
            }
            return TextManager.GetWithVariable("currencyformat", "[credits]", rewardAmountText);
        }

        /// <summary>
        /// Returns the full reward text of the mission (e.g. "Reward: 2,000 mk" or "Reward: 500 mk x 2 (out of max 5) = 1,000 mk")
        /// </summary>
        public virtual RichString GetMissionRewardText(Submarine sub)
        {
            LocalizedString rewardText = GetRewardAmountText(sub);
            return RichString.Rich(TextManager.GetWithVariable("missionreward", "[reward]", "‖color:gui.orange‖" + rewardText + "‖end‖"));
        }

        public RichString GetReputationRewardText()
        {
            List<LocalizedString> reputationRewardTexts = new List<LocalizedString>();
            foreach (var reputationReward in ReputationRewards)
            {
                FactionPrefab factionPrefab;
                if (reputationReward.FactionIdentifier == "location" )
                {
                    factionPrefab = OriginLocation.Faction?.Prefab;
                }
                else
                {
                    FactionPrefab.Prefabs.TryGet(reputationReward.FactionIdentifier, out factionPrefab);
                }

                if (factionPrefab != null)
                {
                    AddReputationText(factionPrefab, reputationReward.Amount);
                    if (!MathUtils.NearlyEqual(reputationReward.AmountForOpposingFaction, 0.0f) &&
                        FactionPrefab.Prefabs.TryGet(factionPrefab.OpposingFaction, out var opposingFactionPrefab))
                    {
                        AddReputationText(opposingFactionPrefab, reputationReward.AmountForOpposingFaction);
                    }
                }
            }

            void AddReputationText(FactionPrefab factionPrefab, float amount)
            {
                if (factionPrefab == null) { return; }

                float totalReputationChange = amount;
                if (GameMain.GameSession?.Campaign?.Factions.Find(f => f.Prefab == factionPrefab) is Faction faction)
                {
                    totalReputationChange = amount * faction.Reputation.GetReputationChangeMultiplier(amount);
                }

                LocalizedString name = $"‖color:{XMLExtensions.ToStringHex(factionPrefab.IconColor)}‖{factionPrefab.Name}‖end‖";
                float normalizedValue = MathUtils.InverseLerp(-100.0f, 100.0f, totalReputationChange);
                string formattedValue = ((int)Math.Round(totalReputationChange)).ToString("+#;-#;0"); //force plus sign for positive numbers
                LocalizedString rewardText = TextManager.GetWithVariables(
                    "reputationformat",
                    ("[reputationname]", name),
                    ("[reputationvalue]", $"‖color:{XMLExtensions.ToStringHex(Reputation.GetReputationColor(normalizedValue))}‖{formattedValue}‖end‖"));
                reputationRewardTexts.Add(rewardText.Value);
            }


            if (reputationRewardTexts.Any())
            {
                return RichString.Rich(TextManager.AddPunctuation(':', TextManager.Get("reputation"), LocalizedString.Join(", ", reputationRewardTexts)));
            }
            else
            {
                return string.Empty;
            }
        }
        partial void DistributeExperienceToCrew(IEnumerable<Character> crew, int experienceGain)
        {
            foreach (Character character in crew)
            {
                GiveMissionExperience(character.Info);
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
                info.GiveExperience((int)(experienceGain * experienceGainMultiplierIndividual.Value));
            }
        }

        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Length && messageIndex >= Messages.Length) { return; }
            if (messageIndex < 0) { return; }

            LocalizedString header = messageIndex < Headers.Length ? Headers[messageIndex] : "";
            LocalizedString message = messageIndex < Messages.Length ? Messages[messageIndex] : "";
            if (!message.IsNullOrEmpty())
            {
                message = ModifyMessage(message);
            }

            CoroutineManager.StartCoroutine(ShowMessageBoxWhenRoundSummaryIsNotActive(header, message));
        }

        private IEnumerable<CoroutineStatus> ShowMessageBoxWhenRoundSummaryIsNotActive(LocalizedString header, LocalizedString message)
        {
            while (GUIMessageBox.VisibleBox?.UserData is RoundSummary)
            {
                yield return new WaitForSeconds(1.0f);
            }
            CreateMessageBox(header, message);
            yield return CoroutineStatus.Success;
        }

        protected void CreateMessageBox(LocalizedString header, LocalizedString message)
        {
            shownMessages.Add(message);
            new GUIMessageBox(RichString.Rich(header), RichString.Rich(message), buttons: Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: Prefab.Icon)
            {
                IconColor = Prefab.IconColor
            };
        }

        public Identifier GetOverrideMusicType()
        {
            return Prefab.GetOverrideMusicType(State);
        }

        public virtual void ClientRead(IReadMessage msg)
        {
            State = msg.ReadInt16();
        }

        public virtual void ClientReadInitial(IReadMessage msg)
        {
            state = msg.ReadInt16();
        }
    }
}
