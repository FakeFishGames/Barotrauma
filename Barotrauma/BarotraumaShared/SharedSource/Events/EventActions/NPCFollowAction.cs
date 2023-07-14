using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class NPCFollowAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier NPCTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool Follow { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxTargets { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AbandonOnReset { get; set; }

        private bool isFinished = false;

        public NPCFollowAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }


        private List<Character> affectedNpcs = null;
        private Entity target = null;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            target = ParentEvent.GetTargets(TargetTag).FirstOrDefault();
            if (target == null) { return; }

            int targetCount = 0;
            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character).ToList();
            foreach (var npc in affectedNpcs)
            {
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