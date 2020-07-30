using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class MoneyAction : EventAction
    {
        public MoneyAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        [Serialize(0, true)]
        public int Amount { get; set; }

        private bool isFinished;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                campaign.Money += Amount;
#if SERVER
                (campaign as MultiPlayerCampaign).LastUpdateID++;
#endif
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetDataAction)} -> (Amount: {Amount.ColorizeObject()})";
        }
    }
}