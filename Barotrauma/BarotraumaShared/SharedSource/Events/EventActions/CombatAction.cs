using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CombatAction : EventAction
    {
        [Serialize(AIObjectiveCombat.CombatMode.Offensive, IsPropertySaveable.Yes)]
        public AIObjectiveCombat.CombatMode CombatMode { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Did this NPC start the fight (as an aggressor)?")]
        public bool IsInstigator { get; set; }

        [Serialize(AIObjectiveCombat.CombatMode.None, IsPropertySaveable.Yes)]
        public AIObjectiveCombat.CombatMode GuardReaction { get; set; }

        [Serialize(AIObjectiveCombat.CombatMode.None, IsPropertySaveable.Yes)]
        public AIObjectiveCombat.CombatMode WitnessReaction { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier NPCTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier EnemyTag { get; set; }

        [Serialize(120.0f, IsPropertySaveable.Yes)]
        public float CoolDown { get; set; }

        private bool isFinished = false;


        private IEnumerable<Character> affectedNpcs = null;

        public CombatAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(e => e is Character).Select(e => e as Character);

            foreach (var npc in affectedNpcs)
            {
                if (!(npc.AIController is HumanAIController humanAiController)) { continue; }

                Character enemy = null;
                float closestDist = float.MaxValue;
                foreach (Entity target in ParentEvent.GetTargets(EnemyTag))
                {
                    if (!(target is Character character)) { continue; }
                    float dist = Vector2.DistanceSquared(npc.WorldPosition, target.WorldPosition);
                    if (dist < closestDist)
                    {
                        enemy = character;
                        closestDist = dist;
                    }
                }
                if (enemy == null) { continue; }

                npc.CombatAction = this;

                var objectiveManager = humanAiController.ObjectiveManager;
                foreach (var goToObjective in objectiveManager.GetActiveObjectives<AIObjectiveGoTo>())
                {
                    goToObjective.Abandon = true;                    
                }
                objectiveManager.AddObjective(new AIObjectiveCombat(npc, enemy, CombatMode, objectiveManager, coolDown: CoolDown));
            }
            isFinished = true;
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            if (affectedNpcs != null)
            {
                foreach (var npc in affectedNpcs)
                {
                    if (npc.Removed || !(npc.AIController is HumanAIController humanAiController)) { continue; }
                    foreach (var combatObjective in humanAiController.ObjectiveManager.GetActiveObjectives<AIObjectiveCombat>())
                    {
                        combatObjective.Abandon = true;
                    }
                }
                affectedNpcs = null;
            }
            isFinished = false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(CombatAction)} -> (Cooldown: {CoolDown.ColorizeObject()}, CombatMode: {CombatMode.ColorizeObject()}, NPCTag: {NPCTag.ColorizeObject()}, EnemyTag: {EnemyTag.ColorizeObject()})";
        }
    }
}