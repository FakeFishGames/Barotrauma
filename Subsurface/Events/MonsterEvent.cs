using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System;

namespace Subsurface
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
            maxAmount = Math.Max(ToolBox.GetAttributeInt(element, "maxamount", 1),minAmount);
        }

        protected override void Start()
        {
            WayPoint randomWayPoint = WayPoint.GetRandom(WayPoint.SpawnType.Enemy);

            int amount = Rand.Range(minAmount, maxAmount, false);

            monsters = new Character[amount];

            for (int i = 0; i < amount; i++)
            {
                Vector2 position = (randomWayPoint == null) ? Vector2.Zero : randomWayPoint.SimPosition;
                position.X += Rand.Range(-0.5f, 0.5f);
                position.Y += Rand.Range(-0.5f, 0.5f);
                monsters[i] = new Character(characterFile, position);
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!isStarted) return;
            
            if (!isFinished)
            {
                bool monstersDead = true;
                for (int i = 0; i < monsters.Length; i++)
                {
                    if (monsters[i].IsDead) continue;
                    
                    monstersDead = false;
                    break;                    
                }
                if (monstersDead) Finished();
            }
        }
    }
}
