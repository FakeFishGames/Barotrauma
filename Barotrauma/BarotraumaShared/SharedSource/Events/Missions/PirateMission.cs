﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class PirateMission : Mission
    {
        private readonly ContentXElement submarineTypeConfig;
        private readonly ContentXElement characterTypeConfig;
        private readonly float addedMissionDifficultyPerPlayer;

        private float missionDifficulty;
        private int alternateReward;

        private Identifier factionIdentifier;

        private Submarine enemySub;

        private readonly Dictionary<HumanPrefab, List<StatusEffect>> characterStatusEffects = new Dictionary<HumanPrefab, List<StatusEffect>>();

        // Update the last sighting periodically so that the players can find the pirate sub even if they have lost the track of it.
        private readonly float pirateSightingUpdateFrequency = 30;
        private float pirateSightingUpdateTimer;
        private Vector2? lastSighting;

        private LevelData levelData;

        public override int TeamCount => 2;

        private bool outsideOfSonarRange;

        private readonly List<Vector2> patrolPositions = new List<Vector2>();

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (!outsideOfSonarRange || state > 1)
                {
                    yield break;

                }
                else if (state == 0)
                {
                    foreach (Vector2 patrolPos in patrolPositions)
                    {
                        yield return (Prefab.SonarLabel, patrolPos);
                    }
                }
                else if (state == 1)
                {
                    if (lastSighting.HasValue)
                    {
                        yield return (Prefab.SonarLabel, lastSighting.Value);
                    }
                    else
                    {
                        yield break;
                    }
                }
            }
        }

        public override float GetBaseReward(Submarine sub)
        {
            return alternateReward;
        }

        private SubmarineInfo submarineInfo;

        public override SubmarineInfo EnemySubmarineInfo
        {
            get
            {
                return submarineInfo;
            }
        }

        // these values could also be defined within the mission XML
        private const float RandomnessModifier = 25;
        private const float ShipRandomnessModifier = 15;

        private const float MaxDifficulty = 100;

        public PirateMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            submarineTypeConfig = prefab.ConfigElement.GetChildElement("SubmarineTypes");
            characterTypeConfig = prefab.ConfigElement.GetChildElement("CharacterTypes");
            addedMissionDifficultyPerPlayer = prefab.ConfigElement.GetAttributeFloat("addedmissiondifficultyperplayer", 0);

            factionIdentifier = prefab.ConfigElement.GetAttributeIdentifier("faction", Identifier.Empty);

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
                var characterTypeElement = characterTypeConfig.Elements().FirstOrDefault(e =>
                    e.GetAttributeIdentifier("typeidentifier", Identifier.Empty) == typeId);
                if (characterTypeElement == null)
                {
                    DebugConsole.ThrowError($"Error in mission \"{prefab.Identifier}\". Could not find a character type element for the character \"{typeId}\".",
                        contentPackage: Prefab.ContentPackage);
                }
            }
            //make sure all defined character types can be found from human prefabs
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

            // for campaign missions, set level at construction
            LevelData levelData = locations[0].Connections.Where(c => c.Locations.Contains(locations[1])).FirstOrDefault()?.LevelData ?? locations[0]?.LevelData;
            if (levelData != null)
            {
                SetLevel(levelData);
            }
        }

        public override void SetLevel(LevelData level)
        {
            if (levelData != null)
            {
                //level already set
                return;
            }
            submarineInfo = null;

            levelData = level;
            missionDifficulty = level?.Difficulty ?? 0;

            //no specific sub configured, choose a random one
            if (submarineTypeConfig == null)
            {
                submarineInfo = GetRandomDifficultyModifiedSubmarine(missionDifficulty, ShipRandomnessModifier);
                alternateReward = (int)submarineInfo.EnemySubmarineInfo.Reward;
            }
            else
            {
                XElement submarineConfig = GetRandomDifficultyModifiedElement(submarineTypeConfig, missionDifficulty, ShipRandomnessModifier);
                alternateReward = submarineConfig.GetAttributeInt("alternatereward", Reward);
                factionIdentifier = submarineConfig.GetAttributeIdentifier("faction", factionIdentifier);

                ContentPath submarinePath = submarineConfig.GetAttributeContentPath("path", Prefab.ContentPackage);
                if (submarinePath.IsNullOrEmpty())
                {
                    DebugConsole.ThrowError($"No path used for submarine for the pirate mission \"{Prefab.Identifier}\"!",
                        contentPackage: Prefab.ContentPackage);
                    return;
                }

                BaseSubFile contentFile = 
                    GetSubFile<EnemySubmarineFile>(submarinePath) ?? 
                    GetSubFile<SubmarineFile>(submarinePath);
                BaseSubFile GetSubFile<T>(ContentPath path) where T : BaseSubFile
                {
                    return ContentPackageManager.EnabledPackages.All.SelectMany(p => p.GetFiles<T>()).FirstOrDefault(f => f.Path == submarinePath);
                }

                if (contentFile == null)
                {
                    DebugConsole.ThrowError($"No submarine file found from the path {submarinePath}!",
                        contentPackage: Prefab.ContentPackage);
                    return;
                }

                submarineInfo = new SubmarineInfo(contentFile.Path.Value);
            }

            string rewardText = $"‖color:gui.orange‖{string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", alternateReward)}‖end‖";
            if (descriptionWithoutReward != null) { description = descriptionWithoutReward.Replace("[reward]", rewardText); }

        }

        private static float GetDifficultyModifiedValue(float preferredDifficulty, float levelDifficulty, float randomnessModifier, Random rand)
        {
            return Math.Abs(levelDifficulty - preferredDifficulty + MathHelper.Lerp(-randomnessModifier, randomnessModifier, (float)rand.NextDouble()));
        }
        private static int GetDifficultyModifiedAmount(int minAmount, int maxAmount, float levelDifficulty, Random rand)
        {
            return Math.Max((int)Math.Round(minAmount + (maxAmount - minAmount) * (levelDifficulty + MathHelper.Lerp(-RandomnessModifier, RandomnessModifier, (float)rand.NextDouble())) / MaxDifficulty), minAmount);
        }

        private SubmarineInfo GetRandomDifficultyModifiedSubmarine(float levelDifficulty, float randomnessModifier)
        {
            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
            // look for the saved submarine that is closest to our difficulty, with some randomness
            SubmarineInfo bestSubmarine = null;
            float bestValue = float.MaxValue;
            var submarineInfos = SubmarineInfo.SavedSubmarines.Where(i => i.IsEnemySubmarine);
            foreach (SubmarineInfo submarineInfo in submarineInfos)
            {
                if (!Prefab.Tags.Any(t => submarineInfo.EnemySubmarineInfo.MissionTags.Contains(t))) { continue; }
                float applicabilityValue = GetDifficultyModifiedValue(submarineInfo.EnemySubmarineInfo.PreferredDifficulty, levelDifficulty, randomnessModifier, rand);
                if (applicabilityValue < bestValue)
                {
                    bestSubmarine = submarineInfo;
                    bestValue = applicabilityValue;
                }
            }

            if (bestSubmarine == null)
            {
                DebugConsole.ThrowError("No EnemySubmarine found that matches the mission's tags!");
                return SubmarineInfo.SavedSubmarines.First(i => i.IsEnemySubmarine);
            }

            return bestSubmarine;
        }

        private XElement GetRandomDifficultyModifiedElement(XElement parentElement, float levelDifficulty, float randomnessModifier)
        {
            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
            // look for the element that is closest to our difficulty, with some randomness
            XElement bestElement = null;
            float bestValue = float.MaxValue;
            foreach (XElement element in parentElement.Elements())
            {
                float applicabilityValue = GetDifficultyModifiedValue(element.GetAttributeFloat(0f, "preferreddifficulty"), levelDifficulty, randomnessModifier, rand);
                if (applicabilityValue < bestValue)
                {
                    bestElement = element;
                    bestValue = applicabilityValue;
                }
            }
            return bestElement;
        }

        private void CreateMissionPositions(out Vector2 preferredSpawnPos)
        {
            Vector2 patrolPos = Level.Loaded.EndPosition;
            Point subSize = enemySub.GetDockedBorders().Size;

            preferredSpawnPos = Level.Loaded.EndPosition;

            if (Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out var potentialSpawnPos))
            {
                preferredSpawnPos = potentialSpawnPos.Position.ToVector2();
            }
            else
            {
                DebugConsole.ThrowError("Could not spawn pirate submarine in an interesting location! " + this,
                    contentPackage: Prefab.ContentPackage);
            }
            if (Level.Loaded.TryGetInterestingPositionAwayFromPoint(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out var potentialPatrolPos, preferredSpawnPos, minDistFromPoint: 10000f))
            {
                patrolPos = potentialPatrolPos.Position.ToVector2();
            }
            else
            {
                DebugConsole.ThrowError("Could not give pirate submarine an interesting location to patrol to! " + this,
                    contentPackage: Prefab.ContentPackage);
            }

            patrolPos = enemySub.FindSpawnPos(patrolPos, subSize);

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

                int graceDistance = 500; // the sub still spawns awkwardly close to walls, so this helps. could also be given as a parameter instead
                preferredSpawnPos = enemySub.FindSpawnPos(preferredSpawnPos, new Point(subSize.X + graceDistance, subSize.Y + graceDistance));
            }
        }

        private void InitPirateShip()
        {
            enemySub.NeutralizeBallast();
            if (enemySub.GetItems(alsoFromConnectedSubs: false).Find(i => i.HasTag(Tags.Reactor) && !i.NonInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                reactor.PowerUpImmediately();
            }
            enemySub.EnableMaintainPosition();
            enemySub.TeamID = CharacterTeamType.None;
            //make the enemy sub withstand atleast the same depth as the player sub
            enemySub.SetCrushDepth(Math.Max(enemySub.RealWorldCrushDepth, Submarine.MainSub.RealWorldCrushDepth));
            if (Level.Loaded != null)
            {
                //...and the depth of the patrol positions + 1000 m
                foreach (var patrolPos in patrolPositions)
                {
                    enemySub.SetCrushDepth(Math.Max(enemySub.RealWorldCrushDepth, Level.Loaded.GetRealWorldDepth(patrolPos.Y) + 1000));
                }
            }
            enemySub.ImmuneToBallastFlora = true;
            enemySub.EnableFactionSpecificEntities(factionIdentifier);
        }

        private void InitPirates()
        {
            characters.Clear();
            characterItems.Clear();

            if (characterConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize characters for escort mission (characterConfig == null)", 
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            int playerCount = 1;

#if SERVER
            playerCount = GameMain.Server.ConnectedClients.Where(c => !c.SpectateOnly || !GameMain.Server.ServerSettings.AllowSpectating).Count();
#endif

            float enemyCreationDifficulty = missionDifficulty + playerCount * addedMissionDifficultyPerPlayer;

            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));

            bool commanderAssigned = false;
            foreach (ContentXElement element in characterConfig.Elements())
            {
                //there's two ways to define the characters in pirate missions
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
                        XElement characterType = characterTypeConfig.Elements().Where(e => e.GetAttributeIdentifier("typeidentifier", Identifier.Empty) == characterTypeId).FirstOrDefault();
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

                    Character spawnedCharacter = CreateHuman(humanPrefab, characters, characterItems, enemySub, CharacterTeamType.None, null);
                    if (element.GetAttribute("color") != null)
                    {
                        spawnedCharacter.UniqueNameColor = element.GetAttributeColor("color", Color.Red);
                    }
                    if (!commanderAssigned)
                    {
                        if (isCommander && spawnedCharacter.AIController is HumanAIController humanAIController)
                        {
                            humanAIController.InitShipCommandManager();
                            foreach (var patrolPos in patrolPositions)
                            {
                                humanAIController.ShipCommandManager.patrolPositions.Add(patrolPos);
                            }
                            commanderAssigned = true;
                        }
                    }

                    foreach (var subElement in element.Elements())
                    {
                        if (subElement.NameAsIdentifier() == "statuseffect")
                        {
                            var newEffect = StatusEffect.Load(subElement, parentDebugName: Prefab.Name.Value);
                            newEffect?.Apply(newEffect.type, 1.0f, spawnedCharacter, spawnedCharacter);                            
                        }
                    }

                    foreach (Item item in spawnedCharacter.Inventory.AllItems)
                    {
                        if (item?.GetComponent<IdCard>() != null)
                        {
                            item.AddTag("id_pirate");
                        }
                    }
                }
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
            if (characters.Count > 0)
            {
#if DEBUG
                throw new Exception($"characters.Count > 0 ({characters.Count})");
#else
                DebugConsole.AddWarning("Character list was not empty at the start of a pirate mission. The mission instance may not have been ended correctly on previous rounds.");
                characters.Clear();            
#endif
            }

            if (patrolPositions.Count > 0)
            {
#if DEBUG
                throw new Exception($"patrolPositions.Count > 0 ({patrolPositions.Count})");
#else
                DebugConsole.AddWarning("Patrol point list was not empty at the start of a pirate mission. The mission instance may not have been ended correctly on previous rounds.");
                patrolPositions.Clear();            
#endif
            }

            enemySub = Submarine.MainSubs[1];

            if (enemySub == null)
            {
                DebugConsole.ThrowError(submarineInfo == null ? 
                    $"Error in PirateMission: enemy sub was not created (submarineInfo == null)." :
                    $"Error in PirateMission: enemy sub was not created.", 
                    contentPackage: Prefab.ContentPackage);
                return;
            }

            CreateMissionPositions(out Vector2 spawnPos); // patrol positions are not explicitly replicated, instead they are acquired the same way the server acquires them
#if DEBUG
            if (IsClient)
            {
                DebugConsole.NewMessage("The patrol positions set by client were: ");
            }
            else
            {
                DebugConsole.NewMessage("The patrol positions set by server were: ");
            }
            foreach (var patrolPos in patrolPositions)
            {
                DebugConsole.NewMessage("Patrol pos: " + patrolPos);
            }
#endif
            enemySub.SetPosition(spawnPos);
            InitPirateShip();            

            // flipping the sub on the frame it is moved into place must be done after it's been moved, or it breaks item connections in the submarine
            // creating the pirates has to be done after the sub has been flipped, or it seems to break the AI pathing
            enemySub.FlipX();
            enemySub.ShowSonarMarker = false;

            if (!IsClient)
            {
                InitPirates();
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (state >= 2 || enemySub == null) { return; }

            float sqrSonarRange = MathUtils.Pow2(Sonar.DefaultSonarRange);
            outsideOfSonarRange = Vector2.DistanceSquared(enemySub.WorldPosition, Submarine.MainSub.WorldPosition) > sqrSonarRange;

            if (CheckWinState())
            {
                State = 2;
            }
            else
            {
                switch (State)
                {
                    case 0:
                        for (int i = patrolPositions.Count - 1; i >= 0; i--)
                        {
                            if (Vector2.DistanceSquared(patrolPositions[i], Submarine.MainSub.WorldPosition) < sqrSonarRange)
                            {
                                patrolPositions.RemoveAt(i);
                            }
                        }
                        if (!outsideOfSonarRange || patrolPositions.None())
                        {
                            State = 1;
                        }
                        break;
                    case 1:
                        if (outsideOfSonarRange)
                        {
                            if (lastSighting.HasValue && Vector2.DistanceSquared(lastSighting.Value, Submarine.MainSub.WorldPosition) < sqrSonarRange)
                            {
                                lastSighting = null;
                            }
                            pirateSightingUpdateTimer -= deltaTime;
                            if (pirateSightingUpdateTimer < 0)
                            {
                                pirateSightingUpdateTimer = pirateSightingUpdateFrequency;
                                lastSighting = enemySub.WorldPosition;
                            }
                        }
                        else
                        {
                            lastSighting = enemySub.WorldPosition;
                            pirateSightingUpdateTimer = 0;
                        }
                        break;
                }
            }
        }

        private bool CheckWinState() => !IsClient && characters.All(m => DeadOrCaptured(m));

        private static bool DeadOrCaptured(Character character)
        {
            return character == null || character.Removed || character.Submarine == null || (character.LockHands && character.Submarine == Submarine.MainSub) || character.IsIncapacitated;
        }

        protected override bool DetermineCompleted()
        {
            return state == 2;
        }

        protected override void EndMissionSpecific(bool completed)
        {
            characters.Clear();
            characterItems.Clear();
            failed = !completed;
            submarineInfo = null;
        }
    }
}
