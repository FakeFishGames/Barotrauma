using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Collision;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class PirateMission : Mission
    {
        private readonly XElement characterConfig;
        private readonly XElement characterTypeConfig;
        private readonly float addedMissionDifficultyPerPlayer;

        private float missionDifficulty;
        private int alternateReward;

        private Submarine enemySub;
        private readonly List<Character> characters = new List<Character>();
        private readonly Dictionary<Character, List<Item>> characterItems = new Dictionary<Character, List<Item>>();

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

        public override int GetReward(Submarine sub)
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
            characterConfig = prefab.ConfigElement.GetChildElement("Characters");
            characterTypeConfig = prefab.ConfigElement.GetChildElement("CharacterTypes");
            addedMissionDifficultyPerPlayer = prefab.ConfigElement.GetAttributeFloat("addedmissiondifficultyperplayer", 0);

            //make sure all referenced character types are defined
            foreach (XElement characterElement in characterConfig.Elements())
            {
                var characterId = characterElement.GetAttributeString("typeidentifier", string.Empty);
                var characterTypeElement = characterTypeConfig.Elements().FirstOrDefault(e => e.GetAttributeString("typeidentifier", string.Empty) == characterId);
                if (characterTypeElement == null)
                {
                    DebugConsole.ThrowError($"Error in mission \"{prefab.Identifier}\". Could not find a character type element for the character \"{characterId}\".");
                }
            }
            //make sure all defined character types can be found from human prefabs
            foreach (XElement characterTypeElement in characterTypeConfig.Elements())
            {
                foreach (XElement characterElement in characterTypeElement.Elements())
                {
                    Identifier characterIdentifier = characterElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    Identifier characterFrom = characterElement.GetAttributeIdentifier("from", Identifier.Empty);
                    HumanPrefab humanPrefab = NPCSet.Get(characterFrom, characterIdentifier);
                    if (humanPrefab == null)
                    {
                        DebugConsole.ThrowError($"Error in mission \"{prefab.Identifier}\". Character prefab \"{characterIdentifier}\" not found in the NPC set \"{characterFrom}\".");
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

        private new void SetLevel(LevelData level)
        {
            if (levelData != null)
            {
                //level already set
                return;
            }
            submarineInfo = null;

            levelData = level;
            missionDifficulty = level?.Difficulty ?? 0;

            submarineInfo = GetRandomDifficultyModifiedSubmarine(missionDifficulty, ShipRandomnessModifier);

            alternateReward = (int)submarineInfo.EnemySubmarineInfo.Reward;
            string rewardText = $"‖color:gui.orange‖{string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N0}", alternateReward)}‖end‖";
            if (descriptionWithoutReward != null) { description = descriptionWithoutReward.Replace("[reward]", rewardText); }
        }

        private float GetDifficultyModifiedValue(float preferredDifficulty, float levelDifficulty, float randomnessModifier, Random rand)
        {
            return Math.Abs(levelDifficulty - preferredDifficulty + MathHelper.Lerp(-randomnessModifier, randomnessModifier, (float)rand.NextDouble()));
        }
        private int GetDifficultyModifiedAmount(int minAmount, int maxAmount, float levelDifficulty, Random rand)
        {
            return Math.Max((int)Math.Round(minAmount + (maxAmount - minAmount) * (levelDifficulty + MathHelper.Lerp(-RandomnessModifier, RandomnessModifier, (float)rand.NextDouble())) / MaxDifficulty), minAmount);
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

        private SubmarineInfo GetRandomDifficultyModifiedSubmarine(float levelDifficulty, float randomnessModifier)
        {
            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));
            // look for the saved submarine that is closest to our difficulty, with some randomness
            SubmarineInfo bestSubmarine = null;
            float bestValue = float.MaxValue;
            var submarineInfos = SubmarineInfo.SavedSubmarines.Where(i => i.IsEnemySubmarine);
            foreach (SubmarineInfo submarineInfo in submarineInfos)
            {
                string[] tags = submarineInfo.EnemySubmarineInfo.MissionTags.Split(',');
                var value = tags.Length;
                foreach (string tag in tags)
                {
                    if (!this.Prefab.Tags.Contains(tag))
                    {
                        value--;
                    }
                }
                if (value == 0) { continue; }

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
                return SubmarineInfo.SavedSubmarines.Where(i => i.IsEnemySubmarine).First();
            }

            return bestSubmarine;
        }

        private void CreateMissionPositions(out Vector2 preferredSpawnPos)
        {
            Vector2 patrolPos = enemySub.WorldPosition;
            Point subSize = enemySub.GetDockedBorders().Size;

            if (!Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out preferredSpawnPos))
            {
                DebugConsole.ThrowError("Could not spawn pirate submarine in an interesting location! " + this);
            }
            if (!Level.Loaded.TryGetInterestingPositionAwayFromPoint(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out patrolPos, preferredSpawnPos, minDistFromPoint: 10000f))
            {
                DebugConsole.ThrowError("Could not give pirate submarine an interesting location to patrol to! " + this);
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
            if (enemySub.GetItems(alsoFromConnectedSubs: false).Find(i => i.HasTag("reactor") && !i.NonInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                reactor.PowerUpImmediately();
            }
            enemySub.EnableMaintainPosition();
            enemySub.TeamID = CharacterTeamType.None;
            //make the enemy sub withstand atleast the same depth as the player sub
            enemySub.RealWorldCrushDepth = Math.Max(enemySub.RealWorldCrushDepth, Submarine.MainSub.RealWorldCrushDepth);
            if (Level.Loaded != null)
            {
                //...and the depth of the patrol positions + 1000 m
                foreach (var patrolPos in patrolPositions)
                {
                    enemySub.RealWorldCrushDepth = Math.Max(enemySub.RealWorldCrushDepth, Level.Loaded.GetRealWorldDepth(patrolPos.Y) + 1000);
                }
            }
            enemySub.ImmuneToBallastFlora = true;
        }

        private void InitPirates()
        {
            characters.Clear();
            characterItems.Clear();

            if (characterConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize characters for escort mission (characterConfig == null)");
                return;
            }

            int playerCount = 1;

#if SERVER
            playerCount = GameMain.Server.ConnectedClients.Where(c => !c.SpectateOnly || !GameMain.Server.ServerSettings.AllowSpectating).Count();
#endif

            float enemyCreationDifficulty = missionDifficulty + playerCount * addedMissionDifficultyPerPlayer;

            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed));

            bool commanderAssigned = false;
            foreach (XElement element in characterConfig.Elements())
            {
                // it is possible to get more than the "max" amount of characters if the modified difficulty is high enough; this is intentional
                // if necessary, another "hard max" value could be used to clamp the value for performance/gameplay concerns
                int amountCreated = GetDifficultyModifiedAmount(element.GetAttributeInt("minamount", 0), element.GetAttributeInt("maxamount", 0), enemyCreationDifficulty, rand);
                var characterId = element.GetAttributeString("typeidentifier", string.Empty);
                for (int i = 0; i < amountCreated; i++)
                {
                    XElement characterType = characterTypeConfig.Elements().Where(e => e.GetAttributeString("typeidentifier", string.Empty) == characterId).FirstOrDefault();

                    if (characterType == null)
                    {
                        DebugConsole.ThrowError($"No character types defined in CharacterTypes for a declared type identifier in mission \"{Prefab.Identifier}\".");
                        return;
                    }

                    XElement variantElement = GetRandomDifficultyModifiedElement(characterType, enemyCreationDifficulty, RandomnessModifier);

                    var humanPrefab = GetHumanPrefabFromElement(variantElement);
                    if (humanPrefab == null) { continue; }

                    Character spawnedCharacter = CreateHuman(humanPrefab, characters, characterItems, enemySub, CharacterTeamType.None, null);
                    if (!commanderAssigned)
                    {
                        bool isCommander = variantElement.GetAttributeBool("iscommander", false);
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
                    $"Error in PirateMission: enemy sub was not created.");
                return;
            }

            Vector2 spawnPos = Level.Loaded.EndPosition; // in case TryGetInterestingPosition fails, though this should not happen
            CreateMissionPositions(out spawnPos); // patrol positions are not explicitly replicated, instead they are acquired the same way the server acquires them
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
            if (!IsClient)
            {
                InitPirateShip();
            }
            enemySub.SetPosition(spawnPos);

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

        private bool DeadOrCaptured(Character character)
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
