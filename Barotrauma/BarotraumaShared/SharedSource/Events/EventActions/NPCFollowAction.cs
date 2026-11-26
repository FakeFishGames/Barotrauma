using Barotrauma.Extensions;
using System;
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

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the NPC be forced to walk towards the target?")]
        public bool ForceWalk { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Maximum number of NPCs to target (e.g. you could choose to only make a specific number of security officers follow the player.)")]
        public int MaxTargets { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "The event actions reset when a GoTo action makes the event jump to a different point. Should the NPC stop following the target when the event resets?")]
        public bool AbandonOnReset { get; set; }

        [Serialize(AIObjectiveManager.EmergencyObjectivePriority, IsPropertySaveable.Yes, description: "AI priority for the action. Uses 90 by default, which is the priority of the emergency objectives, " +
                                                                                                 "meaning the character can still react to emergencies, such as find safety and combat, while following." +
                                                                                                 "Setting the priority to 100 (the absolute maximum for any objectives) would mean nothing can be prioritized over it, including the emergency objectives." +
                                                                                                 "Setting the priority to 70 would function like a regular order, but with the highest priority." +
                                                                                                 "A priority of 60 would make the objective work like a lowest priority order.")]
        public float Priority
        {
            get => _priority;
            set => _priority = Math.Clamp(value, AIObjectiveManager.LowestOrderPriority, AIObjectiveManager.MaxObjectivePriority);
        }
        
        private float _priority;

        [Serialize(false, IsPropertySaveable.Yes, description: "If there are multiple entities with the target tag, should each NPC pick a random one of them as its own target (instead of all of them using the first one)? The targets are spread out as evenly as possible, so NPCs only share a target if there are more NPCs than targets.")]
        public bool RandomizeTarget { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional tag of an entity the NPC should face after reaching the target (instead of facing the target itself).")]
        public Identifier FaceTargetTag { get; set; }

        private bool isFinished = false;

        public NPCFollowAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }


        private IEnumerable<Character> affectedNpcs;

        private List<Entity> targets = new List<Entity>();

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            var possibleTargets = ParentEvent.GetTargets(TargetTag).ToList();
            if (possibleTargets.None()) { return; }
            targets = RandomizeTarget ? possibleTargets : [possibleTargets.First()];

            Entity faceTarget = FaceTargetTag.IsEmpty ? null : ParentEvent.GetTargets(FaceTargetTag).FirstOrDefault();

            List<Entity> unusedTargets = new List<Entity>();

            int targetCount = 0;
            affectedNpcs = ParentEvent.GetTargets(NPCTag).OfType<Character>();
            foreach (var npc in affectedNpcs)
            {
                if (npc.Removed) { continue; }
                if (npc.AIController is not HumanAIController humanAiController) { continue; }

                if (Follow)
                {
                    Entity npcTarget = targets[0];
                    if (RandomizeTarget)
                    {
                        if (unusedTargets.None())
                        {
                            unusedTargets.AddRange(targets.Where(t => !t.Removed).OrderBy(_ => Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced)));
                        }
                        if (unusedTargets.None()) { continue; }
                        npcTarget = unusedTargets[0];
                        unusedTargets.RemoveAt(0);
                    }

                    var newObjective = new AIObjectiveGoTo(npcTarget, npc, humanAiController.ObjectiveManager, repeat: true)
                    {
                        OverridePriority = Priority,
                        IsFollowOrder = true,
                        ForceWalkPermanently = ForceWalk,
                        FaceOverrideTarget = faceTarget
                    };
                    humanAiController.ObjectiveManager.AddObjective(newObjective);
                    humanAiController.ObjectiveManager.WaitTimer = 0.0f;
                }
                else
                {
                    foreach (var objective in humanAiController.ObjectiveManager.Objectives)
                    {
                        if (objective is AIObjectiveGoTo goToObjective && targets.Contains(goToObjective.Target))
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

        public override void Reset(bool shouldEndConversations = true)
        {
            if (affectedNpcs != null && targets.Any() && AbandonOnReset)
            {
                foreach (var npc in affectedNpcs)
                {
                    if (npc.Removed || npc.AIController is not HumanAIController humanAiController) { continue; }
                    foreach (var goToObjective in humanAiController.ObjectiveManager.GetActiveObjectives<AIObjectiveGoTo>())
                    {
                        if (targets.Contains(goToObjective.Target))
                        {
                            goToObjective.Abandon = true;
                        }
                    }
                }
                targets = new List<Entity>();
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
