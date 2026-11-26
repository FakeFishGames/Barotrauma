using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    public abstract class ShipIssueWorker
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

        protected virtual Character CommandingCharacter => shipCommandManager.Commander;

        public virtual float TimeSinceLastAttempt { get; set; }
        public virtual float RedundantIssueModifier => 0.5f;
        public virtual bool StopIfHostilesOnBoard => true; // limit certain issue assessments when invaded by the enemies
        public virtual bool AllowEasySwitching => false;

        public ShipIssueWorker(ShipCommandManager shipCommandManager, Order suggestedOrder)
        {
            this.shipCommandManager = shipCommandManager;
            SuggestedOrder = suggestedOrder;
        }

        public void SetOrder(Character orderedCharacter)
        {
            OrderedCharacter = orderedCharacter;
            TimeSinceLastAttempt = 0f;
            if (OrderedCharacter.AIController is not HumanAIController) { return; }
            if (OrderedCharacter.GetCurrentOrderWithTopPriority() is Order order &&
                order.MatchesOrder(SuggestedOrder.Identifier, Option) && order.TargetEntity == TargetItem)
            {
                //the character is already doing this as their top priority (e.g. ordered by someone else), no need to reissue
                CurrentOrder = order;
            }
            else
            {
                //note that the character may still have this order at a lower priority:
                //reissue it in that case, otherwise the issue would never be considered attended to
                bool orderGivenByDifferentCharacter = orderedCharacter != CommandingCharacter;
                if (orderGivenByDifferentCharacter)
                {
                    CommandingCharacter.Speak(SuggestedOrder.GetChatMessage(OrderedCharacter.Name, "", givingOrderToSelf: false),
                        //large duration between similar: these orders can be reissued frequently if the ordered character is busy doing something else
                        minDurationBetweenSimilar: 60,
                        identifier: ("GiveOrder." + SuggestedOrder.Prefab.Identifier + orderedCharacter.Name).ToIdentifier());
                }
                CurrentOrder = SuggestedOrder
                    .WithOption(Option)
                    .WithItemComponent(TargetItem, TargetItemComponent)
                    .WithOrderGiver(CommandingCharacter)
                    .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
                //cheat a bit and force the order to be set: otherwise we might have issues with orders
                //not being issued due to the character being at the other side of the outpost, too far to hear the order
                OrderedCharacter.SetOrder(CurrentOrder, isNewOrder: CommandingCharacter != OrderedCharacter, force: true);
                if (orderGivenByDifferentCharacter)
                {
                    OrderedCharacter.Speak(TextManager.Get("DialogAffirmative").Value, delay: 1.0f,
                        minDurationBetweenSimilar: 5,
                        identifier: ("ReceiveOrder." + SuggestedOrder.Prefab.Identifier).ToIdentifier());
                }
            }
            TimeSinceLastAttempt = 0f;
        }

        public void RemoveOrder()
        {
            if (CurrentOrder != null && OrderedCharacter is { IsDead: false, Removed: false, AIController: HumanAIController humanAi } &&
                // CurrentOrder is the order we gave, but the objective is attached to the character's own copy of it, find it here
                humanAi.ObjectiveManager.CurrentOrders.FirstOrDefault(o => o.MatchesOrder(CurrentOrder) && o.TargetEntity == CurrentOrder.TargetEntity)?.Objective is AIObjective orderObjective)
            {
                orderObjective.Abandon = true;
            }
            OrderedCharacter = null;
            CurrentOrder = null;
        }

        protected virtual bool IsIssueViable() 
        {
            return true;
        }

        public float CalculateImportance(bool hostilesOnBoard)
        {
            Importance = 0f; // reset anything that needs resetting

            if (!Active)
            {
                return Importance;
            }

            Active = IsIssueViable();

            if (hostilesOnBoard && StopIfHostilesOnBoard)
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
                ShipCommandManager.LogDebugMessage($"{this} is no longer the top priority of {OrderedCharacter}, considering the issue unattended.");
#endif
                return false;
            }

            if (!shipCommandManager.AbleToContinueOrder(OrderedCharacter))
            {
#if DEBUG
                ShipCommandManager.LogDebugMessage(OrderedCharacter + " was unable to perform assigned order in " + this);
#endif
                return false;
            }
            return true;
        }
        public abstract void CalculateImportanceSpecific();
    }
}
