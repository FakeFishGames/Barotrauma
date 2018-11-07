using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        public const float OrderPriority = 50.0f;

        private List<AIObjective> objectives;

        private Character character;

        private AIObjective currentOrder;

        /// <summary>
        /// When set above zero, the character will stand still doing nothing until the timer runs out (assuming they don't a high priority order active)
        /// </summary>
        public float WaitTimer;
        
        public AIObjective CurrentOrder
        {
            get { return currentOrder; }
        }

        public AIObjective CurrentObjective
        {
            get;
            private set;
        }

        public AIObjectiveManager(Character character)
        {
            this.character = character;

            objectives = new List<AIObjective>();
        }

        public void AddObjective(AIObjective objective)
        {
            if (objectives.Find(o => o.IsDuplicate(objective)) != null) return;

            objectives.Add(objective);
        }

        public T GetObjective<T>() where T : AIObjective
        {
            foreach (AIObjective objective in objectives)
            {
                if (objective is T) return (T)objective;
            }
            return null;
        }

        private AIObjective GetCurrentObjective()
        {
            if (CurrentOrder != null &&
                (objectives.Count == 0 || currentOrder.GetPriority(this) > objectives[0].GetPriority(this)))
            {
                return CurrentOrder;
            }

            return objectives.Count == 0 ? null : objectives[0];
        }

        public float GetCurrentPriority()
        {
            var currentObjective = GetCurrentObjective();
            return currentObjective == null ? 0.0f : currentObjective.GetPriority(this);
        }

        public void UpdateObjectives()
        {
            if (!objectives.Any()) return;

            //remove completed objectives and ones that can't be completed
            objectives = objectives.FindAll(o => !o.IsCompleted() && o.CanBeCompleted);

            //sort objectives according to priority
            objectives.Sort((x, y) => y.GetPriority(this).CompareTo(x.GetPriority(this)));
            GetCurrentObjective()?.SortSubObjectives(this);
        }

        
        public void DoCurrentObjective(float deltaTime)
        {
            CurrentObjective = GetCurrentObjective();

            if (CurrentObjective == null || (CurrentObjective.GetPriority(this) < OrderPriority && WaitTimer > 0.0f))
            {
                WaitTimer -= deltaTime;
                character.AIController.SteeringManager.Reset();
                return;
            }

            CurrentObjective?.TryComplete(deltaTime);
        }

        public void SetOrder(Order order, string option, Character orderGiver)
        {
            currentOrder = null;
            if (order == null) return;

            switch (order.AITag.ToLowerInvariant())
            {
                case "follow":
                    currentOrder = new AIObjectiveGoTo(orderGiver, character, true)
                    {
                        CloseEnough = 1.5f,
                        IgnoreIfTargetDead = true
                    };
                    break;
                case "wait":
                    currentOrder = new AIObjectiveGoTo(character, character, true);
                    break;
                case "fixleaks":
                    currentOrder = new AIObjectiveFixLeaks(character);
                    break;
                case "chargebatteries":
                    currentOrder = new AIObjectiveChargeBatteries(character, option);
                    break;
                case "rescue":
                    currentOrder = new AIObjectiveRescueAll(character);
                    break;
                case "repairsystems":
                    currentOrder = new AIObjectiveRepairItems(character);
                    break;
                case "pumpwater":
                    currentOrder = new AIObjectivePumpWater(character, option);
                    break;
                case "extinguishfires":
                    currentOrder = new AIObjectiveExtinguishFires(character);
                    break;
                case "steer":
                    var steering = (order?.TargetEntity as Item)?.GetComponent<Steering>();
                    if (steering != null) steering.PosToMaintain = steering.Item.Submarine?.WorldPosition;
                    if (order.TargetItemComponent == null) return;
                    currentOrder = new AIObjectiveOperateItem(order.TargetItemComponent, character, option, false, null, order.UseController);
                    break;
                default:
                    if (order.TargetItemComponent == null) return;
                    currentOrder = new AIObjectiveOperateItem(order.TargetItemComponent, character, option, false, null, order.UseController);
                    break;
            }
        }
    }
}
