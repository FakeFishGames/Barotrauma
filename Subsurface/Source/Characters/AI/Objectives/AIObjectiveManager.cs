using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        const float OrderPriority = 50.0f;

        private List<AIObjective> objectives;

        private Character character;

        private AIObjective currentObjective;
        
        public AIObjective CurrentObjective
        {
            get
            {
                if (currentObjective != null) return currentObjective;
                return objectives.Any() ? objectives[0] : null;
            }
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

        public float GetCurrentPriority(Character character)
        {
            if (currentObjective != null) return OrderPriority;
            return (CurrentObjective == null) ? 0.0f : CurrentObjective.GetPriority(character);
        }

        public void UpdateObjectives()
        {
            if (!objectives.Any()) return;

            //remove completed objectives and ones that can't be completed
            objectives = objectives.FindAll(o => !o.IsCompleted() && o.CanBeCompleted);

            //sort objectives according to priority
            objectives.Sort((x, y) => y.GetPriority(character).CompareTo(x.GetPriority(character)));
        }

        public void DoCurrentObjective(float deltaTime)
        {
            if (currentObjective != null && (!objectives.Any() || objectives[0].GetPriority(character) < OrderPriority))
            {
                currentObjective.TryComplete(deltaTime);
                return;
            }

            if (!objectives.Any()) return;
            objectives[0].TryComplete(deltaTime);
        }

        public void SetOrder(Order order, string option)
        {
            if (order == null) return;

            currentObjective = null;

            switch (order.Name.ToLower())
            {
                case "follow":
                    currentObjective = new AIObjectiveGoTo(Character.Controlled, character, true);
                    break;
                case "wait":
                    currentObjective = new AIObjectiveGoTo(character, character, true);
                    break;
                case "fixleaks":
                case "fix leaks":
                    currentObjective = new AIObjectiveFixLeaks(character);
                    break;
                default:
                    if (order.TargetItem == null) return;

                    currentObjective = new AIObjectiveOperateItem(order.TargetItem, character, option, null, order.UseController);

                    break;
            }
        }
    }
}
