using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Makes the bot take care of the plants in a specific planter (watering, fertilizing, removing decayed plants).
    /// </summary>
    public class AIObjectiveGardeningSpecificPlant : AIObjective
    {
        public override Identifier Identifier { get; set; } = "gardening specific plant".ToIdentifier();

        public override bool AllowMultipleInstances => false;
        protected override bool AllowInFriendlySubs => true;

        //brief initial delay so the bot doesn't immediately react with e.g. "this plant needs water" the moment you assign the order
        private float updateDelay = 5.0f;

        /// <summary>
        /// When true, the operate objective is never completed, unless it's abandoned.
        /// </summary>
        public bool Repeat { get; init; } = true;

        private const float UpdateInterval = 5.0f;

        public override bool IsDuplicate<T>(T otherObjective) => 
            otherObjective is AIObjectiveGardeningSpecificPlant gardeningObjective && 
            objectiveManager.IsOrder(gardeningObjective) == objectiveManager.IsOrder(this);

        private readonly Planter targetPlanter;

        public AIObjectiveGardeningSpecificPlant(Character character, AIObjectiveManager objectiveManager, Planter targetPlanter, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetPlanter = targetPlanter;
            ForceWalkPermanently = true;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                HandleDisallowed();
                return Priority;
            }
            else
            {
                Priority = 
                    AIObjectiveGardening.GetTargetPriority(targetPlanter.Item, character)
                    //devotion makes the NPC prefer sticking to the current objective
                    //without this, the bot might drop just a bit of water on a plant, then realize another plant has a tiny bit less water and switch to that one
                    * MathUtils.Max(1.0f, CumulatedDevotion / MaxDevotion); 
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        { 
            updateDelay -= deltaTime;
            if (updateDelay > 0.0f) 
            { 
                character.AIController?.SteeringManager.Reset();
                return;
            }
            updateDelay = UpdateInterval;

            //making bots plant seeds disabled (at least for now?), because we don't want the bots to decide what to grow and where
            /*if (planter.GrowableSeeds.None(s => s != null))
            {
                return new AIObjectiveContainItem(character, "seed".ToIdentifier(), item.GetComponent<ItemContainer>(), objectiveManager)
                {
                    GetItemPriority = (Item item) =>
                    {
                        if (item.GetComponent<Growable>() is { Decayed: true })
                        {
                            return 0.0f;
                        }
                        return 1.0f;
                    },
                    //don't try to take seeds from other planters
                    ignoredContainerIdentifiers = "planter".ToIdentifier().ToEnumerable().ToImmutableHashSet(),
                    ForceWalk = true
                };
            }*/

            foreach (var growable in targetPlanter.GrowableSeeds)
            {
                if (growable == null) { continue; }

                //use this in the dialog identifiers so each plant is handled separately
                //(a character should not refraing from saying a plant is drying up because they just said that line about a different plant)
                ushort plantInstanceIdentifier = growable.Item.ID;

                if (growable.Decayed)
                {
                    //remove the decayed growable from the planter
                    character.Speak(AIObjectiveGardening.GetDialogWithPlantName("dialoggardening.uproot", growable), identifier: $"dialoggardening.uproot{plantInstanceIdentifier}".ToIdentifier(), 
                        minDurationBetweenSimilar: 10.0f);
                    Item decayedSeed = growable.Item;
                    var upRootObjective = new AIObjectiveGetItem(character, decayedSeed, objectiveManager);
                    upRootObjective.Completed += () =>
                    {
                        //destroy the uprooted seed, same as when a player uproots a plant (see Planter.TryUpRoot)
                        if (decayedSeed.GetRootInventoryOwner() == character)
                        {
                            decayedSeed.ParentInventory?.RemoveItem(decayedSeed);
                            Entity.Spawner?.AddItemToRemoveQueue(decayedSeed);
                        }
                        //no more plant to tend to, consider the objective completed
                        IsCompleted = true;
                    };
                    AddSubObjective(upRootObjective);
                    return;
                }
                else if (AIObjectiveGardening.WaterLevel(targetPlanter, growable) < AIObjectiveGardening.WaterPlantsThreshold)
                {
                    // Water the plant (technically "repair" it's condition with a watering can)
                    if (character.Inventory.FindItem(it => it.HasIdentifierOrTag("wateringcan".ToIdentifier()), recursive: true) is { } wateringCan &&
                        wateringCan.GetComponent<RepairTool>() is RepairTool repairTool)
                    {
                        var operateOrder = new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: Identifier.Empty, requireEquip: true, operateTarget: targetPlanter.Item)
                        {
                            completionCondition = () => AIObjectiveGardening.WaterLevel(targetPlanter, growable) > AIObjectiveGardening.WaterPlantsThresholdHigh
                        };
                        var gotoObjective = new AIObjectiveGoTo(targetPlanter.Item, character, objectiveManager,
                            minimumCloseEnoughDistance: repairTool.CalculateReach(character) * 0.8f);
                        gotoObjective.Completed += () =>
                        {
                            character.Speak(AIObjectiveGardening.GetDialogWithPlantName("dialoggardening.waterplant", growable), 
                                identifier: $"dialoggardening.waterplant{plantInstanceIdentifier}".ToIdentifier(), 
                                minDurationBetweenSimilar: 120.0f);
                        };
                        operateOrder.AddSubObjective(gotoObjective);
                        AddSubObjective(operateOrder);
                        return;
                    }
                    else
                    {
                        character.Speak(TextManager.Get("dialoggardening.findwateringcan").Value, identifier: 
                            "dialoggardening.findwateringcan".ToIdentifier(), 
                            minDurationBetweenSimilar: 60.0f);
                        var getWateringCanObjective = new AIObjectiveGetItem(character, "wateringcan".ToIdentifier(), objectiveManager, equip: true);
                        getWateringCanObjective.Completed += () =>
                        {
                            updateDelay = 0.0f;
                        };
                        AddSubObjective(getWateringCanObjective);
                        return;
                    }
                }
                else if (AIObjectiveGardening.FertilizerLevel(targetPlanter, growable) < AIObjectiveGardening.FertilizePlantsThreshold)
                {
                    // Fertilize the plant (technically "repair" it's condition with a sack of fertilizer)
                    if (character.Inventory.FindItem(it => it.HasIdentifierOrTag("fertilizer".ToIdentifier()), recursive: true) is { } fertilizer &&
                        fertilizer.GetComponent<RepairTool>() is RepairTool repairTool)
                    {
                        var operateOrder = new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: Identifier.Empty, requireEquip: true, operateTarget: targetPlanter.Item)
                        {
                            completionCondition = () => AIObjectiveGardening.FertilizerLevel(targetPlanter, growable) > AIObjectiveGardening.FertilizePlantsThresholdHigh
                        };
                        var gotoObjective = new AIObjectiveGoTo(targetPlanter.Item, character, objectiveManager,
                            minimumCloseEnoughDistance: repairTool.CalculateReach(character) * 0.8f);
                        gotoObjective.Completed += () =>
                        {
                            character.Speak(AIObjectiveGardening.GetDialogWithPlantName("dialoggardening.fertilizeplant", growable), 
                                identifier: $"dialoggardening.fertilizeplant{plantInstanceIdentifier}".ToIdentifier(), 
                                minDurationBetweenSimilar: 120.0f);
                        };
                        operateOrder.AddSubObjective(gotoObjective);
                        AddSubObjective(operateOrder);
                        return;
                    }
                    else
                    {
                        character.Speak(TextManager.Get("dialoggardening.findfertilizer").Value, 
                            identifier: "dialoggardening.findfertilizer".ToIdentifier(), 
                            minDurationBetweenSimilar: 60.0f);
                        var getFertilizerObjective = new AIObjectiveGetItem(character, "fertilizer".ToIdentifier(), objectiveManager, equip: true) 
                        { 
                            SpeakIfFails = true 
                        };
                        getFertilizerObjective.Completed += () =>
                        {
                            updateDelay = 0.0f;
                        };
                        AddSubObjective(getFertilizerObjective);
                        return;
                    }
                }
            }

            //in the case of plant-specific contextual orders, we want make the bot continue the objective because the plant may need attention later
            //in the case of AIObjectiveGardening, which creates objectives for each plant when needed, it's ok to mark this as completed
            if (!Repeat)
            {
                IsCompleted = true;
            }
        }

        public static bool IsValidTarget(Item item, Character character)
        {
            if (item == null) { return false; }
            if (item.IgnoreByAI(character)) { return false; }
            if (item.ParentInventory is CharacterInventory && item.ParentInventory != character.Inventory) { return false; }
            if (!item.IsInteractable(character)) { return false; }
            if (item.Submarine == null || character.Submarine == null) { return false; }
            //player crew ignores items in outposts
            if (character.IsOnPlayerTeam && item.Submarine.Info.IsOutpost) { return false; }
            if (!IsAllowedInSubmarine(item.Submarine, character, allowInAnySub: false, allowInFriendlySubs: false)) { return false; }
            if (!character.Submarine.IsEntityFoundOnThisSub(item, includingConnectedSubs: true, allowDifferentType: true)) { return false; }

            if (item.GetComponent<Planter>() is not Planter planter)
            {
                return false;
            }
            //planters that aren't placed don't grow and can't be watered or fertilized
            if (!planter.HasBeenPlaced) { return false; }
            if (AIObjectiveGardening.PlantSeedPriority <= 0)
            {
                if (planter.GrowableSeeds.None(s => s != null)) { return false; }
            }
            return true;
        }

        protected override bool CheckObjectiveState()
        {
            return IsCompleted;
        }

        public override string DebugTag => $"{base.DebugTag} ({targetPlanter.GrowableSeeds.FirstOrDefault()?.Item.Name ?? "none"})";
    }
}
