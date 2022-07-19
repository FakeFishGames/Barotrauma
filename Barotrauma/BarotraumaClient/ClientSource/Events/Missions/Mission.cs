using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
            int v = Difficulty ?? MissionPrefab.MinDifficulty;
            float t = MathUtils.InverseLerp(MissionPrefab.MinDifficulty, MissionPrefab.MaxDifficulty, v);
            return ToolBox.GradientLerp(t, GUIStyle.Green, GUIStyle.Orange, GUIStyle.Red);
        }

        public virtual RichString GetMissionRewardText(Submarine sub)
        {
            LocalizedString rewardText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", GetReward(sub)));
            return RichString.Rich(TextManager.GetWithVariable("missionreward", "[reward]", "‖color:gui.orange‖"+rewardText+"‖end‖"));
        }

        public RichString GetReputationRewardText(Location currLocation)
        {
            List<LocalizedString> reputationRewardTexts = new List<LocalizedString>();
            foreach (var reputationReward in ReputationRewards)
            {
                LocalizedString name = "";
                
                if (reputationReward.Key == "location")
                {
                    name = $"‖color:gui.orange‖{currLocation.Name}‖end‖";
                }
                else
                {
                    var faction = FactionPrefab.Prefabs.Find(f => f.Identifier == reputationReward.Key);
                    if (faction != null)
                    {
                        name = $"‖color:{XMLExtensions.ColorToString(faction.IconColor)}‖{faction.Name}‖end‖";
                    }
                    else
                    {
                        name = TextManager.Get(reputationReward.Key);
                    }
                }
                float normalizedValue = MathUtils.InverseLerp(-100.0f, 100.0f, reputationReward.Value);
                string formattedValue = ((int)reputationReward.Value).ToString("+#;-#;0"); //force plus sign for positive numbers
                LocalizedString rewardText = TextManager.GetWithVariables(
                    "reputationformat",
                    ("[reputationname]", name),
                    ("[reputationvalue]", $"‖color:{XMLExtensions.ColorToString(Reputation.GetReputationColor(normalizedValue))}‖{formattedValue}‖end‖" ));
                reputationRewardTexts.Add(rewardText.Value);
            }
            return RichString.Rich(TextManager.AddPunctuation(':', TextManager.Get("reputation"), LocalizedString.Join(", ", reputationRewardTexts)));
        }

        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Length && messageIndex >= Messages.Length) { return; }
            if (messageIndex < 0) { return; }

            LocalizedString header = messageIndex < Headers.Length ? Headers[messageIndex] : "";
            LocalizedString message = messageIndex < Messages.Length ? Messages[messageIndex] : "";

            CoroutineManager.StartCoroutine(ShowMessageBoxAfterRoundSummary(header, message));
        }

        private IEnumerable<CoroutineStatus> ShowMessageBoxAfterRoundSummary(LocalizedString header, LocalizedString message)
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
