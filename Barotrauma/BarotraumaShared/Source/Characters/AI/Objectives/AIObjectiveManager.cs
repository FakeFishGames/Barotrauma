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

        public float GetCurrentPriority(Character character)
        {
            if (CurrentOrder != null &&
                (objectives.Count == 0 || currentOrder.GetPriority(this) > objectives[0].GetPriority(this)))
            {
                return CurrentOrder.GetPriority(this);
            }

            return objectives.Count == 0 ? 0.0f : objectives[0].GetPriority(this);
        }

        public void UpdateObjectives()
        {
            if (!objectives.Any()) return;

            //remove completed objectives and ones that can't be completed
            objectives = objectives.FindAll(o => !o.IsCompleted() && o.CanBeCompleted);

            //sort objectives according to priority
            objectives.Sort((x, y) => y.GetPriority(this).CompareTo(x.GetPriority(this)));
        }

        public void DoCurrentObjective(float deltaTime)
        {
            if (currentOrder != null && (!objectives.Any() || objectives[0].GetPriority(this) < currentOrder.GetPriority(this)))
            {
                CurrentObjective = currentOrder;
                currentOrder.TryComplete(deltaTime);
                return;
            }

            if (!objectives.Any()) return;
            objectives[0].TryComplete(deltaTime);

            CurrentObjective = objectives[0];
        }

        public void SetOrder(Order order, string option)
        {
            if (order == null) return;

            currentOrder = null;

            switch (order.Name.ToLowerInvariant())
            {
                case "follow":
                    currentOrder = new AIObjectiveGoTo(Character.Controlled, character, true);
                    break;
                case "wait":
                    currentOrder = new AIObjectiveGoTo(character, character, true);
                    break;
                case "fixleaks":
                case "fix leaks":
                    currentOrder = new AIObjectiveFixLeaks(character);
                    break;
                default:
                    if (order.TargetItem == null) return;

                    currentOrder = new AIObjectiveOperateItem(order.TargetItem, character, option, false, null, order.UseController);

                    break;
            }
        }
    }
}
