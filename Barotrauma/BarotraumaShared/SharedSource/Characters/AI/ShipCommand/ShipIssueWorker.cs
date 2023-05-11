using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class ShipIssueWorker
    {
        public const float MaxImportance = 100f;
        public const float MinImportance = 0f;
        public Order SuggestedOrder { get; }

        private float importance;
        public float Importance 
        {
            get
            {
                return importance;
            }
            set
            {
                importance = MathHelper.Clamp(value, MinImportance, MaxImportance);
            } 
        }
        public float CurrentRedundancy { get; set; }

        public readonly ShipCommandManager shipCommandManager;
        public Identifier Option => SuggestedOrder.Option;
        public Character OrderedCharacter { get; set; }
        public Order CurrentOrder { get; private set; }
        public ItemComponent TargetItemComponent => SuggestedOrder.TargetItemComponent;
        public Item TargetItem => SuggestedOrder.TargetEntity as Item;
        public bool Active { get; protected set; } = true; // used to turn off the instance if errors are detected

        protected virtual Character CommandingCharacter => shipCommandManager.character;
        public virtual float TimeSinceLastAttempt { get; set; }
        public virtual float RedundantIssueModifier => 0.5f;
        public virtual bool StopDuringEmergency => true; // limit certain issue assessments when invaded by the enemies
        public virtual bool AllowEasySwitching => false;

        public ShipIssueWorker(ShipCommandManager shipCommandManager, Order suggestedOrder)
        {
            this.shipCommandManager = shipCommandManager;
            SuggestedOrder = suggestedOrder;
        }

        public void SetOrder(Character orderedCharacter)
        {
            OrderedCharacter = orderedCharacter;
            if (OrderedCharacter.AIController is HumanAIController humanAI && 
                humanAI.ObjectiveManager.CurrentOrders.None(o => o.MatchesOrder(SuggestedOrder.Identifier, Option) && o.TargetEntity == TargetItem))
            {
                if (orderedCharacter != CommandingCharacter)
                {
                    CommandingCharacter.Speak(SuggestedOrder.GetChatMessage(OrderedCharacter.Name, "", false), minDurationBetweenSimilar: 5);
                }
                CurrentOrder = SuggestedOrder
                    .WithOption(Option)
                    .WithItemComponent(TargetItem, TargetItemComponent)
                    .WithOrderGiver(CommandingCharacter)
                    .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                OrderedCharacter.SetOrder(CurrentOrder, CommandingCharacter != OrderedCharacter);
                OrderedCharacter.Speak(TextManager.Get("DialogAffirmative").Value, delay: 1.0f, minDurationBetweenSimilar: 5);
            }
            TimeSinceLastAttempt = 0f;
        }

        public void RemoveOrder()
        {
            OrderedCharacter = null;
            CurrentOrder = null;
        }

        protected virtual bool IsIssueViable() 
        {
            return true;
        }

        public float CalculateImportance(bool isEmergency)
        {
            Importance = 0f; // reset anything that needs resetting

            if (!Active)
            {
                return Importance;
            }

            Active = IsIssueViable();

            if (isEmergency && StopDuringEmergency)
            {
                return Importance;
            }

            CalculateImportanceSpecific();

            // if there are other orders of the same type already being attended to, such as fixing leaks
            // reduce the relative importance of this issue
            CurrentRedundancy = 1f;
            foreach (ShipIssueWorker shipIssueWorker in shipCommandManager.ShipIssueWorkers)
            {
                if (shipIssueWorker.GetType() == GetType() && shipIssueWorker != this && shipIssueWorker.OrderAttendedTo()) 
                {
                    CurrentRedundancy *= RedundantIssueModifier;
                }
            }
            Importance *= CurrentRedundancy;

            return Importance;
        }

        public bool OrderAttendedTo(float timeSinceLastCheck = 0f)
        {
            if (!HumanAIController.IsActive(OrderedCharacter))
            {
                return false;
            }

            // accept only the highest priority order
            if (CurrentOrder == null || OrderedCharacter.GetCurrentOrderWithTopPriority() != CurrentOrder)
            {
#if DEBUG
                ShipCommandManager.ShipCommandLog($"{this} is no longer the top priority of {OrderedCharacter}, considering the issue unattended.");
#endif
                return false;
            }

            if (!shipCommandManager.AbleToTakeOrder(OrderedCharacter))
            {
#if DEBUG
                ShipCommandManager.ShipCommandLog(OrderedCharacter + " was unable to perform assigned order in " + this);
#endif
                return false;
            }
            return true;
        }
        public abstract void CalculateImportanceSpecific();
    }
}
