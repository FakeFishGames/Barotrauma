using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
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
            float minDist = Math.Max(Submarine.MainSub.Borders.Width, Submarine.MainSub.Borders.Height);

            //find a random spawnpos that isn't too close to the main sub
            int tries = 0;
            Vector2 spawnPos = Vector2.Zero;
            do
            {
                spawnPos = Level.Loaded.GetRandomInterestingPosition(true, Level.PositionType.MainPath);
                tries++;
            } while (tries < 50 && Vector2.Distance(spawnPos, Submarine.MainSub.WorldPosition) < minDist);


            monster = Character.Create(monsterFile, spawnPos, null, GameMain.Client != null);
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
                    else if (GameMain.Client == null)
                    {   Vector2 diff = monster.WorldPosition - Submarine.MainSub.WorldPosition;
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
