using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Barotrauma
{
    /// <summary>
    /// Makes an NPC select an item, and operate it if it's something AI characters can operate.
    /// </summary>
    class NPCOperateItemAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the NPC(s) that should operate the item.")]
        public Identifier NPCTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the item to operate. If it's not something AI characters can or know how to operate, such as a cabinet or an engine, the NPC will just select it.")]
        public Identifier TargetTag { get; set; }

        [Serialize("Controller", IsPropertySaveable.Yes, description: "Name of the component to operate. For example, the Controller component of a periscope or the Reactor component of a nuclear reactor.")]
        public Identifier ItemComponentName { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the option, if there are several ways the item can be operated. For example, \"powerup\" or \"shutdown\" when operating a reactor.")]
        public Identifier OrderOption { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the character equip the item before attempting to operate it (only valid if the item is equippable).")]
        public bool RequireEquip { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the character start or stop operating the item.")]
        public bool Operate { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Maximum number of NPCs the action can target. For example, you could only make a specific number of security officers man a periscope.")]
        public int MaxTargets { get; set; }


        [Serialize(AIObjectiveManager.MaxObjectivePriority, IsPropertySaveable.Yes, description: "AI priority for the action. Uses 100 by default, which is the absolute maximum for any objectives, " +
                                                                                                 "meaning nothing can be prioritized over it, including the emergency objectives, such as find safety and combat." +
                                                                                                 "Setting the priority to 70 would function like a regular order, but with the highest priority." +
                                                                                                 "A priority of 60 would make the objective work like a lowest priority order." +
                                                                                                 "So, if we'll want the character to operate the item, but still be able to find safety, defend themselves when attacked, or flee from dangers," +
                                                                                                 "it's better to use e.g. 70 instead of 100.")]
        public float Priority
        {
            get => _priority;
            set => _priority = Math.Clamp(value, AIObjectiveManager.LowestOrderPriority, AIObjectiveManager.MaxObjectivePriority);
        }
        private float _priority;

        [Serialize(true, IsPropertySaveable.Yes, description: "The event actions reset when a GoTo action makes the event jump to a different point. Should the NPC stop operating the item when the event resets?")]
        public bool AbandonOnReset { get; set; }

        private bool isFinished = false;
        
        public NPCOperateItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private List<Character> affectedNpcs = null;
        private Item target = null;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            var potentialTargets = ParentEvent.GetTargets(TargetTag).OfType<Item>();
            var nonSelectedItems = potentialTargets.Where(it => it.GetComponent<Controller>()?.User == null);

            target =
                nonSelectedItems.Any() ? 
                nonSelectedItems.GetRandomUnsynced() :
                potentialTargets.GetRandomUnsynced();
            if (target == null) { return; }

            int targetCount = 0;
            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character).ToList();
            foreach (var npc in affectedNpcs)
            {
                if (npc.Removed) { continue; }
                if (npc.AIController is not HumanAIController humanAiController) { continue; }

                if (Operate)
                {
                    var itemComponent = target.Components.FirstOrDefault(ic => ItemComponentName == ic.Name);
                    if (itemComponent == null)
                    {
                        DebugConsole.AddWarning($"Error in NPCOperateItemAction: could not find the component \"{ItemComponentName}\" in item \"{target.Name}\".");
                    }
                    else
                    {
                        var newObjective = new AIObjectiveOperateItem(itemComponent, npc, humanAiController.ObjectiveManager, OrderOption, RequireEquip)
                        {
                            OverridePriority = Priority
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
                        if (objective is AIObjectiveOperateItem operateItemObjective && operateItemObjective.Component.Item == target)
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
                        if (operateItemObjective.Component.Item == target)
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