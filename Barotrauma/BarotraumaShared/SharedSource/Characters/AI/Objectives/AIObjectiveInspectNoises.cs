using System;

namespace Barotrauma
{
    class AIObjectiveInspectNoises : AIObjective
    {
        public override Identifier Identifier { get; set; } = "inspect noises".ToIdentifier();

        private AIObjectiveGoTo inspectNoiseObjective;

        /// <summary>
        /// Initial priority of the objective to check noises made by enemies
        /// </summary>
        const float InspectNoisePriority = 10.0f;
        /// <summary>
        /// How much the priority  of the objective to check noises made by enemies increases per noise
        /// </summary>
        const float InspectNoisePriorityIncrease = 10.0f;
        private const float InspectNoiseInterval = 1.0f;
        private float inspectNoiseTimer;

        /// <summary>
        /// If the character is not currently inspecting the noise (= if some other objective is taking priority)
        /// it forgets about it after this delay runs out. Otherwise they might unnecessarily go and inspect some
        /// noise that was emitted a long time ago once done with the higher-prio objective.
        /// </summary>
        private const float InspectNoiseExpirationDelay = 60.0f;
        private float inspectNoiseExpirationTimer = 0.0f;

        protected override float GetPriority() => inspectNoiseObjective?.Priority ?? 0.0f;

        public AIObjectiveInspectNoises(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) 
        {
            inspectNoiseTimer = Rand.Range(0.0f, InspectNoiseInterval);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            inspectNoiseTimer -= deltaTime;
            if (inspectNoiseTimer <= 0.0f)
            {
                CheckEnemyNoises();
                inspectNoiseTimer = InspectNoiseInterval;
            }
            //if we're not currently inspecting the noise (something else taking priority), forget about it after a while
            if (inspectNoiseObjective != null && objectiveManager.GetActiveObjective() != inspectNoiseObjective)
            {
                inspectNoiseExpirationTimer += deltaTime;
                if (inspectNoiseExpirationTimer > InspectNoiseExpirationDelay)
                {
                    inspectNoiseObjective.Abandon = true;
                }
            }
        }

        /// <summary>
        /// Check if there's any loud provocative items used by enemies nearby (= if someone fired a gun somewhere), and go inspect them
        /// </summary>
        private void CheckEnemyNoises()
        {
            if (character.CurrentHull == null) { return; }

            //forget about inspecting if we're doing another subobjective (= fighting something)
            if (inspectNoiseObjective != null &&
                CurrentSubObjective != inspectNoiseObjective)
            {
                inspectNoiseObjective.Abandon = true;
            }

            foreach (var aiTarget in AITarget.List)
            {
                if (aiTarget.ShouldBeIgnored()) { continue; }
                if (!aiTarget.IsWithinSector(character.WorldPosition)) { continue; }
                if (aiTarget.Entity is not Item item) { continue; }
                if (!item.HasTag(Tags.ProvocativeToHumanAI)) { continue; }
                if (item.GetRootInventoryOwner() is Character targetCharacter &&
                    AIObjectiveFightIntruders.IsValidTarget(targetCharacter, character, targetCharactersInOtherSubs: false))
                {
                    float dist = character.CurrentHull.GetApproximateDistance(character.Position, targetCharacter.Position, targetCharacter.CurrentHull, aiTarget.SoundRange, distanceMultiplierPerClosedDoor: 2);
                    if (dist * HumanAIController.Hearing > aiTarget.SoundRange) { continue; }
                    
                    character.Speak(TextManager.Get("dialogheardenemy").Value, identifier: "heardenemy".ToIdentifier(), minDurationBetweenSimilar: 30.0f);
                    if (inspectNoiseObjective != null && subObjectives.Contains(inspectNoiseObjective))
                    {
                        //priority of inspecting noises increases with each noise
                        //but orders still remain a higher priority
                        inspectNoiseObjective.Priority = Math.Min(inspectNoiseObjective.Priority + InspectNoisePriorityIncrease, AIObjectiveManager.LowestOrderPriority - 1);
                        //only refresh the target if the character hasn't yet started inspecting the noise
                        //(if it has, it should not switch the target, otherwise you could e.g. bounce an NPC back and forth by firing guns at different sides of an outpost)
                        if (objectiveManager.GetActiveObjective() != inspectNoiseObjective &&
                            inspectNoiseObjective.Target != targetCharacter.CurrentHull)
                        {
                            CreateInspectNoiseObjective(targetCharacter.CurrentHull, priority: inspectNoiseObjective.Priority);
                        }
                    }
                    else
                    {
                        CreateInspectNoiseObjective(targetCharacter.CurrentHull, priority: InspectNoisePriority);
                    }                    
                }
            }
            
            void CreateInspectNoiseObjective(ISpatialEntity target, float priority)
            {
                RemoveSubObjective(ref inspectNoiseObjective);
                inspectNoiseObjective = new AIObjectiveGoTo(target, character, objectiveManager)
                {
                    Priority = priority,
                    SourceObjective =  this
                };
                inspectNoiseObjective.Completed += () => { inspectNoiseObjective = null; inspectNoiseExpirationTimer = 0.0f; };
                inspectNoiseObjective.Abandoned += () => { inspectNoiseObjective = null; inspectNoiseExpirationTimer = 0.0f; };
                AddSubObjective(inspectNoiseObjective);
            }
        }

        protected override void Act(float deltaTime)
        {
        }

        protected override bool CheckObjectiveSpecific() => false;

    }
}
