using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class MonsterEvent : ScriptedEvent
    {
        private string characterFile;

        private int minAmount, maxAmount;

        private Character[] monsters;

        public MonsterEvent(XElement element)
            : base (element)
        {
            characterFile = ToolBox.GetAttributeString(element, "characterfile", "");

            minAmount = ToolBox.GetAttributeInt(element, "minamount", 1);
            maxAmount = Math.Max(ToolBox.GetAttributeInt(element, "maxamount", 1), minAmount);
        }

        private void SpawnMonsters()
        {
            WayPoint randomWayPoint = WayPoint.GetRandom(SpawnType.Enemy);

            int amount = Rand.Range(minAmount, maxAmount, false);

            monsters = new AICharacter[amount];

            for (int i = 0; i < amount; i++)
            {
                Vector2 position = (randomWayPoint == null) ? Vector2.Zero : FarseerPhysics.ConvertUnits.ToSimUnits(randomWayPoint.Position + Level.Loaded.Position);
                position.X += Rand.Range(-0.5f, 0.5f);
                position.Y += Rand.Range(-0.5f, 0.5f);
                monsters[i] = new AICharacter(characterFile, position);
            }
        }

        public override void Update(float deltaTime)
        {
            if (monsters == null) SpawnMonsters();

            //base.Update(deltaTime);

            //if (!isStarted) return;

            if (isFinished) return;

            bool monstersDead = true;
            for (int i = 0; i < monsters.Length; i++)
            {
                if (monsters[i].IsDead) continue;

                if (!isStarted && monsters[i].SimPosition != Vector2.Zero && monsters[i].SimPosition.Length() < 20.0) isStarted = true;
                    
                monstersDead = false;
                break;                    
            }
            if (monstersDead) Finished();
            
        }
    }
}
