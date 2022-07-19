using Barotrauma.Networking;
using System.Globalization;

namespace Barotrauma
{
    partial class CargoMission : Mission
    {
        public override bool DisplayAsCompleted => false;
        public override bool DisplayAsFailed => false;

        public override RichString GetMissionRewardText(Submarine sub)
        {
            LocalizedString rewardText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", GetReward(sub)));

            LocalizedString retVal;
            if (rewardPerCrate.HasValue)
            {
                LocalizedString rewardPerCrateText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", rewardPerCrate.Value));
                retVal = TextManager.GetWithVariables("missionrewardcargopercrate",
                    ("[rewardpercrate]", rewardPerCrateText),
                    ("[itemcount]", itemsToSpawn.Count.ToString()),
                    ("[maxitemcount]", maxItemCount.ToString()),
                    ("[totalreward]", $"‖color:gui.orange‖{rewardText}‖end‖"));
            }
            else
            {
                retVal = TextManager.GetWithVariables("missionrewardcargo",
                    ("[totalreward]", $"‖color:gui.orange‖{rewardText}‖end‖"),
                    ("[itemcount]", itemsToSpawn.Count.ToString()),
                    ("[maxitemcount]", maxItemCount.ToString()));
            }

            return RichString.Rich(retVal);
        }
        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
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
