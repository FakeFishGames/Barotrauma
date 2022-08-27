using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {
        //string = filename, point = min,max
        private readonly HashSet<(CharacterPrefab character, Point amountRange)> monsterPrefabs = new HashSet<(CharacterPrefab character, Point amountRange)>();
        private readonly List<Character> monsters = new List<Character>();
        private readonly List<Vector2> sonarPositions = new List<Vector2>();

        private readonly List<Vector2> tempSonarPositions = new List<Vector2>();

        private readonly float maxSonarMarkerDistance = 10000.0f;

        private readonly Level.PositionType spawnPosType;

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                if (State > 0)
                {
                    return Enumerable.Empty<Vector2>();
                }
                else
                {
                    return sonarPositions;
                }
            }
        }

        public MonsterMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            Identifier speciesName = prefab.ConfigElement.GetAttributeIdentifier("monsterfile", Identifier.Empty);
            if (!speciesName.IsEmpty)
            {
                var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                if (characterPrefab != null)
                {
                    int monsterCount = Math.Min(prefab.ConfigElement.GetAttributeInt("monstercount", 1), 255);
                    monsterPrefabs.Add((characterPrefab, new Point(monsterCount)));
                }
                else
                {
                    DebugConsole.ThrowError($"Error in monster mission \"{prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".");
                }
            }

            maxSonarMarkerDistance = prefab.ConfigElement.GetAttributeFloat("maxsonarmarkerdistance", 10000.0f);

            var spawnPosTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");
            if (string.IsNullOrWhiteSpace(spawnPosTypeStr) ||
                !Enum.TryParse(spawnPosTypeStr, true, out spawnPosType))
            {
                spawnPosType = Level.PositionType.MainPath | Level.PositionType.SidePath;
            }

            foreach (var monsterElement in prefab.ConfigElement.GetChildElements("monster"))
            {
                speciesName = monsterElement.GetAttributeIdentifier("character", Identifier.Empty);
                int defaultCount = monsterElement.GetAttributeInt("count", -1);
                if (defaultCount < 0)
                {
                    defaultCount = monsterElement.GetAttributeInt("amount", 1);
                }
                int min = Math.Min(monsterElement.GetAttributeInt("min", defaultCount), 255);
                int max = Math.Min(Math.Max(min, monsterElement.GetAttributeInt("max", defaultCount)), 255);
                var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                if (characterPrefab != null)
                {
                    monsterPrefabs.Add((characterPrefab, new Point(min, max)));
                }
                else
                {
                    DebugConsole.ThrowError($"Error in monster mission \"{prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".");
                }
            }

            if (monsterPrefabs.Any())
            {
                var characterParams = new CharacterParams(monsterPrefabs.First().character);
                description = description.Replace("[monster]",
                    TextManager.Get("character." + characterParams.SpeciesTranslationOverride).Fallback(
                    TextManager.Get("character." + characterParams.SpeciesName)));
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
            if (monsters.Count > 0)
            {
#if DEBUG
                throw new Exception($"monsters.Count > 0 ({monsters.Count})");
#else
                DebugConsole.AddWarning("Monster list was not empty at the start of a monster mission. The mission instance may not have been ended correctly on previous rounds.");
                monsters.Clear();            
#endif
            }

            if (tempSonarPositions.Count > 0)
            {
#if DEBUG
                throw new Exception($"tempSonarPositions.Count > 0 ({tempSonarPositions.Count})");
#else
                DebugConsole.AddWarning("Sonar position list was not empty at the start of a monster mission. The mission instance may not have been ended correctly on previous rounds.");
                tempSonarPositions.Clear();            
#endif
            }

            if (!IsClient)
            {
                Level.Loaded.TryGetInterestingPosition(true, spawnPosType, Level.Loaded.Size.X * 0.3f, out Vector2 spawnPos);
                foreach (var (character, amountRange) in monsterPrefabs)
                {
                    int amount = Rand.Range(amountRange.X, amountRange.Y + 1);
                    for (int i = 0; i < amount; i++)
                    {
                        monsters.Add(Character.Create(character.Identifier, spawnPos, ToolBox.RandomSeed(8), createNetworkEvent: false));
                    }
                }

                InitializeMonsters(monsters);
            }                         
        }

        private void InitializeMonsters(IEnumerable<Character> monsters)
        {
            foreach (var monster in monsters)
            {
                monster.Enabled = false;
                if (monster.Params.AI != null && monster.Params.AI.EnforceAggressiveBehaviorForMissions)
                {
                    monster.Params.AI.FleeHealthThreshold = 0;
                    foreach (var targetParam in monster.Params.AI.Targets)
                    {
                        if (targetParam.Tag.Equals("engine", StringComparison.OrdinalIgnoreCase)) { continue; }
                        switch (targetParam.State)
                        {
                            case AIState.Avoid:
                            case AIState.Escape:
                            case AIState.Flee:
                            case AIState.PassiveAggressive:
                                targetParam.State = AIState.Attack;
                                break;
                        }
                    }
                }
            }
            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
            foreach (Character monster in monsters)
            {
                tempSonarPositions.Add(monster.WorldPosition + Rand.Vector(maxSonarMarkerDistance));
            }
            if (monsters.Count() != tempSonarPositions.Count)
            {
                throw new Exception($"monsters.Count != tempSonarPositions.Count ({monsters.Count()} != {tempSonarPositions.Count})");
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            switch (State)
            {
                case 0:
                    //keep sonar markers within maxSonarMarkerDistance from the monster(s)
                    for (int i = 0; i < tempSonarPositions.Count; i++)
                    {
                        if (monsters.Count != tempSonarPositions.Count)
                        {
                            throw new Exception($"monsters.Count != tempSonarPositions.Count ({monsters.Count} != {tempSonarPositions.Count})");
                        }

                        if (i < 0 || i >= monsters.Count)
                        {
                            throw new Exception($"Index {i} outside of bounds 0-{monsters.Count} ({tempSonarPositions.Count})");
                        }

                        if (monsters[i].Removed || monsters[i].IsDead) { continue; }
                        Vector2 diff = tempSonarPositions[i] - monsters[i].Position;

                        float maxDist = maxSonarMarkerDistance; 
                        Submarine refSub = Character.Controlled?.Submarine ?? Submarine.MainSub;
                        if (refSub != null)
                        {
                            Vector2 refPos = refSub == null ? Vector2.Zero : refSub.WorldPosition;
                            float subDist = Vector2.Distance(refPos, tempSonarPositions[i]) / maxDist;

                            maxDist = Math.Min(subDist * subDist * maxDist, maxDist);
                            maxDist = Math.Min(Vector2.Distance(refPos, monsters[i].Position), maxDist);
                        }

                        if (diff.LengthSquared() > maxDist * maxDist)
                        {
                            tempSonarPositions[i] = monsters[i].Position + Vector2.Normalize(diff) * maxDist;
                        }
                    }
                    
                    sonarPositions.Clear();
                    for (int i = 0; i < monsters.Count; i++)
                    {
                        if (monsters[i].Removed || monsters[i].IsDead) { continue; }
                        //don't add another label if there's another monster roughly at the same spot
                        if (sonarPositions.All(p => Vector2.DistanceSquared(p, tempSonarPositions[i]) > 1000.0f * 1000.0f))
                        {
                            sonarPositions.Add(tempSonarPositions[i]);
                        }
                    }
                    if (!IsClient && monsters.All(m => IsEliminated(m)))
                    {
                        State = 1;
                    }
                    break;
            }
        }
        
        public override void End()
        {
            tempSonarPositions.Clear();
            monsters.Clear();
            if (State < 1) { return; }

            if (Prefab.LocationTypeChangeOnCompleted != null)
            {
                ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
            }
            GiveReward();
            completed = true;
            if (level?.LevelData != null && Prefab.Tags.Any(t => t.Equals("huntinggrounds", StringComparison.OrdinalIgnoreCase)))
            {
                level.LevelData.HasHuntingGrounds = false;
            }
        }

        public bool IsEliminated(Character enemy) =>
            enemy == null ||
            enemy.Removed || 
            enemy.IsDead || 
            enemy.AIController is EnemyAIController ai && ai.State == AIState.Flee;
    }
}
