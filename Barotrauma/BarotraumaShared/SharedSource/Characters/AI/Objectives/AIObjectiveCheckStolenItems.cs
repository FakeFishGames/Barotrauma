#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveCheckStolenItems : AIObjective
    {
        public override Identifier Identifier { get; set; } = "check stolen items".ToIdentifier();
        protected override bool AllowOutsideSubmarine => false;
        protected override bool AllowInAnySub => false;

        public float FindStolenItemsProbability = 1.0f;

        enum State
        {
            GotoTarget,
            Inspect, 
            Warn,
            Done
        }

        private const float InspectTime = 5.0f;
        private const float NormalWarnDelay = 5.0f;
        private const float CriminalWarnDelay = 3.0f;
        private float inspectTimer;
        private float warnTimer;
        private float currentWarnDelay;

        private State currentState;

        public readonly Character Target;

        private AIObjectiveGoTo? goToObjective;

        private readonly List<Item> stolenItems = new List<Item>();

        public AIObjectiveCheckStolenItems(Character character, Character target, AIObjectiveManager objectiveManager, float priorityModifier = 1) : 
            base(character, objectiveManager, priorityModifier)
        {
            Target = target;
            InitTimers();
        }
        
        protected override bool CheckObjectiveSpecific() => false;

        protected override float GetPriority()
        {
            if (character.IsClimbing)
            {
                // Target is climbing -> stop following the objective (soft abandon, without ignoring the target).
                Priority = 0;
            }
            else if (!Abandon && !IsCompleted && objectiveManager.IsOrder(this))
            {
                Priority = objectiveManager.GetOrderPriority(this);
            }
            else
            {
                Priority = AIObjectiveManager.LowestOrderPriority - 1;
            }
            return Priority;
        }

        public void ForceComplete()
        {
            IsCompleted = true;
        }

        protected override void Act(float deltaTime)
        {
            switch (currentState)
            {
                case State.Done:
                    IsCompleted = true;
                    break;
                case State.GotoTarget:
                    TryAddSubObjective(ref goToObjective,
                    constructor: () => new AIObjectiveGoTo(Target, character, objectiveManager, repeat: false)
                    {
                        SpeakIfFails = false
                    },
                    onCompleted: () =>
                    {
                        RemoveSubObjective(ref goToObjective);
                        if (character.IsClimbing)
                        {
                            // Shouldn't start inspecting characters when they climb, nor get here, because the priority should be 0,
                            // but if this still happens, we'll have to abandon the objective
                            // because it's not currently possible to hold to characters and ladders at the same time.
                            Abandon = true;
                        }
                        else
                        {
                            currentState = State.Inspect;
                            stolenItems.Clear();
                            Target.Inventory.FindAllItems(it => it.Illegitimate, recursive: true, stolenItems);
                            character.Speak(TextManager.Get(Target.IsCriminal ? "dialogcheckstolenitems.criminal" : "dialogcheckstolenitems").Value);
                        }
                    },
                    onAbandon: () =>
                    {
                        Abandon = true;
                    });
                    break;
                case State.Inspect:
                    Inspect(deltaTime);
                    break;
                case State.Warn:
                    Warn(deltaTime);
                    break;
            }
        }

        private void Inspect(float deltaTime)
        {
            if (inspectTimer > 0.0f)
            {
                character.SelectCharacter(Target);
                inspectTimer -= deltaTime;
                if (inspectTimer < InspectTime - 1)
                {
                    if (Target.AnimController.IsMovingFast)
                    {
                        ArrestFleeing();
                    }
                    else if (Math.Abs(Target.AnimController.TargetMovement.X) > 1.0f)
                    {
                        // If the target moves, reset the inspect timer and tell to hold still
                        character.Speak(TextManager.Get("dialogcheckstolenitems.holdstill").Value, identifier: "holdstill".ToIdentifier(), minDurationBetweenSimilar: 3f);
                        inspectTimer = InspectTime;
                    }   
                }
                return;
            }

            if (stolenItems.Any() &&
                Rand.Range(0.0f, 1.0f, Rand.RandSync.Unsynced) < FindStolenItemsProbability)
            {
                character.Speak(TextManager.Get("dialogcheckstolenitems.warn").Value);
                currentState = State.Warn;
            }
            else
            {
                character.Speak(TextManager.Get(Target.IsCriminal ? "dialogcheckstolenitems.nostolenitems.criminal" : "dialogcheckstolenitems.nostolenitems").Value);
                currentState = State.Done;
                IsCompleted = true;
            }
            character.DeselectCharacter();
        }

        private void Warn(float deltaTime)
        {
            if (warnTimer > 0.0f)
            {
                warnTimer -= deltaTime;
                if (warnTimer < currentWarnDelay - 1)
                {
                    if (Target.AnimController.IsMovingFast)
                    {
                        ArrestFleeing();
                    }
                }
                return;
            }
            var stolenItemsOnCharacter = stolenItems.Where(it => it.GetRootInventoryOwner() == Target);
            if (stolenItemsOnCharacter.Any())
            {
                character.Speak(TextManager.Get(character.IsCriminal ? "dialogcheckstolenitems.arrest.criminal" : "dialogcheckstolenitems.arrest").Value);
                Arrest(abortWhenItemsDropped: true, allowHoldFire: true);
                foreach (var stolenItem in stolenItemsOnCharacter)
                {
                    HumanAIController.ApplyStealingReputationLoss(stolenItem);
                }
            }
            else
            {
                character.Speak(TextManager.Get("dialogcheckstolenitems.comply").Value);
            }
            foreach (var item in stolenItems)
            {
                HumanAIController.ObjectiveManager.AddObjective(new AIObjectiveGetItem(character, item, objectiveManager, equip: false)
                {
                    BasePriority = 10
                });
            }                
            currentState = State.Done;
            IsCompleted = true;
        }
        
        private void ArrestFleeing()
        {
            character.Speak(TextManager.Get("dialogcheckstolenitems.arrest").Value);
            currentState = State.Done;
            IsCompleted = true;
            Arrest(abortWhenItemsDropped: false, allowHoldFire: false);
        }
        
        private void Arrest(bool abortWhenItemsDropped, bool allowHoldFire)
        {
            bool isCriminal = Target.IsCriminal;
            Func<AIObjective, bool>? abortCondition = null;
            if (abortWhenItemsDropped && !isCriminal)
            {
                abortCondition = obj => Target.Inventory.FindItem(it => it.Illegitimate, recursive: true) == null;
            }
            HumanAIController.AddCombatObjective(AIObjectiveCombat.CombatMode.Arrest, Target, allowHoldFire: allowHoldFire && !isCriminal, speakWarnings: !isCriminal, abortCondition: abortCondition);
        }

        public override void OnDeselected()
        {
            base.OnDeselected();
            character.DeselectCharacter();
        }
        
        public override void Reset()
        {
            base.Reset();
            currentState = State.GotoTarget;
            InitTimers();
        }
        
        private void InitTimers()
        {
            inspectTimer = InspectTime;
            currentWarnDelay = Target.IsCriminal ? CriminalWarnDelay : NormalWarnDelay;
            warnTimer = currentWarnDelay;
        }
    }
}
