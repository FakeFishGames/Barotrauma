using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class MonsterMission : Mission
    {
        private readonly string monsterFile;
        private readonly int monsterCount;
        private readonly HashSet<Tuple<string, int>> monsterFiles = new HashSet<Tuple<string, int>>();
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

            monsterCount = prefab.ConfigElement.GetAttributeInt("monstercount", 1);
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
                int min = monsterElement.GetAttributeInt("min", defaultCount);
                int max = Math.Max(min, monsterElement.GetAttributeInt("max", defaultCount));
                monsterFiles.Add(new Tuple<string, int>(monster, Rand.Range(min, max + 1, Rand.RandSync.Server)));
            }
            description = description.Replace("[monster]",
                TextManager.Get("character." + System.IO.Path.GetFileNameWithoutExtension(monsterFileName)));
        }
        
        public override void Start(Level level)
        {
            Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out Vector2 spawnPos);

            bool isClient = IsClient;
            if (!string.IsNullOrEmpty(monsterFile))
            {
                for (int i = 0; i < monsterCount; i++)
                {
                    monsters.Add(Character.Create(monsterFile, spawnPos, ToolBox.RandomSeed(8), null, isClient, true, false));
                }
            }
            foreach (var monster in monsterFiles)
            {
                for (int i = 0; i < monster.Item2; i++)
                {
                    monsters.Add(Character.Create(monster.Item1, spawnPos, ToolBox.RandomSeed(8), null, isClient, true, false));
                }
            }

            monsters.ForEach(m => m.Enabled = false);
            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
            for (int i = 0; i < monsters.Count; i++)
            {
                tempSonarPositions.Add(spawnPos + Rand.Vector(maxSonarMarkerDistance));
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
            if (State < 1) { return; }
                        
            GiveReward();
            completed = true;
        }

        public bool IsEliminated(Character enemy) => enemy.Removed || enemy.IsDead || enemy.AIController is EnemyAIController ai && ai.State == AIState.Flee;
    }
}
