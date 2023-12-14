#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveCheckStolenItems : AIObjective
    {
        public override Identifier Identifier { get; set; } = "check stolen items".ToIdentifier();
        public override bool AllowOutsideSubmarine => false;
        public override bool AllowInAnySub => false;

        public float FindStolenItemsProbability = 1.0f;

        enum State
        {
            GotoTarget,
            Inspect, 
            Warn,
            Done
        }

        private float inspectDelay;
        private float warnDelay;

        private State currentState;

        public readonly Character TargetCharacter;

        private AIObjectiveGoTo? goToObjective;

        private readonly List<Item> stolenItems = new List<Item>();

        public AIObjectiveCheckStolenItems(Character character, Character targetCharacter, AIObjectiveManager objectiveManager, float priorityModifier = 1) : 
            base(character, objectiveManager, priorityModifier)
        {
            TargetCharacter = targetCharacter;
            inspectDelay = 5.0f;
            warnDelay = 5.0f;
        }

        public override bool IsLoop 
        { 
            get => false; 
            set => throw new Exception("Trying to set the value for IsLoop from: " + Environment.StackTrace.CleanupStackTrace()); 
        }
        
        protected override bool CheckObjectiveSpecific() => false;

        protected override float GetPriority()
        {
            if (!Abandon && !IsCompleted && objectiveManager.IsOrder(this))
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
                case State.GotoTarget:
                    TryAddSubObjective(ref goToObjective,
                    constructor: () =>
                    {
                        return new AIObjectiveGoTo(TargetCharacter, character, objectiveManager, repeat: false)
                        {
                            SpeakIfFails = false
                        };
                    },
                    onCompleted: () =>
                    {
                        RemoveSubObjective(ref goToObjective);
                        currentState = State.Inspect;
                        stolenItems.Clear();
                        TargetCharacter.Inventory.FindAllItems(it => it.SpawnedInCurrentOutpost && !it.AllowStealing, recursive: true, stolenItems);
                        character.Speak(TextManager.Get("dialogcheckstolenitems").Value);
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
            if (inspectDelay > 0.0f)
            {
                character.SelectCharacter(TargetCharacter);
                inspectDelay -= deltaTime;
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
                character.Speak(TextManager.Get("dialogcheckstolenitems.nostolenitems").Value);
                currentState = State.Done;
                IsCompleted = true;
            }
            character.DeselectCharacter();
        }

        private void Warn(float deltaTime)
        {
            if (warnDelay > 0.0f)
            {
                warnDelay -= deltaTime;
                return;
            }
            var stolenItemsOnCharacter = stolenItems.Where(it => it.GetRootInventoryOwner() == TargetCharacter);
            if (stolenItemsOnCharacter.Any())
            {
                character.Speak(TextManager.Get("dialogcheckstolenitems.arrest").Value);
                HumanAIController.AddCombatObjective(AIObjectiveCombat.CombatMode.Arrest, TargetCharacter);
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
    }
}
