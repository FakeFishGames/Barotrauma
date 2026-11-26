using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Xml.Linq;
using static HomeAndHarbor.ResourceGenerator;

namespace HomeAndHarbor
{
    /// <summary>
    /// Manages creatures within the same hull as this item, generating resources,
    /// killing starving creatures, and distributing food to creatures.
    /// </summary>
    public class CreatureResourceGeneratorComponent : ResourceGeneratorBaseComponent
    {
        private class CreatureGenerator
        {
            public readonly Identifier SpeciesName;

            /// <summary>
            /// Required resource by the creature to be able to produce resources, if they don't meet the 
            /// required amount they will halt production and eventually die.
            /// </summary>
            public readonly Identifier RequiredResource;
            public readonly float RequiredAmount;

            public readonly Identifier GrowsIntoSpeciesName;
            public readonly int GrowsAfterWorldSteps;

            /// <summary>
            /// The probability of a reproduction occurring during a world step. If multiple world steps have 
            /// passed, the probabilities are compounded into a single accumulated chance. This accumulated 
            /// chance persists until reproduction succeeds, causing creatures that have waited longer to become 
            /// progressively more likely to reproduce.
            /// </summary>
            public readonly float ReproductionChance;
            /// <summary>
            /// The minimum number of world steps that must pass before creatures become eligible to reproduce. 
            /// After this threshold is reached, each additional world step increases the accumulated reproduction 
            /// chance until reproduction succeeds, at which point the world step timer is reset.
            /// </summary>
            public readonly int MinWorldStepsToReproduce;
            /// <summary>
            /// The minimum number of creatures required to form a reproduction group. For example, a value of 2 
            /// means that 8 creatures form 4 independent groups, each of which rolls for reproduction separately.
            /// </summary>
            public readonly int MinCreaturesToReproduce;

            public readonly ImmutableArray<ResourceGenerator.ProducedResource> ProducedResources;

            public CreatureGenerator(XElement element)
            {
                SpeciesName = element.GetAttributeIdentifier(nameof(SpeciesName), "");
                RequiredResource = element.GetAttributeIdentifier(nameof(RequiredResource), "");
                RequiredAmount = element.GetAttributeFloat(nameof(RequiredAmount), 1f);

                GrowsIntoSpeciesName = element.GetAttributeIdentifier(nameof(GrowsIntoSpeciesName), "");
                GrowsAfterWorldSteps = element.GetAttributeInt(nameof(GrowsAfterWorldSteps), 1);

                ReproductionChance = element.GetAttributeFloat(nameof(ReproductionChance), 0.0f);
                MinWorldStepsToReproduce = element.GetAttributeInt(nameof(MinWorldStepsToReproduce), 1);
                MinCreaturesToReproduce = element.GetAttributeInt(nameof(MinCreaturesToReproduce), 1);

                List<ResourceGenerator.ProducedResource> producedResources = [];

                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.NameAsIdentifier() == ProducedResourceElementName)
                    {
                        producedResources.Add(ProducedResource.FromXml(subElement, nameof(CreatureResourceGeneratorComponent)));
                    }
                }

                ProducedResources = producedResources.ToImmutableArray();
            }
        }

        [Serialize(0.006f, IsPropertySaveable.No, "Chance per second of hostile creatures trying to escape if the ranching fence is currently inactive or missing.")]
        public float ChanceToRandomlyAttackFencePerSecond { get; private set; }

        [Serialize(60f, IsPropertySaveable.No, "Time in seconds in which this item will drop the food inside the inventory to feed the creatures.")]
        public float FeedDelay { get; private set; }        

        [Serialize(0.25f, IsPropertySaveable.No, "Percentage of creatures that get killed every world step when they are starving.")]
        public float KillPercentageWhenStarvingPerWorldStep { get; private set; }

        [Serialize(0.25f, IsPropertySaveable.No, "Percentage of creatures that get angered every world step when they are starving.")]
        public float AngerPercentageWhenStarvingPerWorldStep { get; private set; }

        public IEnumerable<Character> CreaturesInPen => charactersInPen.Where(static c => !c.IsHuman && !c.Removed && !c.IsDead);

        private const double refreshInterval = 2f;
        private double refreshTimer;

        private int lastSavedWorldStep = 0;

        private List<Item> fences = new List<Item>();

        private readonly List<Character> charactersInPen = new List<Character>();
        private readonly List<Character> starvingCreatures = new List<Character>();
        private readonly HashSet<Character> angeredCreatures = new HashSet<Character>();

        private readonly List<Item> droppedFoodItems = new List<Item>();

        private readonly List<CreatureGenerator> creatureGenerators = new List<CreatureGenerator>();

        private readonly List<Character> newCharacterList = new List<Character>();

        public CreatureResourceGeneratorComponent(Item item, ContentXElement element) : base(item, element)
        {
            foreach (ContentXElement subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == nameof(CreatureGenerator))
                {
                    creatureGenerators.Add(new CreatureGenerator(subElement));
                }
            }
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            lastSavedWorldStep = componentElement.GetAttributeInt(nameof(lastSavedWorldStep), GameMain.GameSession?.Campaign?.Map.WorldStepCount ?? 0);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = base.Save(parentElement);
            element.Add(new XAttribute(nameof(lastSavedWorldStep), GameMain.GameSession?.Campaign?.Map.WorldStepCount ?? 0));
            return element;
        }

        /// <summary>
        /// Gets all currently active creature generators (that have creatures)
        /// </summary>
        /// <remarks>
        /// Contains duplicate CreatureGenerator on purpose, to indicate that there's more than one creature generating resources
        /// </remarks>
        private IEnumerable<CreatureGenerator> GetActiveCreatureGenerators(IEnumerable<Character> characters)
        {
            foreach (Character character in characters)
            {
                foreach (CreatureGenerator creatureLoot in creatureGenerators)
                {
                    if (character.MatchesSpeciesNameOrGroup(creatureLoot.SpeciesName))
                    {
                        yield return creatureLoot;

                        break;
                    }
                }
            }
        }

        private CreatureGenerator? GetCreatureGenerator(Character character)
            => creatureGenerators.FirstOrDefault(g => character.MatchesSpeciesNameOrGroup(g.SpeciesName));

        public void RefreshResourceGenerator(IEnumerable<Character> characters)
        {
            Dictionary<Identifier, float> requiredResources = [];
            List<ProducedResource> producedResources = ResourceGenerator.ProducedResourcesList;

            producedResources.Clear();

            foreach (CreatureGenerator creatureGenerator in GetActiveCreatureGenerators(characters))
            {
                // Build a combined list of produced resources from all active creature generators
                foreach (ProducedResource producedResource in creatureGenerator.ProducedResources)
                {
                    int existingIndex = producedResources.FindIndex(x => x.Identifier == producedResource.Identifier);

                    if (existingIndex >= 0)
                    {
                        // Merge similar resources to avoid creating duplicate resource entries (e.g. three separate mudraptor exhaust tubes showing in the UI)
                        ProducedResource existing = producedResources[existingIndex];

                        existing.Amount += producedResource.Amount;

                        producedResource.ItemResourceAddedItems.ForEach(x =>
                        {
                            if (existing.ItemResourceAddedItems.TryGetValue(x.Key, out float value))
                            {
                                existing.ItemResourceAddedItems[x.Key] += x.Value;
                            }
                            else
                            {
                                existing.ItemResourceAddedItems[x.Key] = x.Value;
                            }
                        });

                        producedResources[existingIndex] = existing;
                    }
                    else
                    {
                        ProducedResource producedResourceCopy = producedResource;

                        // Copy the dictionary so modifying the merged resource doesn't modify the original creature generator
                        producedResourceCopy.ItemResourceAddedItems = producedResourceCopy.ItemResourceAddedItems.ToDictionary();

                        producedResources.Add(producedResourceCopy);
                    }
                }

                // Also fill in a dictionary of resource requirements
                if (!creatureGenerator.RequiredResource.IsEmpty)
                {
                    requiredResources.TryGetValue(creatureGenerator.RequiredResource, out float value);
                    requiredResources[creatureGenerator.RequiredResource] = value + creatureGenerator.RequiredAmount;
                }
            }

            ResourceGenerator.RequiredResources.Clear();
            foreach ((Identifier resource, float amount) in requiredResources)
            {
                // Make creatures subtract their required resources
                ResourceGenerator.ProducedResourcesList.Add(new ProducedResource(
                    Identifier: resource, 
                    Amount: -amount, 
                    AmountAtMinSkill: null,
                    AmountAtMaxSkill: null,
                    ItemResourceAddedItems: [],
                    DisplayNameTag: null,
                    AllowedBiomes: []));

                // Resource generator goes inactive if we don't meet any of the resource requirements
                ResourceGenerator.RequiredResources.Add(new Requirement(resource, PropertyConditional.ComparisonOperatorType.GreaterThan, amount));
            }
        }

        float feedTimer = 0f;
        private void FeedCreatures()
        {
            if (feedTimer > 0) 
            { 
                return; 
            }

            feedTimer = FeedDelay;

            if (!CreaturesInPen.Any())
            {
                return;
            }

            droppedFoodItems.RemoveAll(food => food.Removed || food.CurrentHull != Item.CurrentHull);

            Item? foodItem = droppedFoodItems.FirstOrDefault();
            if (foodItem == null)
            {
                foodItem = item.OwnInventory.FirstOrDefault();
            }

            if (foodItem == null) { return; }

            Character? randomHungryPet = CreaturesInPen
                .Where(c => c.AIController is EnemyAIController { } enemyAIController &&
                    enemyAIController.PetBehavior is { } petBehaviour &&
                    petBehaviour.Hunger >= petBehaviour.HungryThreshold)
                .GetRandomUnsynced();

            bool isNotPetOnlyPen = CreaturesInPen.Any(static c => !c.IsPet);

            // Only drop food if there's hungry pets or if this pen doesn't only contain pets (hostile creatures don't really have a
            // hunger bar, but we still make them eat to make it look better visually)
            if (randomHungryPet != null || isNotPetOnlyPen)
            {
                foodItem.Drop(null);

                // Prevent bots from taking the dropped food item and putting back it into the food trough
                if (!foodItem.HasTag(Tags.IgnoredByAI))
                {
                    foodItem.AddTag(Tags.IgnoredByAI);
                }

                droppedFoodItems.Add(foodItem);

                // Prioritize hungry pets since they actually have a hunger bar
                Character? randomCreature = randomHungryPet ?? CreaturesInPen.GetRandomUnsynced();

                if (foodItem.AiTarget != null && randomCreature != null && randomCreature.AIController is EnemyAIController enemyAI)
                {
                    enemyAI.State = AIState.Eat;
                    enemyAI.SelectTarget(foodItem.AiTarget, 100f);
                }
            }
        }

        private void CheckCreaturesAfterWorldStepChanges(int worldStepsPassed)
        {
            if (Level.Loaded is not { Type: LevelData.LevelType.Outpost } ||
                Level.Loaded.StartLocation is not { } location ||
                PersistentOutpostData.GetPersistentOutpostData(location) is not { } outpostData ||
                outpostData.ResourceManager == null)
            {
                return;
            }

            if (outpostData.CreatureManager != null)
            {
                // Grow creatures
                foreach (Character character in CreaturesInPen.ToList())
                {
                    CreatureGenerator? creatureGenerator = GetCreatureGenerator(character);
                    if (creatureGenerator == null) { continue; }
                    if (creatureGenerator.GrowsIntoSpeciesName.IsEmpty) { continue; }

                    OutpostCreatureInfo creatureInfo = outpostData.CreatureManager.GetOrAddCreatureInfo(character);
                    if (creatureInfo.WorldStepsInOutpost < creatureGenerator.GrowsAfterWorldSteps)
                    {
                        continue;
                    }

                    Entity.Spawner.AddCharacterToSpawnQueue(creatureGenerator.GrowsIntoSpeciesName, character.WorldPosition);
                    Entity.Spawner.AddEntityToRemoveQueue(character);

                    charactersInPen.Remove(character);
                }

                // Reproduce creatures
                foreach (CreatureGenerator creatureGenerator in creatureGenerators)
                {
                    List<Character> eligibleCreatures = [];
                    foreach (Character character in CreaturesInPen)
                    {
                        OutpostCreatureInfo creatureInfo = outpostData.CreatureManager.GetOrAddCreatureInfo(character);

                        if (character.MatchesSpeciesNameOrGroup(creatureGenerator.SpeciesName))
                        {
                            eligibleCreatures.Add(character);
                        }
                    }

                    int groupSize = creatureGenerator.MinCreaturesToReproduce;
                    int reproductionGroups = eligibleCreatures.Count / groupSize;

                    for (int group = 0; group < reproductionGroups; group++)
                    {
                        int groupStart = group * groupSize;

                        // Use the creature with the fewest elapsed world steps as the group's limiting world step timer
                        int groupWorldStepsSinceAttempt = int.MaxValue;
                        for (int i = groupStart; i < groupStart + groupSize; i++)
                        {
                            OutpostCreatureInfo info = outpostData.CreatureManager.GetOrAddCreatureInfo(eligibleCreatures[i]);

                            int worldStepsSinceAttempt =
                                info.LastReproductionWorldStep == -1
                                    ? info.WorldStepsInOutpost
                                    : info.WorldStepsInOutpost - info.LastReproductionWorldStep;

                            groupWorldStepsSinceAttempt = Math.Min(groupWorldStepsSinceAttempt, worldStepsSinceAttempt);
                        }

                        if (groupWorldStepsSinceAttempt < creatureGenerator.MinWorldStepsToReproduce) { continue; }

                        int worldStepDifference = groupWorldStepsSinceAttempt - creatureGenerator.MinWorldStepsToReproduce;

                        // Accumulate the chance through out the world step difference, note that this doesn't take in account the
                        // possibility of there being more than one reproduction in these world steps. This is by design to prevent
                        // overwhelming the player by spawning too many creatures at the same time if they leave the outpost for too long
                        float accumulatedChance = 
                            ToolBox.CompoundPercentage(
                                percentagePerStep: creatureGenerator.ReproductionChance, 
                                steps: worldStepDifference);

                        if (Random.Shared.NextSingle() > accumulatedChance)
                        {
                            continue;
                        }

                        Entity.Spawner.AddCharacterToSpawnQueue(creatureGenerator.SpeciesName, eligibleCreatures[groupStart].WorldPosition, character =>
                        {
                            FillPetNeeds(character);
                            Pacify(character);
                        });

                        // Set the creature's reproduced world step to it's current world step in the outpost to reset the timer
                        for (int i = groupStart; i < groupStart + groupSize; i++)
                        {
                            OutpostCreatureInfo info = outpostData.CreatureManager.GetOrAddCreatureInfo(eligibleCreatures[i]);
                            info.LastReproductionWorldStep = info.WorldStepsInOutpost;
                        }
                    }
                }
            }

            // Calculate resource requirements in this pen
            Dictionary<Identifier, float> requiredResources = [];

            foreach (CreatureGenerator creatureGenerator in GetActiveCreatureGenerators(CreaturesInPen))
            {
                if (!creatureGenerator.RequiredResource.IsEmpty)
                {
                    requiredResources.TryGetValue(creatureGenerator.RequiredResource, out float value);
                    requiredResources[creatureGenerator.RequiredResource] = value + creatureGenerator.RequiredAmount;
                }
            }

            // Check which resources are missing
            HashSet<Identifier> missingResources = [];

            foreach ((Identifier resourceIdentifier, float requiredAmount) in requiredResources)
            {
                if (outpostData.ResourceManager.GetResource(resourceIdentifier) is { } resource && resource.Amount < requiredAmount)
                {
                    missingResources.Add(resourceIdentifier);
                }
            }

            starvingCreatures.Clear();
            starvingCreatures.AddRange(CreaturesInPen.Where(c => GetCreatureGenerator(c) is { } generator && missingResources.Contains(generator.RequiredResource)));

            float percentageToKill = ToolBox.CompoundPercentage(
                percentagePerStep: KillPercentageWhenStarvingPerWorldStep,
                steps: worldStepsPassed);

            int amountToKill = (int)Math.Min(starvingCreatures.Count, starvingCreatures.Count * percentageToKill + Rand.Range(-1.0f, 1.0f));

            // Kill a fraction of the starving creature
            for (int i = 0; i < amountToKill; i++)
            {
                Character? creatureToKill = starvingCreatures.GetRandomUnsynced();
                if (creatureToKill == null)
                {
                    break;
                }

                creatureToKill.CharacterHealth.ApplyAffliction(creatureToKill.AnimController.MainLimb, AfflictionPrefab.InternalDamage.Instantiate(1000));
                starvingCreatures.Remove(creatureToKill);
            }

            float percentageToAnger = ToolBox.CompoundPercentage(
                percentagePerStep: AngerPercentageWhenStarvingPerWorldStep,
                steps: worldStepsPassed);

            int amountToAnger = (int)Math.Min(starvingCreatures.Count, starvingCreatures.Count * percentageToAnger + Rand.Range(-1.0f, 1.0f));

            // Anger a fraction of the starving creatures
            for (int i = 0; i < amountToAnger; i++)
            {
                Character? creatureToAnger = starvingCreatures.Except(angeredCreatures).GetRandomUnsynced();
                if (creatureToAnger == null)
                {
                    break;
                }

                angeredCreatures.Add(creatureToAnger);
            }

            foreach (var character in CreaturesInPen.Except(starvingCreatures))
            {
                // Pacify creatures instantly so they don't try to break out as soon as the round starts
                Pacify(character);

                // Max out hunger and happiness for all pets that aren't starving when you enter the outpost
                FillPetNeeds(character);
            }
        }

        private void Pacify(Character character)
        {
            if (character.AIController is EnemyAIController enemyAI)
            {
                enemyAI.State = AIState.Idle;
                enemyAI.Reset();
                character.ForceDisableTargetingTimer = 1f;
            }
        }

        private void FillPetNeeds(Character character)
        {
            if (character.AIController is not EnemyAIController { } enemyAI) { return; }
            if (enemyAI.PetBehavior is not { } petBehavior) { return; }

            petBehavior.Happiness = petBehavior.MaxHappiness;
            petBehavior.Hunger = 0f;
        }

        private void UpdateCreatures(float deltaTime)
        {
            foreach (Character character in CreaturesInPen)
            {
                if (character.IsPet) { continue; }

                if (character.Stun > 1f)
                {
                    // Pacify if the creature was stunned
                    angeredCreatures.Remove(character);
                    if (character.AIController is EnemyAIController enemyAI)
                    {
                        enemyAI.State = AIState.Idle;
                        enemyAI.Reset();
                    }
                }

                if (!angeredCreatures.Contains(character))
                {
                    if (item.InWater != character.NeedsWater)
                    {
                        // Anger creatures if pen is underwater or if without water for water-breathing creatures
                        angeredCreatures.Add(character);
                        continue;
                    }
                    else if (fences.None(fence => fence.GetComponent<SelectiveColliderComponent>() is { } selectiveCollider && selectiveCollider.ColliderEnabled))
                    {
                        // Pen fence is broken/disabled, try to escape
                        angeredCreatures.Add(character);
                        continue;
                    }

                    if (character.AIController is EnemyAIController enemyAI)
                    {
                        Character? badCharacter = charactersInPen.FirstOrDefault(
                            c => 
                            !character.IsFriendly(c) && 
                            // Always ignore humans, NPCs are too prone to accidentally entering pens
                            !c.IsHuman);

                        if (character.LastDamage.Damage > 0 && 
                            (Timing.TotalTime - character.LastDamageTime) < 5f &&
                            character.LastAttacker is Character damageSourceCharacter)
                        {
                            if (damageSourceCharacter.CurrentHull == character.CurrentHull)
                            {
                                // Attack character if inside the pen
                                enemyAI.Reset();
                                enemyAI.State = AIState.Attack;
                                enemyAI.SelectTarget(damageSourceCharacter.AiTarget, 100f);
                            }
                            else
                            {
                                // Otherwise just become angered (= try to break out)
                                angeredCreatures.Add(character);
                                continue;
                            }
                        }
                        else if (fences.Any() && Random.Shared.NextSingle() <= ChanceToRandomlyAttackFencePerSecond * deltaTime)
                        {
                            enemyAI.State = AIState.Attack;
                            enemyAI.SelectTarget(fences.GetRandomUnsynced()!.AiTarget, 100f);
                        }
                        else if (badCharacter != null && enemyAI.SelectedAiTarget != badCharacter.AiTarget)
                        {
                            enemyAI.Reset();
                            enemyAI.State = AIState.Attack;
                            enemyAI.SelectTarget(badCharacter.AiTarget, 100f);
                        }
                        else if (enemyAI.SelectedAiTarget == null)
                        {
                            enemyAI.State = AIState.Idle;
                        }
                    }

                    character.ForceDisableTargetingTimer = 1f;
                }
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();

            ResourceGenerator.IsActive = true;
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            refreshTimer -= deltaTime;
            feedTimer -= deltaTime;

            if (item.Submarine is not { Info.IsOutpost: true } || 
                Item.CurrentHull == null || 
                (item.GetComponent<Holdable>() is { } holdable && !holdable.IsAttached))
            {
                ResourceGenerator.IsActive = false;
                return;
            }

            item.CurrentHull.TemporaryAvoidStayingTime = 5f;

            if (refreshTimer <= 0)
            {
                newCharacterList.Clear();
                newCharacterList.AddRange(Character.CharacterList.Where(c => c.CurrentHull == item.CurrentHull && !c.IsDead && !c.Removed));

                if (!newCharacterList.SequenceEqual(charactersInPen))
                {
                    charactersInPen.Clear();
                    charactersInPen.AddRange(newCharacterList);

                    // Refresh immediately for the ranching overwhelmed check
                    RefreshResourceGenerator(CreaturesInPen);

#if CLIENT
                    // Creatures list changed, refresh overview UI in case we should display the ranching overwhelmed alert
                    OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
#endif
                }

                HashSet<Hull> connectedHulls = Item.CurrentHull.GetConnectedHulls(true, 0, true).ToHashSet();

                fences.Clear();
                fences.AddRange(Item.Submarine.GetItems(true)
                    .Where(fence => fence.HasTag("ranchingfence".ToIdentifier()) && (connectedHulls.Contains(fence.CurrentHull))));

                refreshTimer = refreshInterval;
            }

            ResourceGenerator.IsActive = true;

            if (GameMain.NetworkMember is not { IsClient: true })
            {
                // Check for starving creatures after a world step has passed
                if (GameMain.GameSession?.Campaign?.Map is Map map && map.WorldStepCount > lastSavedWorldStep)
                {
                    CheckCreaturesAfterWorldStepChanges(map.WorldStepCount - lastSavedWorldStep);
                    lastSavedWorldStep = map.WorldStepCount;
                }

                FeedCreatures();

                UpdateCreatures(deltaTime);
            }
        }

        public bool RequiresRanching()
        {
            if (!CreaturesInPen.Any())
            {
                return false;
            }

            if (item.OwnInventory.IsEmpty())
            {
                return true;
            }

            if (CreaturesInPen.Where(c => c.IsPet)
                .Any(c => 
                    c.AIController is EnemyAIController enemyController && 
                    enemyController.PetBehavior != null && 
                    enemyController.PetBehavior.Happiness < enemyController.PetBehavior.UnhappyThreshold))
            {
                return true;
            }

            return false;
        }

        public static bool DoesRanchingFoodConsumptionExceedProduction()
        {
            if (Level.Loaded?.StartOutpost is not Submarine outpost)
            {
                return false;
            }

            // Find all the resources that creature resource generators are currently consuming (negative amount)
            Dictionary<Identifier, float> consumption = [];

            foreach (CreatureResourceGeneratorComponent generator in outpost.GetItems(alsoFromConnectedSubs: true)
                .Select(item => item.GetComponent<CreatureResourceGeneratorComponent>())
                .NotNull()
                .Where(gen => gen.IsActive))
            {
                foreach (ProducedResource resource in generator.ResourceGenerator.ProducedResourcesList)
                {
                    if (resource.Amount < 0)
                    {
                        consumption.TryGetValue(resource.Identifier, out float amount);
                        consumption[resource.Identifier] = amount - resource.Amount;
                    }
                }
            }

            ResourceManager? resourceManager = null;

            if (Level.Loaded?.StartLocation is Location location)
            {
                resourceManager = PersistentOutpostData.GetPersistentOutpostData(location).ResourceManager;
            }

            // Find all resources that workstations are currently producing
            Dictionary<Identifier, float> production = [];

            foreach (WorkstationComponent workstation in WorkstationComponent.WorkstationComponents.Where(w =>
                w.Item.Submarine == outpost &&
                w.Item.Prefab.Identifier == "ranchersworkstation" &&
                w.ResourceGenerator.IsActive))
            {
                foreach (ProducedResource resource in workstation.ResourceGenerator?.ProducedResourcesList ?? [])
                {
                    production.TryGetValue(resource.Identifier, out float amount);

                    production[resource.Identifier] = amount + resource.GetScaledAmount(workstation?.ResourceGenerator?.AssignedSkillLevel ?? 0, resourceManager);
                }
            }

            // Check if any of the resource consumptions are greater than production
            return consumption.Any(pair => pair.Value > production.GetValueOrDefault(pair.Key));
        }
    }
}