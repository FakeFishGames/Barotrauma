using Barotrauma.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Give or remove money from the crew or a specific character.
    /// </summary>
    class MoneyAction : EventAction
    {
        public MoneyAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        [Serialize(0, IsPropertySaveable.Yes, description: "Amount of money to give or remove.")]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "If set, the money is removed from character(s) with this tag.")]
        public Identifier TargetTag { get; set; }

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

#if SERVER
            bool hasTag = !TargetTag.IsEmpty;
            List<Client> matchingClients = new List<Client>();
            if (hasTag)
            {
                IEnumerable targets = ParentEvent.GetTargets(TargetTag);

                foreach (Entity entity in targets)
                {
                    if (entity is Character && GameMain.Server?.ConnectedClients.FirstOrDefault(c => c.Character == entity) is { } matchingCharacter)
                    {
                        matchingClients.Add(matchingCharacter);
                        break;
                    }
                }
            }
#endif

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
#if SERVER
                if (!hasTag)
                {
                    campaign.Bank.Give(Amount);
                }
                else
                {
                    foreach (Client client in matchingClients)
                    {
                        campaign.GetWallet(client).Give(Amount);
                    }
                }
#else
                campaign.Wallet.Give(Amount);
#endif
                GameAnalyticsManager.AddMoneyGainedEvent(Amount, GameAnalyticsManager.MoneySource.Event, ParentEvent.Prefab.Identifier.Value);
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(SetDataAction)} -> (Amount: {Amount.ColorizeObject()})";
        }
    }
}