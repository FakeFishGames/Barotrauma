using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class MonsterMission : Mission
    {
        private string monsterFile;

        private int state;

        private Character monster;

        private Vector2 sonarPosition;

        public override Vector2 SonarPosition
        {
            get { return monster != null && !monster.IsDead ? sonarPosition : Vector2.Zero; }
        }

        public MonsterMission(MissionPrefab prefab, Location[] locations)
            : base(prefab, locations)
        {
            monsterFile = prefab.ConfigElement.GetAttributeString("monsterfile", "");
        }
        
        public override void Start(Level level)
        {
            Vector2 spawnPos;
            Level.Loaded.TryGetInterestingPosition(true, Level.PositionType.MainPath, Level.Loaded.Size.X * 0.3f, out spawnPos);

            bool isClient = false;
#if CLIENT
            isClient = GameMain.Client != null;
#endif
            monster = Character.Create(monsterFile, spawnPos, ToolBox.RandomSeed(8), null, isClient, true, false);
            monster.Enabled = false;
            sonarPosition = spawnPos;
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    if (monster.Enabled)
                    {
                        sonarPosition = monster.Position;
                    }

                    if (!monster.IsDead) return;
#if CLIENT
                    ShowMessage(state);
#endif
                    state = 1;
                    break;
            }
        }
        
        public override void End()
        {
            if (!monster.IsDead) return;
                        
            GiveReward();

            completed = true;
        }
    }
}
