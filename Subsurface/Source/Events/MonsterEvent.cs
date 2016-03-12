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

        private bool spawnDeep;

        public MonsterEvent(XElement element)
            : base (element)
        {
            characterFile = ToolBox.GetAttributeString(element, "characterfile", "");

            minAmount = ToolBox.GetAttributeInt(element, "minamount", 1);
            maxAmount = Math.Max(ToolBox.GetAttributeInt(element, "maxamount", 1), minAmount);

            spawnDeep = ToolBox.GetAttributeBool(element, "spawndeep", false);
        }

        protected override void Start()
        {
            SpawnMonsters();
        }

        private void SpawnMonsters()
        {
            WayPoint randomWayPoint = WayPoint.GetRandom(SpawnType.Enemy);

            int amount = Rand.Range(minAmount, maxAmount, false);

            monsters = new Character[amount];

            for (int i = 0; i < amount; i++)
            {
                Vector2 position = (randomWayPoint == null) ? Vector2.Zero : randomWayPoint.Position;

                if (spawnDeep)
                {
                    position.Y -= Level.Loaded.Size.Y;
                }

                position.X += Rand.Range(-0.5f, 0.5f, false);
                position.Y += Rand.Range(-0.5f, 0.5f, false);
                monsters[i] = Character.Create(characterFile, position, null, GameMain.Client != null);
            }
        }

        public override void Update(float deltaTime)
        {
            if (monsters == null) SpawnMonsters();

            //base.Update(deltaTime);

            //if (!isStarted) return;

            if (isFinished) return;

            bool monstersDead = true;
            foreach (Character monster in monsters)
            {
                if (monster.IsDead) continue;

                if (!isStarted && Vector2.Distance(monster.WorldPosition, Submarine.Loaded.WorldPosition) < 5000.0f) isStarted = true;
                    
                monstersDead = false;
                break;
            }

            if (monstersDead) Finished();
        }
    }
}
