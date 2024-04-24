using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Check whether the crew or a specific player has enough money.
    /// </summary>
    class CheckMoneyAction : BinaryOptionAction
    {
        [Serialize(0, IsPropertySaveable.Yes, description: "Minimum amount of money the crew or the player must have.")]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the player to check. If omitted, the crew's shared wallet is checked instead.")]
        public Identifier TargetTag { get; set; }

        public CheckMoneyAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
        }

        protected override bool? DetermineSuccess()
        {
            Client matchingClient = null;
            bool hasTag = !TargetTag.IsEmpty;
#if SERVER
            IEnumerable<Entity> targets = ParentEvent.GetTargets(TargetTag);

            if (hasTag)
            {
                foreach (Entity entity in targets)
                {
                    if (entity is Character && GameMain.Server?.ConnectedClients.FirstOrDefault(c => c.Character == entity) is { } matchingCharacter)
                    {
                        matchingClient = matchingCharacter;
                        break;
                    }
                }
            }
#endif

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                return !hasTag ? campaign.Bank.CanAfford(Amount) : campaign.GetWallet(matchingClient).CanAfford(Amount);
            }
            return false;
        }

        public override string ToDebugString()
        {
            string subActionStr = "";
            if (succeeded.HasValue)
            {
                subActionStr = $"\n            Sub action: {(succeeded.Value ? Success : Failure)?.CurrentSubAction.ColorizeObject()}";
            }
            return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(CheckMoneyAction)} -> (Amount: {Amount.ColorizeObject()}" +
                   $" Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})" +
                   subActionStr;
        }
    }
}