using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class NPCOperateItemAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier NPCTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemComponentName { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier OrderOption { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool RequireEquip { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool Operate { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxTargets { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AbandonOnReset { get; set; }

        private bool isFinished = false;

        public NPCOperateItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }


        private List<Character> affectedNpcs = null;
        private Item target = null;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            target = ParentEvent.GetTargets(TargetTag).FirstOrDefault() as Item;
            if (target == null) { return; }

            int targetCount = 0;
            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character).ToList();
            foreach (var npc in affectedNpcs)
            {
                if (npc.AIController is not HumanAIController humanAiController) { continue; }

                if (Operate)
                {
                    ItemComponentName = "Controller".ToIdentifier();
                    var itemComponent = target.Components.FirstOrDefault(ic => ItemComponentName == ic.Name);
                    if (itemComponent == null)
                    {
                        DebugConsole.AddWarning($"Error in NPCOperateItemAction: could not find the component \"{ItemComponentName}\" in item \"{target.Name}\".");
                    }
                    else
                    {
                        var newObjective = new AIObjectiveOperateItem(itemComponent, npc, humanAiController.ObjectiveManager, OrderOption, RequireEquip)
                        {
                            OverridePriority = 100.0f
                        };
                        humanAiController.ObjectiveManager.AddObjective(newObjective);
                        humanAiController.ObjectiveManager.WaitTimer = 0.0f;
                        humanAiController.ObjectiveManager.Objectives.RemoveAll(o => o is AIObjectiveGoTo gotoOjective);
                    }
                }
                else
                {
                    foreach (var objective in humanAiController.ObjectiveManager.Objectives)
                    {
                        if (objective is AIObjectiveOperateItem operateItemObjective && operateItemObjective.OperateTarget == target)
                        {
                            objective.Abandon = true;
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
                    foreach (var operateItemObjective in humanAiController.ObjectiveManager.GetActiveObjectives<AIObjectiveOperateItem>())
                    {
                        if (operateItemObjective.OperateTarget == target)
                        {
                            operateItemObjective.Abandon = true;
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
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(AIObjectiveOperateItem)} -> (NPCTag: {NPCTag.ColorizeObject()}, TargetTag: {TargetTag.ColorizeObject()}, Operate: {Operate.ColorizeObject()})";
        }
    }
}