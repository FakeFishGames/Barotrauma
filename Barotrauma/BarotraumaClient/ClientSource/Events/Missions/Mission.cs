using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Barotrauma
{
    abstract partial class Mission
    {
        private readonly List<string> shownMessages = new List<string>();
        public IEnumerable<string> ShownMessages
        {
            get { return shownMessages; }
        }

        public Color GetDifficultyColor()
        {
            int v = Difficulty ?? MissionPrefab.MinDifficulty;
            float t = MathUtils.InverseLerp(MissionPrefab.MinDifficulty, MissionPrefab.MaxDifficulty, v);
            return ToolBox.GradientLerp(t, GUI.Style.Green, GUI.Style.Orange, GUI.Style.Red);
        }

        public string GetMissionRewardText()
        {
            string rewardText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", Reward));
            return TextManager.GetWithVariable("missionreward", "[reward]", $"‖color:gui.orange‖{rewardText}‖end‖");
        }

        public string GetReputationRewardText(Location currLocation)
        {
            List<string> reputationRewardTexts = new List<string>();
            foreach (var reputationReward in ReputationRewards)
            {
                string name = "";
                
                if (reputationReward.Key.Equals("location", StringComparison.OrdinalIgnoreCase))
                {
                    name = $"‖color:gui.orange‖{currLocation.Name}‖end‖";
                }
                else
                {
                    var faction = FactionPrefab.Prefabs.Find(f => f.Identifier.Equals(reputationReward.Key, StringComparison.OrdinalIgnoreCase));
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
                string rewardText = TextManager.GetWithVariables(
                    "reputationformat",
                    new string[] { "[reputationname]", "[reputationvalue]" },
                    new string[] { name, $"‖color:{XMLExtensions.ColorToString(Reputation.GetReputationColor(normalizedValue))}‖{formattedValue}‖end‖" });
                reputationRewardTexts.Add(rewardText);
            }
            return TextManager.AddPunctuation(':', TextManager.Get("reputation"), string.Join(", ", reputationRewardTexts));
        }

        partial void ShowMessageProjSpecific(int missionState)
        {
            int messageIndex = missionState - 1;
            if (messageIndex >= Headers.Count && messageIndex >= Messages.Count) { return; }
            if (messageIndex < 0) { return; }

            string header = messageIndex < Headers.Count ? Headers[messageIndex] : "";
            string message = messageIndex < Messages.Count ? Messages[messageIndex] : "";

            CoroutineManager.StartCoroutine(ShowMessageBoxAfterRoundSummary(header, message));
        }

        private IEnumerable<object> ShowMessageBoxAfterRoundSummary(string header, string message)
        {
            while (GUIMessageBox.VisibleBox?.UserData is RoundSummary)
            {
                yield return new WaitForSeconds(1.0f);
            }
            CreateMessageBox(header, message);
            yield return CoroutineStatus.Success;
        }

        protected void CreateMessageBox(string header, string message)
        {
            shownMessages.Add(message);
            new GUIMessageBox(header, message, buttons: new string[0], type: GUIMessageBox.Type.InGame, icon: Prefab.Icon, parseRichText: true)
            {
                IconColor = Prefab.IconColor
            };
        }

        public void ClientRead(IReadMessage msg)
        {
            State = msg.ReadInt16();
        }

        public abstract void ClientReadInitial(IReadMessage msg);
    }
}
