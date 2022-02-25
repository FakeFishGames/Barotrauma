using System.Xml.Linq;

namespace Barotrauma
{
    class CheckMoneyAction : BinaryOptionAction
    {
        [Serialize(0, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        public CheckMoneyAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
        }

        protected override bool? DetermineSuccess()
        {
            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                return campaign.Money >= Amount;
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