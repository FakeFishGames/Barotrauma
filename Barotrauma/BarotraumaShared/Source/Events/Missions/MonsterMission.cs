using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class MonsterMission : Mission
    {
        private string monsterFile;

        private int monsterCount;

        private readonly List<Character> monsters = new List<Character>();
        private readonly List<Vector2> sonarPositions = new List<Vector2>();

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                return sonarPositions;
            }
        }

        public MonsterMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            monsterFile = prefab.ConfigElement.GetAttributeString("monsterfile", "");
            monsterCount = prefab.ConfigElement.GetAttributeInt("monstercount", 1);

            description = description.Replace("[monster]",
                TextManager.Get("character." + System.IO.Path.GetFileNameWithoutExtension(monsterFile)));
        }
        
        public override void Start(Level level)
        {
            Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out Vector2 spawnPos);

            bool isClient = IsClient;
            for (int i = 0; i < monsterCount; i++)
            {
                monsters.Add(Character.Create(monsterFile, spawnPos, ToolBox.RandomSeed(8), null, isClient, true, false));
            }
            monsters.ForEach(m => m.Enabled = false);
            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
            sonarPositions.Add(spawnPos);
        }

        public override void Update(float deltaTime)
        {
            switch (State)
            {
                case 0:
                    sonarPositions.Clear();
                    foreach (var monster in monsters)
                    {
                        if (monster.Removed || monster.IsDead) { continue; }
                        //don't add another label if there's another monster roughly at the same spot
                        if (sonarPositions.All(p => Vector2.DistanceSquared(p, monster.Position) > 1000.0f * 1000.0f))
                        {
                            sonarPositions.Add(monster.Position);
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
            if (State == 1) { return; }
                        
            GiveReward();
            completed = true;
        }

        public bool IsEliminated(Character enemy) => enemy.Removed || enemy.IsDead;
    }
}
