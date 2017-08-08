using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    class MonsterMission : Mission
    {
        private string monsterFile;

        private int state;

        private Character monster;

        private Vector2 radarPosition;

        public override Vector2 RadarPosition
        {
            get { return monster != null && !monster.IsDead ? radarPosition : Vector2.Zero; }
        }

        public MonsterMission(XElement element, Location[] locations)
            : base(element, locations)
        {
            monsterFile = ToolBox.GetAttributeString(element, "monsterfile", "");

        }
        
        public override void Start(Level level)
        {
            Vector2 spawnPos = Level.Loaded.GetRandomInterestingPosition(true, Level.PositionType.MainPath, true);

            monster = Character.Create(monsterFile, spawnPos, null, GameMain.Client != null, true, false);
            monster.Enabled = false;
            radarPosition = spawnPos;
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    if (monster.Enabled)
                    {
                        radarPosition = monster.Position;
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
