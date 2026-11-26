using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Makes the bot take care of all plants on the sub/outpost, prioritizing the ones that need the most urgent care.
    /// </summary>
    public class AIObjectiveGardening : AIObjectiveLoop<Item>
    {
        /// <summary>
        /// How low the plants' relative water amount needs to drop for bots to start watering them
        /// </summary>
        public const float WaterPlantsThreshold = 0.5f;

        /// <summary>
        /// How high the plants' relative water amount needs to be for bots to stop watering them 
        /// (using the same threshold for starting and stopping would mean the bot just goes and 
        /// drops a tiny bit of water on the plant, and then a few seconds later it drops below the threshold again and needs more water)
        /// </summary>
        public const float WaterPlantsThresholdHigh = 0.75f;

        /// <summary>
        /// How low the plants' relative fertilizer amount needs to drop for bots to start fertilizing them
        /// </summary>
        public const float FertilizePlantsThreshold = 0.5f;

        /// <summary>
        /// How high the plants' relative fertilizer amount needs to be for the bot to stop fertilizing
        /// </summary>
        public const float FertilizePlantsThresholdHigh = 0.75f;

        //making bots plant seeds disabled (at least for now?), because we don't want the bots to decide what to grow and where
        public const float PlantSeedPriority = 0;// AIObjectiveManager.LowestOrderPriority + 1;

        public override Identifier Identifier { get; set; } = "gardening".ToIdentifier();

        public override bool AllowMultipleInstances => false;
        protected override bool RequireBothHandsAvailable => true;

        /// <summary>
        /// Periodically reset ignore list (to which planters may get added if the bot can't find a watering can or fertilizer)
        /// </summary>
        protected override float IgnoreListClearInterval => 30.0f;

        protected override bool RequireReports => false;

        public override bool IsDuplicate<T>(T otherObjective) => 
            otherObjective is AIObjectiveGardening gardeningObjective && 
            objectiveManager.IsOrder(gardeningObjective) == objectiveManager.IsOrder(this);

        public AIObjectiveGardening(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            ForceWalkPermanently = true;
        }

        protected override bool IsValidTarget(Item item)
        {
            if (HumanAIController == null) { return false; }
            if (!ViableForGardening(item, character, HumanAIController)) { return false; };
            return true;
        }

        public static bool ViableForGardening(Item item, Character character, HumanAIController humanAIController)
        {
            if (!IsValidTarget(item, character)) { return false; }
            if (item.CurrentHull == null) { return true; }
            if (item.CurrentHull.FireSources.Count > 0) { return false; }
            // Don't tend to plants in rooms that have enemies inside.
            if (Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !humanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            return true;
        }

        /// <summary>
        /// Get the planter's water level relative to the minimum and maximum thresholds of what's growing in it.
        /// </summary>
        /// <returns>Decimal number between 0 and 1 when the value is between the minimum and maximum thresholds, 0.5 at halfway, negative if below and more than 1 if above.</returns>
        public static float WaterLevel(Planter planter, Growable growable)
        {
            return (planter.NormalizedWaterLevel - growable.TooDryThreshold) / (growable.OverwaterThreshold - growable.TooDryThreshold);
        }
        
        /// <summary>
        /// Get the planter's fertilizer level relative to the minimum and maximum thresholds of what's growing in it.
        /// </summary>
        /// <returns>Decimal number between 0 and 1 when the value is between the minimum and maximum thresholds, 0.5 at halfway, negative if below and more than 1 if above.</returns>
        public static float FertilizerLevel(Planter planter, Growable growable)
        {
            return (planter.NormalizedFertilizerLevel - growable.UnderfertilizedThreshold) / (growable.OverfertilizedThreshold - growable.UnderfertilizedThreshold);
        }

        protected override float GetTargetPriority()
        {
            int items = Targets.Count;
            if (items == 0)
            {
                return 0;
            }
            return Targets.Sum(t => GetTargetPriority(t, character));            
        }

        public static float GetTargetPriority(Item item, Character character)
        {
            const float RemoveDecayedPlantPriority = AIObjectiveManager.LowestOrderPriority;

            var planter = item.GetComponent<Planter>();
            if (planter.GrowableSeeds.None(s => s!= null))
            {
                return PlantSeedPriority;
            }

            float highestPriority = 0.0f;

            foreach (var growable in planter.GrowableSeeds)
            {
                if (growable == null) { continue; }
                if (growable.Decayed)
                {
                    return RemoveDecayedPlantPriority;
                }
                float waterLevel = WaterLevel(planter, growable);

                if (waterLevel < WaterPlantsThreshold)
                {
                    //if the plant needs very high water levels, watering it isn't a viable way to keep it alive but it must be submerged instead
                    //-> let's not even dry watering it
                    bool growableMustBeSubmerged = growable.DrynessDamage > 0.0f && growable.TooDryThreshold > 0.95f;
                    if (growableMustBeSubmerged)
                    {
                        character.Speak(GetDialogWithPlantName("dialoggardening.plantnotsubmerged", growable), identifier: $"dialoggardening.plantnotsubmerged{growable.Item.Prefab.Identifier}".ToIdentifier(), minDurationBetweenSimilar: 30.0f);
                    }
                    else
                    {
                        //high prio when water is low
                        float waterPriority = MathHelper.Lerp(100, 0, MathUtils.InverseLerp(min: -1, WaterPlantsThreshold, waterLevel));
                        highestPriority = MathHelper.Max(highestPriority, waterPriority);
                    }
                }
                float fertilizerLevel = FertilizerLevel(planter, growable);
                if (fertilizerLevel < FertilizePlantsThreshold)
                {
                    //high prio when fertilizer is low
                    float fertilizerPriority = MathHelper.Lerp(100, 0, MathUtils.InverseLerp(min: -1, FertilizePlantsThreshold, fertilizerLevel));
                    highestPriority = MathHelper.Max(highestPriority, fertilizerPriority);
                }
            }
            return highestPriority;
        }
        public static string GetDialogWithPlantName(string textIdentifier, Growable growable)
        {
            return TextManager.GetWithVariable(textIdentifier, "[plantname]", growable.GrownPlantName).Value;
        }

        protected override IEnumerable<Item> GetList() => Item.PlanterItems;

        protected override AIObjective ObjectiveConstructor(Item item)
        {
            var planter = item.GetComponent<Planter>();
            return new AIObjectiveGardeningSpecificPlant(character, objectiveManager, item.GetComponent<Planter>(), priorityModifier: PriorityModifier)
            {
                Repeat = false
            };
        }

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveGardening, Item>(character, target);

        public static bool IsValidTarget(Item item, Character character)
        {
            return AIObjectiveGardeningSpecificPlant.IsValidTarget(item, character);
        }        
    }
}
