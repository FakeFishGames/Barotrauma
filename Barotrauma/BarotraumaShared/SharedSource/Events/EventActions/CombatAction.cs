using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Makes an NPC switch to a combat state (with options for different kinds of behaviors, such as offensive, arresting or retreating).
    /// </summary>
    class CombatAction : EventAction
    {
        [Serialize(AIObjectiveCombat.CombatMode.Offensive, IsPropertySaveable.Yes, description: $"What kind of combat mode should the NPC switch to (Defensive, Offensive, Arrest, Retreat, None)?")]
        public AIObjectiveCombat.CombatMode CombatMode { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Did this NPC start the fight (as an aggressor)? Attacking instigators doesn't reduce reputation or trigger outpost security.")]
        public bool IsInstigator { get; set; }

        [Serialize(AIObjectiveCombat.CombatMode.None, IsPropertySaveable.Yes, description: "How do guards react to this character attacking others?")]
        public AIObjectiveCombat.CombatMode GuardReaction { get; set; }

        [Serialize(AIObjectiveCombat.CombatMode.None, IsPropertySaveable.Yes, description: "How do other NPCs react to this character attacking others?")]
        public AIObjectiveCombat.CombatMode WitnessReaction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The tag of the NPC to switch to combat mode.")]
        public Identifier NPCTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character the NPC should attack.")]
        public Identifier EnemyTag { get; set; }

        [Serialize(120.0f, IsPropertySaveable.Yes, description: "How long it takes for the NPC to \"cool down\" (stop attacking).")]
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
                if (npc.Removed) { continue; }
                if (npc.AIController is not HumanAIController humanAiController) { continue; }

                Character enemy = null;
                float closestDist = float.MaxValue;
                foreach (Entity target in ParentEvent.GetTargets(EnemyTag))
                {
                    if (target is not Character character) { continue; }
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
                    if (npc.Removed || npc.AIController is not HumanAIController humanAiController) { continue; }
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