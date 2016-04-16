using Barotrauma.Networking;
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

        public MonsterMission(XElement element)
            : base(element)
        {
            monsterFile = ToolBox.GetAttributeString(element, "monsterfile", "");
        }

        public override void Start(Level level)
        {
            Vector2 position = level.GetRandomInterestingPosition(true, true);

            monster = Character.Create(monsterFile, position, null, GameMain.Client != null);
            monster.Enabled = false;
            radarPosition = monster.Position;
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
                    else if (GameMain.Client == null)
                    {   Vector2 diff = monster.WorldPosition-Submarine.Loaded.WorldPosition;
                        monster.Enabled = FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) < NetConfig.CharacterIgnoreDistance;
                    }

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
