using Barotrauma.Networking;
using System.Globalization;

namespace Barotrauma
{
    partial class CargoMission : Mission
    {
        public override string GetMissionRewardText(Submarine sub)
        {
            string rewardText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", GetReward(sub)));

            if (rewardPerCrate.HasValue)
            {
                string rewardPerCrateText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", rewardPerCrate.Value));
                return TextManager.GetWithVariables("missionrewardcargopercrate",
                    new string[] { "[rewardpercrate]", "[itemcount]", "[maxitemcount]", "[totalreward]" },
                    new string[] { rewardPerCrateText, itemsToSpawn.Count.ToString(), maxItemCount.ToString(), $"‖color:gui.orange‖{rewardText}‖end‖" });
            }
            else
            {
                return TextManager.GetWithVariables("missionrewardcargo",
                    new string[] { "[totalreward]", "[itemcount]", "[maxitemcount]" },
                    new string[] { $"‖color:gui.orange‖{rewardText}‖end‖", itemsToSpawn.Count.ToString(), maxItemCount.ToString() });
            }
        }
        public override void ClientReadInitial(IReadMessage msg)
        {
            items.Clear();
            ushort itemCount = msg.ReadUInt16();
            for (int i = 0; i < itemCount; i++)
            {
                items.Add(Item.ReadSpawnData(msg));
            }
            if (items.Contains(null))
            {
                throw new System.Exception("Error in CargoMission.ClientReadInitial: item list contains null (mission: " + Prefab.Identifier + ")");
            }
            if (items.Count != itemCount)
            {
                throw new System.Exception("Error in CargoMission.ClientReadInitial: item count does not match the server count (" + itemCount + " != " + items.Count + ", mission: " + Prefab.Identifier + ")");
            }
            if (requiredDeliveryAmount == 0) { requiredDeliveryAmount = items.Count; }
            if (requiredDeliveryAmount > items.Count)
            {
                DebugConsole.AddWarning($"Error in mission \"{Prefab.Identifier}\". Required delivery amount is {requiredDeliveryAmount} but there's only {items.Count} items to deliver.");
                requiredDeliveryAmount = items.Count;
            }
        }
    }
}
