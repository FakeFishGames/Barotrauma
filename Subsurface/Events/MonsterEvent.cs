using System.Xml.Linq;
using Microsoft.Xna.Framework;

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
            maxAmount = ToolBox.GetAttributeInt(element, "maxamount", 1);
        }

        protected override void Start()
        {
            WayPoint randomWayPoint = WayPoint.GetRandom(WayPoint.SpawnType.Enemy);

            int amount = Game1.random.Next(minAmount, maxAmount);

            monsters = new Character[amount];

            for (int i = 0; i < amount; i++)
            {
                monsters[i] = new Character(characterFile,
                    (randomWayPoint == null) ? Vector2.Zero : randomWayPoint.SimPosition);
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
