using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Makes an NPC follow or stop following a specific target.
    /// </summary>
    class NPCFollowAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the NPC(s) that should follow the target.")]
        public Identifier NPCTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the target. Can be any type of entity: if it's a static one like a device or a hull, the NPC will just stay at the position of that target.")]
        public Identifier TargetTag { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the NPC start or stop following the target?")]
        public bool Follow { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Maximum number of NPCs to target (e.g. you could choose to only make a specific number of security officers follow the player.)")]
        public int MaxTargets { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "The event actions reset when a GoTo action makes the event jump to a different point. Should the NPC stop following the target when the event resets?")]
        public bool AbandonOnReset { get; set; }

        private bool isFinished = false;

        public NPCFollowAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }


        private IEnumerable<Character> affectedNpcs;
        private Entity target;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            target = ParentEvent.GetTargets(TargetTag).FirstOrDefault();
            if (target == null) { return; }

            int targetCount = 0;
            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character);
            foreach (var npc in affectedNpcs)
            {
                if (npc.Removed) { continue; }
                if (npc.AIController is not HumanAIController humanAiController) { continue; }

                if (Follow)
                {
                    var newObjective = new AIObjectiveGoTo(target, npc, humanAiController.ObjectiveManager, repeat: true)
                    {
                        OverridePriority = 100.0f,
                        IsFollowOrder = true
                    };
                    humanAiController.ObjectiveManager.AddObjective(newObjective);
                    humanAiController.ObjectiveManager.WaitTimer = 0.0f;
                }
                else
                {
                    foreach (var objective in humanAiController.ObjectiveManager.Objectives)
                    {
                        if (objective is AIObjectiveGoTo goToObjective && goToObjective.Target == target)
                        {
                            goToObjective.Abandon = true;
                        }
                    }
                }
                targetCount++;
                if (MaxTargets > -1 && targetCount >= MaxTargets)
                {
                    break;
                }
            }
            isFinished = true;
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            if (affectedNpcs != null && target != null && AbandonOnReset)
            {
                foreach (var npc in affectedNpcs)
                {
                    if (npc.Removed || npc.AIController is not HumanAIController humanAiController) { continue; }
                    foreach (var goToObjective in humanAiController.ObjectiveManager.GetActiveObjectives<AIObjectiveGoTo>())
                    {
                        if (goToObjective.Target == target)
                        {
                            goToObjective.Abandon = true;
                        }
                    }
                }
                target = null;
                affectedNpcs = null;
            }
            isFinished = false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(NPCFollowAction)} -> (NPCTag: {NPCTag.ColorizeObject()}, TargetTag: {TargetTag.ColorizeObject()}, Follow: {Follow.ColorizeObject()})";
        }
    }
}