using Barotrauma.Extensions;
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
        private readonly XElement characterConfig;
        private readonly XElement submarineConfig;

        private Submarine enemySub;
        private Item reactorItem;
        private readonly List<Character> characters = new List<Character>();
        private readonly Dictionary<Character, List<Item>> characterDictionary = new Dictionary<Character, List<Item>>();

        // Update the last sighting periodically so that the players can find the pirate sub even if they have lost the track of it.
        private readonly float pirateSightingUpdateFrequency = 30;
        private float pirateSightingUpdateTimer;
        private Vector2? lastSighting;

        public override int TeamCount => 2;

        private bool outsideOfSonarRange;

        private readonly List<Vector2> patrolPositions = new List<Vector2>();

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                var empty = Enumerable.Empty<Vector2>();
                if (outsideOfSonarRange)
                {
                    return State switch
                    {
                        0 => patrolPositions,
                        1 => lastSighting.HasValue ? lastSighting.Value.ToEnumerable() : empty,
                        _ => empty,
                    };
                }
                else
                {
                    return empty;
                }
            }
        }

        private SubmarineInfo submarineInfo;

        public override SubmarineInfo EnemySubmarineInfo => submarineInfo;

        public PirateMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            submarineConfig = prefab.ConfigElement.Element("Submarine");
            characterConfig = prefab.ConfigElement.Element("Characters");

            string submarineIdentifier = submarineConfig.GetAttributeString("identifier", string.Empty);

            if (submarineIdentifier == string.Empty)
            {
                DebugConsole.ThrowError("No identifier used for submarine for pirate mission!");
                return;
            }
            // maybe a little redundant
            var contentFile = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.EnemySubmarine).FirstOrDefault(x => x.Path == submarineIdentifier);
            if (contentFile == null)
            {
                DebugConsole.ThrowError("No submarine file found with the identifier!");
                return;
            }
            submarineInfo = new SubmarineInfo(contentFile.Path);
        }

        private void CreateMissionPositions(out Vector2 preferredSpawnPos)
        {
            Vector2 patrolPos = enemySub.WorldPosition;
            Point subSize = enemySub.GetDockedBorders().Size;

            if (!Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath | Level.PositionType.SidePath, Level.Loaded.Size.X * 0.3f, out preferredSpawnPos))
            {
                DebugConsole.ThrowError("Could not spawn pirate submarine in an interesting location! " + this);
            }
            if (!Level.Loaded.TryGetInterestingPositionAwayFromPoint(true, Level.PositionType.MainPath | Level.PositionType.SidePath, Level.Loaded.Size.X * 0.3f, out patrolPos, preferredSpawnPos, minDistFromPoint: 10000f))
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
                    preferredSpawnPos = path.Nodes[Rand.Range(0, path.Nodes.Count - 1)].WorldPosition; // spawn the sub in a random point in the path if possible
                }

                int graceDistance = 500; // the sub still spawns awkwardly close to walls, so this helps. could also be given as a parameter instead
                preferredSpawnPos = enemySub.FindSpawnPos(preferredSpawnPos, new Point(subSize.X + graceDistance, subSize.Y + graceDistance));
            }
        }

        private void InitPirateShip(Vector2 spawnPos)
        {
            enemySub.NeutralizeBallast();
            if (enemySub.GetItems(alsoFromConnectedSubs: false).Find(i => i.HasTag("reactor") && !i.NonInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                reactor.PowerUpImmediately();
                reactorItem = reactor.Item;
            }
            enemySub.EnableMaintainPosition();
            enemySub.SetPosition(spawnPos);
            enemySub.TeamID = CharacterTeamType.None;
        }

        private void InitPirates()
        {
            characters.Clear();
            characterDictionary.Clear();

            if (characterConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize characters for escort mission (characterConfig == null)");
                return;
            }

            bool commanderAssigned = false;
            foreach (XElement element in characterConfig.Elements())
            {
                Character spawnedCharacter = CreateHuman(CreateHumanPrefabFromElement(element), characters, characterDictionary, enemySub, CharacterTeamType.None, null);
                if (!commanderAssigned)
                {
                    bool isCommander = element.GetAttributeBool("iscommander", false);
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
                DebugConsole.ThrowError($"Enemy Submarine was not created. SubmarineInfo is likely not defined.");
                // TODO: should we set the state to something here?
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
                InitPirateShip(spawnPos);
            }

            // flipping the sub on the frame it is moved into place must be done after it's been moved, or it breaks item connections to the submarine
            // creating the pirates have to be done after the sub has been flipped, or it seems to break the AI pathing
            enemySub.FlipX();
            enemySub.ShowSonarMarker = false;

            if (!IsClient)
            {
                InitPirates();
            }
        }

        public override void Update(float deltaTime)
        {
            int newState = State;
            float sqrSonarRange = MathUtils.Pow2(Sonar.DefaultSonarRange);
            outsideOfSonarRange = Vector2.DistanceSquared(enemySub.WorldPosition, Submarine.MainSub.WorldPosition) > sqrSonarRange;
            if (State < 2 && CheckWinState())
            {
                newState = 2;
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
                            newState = 1;
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
            State = newState;
        }

        private bool CheckWinState() => !IsClient && (characters.All(m => !Survived(m)) || reactorItem.Condition <= 0f);

        private bool Survived(Character character)
        {
            return character != null && !character.Removed && !character.IsDead;
        }

        public override void End()
        {
            if (state == 2)
            {
                GiveReward();
                completed = true;
            }
            characters.Clear();
            characterDictionary.Clear();
            failed = !completed;
        }
    }
}
