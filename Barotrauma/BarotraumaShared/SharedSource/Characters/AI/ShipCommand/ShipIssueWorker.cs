using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class ShipIssueWorker
    {
        public const float MaxImportance = 100f;
        public const float MinImportance = 0f;
        public Order SuggestedOrderPrefab { get; }

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
        public string Option { get; set; }
        public Character OrderedCharacter { get; set; }
        public Order CurrentOrder { get; private set; }
        public ItemComponent TargetItemComponent { get; protected set; }
        public Item TargetItem { get; protected set; }
        public bool Active { get; protected set; } = true; // used to turn off the instance if errors are detected

        protected virtual Character CommandingCharacter => shipCommandManager.character;
        public virtual float TimeSinceLastAttempt { get; set; }
        public virtual float RedundantIssueModifier => 0.5f;
        public virtual bool StopDuringEmergency => true; // limit certain issue assessments when invaded by the enemies
        public virtual bool AllowEasySwitching => false;

        public ShipIssueWorker(ShipCommandManager shipCommandManager, Order suggestedOrderPrefab, string option = null)
        {
            this.shipCommandManager = shipCommandManager;
            SuggestedOrderPrefab = suggestedOrderPrefab;
            Option = option;
        }

        public void SetOrder(Character orderedCharacter)
        {
            OrderedCharacter = orderedCharacter;
            if (OrderedCharacter.AIController is HumanAIController humanAI && humanAI.ObjectiveManager.CurrentOrders.None(o => o.MatchesOrder(SuggestedOrderPrefab, Option)))
            {
                if (orderedCharacter != CommandingCharacter)
                {
                    CommandingCharacter.Speak(SuggestedOrderPrefab.GetChatMessage(OrderedCharacter.Name, "", false), minDurationBetweenSimilar: 5);
                }
                CurrentOrder = new Order(SuggestedOrderPrefab, TargetItem, TargetItemComponent, CommandingCharacter);
                OrderedCharacter.SetOrder(CurrentOrder, Option, priority: CharacterInfo.HighestManualOrderPriority, CommandingCharacter, CommandingCharacter != OrderedCharacter);
                OrderedCharacter.Speak(TextManager.Get("DialogAffirmative"), delay: 1.0f, minDurationBetweenSimilar: 5);
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
            if (CurrentOrder != null && OrderedCharacter.GetCurrentOrderWithTopPriority()?.Order != CurrentOrder)
            {
#if DEBUG
                ShipCommandManager.ShipCommandLog($"Order {CurrentOrder.Name} did not match current order for character {OrderedCharacter} in {this}");
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
