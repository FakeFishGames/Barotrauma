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
            get { return radarPosition; }
        }

        public MonsterMission(XElement element)
            : base(element)
        {
            monsterFile = ToolBox.GetAttributeString(element, "monsterfile", "");
        }

        public override void Start(Level level)
        {
            Vector2 position = level.PositionsOfInterest[Rand.Int(level.PositionsOfInterest.Count, false)];

            monster = Character.Create(monsterFile, position);
            radarPosition = monster.Position;
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    if (monster.Enabled) radarPosition = monster.Position;

                    if (!monster.IsDead) return;
                    ShowMessage(state);
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
