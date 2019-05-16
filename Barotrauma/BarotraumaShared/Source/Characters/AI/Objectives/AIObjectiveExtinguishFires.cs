using System;
using System.Linq;
using System.Collections.Generic;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    // TODO: use objective loop and sort the targets by severity
    class AIObjectiveExtinguishFires : AIObjective
    {
        public override string DebugTag => "extinguish fires";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        private Dictionary<Hull, AIObjectiveExtinguishFire> extinguishObjectives = new Dictionary<Hull, AIObjectiveExtinguishFire>();

        public AIObjectiveExtinguishFires(Character character, float priorityModifier = 1) : base(character, "", priorityModifier) { }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.Submarine == null) { return 0; }
            float referenceSize = 100;
            // If the fire sources of inside a hull are half the reference size in total(e.g.), the severity for that hull should be 50.
            // The severity values for all hulls are added together. So three hulls with 50 severity, would be 150 in total.
            // Thus the max priority of 100 is reached when one hull has firesources of size 100 in width, if the priority modifier is 1.
            float severity = character.Submarine.GetHulls(true).Sum(h => h.FireSources.Sum(fs => fs.Size.X) / referenceSize * 100);
            if (severity < 1) { return 0; }
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            float basePriority = MathHelper.Clamp(Priority, 0, 10);
            return MathHelper.Clamp(basePriority + severity * PriorityModifier, 0, 99);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsLoop { get => true; set => throw new System.Exception("Trying to set the value for IsLoop from: " + System.Environment.StackTrace); }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveExtinguishFires;
        }

        protected override void Act(float deltaTime)
        {
            SyncRemovedObjectives(extinguishObjectives, Hull.hullList);
            if (character.Submarine == null) { return; }
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.FireSources.None()) { continue; }
                if (hull.Submarine == null) { continue; }
                if (hull.Submarine.TeamID != character.TeamID) { continue; }
                // If the character is inside, only take connected hulls into account.
                if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(hull, true)) { continue; }
                if (!extinguishObjectives.TryGetValue(hull, out AIObjectiveExtinguishFire objective))
                {
                    objective = new AIObjectiveExtinguishFire(character, hull);
                    extinguishObjectives.Add(hull, objective);
                    AddSubObjective(objective);
                }
            }
            if (extinguishObjectives.None())
            {
                character?.Speak(TextManager.Get("DialogNoFire"), null, 3.0f, "nofire", 30.0f);
            }
        }
    }
}
