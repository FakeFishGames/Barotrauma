using System.Xml.Linq;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class MonsterEvent : ScriptedEvent
    {
        private string characterFile;

        private int minAmount, maxAmount;

        private Character[] monsters;

        private bool spawnDeep;

        private bool disallowed;
        
        private Level.PositionType spawnPosType;

        public override string ToString()
        {
            return "ScriptedEvent (" + characterFile + ")";
        }

        public MonsterEvent(XElement element)
            : base (element)
        {
            characterFile = ToolBox.GetAttributeString(element, "characterfile", "");

            minAmount = ToolBox.GetAttributeInt(element, "minamount", 1);
            maxAmount = Math.Max(ToolBox.GetAttributeInt(element, "maxamount", 1), minAmount);

            var spawnPosTypeStr = ToolBox.GetAttributeString(element, "spawntype", "");

            if (string.IsNullOrWhiteSpace(spawnPosTypeStr) ||
                !Enum.TryParse<Level.PositionType>(spawnPosTypeStr, true, out spawnPosType))
            {
                spawnPosType = Level.PositionType.MainPath;
            }

            spawnDeep = ToolBox.GetAttributeBool(element, "spawndeep", false);

            if (GameMain.Server != null)
            {
                List<string> monsterNames = GameMain.Server.monsterEnabled.Keys.ToList();
                string tryKey = monsterNames.Find(s => characterFile.ToLower().Contains(s.ToLower()));
                if (!string.IsNullOrWhiteSpace(tryKey))
                {
                    if (!GameMain.Server.monsterEnabled[tryKey]) disallowed = true; //spawn was disallowed by host
                }
            }
        }

        protected override void Start()
        {
            SpawnMonsters();
        }

        private void SpawnMonsters()
        {
            if (disallowed) return;

            float minDist = Math.Max(Submarine.MainSub.Borders.Width, Submarine.MainSub.Borders.Height);

            //find a random spawnpos that isn't too close to the main sub
            int tries = 0;
            Vector2 spawnPos = Vector2.Zero;
            do
            {
                spawnPos = Level.Loaded.GetRandomInterestingPosition(true, spawnPosType);
                tries++;
            } while (tries < 50 && Vector2.Distance(spawnPos, Submarine.MainSub.WorldPosition) < minDist);

            
            int amount = Rand.Range(minAmount, maxAmount, false);

            monsters = new Character[amount];

            for (int i = 0; i < amount; i++)
            {
                if (spawnDeep)
                {
                    spawnPos.Y -= Level.Loaded.Size.Y;
                }

                spawnPos.X += Rand.Range(-0.5f, 0.5f, false);
                spawnPos.Y += Rand.Range(-0.5f, 0.5f, false);
                monsters[i] = Character.Create(characterFile, spawnPos, null, GameMain.Client != null);
            }
        }

        public override void Update(float deltaTime)
        {
            if (disallowed)
            {
                Finished();
                return;
            }
            if (monsters == null) SpawnMonsters();

            //base.Update(deltaTime);

            //if (!isStarted) return;

            if (isFinished) return;

            bool monstersDead = true;
            foreach (Character monster in monsters)
            {
                if (monster.IsDead) continue;

                if (!isStarted && Vector2.Distance(monster.WorldPosition, Submarine.MainSub.WorldPosition) < 5000.0f) isStarted = true;
                    
                monstersDead = false;
                break;
            }

            if (monstersDead) Finished();
        }
    }
}
