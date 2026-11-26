using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Plugins;
using Barotrauma;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace HomeAndHarbor
{
    public class AIObjectiveRanchingSpecificPenFactory : IOrderObjectiveFactory
    {
        public AIObjective? CreateObjectiveFromOrder(AIObjectiveManager objectiveManager, Order order, Character character, float priorityModifier)
        {
            if (order.Identifier == "ranching")
            {
                return new AIObjectiveRanchingSpecificPen(character, objectiveManager, priorityModifier);
            }

            return null;
        }
    }

    /// <summary>
    /// Makes the bot bring food to the food trough and play with any unhappy pets in the same room as the item.
    /// </summary>
    public class AIObjectiveRanchingSpecificPen : AIObjective
    {
        /// <summary>
        /// If there's multiple pens within this distance, they are considered to be equally likely to be chosen for ranching,
        /// this is to avoid ranchers from only prioritizing the pen that is closest to them, instead of all the different 
        /// pens that are in the ranching module.
        /// </summary>
        private const float ClosestPenDistanceToleranceSquared = 10000.0f;

        public override Identifier Identifier { get; set; } = "ranchingspecificpen".ToIdentifier();

        public override bool AllowMultipleInstances => false;
        protected override bool AllowInFriendlySubs => true;

        private float updateDelay = 5.0f;

        private const float UpdateInterval = 5.0f;

        private readonly Item? targetItem;
        private readonly CreatureResourceGeneratorComponent? targetCreatureResourceGenerator;

        public AIObjectiveRanchingSpecificPen(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            ForceWalkPermanently = true;

            var creatureGenerators = character.Submarine.GetItems(alsoFromConnectedSubs: true)
                .Select(item => item.GetComponent<CreatureResourceGeneratorComponent>())
                .Where(gen => gen != null && gen.RequiresRanching())
                .ToList();

            if (creatureGenerators.None())
            {
                IsCompleted = true;
                return;
            }

            float minDist = creatureGenerators.Min(gen => Vector2.DistanceSquared(character.Position, gen.Item.Position));

            targetCreatureResourceGenerator = creatureGenerators
                .Where(gen => Vector2.DistanceSquared(character.Position, gen.Item.Position) <= minDist + ClosestPenDistanceToleranceSquared)
                .GetRandomUnsynced();

            targetItem = targetCreatureResourceGenerator?.Item;

            if (targetCreatureResourceGenerator == null)
            {
                IsCompleted = true;
            }
        }

        public override bool IsDuplicate<T>(T otherObjective) =>
            otherObjective is AIObjectiveRanchingSpecificPen ranchingObjective &&
            objectiveManager.IsOrder(ranchingObjective) == objectiveManager.IsOrder(this);

        protected override void Act(float deltaTime)
        {
            updateDelay -= deltaTime;
            if (updateDelay > 0.0f)
            {
                character.AIController?.SteeringManager.Reset();
                return;
            }
            updateDelay = UpdateInterval;

            if (targetItem == null || targetCreatureResourceGenerator == null)
            {
                return;
            }

            if (CreatureResourceGeneratorComponent.DoesRanchingFoodConsumptionExceedProduction())
            {
                character.Speak(TextManager.Get("dialogranching.overcapacity").Value, identifier: "dialogranching.overcapacity".ToIdentifier(), minDurationBetweenSimilar: 60f);
            }

            IEnumerable<Character> unhappyPets = targetCreatureResourceGenerator.CreaturesInPen
                .Where(pet => pet.AIController is EnemyAIController controller &&
                              controller.PetBehavior != null &&
                              controller.PetBehavior.Happiness < controller.PetBehavior.UnhappyThreshold);

            // Play with unhappy pets
            if (unhappyPets.Any())
            {
                character.Speak(TextManager.Get("dialogranching.unhappy").Value, identifier: "dialogranching.unhappy".ToIdentifier(), minDurationBetweenSimilar: 60f);

                Character? randomPet = unhappyPets.GetRandomUnsynced();

                var goToObjective = new AIObjectiveGoTo(randomPet, character, objectiveManager);
                goToObjective.Completed += () => 
                {
                    if (randomPet?.AIController is EnemyAIController { } enemyAIController)
                    {
                        enemyAIController.PetBehavior.Play(character);
                    }
                };

                AddSubObjective(goToObjective);
            }
            // Bring food to the pen
            else if (targetCreatureResourceGenerator.CreaturesInPen.Any() && targetItem.OwnInventory.IsEmpty())
            {
                var containObjective = new AIObjectiveContainItem(character, "ranchingfood".ToIdentifier(), targetItem.GetComponent<ItemContainer>(), objectiveManager)
                {
                    ignoredContainerIdentifiers = [Tags.DontTakeItems]
                };

                containObjective.ItemToContainFound += (Item foundItem) =>
                {
                    character.Speak(TextManager.Get("dialogranching.food").Value, identifier: "dialogranching.food".ToIdentifier(), minDurationBetweenSimilar: 60f);
                };

                containObjective.Abandoned += () =>
                {
                    character.Speak(TextManager.Get("dialogranching.failfindfood").Value, identifier: "dialogranching.failfindfood".ToIdentifier(), minDurationBetweenSimilar: 60f);
                    Abandon = true;
                };

                AddSubObjective(containObjective);
            }
            else
            {
                IsCompleted = true;
            }
        }

        protected override bool CheckObjectiveState()
        {
            return IsCompleted;
        }
    }
}
