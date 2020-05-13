using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {
        private readonly string monsterFile;
        private readonly int monsterCount;

        //string = filename, point = min,max
        private readonly HashSet<Tuple<string, Point>> monsterFiles = new HashSet<Tuple<string, Point>>();
        private readonly List<Character> monsters = new List<Character>();
        private readonly List<Vector2> sonarPositions = new List<Vector2>();

        private readonly List<Vector2> tempSonarPositions = new List<Vector2>();

        private readonly float maxSonarMarkerDistance = 10000.0f;


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

        public MonsterMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            monsterFile = prefab.ConfigElement.GetAttributeString("monsterfile", null);

            if (!string.IsNullOrEmpty(monsterFile))
            {
                var characterPrefab = CharacterPrefab.FindByFilePath(monsterFile);
                if (characterPrefab != null)
                {
                    monsterFile = characterPrefab.Identifier;
                }
            }

            maxSonarMarkerDistance = prefab.ConfigElement.GetAttributeFloat("maxsonarmarkerdistance", 10000.0f);

            monsterCount = Math.Min(prefab.ConfigElement.GetAttributeInt("monstercount", 1), 255);
            string monsterFileName = monsterFile;
            foreach (var monsterElement in prefab.ConfigElement.GetChildElements("monster"))
            {
                string monster = monsterElement.GetAttributeString("character", string.Empty);
                if (monsterFileName == null)
                {
                    monsterFileName = monster;
                }
                int defaultCount = monsterElement.GetAttributeInt("count", -1);
                if (defaultCount < 0)
                {
                    defaultCount = monsterElement.GetAttributeInt("amount", 1);
                }
                int min = Math.Min(monsterElement.GetAttributeInt("min", defaultCount), 255);
                int max = Math.Min(Math.Max(min, monsterElement.GetAttributeInt("max", defaultCount)), 255);
                monsterFiles.Add(new Tuple<string, Point>(monster, new Point(min, max)));
            }
            description = description.Replace("[monster]",
                TextManager.Get("character." + Barotrauma.IO.Path.GetFileNameWithoutExtension(monsterFileName)));
        }
        
        public override void Start(Level level)
        {
            if (monsters.Count > 0)
            {
                throw new Exception($"monsters.Count > 0 ({monsters.Count})");
            }

            if (tempSonarPositions.Count > 0)
            {
                throw new Exception($"tempSonarPositions.Count > 0 ({tempSonarPositions.Count})");
            }

            if (!IsClient)
            {
                Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out Vector2 spawnPos);
                if (!string.IsNullOrEmpty(monsterFile))
                {
                    for (int i = 0; i < monsterCount; i++)
                    {
                        monsters.Add(Character.Create(monsterFile, spawnPos, ToolBox.RandomSeed(8), createNetworkEvent: false));
                    }
                }
                foreach (var monster in monsterFiles)
                {
                    int amount = Rand.Range(monster.Item2.X, monster.Item2.Y + 1);
                    for (int i = 0; i < amount; i++)
                    {
                        monsters.Add(Character.Create(monster.Item1, spawnPos, ToolBox.RandomSeed(8), createNetworkEvent: false));
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
                if (monster.Params.AI.EnforceAggressiveBehaviorForMissions)
                {
                    foreach (var targetParam in monster.Params.AI.Targets)
                    {
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

        public override void Update(float deltaTime)
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
            
            GiveReward();
            completed = true;
        }

        public bool IsEliminated(Character enemy) =>
            enemy == null ||
            enemy.Removed || 
            enemy.IsDead || 
            enemy.AIController is EnemyAIController ai && ai.State == AIState.Flee;
    }
}
