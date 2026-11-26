using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum EncounterType
    {
        /// <summary>
        /// The submarine patrols between randomly chosen points in the level.
        /// </summary>
        Patrol,
        /// <summary>
        /// The submarine travels from one end of the level to another. 
        /// If the mission has a time limit, the spawn position depends on the progress of the mission (the closer the mission is to reaching the time limit, the closer to the destination the sub spawns).
        /// </summary>
        Transport,
        /// <summary>
        /// The submarine spawns near a cave and maintains position there.
        /// </summary>
        Cave,
        /// <summary>
        /// The submarine spawns near a wreck and maintains position there.
        /// </summary>
        Wreck
    }

    [TypePreviouslyKnownAs("PirateMission")]
    public partial class SubmarineEncounterMission : Mission
    {
        private enum SonarBehavior
        {
            /// <summary>
            /// Hides the sonar marker of the mission submarine
            /// </summary>
            Hide,
            /// <summary>
            /// Shows an approximate position of the mission submarine 
            /// (either the patrol positions of the submarine, the position where it was last spotted at, or the exact position if it's within sonar range).
            /// </summary>
            Approximate,
            /// <summary>
            /// Shows the exact position of the mission submarine.
            /// </summary>
            Accurate
        }

        public EncounterType EncounterType;
        /// <summary>
        /// The condition that must be met for the mission to succeed.
        /// </summary>
        public MissionResultCondition WinCondition;
        public CharacterTeamType Team;

        private SonarBehavior sonarMarkerBehavior;

        /// <summary>
        /// Similar to <see cref="MissionPrefab.DisallowMissionsOfSameType"/>, but this doesn't prevent the mission from unlocking in the same level, 
        /// only from spawning the submarine if a mission of the same type already spawned one (= i.e. can be used to make sure there's only one extra sub in the level).
        /// </summary>
        public readonly bool DisallowSubSpawnWithMissionsOfSameType;

        /// <summary>
        /// Makes the submarine start hidden until an event uses <see cref="UnhideSubmarineEncounterAction"/>
        /// </summary>
        public readonly bool ShouldSubmarineStartHidden;

        private bool subCurrentlyHidden;

        private bool subSpawnDisallowed;

        const int InitialState = 0;
        const int SubmarineFoundState = 1;
        const int CompletedState = 2;

        public SubmarineInfo MissionSubInfo { get; private set;}
        public Submarine MissionSub { get; private set; }

        private readonly List<ItemContainer> cargoContainersInSub = new List<ItemContainer>();

        private LevelData levelData;
        
        private readonly ContentXElement submarineTypeConfig;
        private readonly ContentXElement characterTypeConfig;
        private readonly ContentXElement submarineWallModifierConfig;

        private bool shouldFlipSub = true;

        private readonly float addedMissionDifficultyPerPlayer;

        private bool itemsSpawned;
        private readonly ContentXElement itemConfig;
        /// <summary>
        /// List of items that should infinitely respawn
        /// </summary>
        private readonly List<(Item item, ItemContainer container)> itemSpawnsToRepeat = new List<(Item item, ItemContainer container)>();

        private float missionDifficulty;

        private Identifier factionIdentifier;

        public Identifier Faction => factionIdentifier;
        
        // Update the last sighting periodically so that the players can find the target sub even if they have lost the track of it.
        private const float SubmarineSightingUpdateFrequency = 30;
        private float submarineSightingUpdateTimer;
        private Vector2? lastSighting;

        private Vector2 spawnPos;
        
        // these values could also be defined within the mission XML
        private const float RandomnessModifier = 25;
        private const float ShipRandomnessModifier = 15;
        private const float MaxDifficulty = 100;

        /// <summary>
        /// When set, the difficulty of the mission (= the selected sub and enemies) is based on the difficulty of the furthest level the player has explored.
        /// </summary>
        private readonly bool difficultyBasedOnFurthestExploredLevel;

        private readonly float minForcedDifficulty, maxForcedDifficulty;

        /// <summary>
        /// Delay before the submarine crew starts acting (more specifically, delay before ShipCommandManager is enabled and starts giving tasks for the crew).
        /// </summary>
        private readonly float startDelay;

        private float startTimer;

        /// <summary>
        /// Is the ShipCommandManager waiting to be enabled (due to <see cref="startDelay"/>, or clients still loading the round in MP)?
        /// </summary>
        private bool waitingToStart;

        /// <summary>
        /// Max time to wait for all clients to finish loading the round in MP before the crew starts acting.
        /// </summary>
        private const float MaxWaitForClientsToLoad = 30.0f;
        private float waitForClientsTimer;

        private bool inSonarRange;

        /// <summary>
        /// If enabled, the crew will turn hostile if you attempt to board their sub.
        /// </summary>
        private readonly bool denyBoarding;

        /// <summary>
        /// If enabled, only the NPC crew's ID cards work inside the submarine.
        /// </summary>
        private readonly bool submarineSpecificIdCards;

        private float boardingDuration;
        private BoardingState currentBoardingState = BoardingState.None;
        private enum BoardingState
        {
            None,
            FirstWarningGiven,
            SecondWarningGiven,
            TurnHostile
        }

        /// <summary>
        /// Reward defined in the enemy submarine file. Used if the reward is not defined in the XML of the mission.
        /// </summary>
        private int submarineBasedReward;

        private ShipCommandManager shipCommandManager;

        /// <summary>
        /// Names of layers that should be enabled in the mission submarine. Can be used to create e.g. non-faction-specific layers for pirate missions.
        /// </summary>
        public ImmutableHashSet<Identifier> EnableLayers { get; private set; }

        public override float GetBaseReward(Submarine sub) => Prefab.RewardDefinedInXML ? Prefab.Reward : submarineBasedReward;

        private readonly List<Vector2> patrolPositions = new List<Vector2>();

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (inSonarRange || sonarMarkerBehavior == SonarBehavior.Hide || MissionSub == null) { yield break; }
                if (sonarMarkerBehavior == SonarBehavior.Accurate || 
                    //on transport missions, there's no patrol positions to show (the sub travels from start to end)
                    //so let's just show the position of the sub
                    EncounterType == EncounterType.Transport)
                {
                    yield return (Prefab.SonarLabel, MissionSub.WorldPosition);
                    yield break;
                }
                switch (State)
                {
                    case InitialState:
                        // Display the (known/expected ?) patrol positions
                        foreach (Vector2 patrolPos in patrolPositions)
                        {
                            yield return (Prefab.SonarLabel, patrolPos);
                        }
                        break;
                    case SubmarineFoundState:
                        // Spotted -> display the last sighting
                        if (lastSighting.HasValue)
                        {
                            yield return (Prefab.SonarLabel, lastSighting.Value);
                        }
                        break;
                }
            }
        }

        public override bool DisallowExploreOutsideOutpost => State < CompletedState && MissionSub != null;

        public SubmarineEncounterMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            EncounterType = prefab.ConfigElement.GetAttributeEnum(nameof(EncounterType), Enum.TryParse(Prefab.Type.Value, ignoreCase: true, out EncounterType type) ? type : EncounterType.Patrol);
            WinCondition = prefab.ConfigElement.GetAttributeEnum(nameof(WinCondition), MissionResultCondition.CrewEliminated);
            Team = prefab.ConfigElement.GetAttributeEnum(nameof(Team), CharacterTeamType.None);
            submarineTypeConfig = prefab.ConfigElement.GetChildElement("SubmarineTypes");
            characterTypeConfig = prefab.ConfigElement.GetChildElement("CharacterTypes");
            itemConfig = prefab.ConfigElement.GetChildElement("Items");
            submarineWallModifierConfig = prefab.ConfigElement.GetChildElement("SubmarineWallModifier");
            addedMissionDifficultyPerPlayer = prefab.ConfigElement.GetAttributeFloat("addedmissiondifficultyperplayer", 0);
            factionIdentifier = prefab.ConfigElement.GetAttributeIdentifier("faction", Identifier.Empty);
            DisallowSubSpawnWithMissionsOfSameType = prefab.ConfigElement.GetAttributeBool(nameof(DisallowSubSpawnWithMissionsOfSameType), true);
            ShouldSubmarineStartHidden = prefab.ConfigElement.GetAttributeBool(nameof(ShouldSubmarineStartHidden), false);
            difficultyBasedOnFurthestExploredLevel = prefab.ConfigElement.GetAttributeBool(nameof(difficultyBasedOnFurthestExploredLevel), false);
            denyBoarding = prefab.ConfigElement.GetAttributeBool(nameof(denyBoarding), true);
            submarineSpecificIdCards = prefab.ConfigElement.GetAttributeBool(nameof(submarineSpecificIdCards), false);
            minForcedDifficulty = prefab.ConfigElement.GetAttributeFloat(nameof(minForcedDifficulty), 0.0f);
            maxForcedDifficulty = prefab.ConfigElement.GetAttributeFloat(nameof(maxForcedDifficulty), 100.0f);
            sonarMarkerBehavior = prefab.ConfigElement.GetAttributeEnum(nameof(sonarMarkerBehavior), SonarBehavior.Approximate);

            startDelay = prefab.ConfigElement.GetAttributeFloat(nameof(startDelay), 0.0f);

            EnableLayers = prefab.ConfigElement.GetAttributeIdentifierImmutableHashSet(nameof(EnableLayers), ImmutableHashSet<Identifier>.Empty);

            if (characterConfig == null)
            {
                DebugConsole.ThrowError($"Failed to initialize characters for Submarine encounter mission {Prefab.Identifier}. Character config not found.",
                    contentPackage: Prefab.ContentPackage);
            }
            else
            {
                //make sure all referenced character types are defined
                foreach (XElement characterElement in characterConfig.Elements())
                {
                    Identifier typeId = characterElement.GetAttributeIdentifier("typeidentifier", Identifier.Empty);
                    if (typeId.IsEmpty)
                    {
                        if (characterElement.GetAttributeIdentifier("identifier", Identifier.Empty).IsEmpty)
                        {
                            DebugConsole.ThrowError($"Error in mission \"{prefab.Identifier}\". Character element with neither a typeidentifier or identifier ({characterElement.ToString()}).",
                                contentPackage: Prefab.ContentPackage);
                        }
                        continue;
                    }
                    var characterTypeElement = characterTypeConfig?.Elements().FirstOrDefault(e =>
                        e.GetAttributeIdentifier("typeidentifier", Identifier.Empty) == typeId);
                    if (characterTypeElement == null)
                    {
                        DebugConsole.ThrowError($"Error in mission \"{prefab.Identifier}\". Could not find a character type element for the character \"{typeId}\".",
                            contentPackage: Prefab.ContentPackage);
                    }
                }
            }
            
            
            //make sure all defined character types can be found from human prefabs
            if (characterTypeConfig != null)
            {
                foreach (XElement characterTypeElement in characterTypeConfig.Elements())
                {
                    foreach (XElement characterElement in characterTypeElement.Elements())
                    {
                        Identifier characterIdentifier = characterElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        Identifier characterFrom = characterElement.GetAttributeIdentifier("from", Identifier.Empty);
                        HumanPrefab humanPrefab = NPCSet.Get(characterFrom, characterIdentifier, contentPackageToLogInError: Prefab.ContentPackage);
                        if (humanPrefab == null)
                        {
                            DebugConsole.ThrowError($"Error in mission \"{prefab.Identifier}\". Character prefab \"{characterIdentifier}\" not found in the NPC set \"{characterFrom}\".",
                                contentPackage: Prefab.ContentPackage);
                        }
                    }
                }
            }
        }

        private static float GetDifficultyModifiedValue(float preferredDifficulty, float levelDifficulty, float randomnessModifier, Random rand)
        {
            return Math.Abs(levelDifficulty - preferredDifficulty + MathHelper.Lerp(-randomnessModifier, randomnessModifier, (float)rand.NextDouble()));
        }
        private static int GetDifficultyModifiedAmount(int minAmount, int maxAmount, float levelDifficulty, Random rand)
        {
            return Math.Max((int)Math.Round(minAmount + (maxAmount - minAmount) * (levelDifficulty + MathHelper.Lerp(-RandomnessModifier, RandomnessModifier, (float)rand.NextDouble())) / MaxDifficulty), minAmount);
        }
        
        private SubmarineInfo LoadSubmarineInfo()
        {
            SubmarineInfo submarineInfo;
            if (submarineTypeConfig == null)
            {
                //no specific sub configured, choose a random one (this is the preferred default)
                submarineInfo = GetRandomDifficultyModifiedSubmarine(missionDifficulty, ShipRandomnessModifier);
                submarineBasedReward = (int)submarineInfo.AISubmarineInfo.Reward;
            }
            else
            {
                XElement submarineConfig = GetRandomDifficultyModifiedElement(submarineTypeConfig, missionDifficulty, ShipRandomnessModifier);
                submarineBasedReward = submarineConfig.GetAttributeInt("alternatereward", Reward);
                factionIdentifier = submarineConfig.GetAttributeIdentifier("faction", factionIdentifier);
                ContentPath submarinePath = submarineConfig.GetAttributeContentPath("path", Prefab.ContentPackage);
                if (submarinePath.IsNullOrEmpty())
                {
                    DebugConsole.ThrowError($"No path used for submarine for the submarine encounter mission \"{Prefab.Identifier}\"!",
                        contentPackage: Prefab.ContentPackage);
                    return null;
                }
                BaseSubFile contentFile = GetSubFile<NonPlayerSubmarineFile>() ?? GetSubFile<SubmarineFile>();
                BaseSubFile GetSubFile<T>() where T : BaseSubFile
                {
                    return ContentPackageManager.EnabledPackages.All.SelectMany(p => p.GetFiles<T>()).FirstOrDefault(f => f.Path == submarinePath);
                }
                if (contentFile == null)
                {
                    DebugConsole.ThrowError($"No submarine file found from the path {submarinePath}!",
                        contentPackage: Prefab.ContentPackage);
                    return null;
                }
                submarineInfo = new SubmarineInfo(contentFile.Path.Value);
            }
            return submarineInfo;
            
            SubmarineInfo GetRandomDifficultyModifiedSubmarine(float levelDifficulty, float randomnessModifier)
            {
                Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
                // look for the saved submarine that is closest to our difficulty, with some randomness
                SubmarineInfo bestSubmarine = null;
                float bestValue = float.MaxValue;
                var submarineInfos = SubmarineInfo.SavedSubmarines.Where(i => i.IsAIControlled);
                foreach (SubmarineInfo subInfo in submarineInfos)
                {
                    if (subInfo.AISubmarineInfo.Commonness <= 0.0f) { continue; }
                    if (!Prefab.Tags.Any(subInfo.AISubmarineInfo.MissionTags.Contains)) { continue; }
                    float applicabilityValue = 
                        GetDifficultyModifiedValue(subInfo.AISubmarineInfo.PreferredDifficulty, levelDifficulty, randomnessModifier, rand)
                        / Math.Max( 0, subInfo.AISubmarineInfo.Commonness);
                    if (applicabilityValue < bestValue)
                    {
                        bestSubmarine = subInfo;
                        bestValue = applicabilityValue;
                    }
                }
                if (bestSubmarine == null)
                {
                    DebugConsole.ThrowError($"No {nameof(SubmarineType.AIControlled)} submarine found that matches the mission's tags!");
                    return submarineInfos.First();
                }
                return bestSubmarine;
            }
        }

        private XElement GetRandomDifficultyModifiedElement(XElement parentElement, float levelDifficulty, float randomnessModifier)
        {
            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
            // look for the element that is closest to our difficulty, with some randomness
            XElement bestElement = null;
            float bestValue = float.MaxValue;
            foreach (XElement element in parentElement.Elements())
            {
                float commonness = element.GetAttributeFloat("commonness", 100f);
                if (commonness <= 0.0f) { continue; }

                float applicabilityValue = 
                    GetDifficultyModifiedValue(element.GetAttributeFloat(0f, "preferreddifficulty"), levelDifficulty, randomnessModifier, rand)
                        / Math.Max(0, commonness);
                if (applicabilityValue < bestValue)
                {
                    bestElement = element;
                    bestValue = applicabilityValue;
                }
            }
            return bestElement;
        }

        public void UnhideSubmarine()
        {
            if (MissionSub == null) { return; }

            MissionSub.SetPosition(spawnPos);
            State = SubmarineFoundState;

            subCurrentlyHidden = false;
        }

        private void CreateMissionPositions(out Vector2 preferredSpawnPos)
        {
            Point subSize = MissionSub.GetDockedBorders().Size;
            preferredSpawnPos = Level.Loaded.EndPosition;
            float minDistFromSubs = Math.Max(Level.Loaded.Size.X * 0.3f, subSize.X);
            //50 meters, an educated guess for a distance at which the sub won't block the player's way to the outpost
            float minDistFromOutposts = 5000.0f;
            int spawnMargin = 0;

            var spawnPointType = GetSpawnPointType();
            if (spawnPointType is Level.PositionType.Cave or Level.PositionType.AbyssCave && 
                Level.Loaded.Caves.Count > 0)
            {
                //choose a random cave and take it's start position
                Vector2 caveStartPos = Level.Loaded.Caves
                    .Where(c => c.IsAbyssCave == (spawnPointType == Level.PositionType.AbyssCave))
                    .GetRandomUnsynced()
                    .StartPos.ToVector2();
                //find the closest main/side path position to the start of the cave
                preferredSpawnPos = Level.Loaded.PositionsOfInterest
                    .Where(p => p.PositionType is Level.PositionType.MainPath or Level.PositionType.SidePath)
                    .MinBy(p => Vector2.DistanceSquared(caveStartPos, p.Position.ToVector2())).Position.ToVector2();
            }
            else if (Level.Loaded.TryGetInterestingPosition(useSyncedRand: true, spawnPointType, minDistFromSubs, out var potentialSpawnPos))
            {
                preferredSpawnPos = potentialSpawnPos.Position.ToVector2();
            }
            else
            {
                DebugConsole.AddWarning($"{nameof(SubmarineEncounterMission)}: Could not find a preferred spawn position of the type {spawnPointType} for the mission submarine.", contentPackage: Prefab.ContentPackage);
            }
            switch (EncounterType)
            {
                case EncounterType.Patrol:
                    spawnMargin = 500;
                    Vector2 patrolPos = GetMainPathPosition(Level.Loaded.EndPosition, minDistFromOutposts);
                    if (Level.Loaded.TryGetInterestingPositionAwayFromPoint(useSyncedRand: true, Level.PositionType.MainPath, minDistFromSubs, out var potentialPatrolPos, preferredSpawnPos, 
                            minDistFromPoint: Math.Min(level.Size.X * 0.25f, 10000f)))
                    {
                        patrolPos = potentialPatrolPos.Position.ToVector2();
                    }
                    else
                    {
                        DebugConsole.AddWarning($"{nameof(SubmarineEncounterMission)}: Could not find a patrol position for the mission submarine.", contentPackage: Prefab.ContentPackage);
                    }
                    patrolPos = MissionSub.FindSpawnPos(patrolPos, subSize);
                    patrolPositions.Add(patrolPos);
                    patrolPositions.Add(preferredSpawnPos);   
                    if (!IsClient)
                    {
                        PathFinder pathFinder = new PathFinder(WayPoint.WayPointList, false);
                        var path = pathFinder.FindPath(ConvertUnits.ToSimUnits(patrolPos), ConvertUnits.ToSimUnits(preferredSpawnPos));
                        if (!path.Unreachable)
                        {
                            var validNodes = path.Nodes.FindAll(n => !Level.Loaded.ExtraWalls.Any(w => w.Cells.Any(c => c.IsPointInside(n.WorldPosition))));
                            if (validNodes.Any())
                            {
                                preferredSpawnPos = validNodes.GetRandomUnsynced().WorldPosition; // spawn the sub in a random point in the path if possible
                            }
                        }
                    }
                    break;
                case EncounterType.Transport:
                    spawnMargin = 500;
                    if (!Level.IsLoadedOutpost)
                    {
                        //keep the sub outside sonar range from the start of the level (= where the player sub spawns)
                        float minDistFromLevelStart = Sonar.DefaultSonarRange * 1.2f;
                        Vector2 levelStartPos = Level.Loaded.StartPosition;
                        bool IsFarEnoughFromLevelStart(Vector2 pos) => Vector2.DistanceSquared(pos, levelStartPos) >= minDistFromLevelStart * minDistFromLevelStart;

                        Vector2 startPos = GetMainPathPosition(level.StartPosition, minDistFromOutposts);
                        Vector2 endPos = GetMainPathPosition(level.EndPosition, minDistFromOutposts);
                        //if the mission goes from the end location to start, flip the positions
                        //(sub should travel from the end to start)
                        if (Locations[0] == level.EndLocation)
                        {
                            (startPos, endPos) = (endPos, startPos);
                        }
                        else
                        {
                            //sub is travelling from start to end (same way as the player sub), don't flip it
                            shouldFlipSub = false;
                        }
                        bool closeToDestination =
                            Prefab.TimeLimitWorldSteps == -1 || WorldStepsSinceUnlocked / (float)Prefab.TimeLimitWorldSteps >= 0.5f;
                        var spawnPos = closeToDestination ? endPos : startPos;
                        if (!IsFarEnoughFromLevelStart(spawnPos))
                        {
                            //too close to the start of the level, move further along the main path if possible
                            Vector2 farEnoughPos = GetMainPathPosition(levelStartPos, minDistFromLevelStart);
                            if (IsFarEnoughFromLevelStart(farEnoughPos))
                            {
                                spawnPos = farEnoughPos;
                            }
                        }
                        preferredSpawnPos = MissionSub.FindSpawnPos(spawnPos, subSize);
                        patrolPositions.Add(endPos);
                    }
                    break;
                case EncounterType.Cave:
                    patrolPositions.Add(preferredSpawnPos);
                    break;
                case EncounterType.Wreck:
                    // TODO: allow to choose to spawn thalamus to make the sub trapped.
                    patrolPositions.Add(preferredSpawnPos);
                    break;
            }

            //returns the closest position on the main path that's at least minDistance away from the desired position
            Vector2 GetMainPathPosition(Vector2 desiredPosition, float minDistance)
            {
                Vector2 closestPosition = desiredPosition;
                float closestDistance = float.MaxValue;
                foreach (var positionOfInterest in Level.Loaded.PositionsOfInterest)
                {
                    if (positionOfInterest.PositionType != Level.PositionType.MainPath) { continue; }
                    float distSqr = Vector2.DistanceSquared(desiredPosition, positionOfInterest.Position.ToVector2());
                    if (distSqr > minDistance * minDistance && distSqr < closestDistance)
                    {
                        closestPosition = positionOfInterest.Position.ToVector2();
                        closestDistance = distSqr;
                    }
                }
                return closestPosition;
            }

            preferredSpawnPos = MissionSub.FindSpawnPos(preferredSpawnPos, new Point(subSize.X + spawnMargin, subSize.Y + spawnMargin), verticalMoveDir: -1);
            
            Level.PositionType GetSpawnPointType() => EncounterType switch
            {
                EncounterType.Cave => Level.PositionType.Cave,
                EncounterType.Wreck => Level.PositionType.Wreck,
                EncounterType.Patrol => Level.PositionType.MainPath,
                EncounterType.Transport => Level.PositionType.MainPath,
                _ => Level.PositionType.MainPath
            };
        }

        private void InitSubmarine()
        {
            cargoContainersInSub.Clear();
            itemSpawnsToRepeat.Clear();
            shouldFlipSub = true;

            if (subSpawnDisallowed) { return; }
            if (MissionSub == null)
            {
                DebugConsole.ThrowError(MissionSub == null ? 
                        $"Error in {nameof(SubmarineEncounterMission)}: sub was not created (MissionSub == null)." :
                        $"Error in {nameof(SubmarineEncounterMission)}: sub was not created.", 
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            CreateMissionPositions(out spawnPos); // patrol positions are not explicitly replicated, instead they are acquired the same way the server acquires them
#if DEBUG
            DebugConsole.NewMessage(IsClient ? "The patrol positions set by client were: " : "The patrol positions set by server were: ");
            foreach (var patrolPos in patrolPositions)
            {
                DebugConsole.NewMessage("Patrol pos: " + patrolPos);
            }
#endif
            MissionSub.SetPosition(spawnPos);
            // flipping the sub on the frame it is moved into place must be done after it's been moved, or it breaks item connections in the submarine
            // creating the crew has to be done after the sub has been flipped, or it seems to break the AI pathing
            if (shouldFlipSub)
            {
                MissionSub.FlipX();
            }
            MissionSub.ShowSonarMarker = false;
            MissionSub.NeutralizeBallast();
            if (MissionSub.GetItems(alsoFromConnectedSubs: false).Find(i => i.HasTag(Tags.Reactor) && !i.NonInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                reactor.PowerUpImmediately();
            }
            MissionSub.EnableMaintainPosition();
            MissionSub.TeamID = Team;
            //make the mission sub withstand atleast the same depth as the player sub
            MissionSub.SetCrushDepth(Math.Max(MissionSub.RealWorldCrushDepth, Submarine.MainSub.RealWorldCrushDepth));
            if (Level.Loaded != null)
            {
                //...and the depth of the patrol positions + 1000 m
                foreach (var patrolPos in patrolPositions)
                {
                    MissionSub.SetCrushDepth(Math.Max(MissionSub.RealWorldCrushDepth, Level.Loaded.GetRealWorldDepth(patrolPos.Y) + 1000));
                }
            }
            MissionSub.ImmuneToBallastFlora = true;
            //done on both the server and clients (no need to sync), the id cards' SubmarineSpecificIDs are synced when the crew is spawned
            MissionSub.RequireSubmarineSpecificIdCards = submarineSpecificIdCards;
            MissionSub.Faction = factionIdentifier;
            MissionSub.EnableFactionSpecificEntities(factionIdentifier);

            if (EnableLayers != null)
            {
                foreach (Identifier layer in EnableLayers)
                {
                    MissionSub.SetLayerEnabled(layer, enabled: true);
                }
            }

            cargoContainersInSub.AddRange(MissionSub.GetCargoContainers().Select(static tuple => tuple.container));
        }

        private void InitCrew()
        {
            characters.Clear();
            characterItems.Clear();

            if (characterConfig == null)
            {
                DebugConsole.ThrowError($"Failed to initialize characters for Submarine encounter mission {Prefab.Identifier}. Character config not found.", 
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            if (MissionSub == null) { return; }

            int playerCount = 1;

#if SERVER
            playerCount = GameMain.Server.GetPlayingClients().Count;
#endif

            float enemyCreationDifficulty = missionDifficulty + playerCount * addedMissionDifficultyPerPlayer;

            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));

            Character desiredCommander = null;
            foreach (ContentXElement element in characterConfig.Elements())
            {
                //there's two ways to define the characters in submarine encounter missions
                //1. "the normal way", referring to a human prefab
                Identifier humanPrefabId = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                //2. the strange way it was initially implemented and the way the vanilla missions work: using a reference to a "character type" in the mission, which refers to a human prefab
                Identifier characterTypeId = element.GetAttributeIdentifier("typeidentifier", Identifier.Empty);

                int minAmount = element.GetAttributeInt("minamount", 0);
                int maxAmount = element.GetAttributeInt("maxamount", 0);
                // it is possible to get more than the "max" amount of characters if the modified difficulty is high enough; this is intentional
                // if necessary, another "hard max" value could be used to clamp the value for performance/gameplay concerns
                int amountCreated = minAmount == 0 && maxAmount == 0 ? 
                    //default to 1 character if amount is not defined
                    1 :
                    //otherwise choose a value between min and max based on difficulty
                    GetDifficultyModifiedAmount(minAmount, maxAmount, enemyCreationDifficulty, rand);
                
                for (int i = 0; i < amountCreated; i++)
                {
                    HumanPrefab humanPrefab = null;
                    bool isCommander = false;
                    if (!characterTypeId.IsEmpty)
                    {
                        XElement characterType = characterTypeConfig.Elements().FirstOrDefault(e => e.GetAttributeIdentifier("typeidentifier", Identifier.Empty) == characterTypeId);
                        if (characterType == null)
                        {
                            DebugConsole.ThrowError($"No character types defined in CharacterTypes for a declared type identifier in mission \"{Prefab.Identifier}\".",
                                contentPackage: element.ContentPackage);
                            return;
                        }
                        XElement variantElement = GetRandomDifficultyModifiedElement(characterType, enemyCreationDifficulty, RandomnessModifier);
                        humanPrefab = GetHumanPrefabFromElement(variantElement);
                        isCommander = variantElement.GetAttributeBool("iscommander", false);
                    }
                    else if (!humanPrefabId.IsEmpty)
                    {
                        humanPrefab = GetHumanPrefabFromElement(element);
                        isCommander = element.GetAttributeBool("iscommander", false);
                    }

                    if (humanPrefab == null) { continue; }
                    
                    Character spawnedCharacter = CreateHuman(humanPrefab, characters, characterItems, MissionSub, Team);
                    if (element.GetAttribute("color") != null)
                    {
                        spawnedCharacter.UniqueNameColor = element.GetAttributeColor("color", Color.Red);
                    }
                    if (spawnedCharacter.Faction.IsEmpty)
                    {
                        spawnedCharacter.Faction = factionIdentifier;
                    }
                    if (isCommander)
                    {
                        desiredCommander = spawnedCharacter;
                    }
                    foreach (var subElement in element.Elements())
                    {
                        if (subElement.NameAsIdentifier() == "statuseffect")
                        {
                            var newEffect = StatusEffect.Load(subElement, parentDebugName: Prefab.Name.Value);
                            newEffect?.Apply(newEffect.Type, 1.0f, spawnedCharacter, spawnedCharacter);                            
                        }
                    }

                    foreach (Item item in spawnedCharacter.Inventory.AllItems)
                    {
                        if (item?.GetComponent<IdCard>() is IdCard idCard)
                        {
                            item.AddTag("id_pirate"); // Needed for backwards support
                            item.AddTag("id_aicrew");
                            if (submarineSpecificIdCards)
                            {
                                idCard.SubmarineSpecificID = MissionSub.SubmarineSpecificIDTag;
                                //the card works as a key to the mission sub, but shouldn't prevent it from working elsewhere (e.g. when boarding the player sub)
                                idCard.RestrictToSpecificSubmarine = false;
                            }
                        }
                    }
                }
            }

            bool commanderAssigned = false;
            if (desiredCommander != null)
            {
                if (desiredCommander.AIController is HumanAIController humanAIController)
                {
                    commanderAssigned = true;
                    InitShipCommandManager(humanAIController);
                }
            }

            if (!commanderAssigned)
            {
                DebugConsole.AddWarning(
                    $"No commander assigned for the crew of the mission {Prefab.Identifier}. The crew will not be able to operate the sub properly." +
                    " Make sure one of the crew members has iscommander=\"true\" in the character config.",
                    contentPackage: Prefab.ContentPackage);
            }
        }

        private void InitShipCommandManager(HumanAIController commander)
        {
            List<Submarine> potentialEnemySubs = Submarine.MainSub.GetConnectedSubs().ToList();
            if (level is { Type: LevelData.LevelType.Outpost, StartOutpost: not null })
            {
                potentialEnemySubs.Add(level.StartOutpost);
            }
            shipCommandManager = commander.InitShipCommandManager(potentialEnemySubs, characters);
            shipCommandManager.AllowPatrolling = EncounterType is EncounterType.Patrol or EncounterType.Transport;
            foreach (var patrolPos in patrolPositions)
            {
                shipCommandManager.PatrolPositions.Add(patrolPos);
            }
            switch (EncounterType)
            {
                case EncounterType.Transport:
                    //require the sub to be somewhat close (by default the sub doesn't need to be very close to the patrol position)
                    shipCommandManager.CloseEnoughToPatrolPosition = 1000.0f;
                    break;
                case EncounterType.Wreck:
                case EncounterType.Cave:
                    shipCommandManager.CloseEnoughToPatrolPosition = 0.0f;
                    break;
            }

            //in MP, also wait for the clients to load in: the server may start the round well before the clients,
            //and we don't want the sub to already be heading towards the players by the time they get in
            if (startDelay > 0.0f || GameMain.NetworkMember is { IsServer: true })
            {
                //deactivate, we activate it once the delay has passed
                shipCommandManager.Enabled = false;
                startTimer = startDelay;
                waitingToStart = true;
            }
        }

        /// <summary>
        /// Have all the connected clients finished loading the round?
        /// </summary>
        private bool HaveClientsLoaded(float deltaTime)
        {
#if SERVER
            if (GameMain.Server == null) { return true; }
            var clients = GameMain.Server.ConnectedClients;
            //wait for at least one client in any case (same as monster missions)
            if (clients.None(c => c.InGame)) { return false; }
            if (clients.All(c => c.AFK || c.SpectateOnly || c.InGame)) { return true; }

            waitForClientsTimer += deltaTime;
            return waitForClientsTimer >= MaxWaitForClientsToLoad;
#else
            return true;
#endif
        }

        public bool TryLoadSubmarine(LevelData data)
        {
            Debug.Assert(MissionSub == null);

            //disallow spawning the sub if
            subSpawnDisallowed =
                DisallowSubSpawnWithMissionsOfSameType && GameMain.GameSession != null &&
                GameMain.GameSession.GameMode.Missions.Any(m => m != this &&
                    // there's another mission of the same type in the level
                    m is SubmarineEncounterMission otherMission && otherMission.Prefab.Type == Prefab.Type && 
                    //which has either already spawned the sub, or has unlocked earlier than this one (meaning it should take priority)
                    (otherMission.MissionSub != null || otherMission.WorldStepsSinceUnlocked > WorldStepsSinceUnlocked || !otherMission.DisallowSubSpawnWithMissionsOfSameType));

            if (subSpawnDisallowed)
            {
                DebugConsole.NewMessage($"Not spawning the sub for the mission \"{Prefab.Identifier}\" because there's another mission of the same type active in the level.");
                return false;
            }

            levelData = data;
            missionDifficulty = levelData.Difficulty;
            if (difficultyBasedOnFurthestExploredLevel && GameMain.GameSession.Map is { } map)
            {
                missionDifficulty = map.GetHighestVisitedLevelDifficulty();
            }
            missionDifficulty = MathHelper.Clamp(missionDifficulty, minForcedDifficulty, maxForcedDifficulty);

            string rewardText = $"‖color:gui.orange‖{string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", GetBaseReward(Submarine.MainSub))}‖end‖";
            if (descriptionWithoutReward != null) { description = descriptionWithoutReward.Replace("[reward]", rewardText); }
            if (LoadSubmarineInfo() is SubmarineInfo submarineInfo)
            {
                MissionSubInfo = submarineInfo;
                MissionSub = new Submarine(MissionSubInfo);
                Submarine.AnotherMainSub ??= MissionSub;
                
                if (submarineWallModifierConfig != null)
                {
                    float submarineHealthMultiplier = submarineWallModifierConfig.GetAttributeFloat("HealthMultiplier", 1.0f);
                    Debug.WriteLine($"Submarine wall health multiplier set to {submarineHealthMultiplier}");
                    foreach (ContentXElement overrideElement in submarineWallModifierConfig.GetChildElements("Override"))
                    {
                        if (levelData.Difficulty < overrideElement.GetAttributeFloat("MinDifficulty", 0)) { continue; }
                        if (levelData.Difficulty > overrideElement.GetAttributeFloat("MaxDifficulty", float.MaxValue)) { continue; }
                        submarineHealthMultiplier = overrideElement.GetAttributeFloat("HealthMultiplier", submarineHealthMultiplier);
                        Debug.WriteLine($"Overriding submarine wall health multiplier with {submarineHealthMultiplier} (Difficulty level: {levelData.Difficulty}");
                        break;
                    }
                    if (submarineHealthMultiplier != 1.0f)
                    {
                        foreach (Structure wall in Structure.WallList)
                        {
                            if (wall.Submarine != MissionSub || !wall.HasBody || wall.Indestructible) { continue; }
                            wall.MaxHealth *= submarineHealthMultiplier;
                        }
                    }
                }

                // TODO: Would seem logical to set the reference here, but previously it was not done and could cause issues?
                //GameSession.EnemySubInfo = MissionSubInfo;
            }
            return MissionSub != null;
        }

        #region Item spawning

        private void DisallowStealingIfNeeded(Item item)
        {
            // Disallow stealing from submarines with friendly NPCs (e.g. neutral cargo submarine), so the player can't just
            // take all supplies from it without the NPCs reacting
            if (Team == CharacterTeamType.FriendlyNPC)
            {
                item.AllowStealing = false;
                item.SpawnedInNonPlayerOutpostOrSubmarine = true;
            }
        }

        private readonly List<(ContentXElement itemElement, ContentXElement parentElement, Item item)> spawnedItems = [];

        private void LoadItemsRecursive(ContentXElement element, ContentXElement parentElement)
        {
            foreach (var subElement in element.Elements())
            {
                int amount = subElement.GetAttributeInt("amount", 1);
                int minAmount = subElement.GetAttributeInt("minamount", amount);
                int maxAmount = subElement.GetAttributeInt("maxamount", amount);

                int amountToSpawn = Rand.Range(minAmount, maxAmount + 1);
                for (int i = 0; i < amountToSpawn; i++)
                {
                    LoadItem(subElement, element);
                    LoadItemsRecursive(subElement, element);
                }
            }
        }


        private void LoadItem(ContentXElement element, ContentXElement parentElement)
        {
            ItemPrefab itemPrefab = FindItemPrefab(element);
            if (itemPrefab == null)
            {
                DebugConsole.AddWarning($"Could not find an item defined in {nameof(SubmarineEncounterMission)} (XML element: {element})");
                return;
            }

            var requireItemOnSub = element.GetAttributeIdentifierArray("requireitemonsub", Array.Empty<Identifier>());
            if (requireItemOnSub.Any())
            {
                if (MissionSub.GetItems(alsoFromConnectedSubs: true).None(it => requireItemOnSub.Any(tag => it.HasIdentifierOrTag(tag))))
                {
                    return;
                }
            }

            Vector2? position = GetCargoSpawnPosition(itemPrefab, out Submarine cargoRoomSub, targetSub: MissionSub);
            if (!position.HasValue) { return; }

            bool infinite = element.GetAttributeBool("infinite", false);

            Entity.Spawner.AddItemToSpawnQueue(itemPrefab, position.Value, onSpawned: (Item newItem) =>
            {
                newItem.Submarine = MissionSub;
                newItem.FindHull();

                DisallowStealingIfNeeded(newItem);

                Item parentItem = spawnedItems.FirstOrDefault(previouslySpawnedItem => previouslySpawnedItem.itemElement == parentElement).item;
                if (parentItem == null)
                {
                    foreach (var container in cargoContainersInSub)
                    {
                        if (container.Inventory.TryPutItem(newItem, user: null))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    parentItem.OwnInventory.TryPutItem(newItem, user: null);
                }
                spawnedItems.Add((element, parentElement, newItem));
                if (infinite)
                {
                    itemSpawnsToRepeat.Add((newItem, newItem.Container?.GetComponent<ItemContainer>()));
                }
                foreach (var tag in Prefab.Tags)
                {
                    newItem.AddTag(tag);
                }
            });
        }

        #endregion

        protected override void StartMissionSpecific(Level level)
        {
            failed = false;
            if (characters.Count > 0)
            {
#if DEBUG
                throw new Exception($"characters.Count > 0 ({characters.Count})");
#else
                DebugConsole.AddWarning("Character list was not empty at the start of a submarine encounter mission. The mission instance may not have been ended correctly on previous rounds.");
                characters.Clear();            
#endif
            }

            if (patrolPositions.Count > 0)
            {
#if DEBUG
                throw new Exception($"patrolPositions.Count > 0 ({patrolPositions.Count})");
#else
                DebugConsole.AddWarning("Patrol point list was not empty at the start of a submarine encounter mission. The mission instance may not have been ended correctly on previous rounds.");
                patrolPositions.Clear();
#endif
            }

            spawnedItems.Clear();
            itemsSpawned = false;

            InitSubmarine();

            if (!IsClient)
            {
                InitCrew();
            }

            if (!subSpawnDisallowed)
            {
                MissionSub.GetItems(alsoFromConnectedSubs: true).ForEach(DisallowStealingIfNeeded);
            }

            if (level.Type == LevelData.LevelType.Outpost && Level.Loaded.StartOutpost is { } startOutpost)
            {
                if (Team == CharacterTeamType.None)
                {
                    //"none" team attacking an outpost -> consider outpost NPCs hostile
                    foreach (var character in characters)
                    {
                        if (character.AIController is HumanAIController humanAi)
                        {
                            humanAi.TryMarkTeamAsHostile(CharacterTeamType.FriendlyNPC);
                        }
                    }
                }
            }

            if (ShouldSubmarineStartHidden)
            {
                float heightAboveLevel = 1000.0f;
                MissionSub.SetPosition(new Vector2(Level.Loaded.StartPosition.X, Level.Loaded.Size.Y + MissionSub.Borders.Height + heightAboveLevel));
                subCurrentlyHidden = true;
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (MissionSub == null) { return; }

            if (!itemsSpawned && itemConfig != null)
            {
                LoadItemsRecursive(itemConfig, parentElement: null);
                itemsSpawned = true;
            }

            bool failureConditionMet = false;
#if SERVER
            if (FailureCondition == MissionResultCondition.OutpostDestroyed)
            {
                if (CheckMissionResultCondition(FailureCondition, level.StartOutpost ?? Submarine.MainSub,
                    characters: GameSession.GetSessionCrewCharacters(CharacterType.Both), out OutpostDestroyedState outpostDestroyedReason))
                {
                    OutpostDestroyedReason = outpostDestroyedReason;
                    failureConditionMet = true;
                }
            }
#endif
            if (State >= CompletedState || failureConditionMet)
            {
#if SERVER
                endTimer += deltaTime;
                if (endTimer > EndDelay)
                {
                    if (GameMain.GameSession.GameMode is not CampaignMode && GameMain.Server != null)
                    {
                        GameMain.Server.EndGame();
                    }
                }
#endif
                return;
            }

            float sqrSonarRange = MathUtils.Pow2(Sonar.DefaultSonarRange);
            inSonarRange = Vector2.DistanceSquared(MissionSub.WorldPosition, Submarine.MainSub.WorldPosition) < sqrSonarRange;

            if (IsClient) { return; }

            if (waitingToStart && shipCommandManager is { Enabled: false } && HaveClientsLoaded(deltaTime))
            {
                startTimer -= deltaTime;
                if (startTimer <= 0)
                {
                    shipCommandManager.Enabled = true;
                    waitingToStart = false;
                }
            }

            if (CheckMissionResultCondition(WinCondition, MissionSub, characters))
            {
                if (State != CompletedState)
                {
                    State = CompletedState;
                    //make sure the sub doesn't come back to attack if the player successfully incapacitated it
                    if (WinCondition is MissionResultCondition.SubmarineIncapacitated)
                    {
                        shipCommandManager.StopFighting();
                    }
                }
            }
            else
            {
                if (!IsClient)
                {
                    UpdateRepeatedItemSpawns();
                    UpdateBoardingChecks(deltaTime);
                }

                switch (State)
                {
                    case InitialState:
                        for (int i = patrolPositions.Count - 1; i >= 0; i--)
                        {
                            if (Vector2.DistanceSquared(patrolPositions[i], Submarine.MainSub.WorldPosition) < sqrSonarRange)
                            {
                                patrolPositions.RemoveAt(i);
                            }
                        }
                        if (!subCurrentlyHidden && 
                            (inSonarRange || (EncounterType == EncounterType.Patrol && patrolPositions.None())))
                        {
                            // Spotted or no patrol positions left.
                            State = SubmarineFoundState;
                        }
                        break;
                    case SubmarineFoundState:
                        if (inSonarRange)
                        {
                            // Spot (again)
                            lastSighting = MissionSub.WorldPosition;
                            submarineSightingUpdateTimer = 0;
                        }
                        else
                        {
                            if (lastSighting.HasValue && Vector2.DistanceSquared(lastSighting.Value, Submarine.MainSub.WorldPosition) < sqrSonarRange)
                            {
                                // Forget the old sighting
                                lastSighting = null;
                            }
                            submarineSightingUpdateTimer -= deltaTime;
                            if (submarineSightingUpdateTimer < 0)
                            {
                                // Update
                                submarineSightingUpdateTimer = SubmarineSightingUpdateFrequency;
                                lastSighting = MissionSub.WorldPosition;
                            }
                        }
                        break;
                }
            }
        }

        private void UpdateRepeatedItemSpawns()
        {
            for (int i = 0; i < itemSpawnsToRepeat.Count; i++)
            {
                var item = itemSpawnsToRepeat[i].item;
                var container = itemSpawnsToRepeat[i].container;

                if (item.Condition <= 0.0f || item.Removed)
                {
                    Vector2? position = GetCargoSpawnPosition(item.Prefab, out Submarine cargoRoomSub, targetSub: MissionSub);

                    int itemIndex = i;
                    Entity.Spawner.AddItemToSpawnQueue(item.Prefab, position ?? MissionSub.WorldPosition, onSpawned: (Item newItem) =>
                    {
                        newItem.Submarine = MissionSub;
                        newItem.FindHull();
                        ItemContainer containerToUse = container;
                        if (containerToUse == null || containerToUse.Item.Removed)
                        {
                            //if the container has been removed, find a new one
                            foreach (var potentialContainer in cargoContainersInSub.Where(static c => !c.Item.Removed))
                            {
                                if (potentialContainer.Inventory.TryPutItem(newItem, user: null))
                                {
                                    containerToUse = potentialContainer;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            containerToUse.Inventory.TryPutItem(newItem, user: null);
                        }

                        foreach (var tag in Prefab.Tags)
                        {
                            newItem.AddTag(tag);
                        }
                        //replace the previous item with the new one (we won't spawn another one until the new one is gone too)
                        itemSpawnsToRepeat[itemIndex] = (newItem, containerToUse);
                        DebugConsole.NewMessage($"Mission \"{Prefab.Name}\" spawned a new {newItem.Name} in {MissionSub.Info.Name}.");
                    });
                }
            }
        }

        private void UpdateBoardingChecks(float deltaTime)
        {
            if (!denyBoarding) { return; }

            Character playerOnBoard = Character.CharacterList.FirstOrDefault(c => c.IsPlayer && c.Submarine == MissionSub);
            if (playerOnBoard == null) { return; }

            //if the sub is already hostile to the player, no need to give any warnings about the boarding
            if (!playerOnBoard.IsOnFriendlyTeam(Team)) { return; }

            boardingDuration += deltaTime;

            switch (currentBoardingState)
            {
                case BoardingState.None:
                    MakeClosestCharacterSpeak(BoardingState.None, "dialog.boardingwarning.first");
                    currentBoardingState = BoardingState.FirstWarningGiven;
                    break;
                case BoardingState.FirstWarningGiven:
                    if (boardingDuration > 10.0f)
                    {
                        MakeClosestCharacterSpeak(BoardingState.FirstWarningGiven, "dialog.boardingwarning.second");
                        currentBoardingState = BoardingState.SecondWarningGiven;
                    }
                    break;
                case BoardingState.SecondWarningGiven:
                    if (boardingDuration > 20.0f)
                    {
                        MakeClosestCharacterSpeak(BoardingState.FirstWarningGiven, "dialog.boardingwarning.turnhostile");
                        currentBoardingState = BoardingState.TurnHostile;
                        foreach (var character in characters)
                        {
                            if (character.AIController is HumanAIController humanAi)
                            {
                                humanAi.TryMarkTeamAsHostile(CharacterTeamType.Team1);
                            }
                        }
                    }
                    break;
            }

            void MakeClosestCharacterSpeak(BoardingState currentBoardingState, string dialogTag)
            {
                var closestCharacter = characters.Where(c => !c.Removed && !c.IsIncapacitated && c.CanSpeak)
                    .MinBy(c => Vector2.DistanceSquared(c.WorldPosition, playerOnBoard.WorldPosition));
                closestCharacter?.Speak(TextManager.Get(dialogTag).Value);
            }
        }

        #region Reacting to structure/character attacks

        private float totalReputationGainFromAttackingMonster;

        public override void StructureDamaged(Structure structure, float damageAmount, Character attacker)
        {
            if (damageAmount <= 0.0f || attacker == null || structure.Submarine != MissionSub) { return; }
            var speaker = characters.FirstOrDefault(c => !c.Removed && !c.IsIncapacitated && c.CanSpeak);
            if (speaker == null) { return; }

            string dialogTag = string.Empty;
            if (attacker.AIController is EnemyAIController)
            {
                dialogTag = "dialogsubunderattackbymonsters";
            }
            else if (attacker.IsOnPlayerTeam)
            {
                //note that we don't handle reputation loss or reactions to player crew attacking the sub here, that's handled by HumanAIController.RespondToAttack
                dialogTag = "dialogsubunderattackbysub";
            }
            speaker.Speak(TextManager.GetWithVariable(dialogTag, "[submarine]", MissionSub.Info.DisplayName).Value,
                Networking.ChatMessageType.Radio,
                identifier: dialogTag.ToIdentifier(), minDurationBetweenSimilar: 60.0f);
        }

        public override void CharacterAttacked(Character attackedCharacter, Character attacker, AttackResult attackResult)
        {
            if (attacker == null || !attacker.IsOnPlayerTeam) { return; }
            if (attacker.IsDead || attacker.IsIncapacitated) { return; }

            if (MissionSub == null) { return; }

            var faction = GameMain.GameSession?.Campaign?.GetFaction(factionIdentifier);
            if (faction == null) { return; }

            if (totalReputationGainFromAttackingMonster >= Reputation.MaxReputationGainFromDamageToAttackingMonster) { return; }

            //monster targeting the mission sub, give rep for attacking it / helping the mission sub
            if (attackedCharacter.AIController is EnemyAIController enemyAi &&
                enemyAi.TryGetAttackTarget(out Entity attackTarget) && 
                attackTarget.Submarine == MissionSub)
            {
                float reputationGain = attackResult.Damage * Reputation.ReputationGainPerDamageToAttackingMonster;
                totalReputationGainFromAttackingMonster += reputationGain;
                float prevReputation = faction.Reputation.Value;
                faction.Reputation.AddReputation(reputationGain);
                if (faction.Reputation.Value > prevReputation)
                {
#if CLIENT
                    HintManager.OnSubmarineHelpedAgainstAttacker(faction);
#endif
                }
            }
        }

        #endregion

        protected override bool DetermineCompleted(CampaignMode.TransitionType transitionType)
        {
            //never consider completed or failed if the sub didn't spawn
            if (subSpawnDisallowed) { return false; }
            //sub failed to load (e.g. TryLoadSubmarine wasn't called for this mission): don't let the empty crew count as eliminated
            if (MissionSub == null) { return false; }

            if (CheckMissionResultCondition(FailureCondition, MissionSub, characters) || 
                FailureCondition == MissionResultCondition.Always)
            {
                failed = true;
                return false;
            }
            else if (WinCondition == MissionResultCondition.Always)
            {
                return true;
            }
            return State == CompletedState;
        }

        protected override void EndMissionSpecific(bool completed)
        {
            characters.Clear();
            characterItems.Clear();
            spawnedItems.Clear();
            cargoContainersInSub.Clear();
            patrolPositions.Clear();
            MissionSub = null;
            itemSpawnsToRepeat.Clear();
            itemsSpawned = false;
            //missions completed by reaching the time limit are still in progress if they weren't completed during the round (failed is set in DetermineCompleted if the failure condition is met)
            if (!Prefab.AllowRetry && !Prefab.CompleteWhenTimeLimitReached)
            {
                failed = !completed;
            }
#if SERVER
            endTimer = 0.0f;
#endif
        }
    }
}
