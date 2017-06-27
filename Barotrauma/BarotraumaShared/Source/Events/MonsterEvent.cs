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

        private bool isActive;
        public override bool IsActive
        {
            get
            {
                return isActive;
            }
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

            if (GameMain.NetworkMember != null)
            {
                List<string> monsterNames = GameMain.NetworkMember.monsterEnabled.Keys.ToList();
                string tryKey = monsterNames.Find(s => characterFile.ToLower().Contains(s.ToLower()));
                if (!string.IsNullOrWhiteSpace(tryKey))
                {
                    if (!GameMain.NetworkMember.monsterEnabled[tryKey]) disallowed = true; //spawn was disallowed by host
                }
            }
        }

        public override void Init()
        {
            base.Init();

            SpawnMonsters();
        }

        private void SpawnMonsters()
        {
            if (disallowed) return;
            
            Vector2 spawnPos = Level.Loaded.GetRandomInterestingPosition(true, spawnPosType, true);
            
            int amount = Rand.Range(minAmount, maxAmount, Rand.RandSync.Server);

            monsters = new Character[amount];

            if (spawnDeep) spawnPos.Y -= Level.Loaded.Size.Y;
                
            for (int i = 0; i < amount; i++)
            {
                spawnPos.X += Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
                spawnPos.Y += Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
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

            if (isFinished) return;

            isActive = false;

            Entity targetEntity = null;
            if (Character.Controlled != null)
            {
                targetEntity = Character.Controlled;
            }
            else
            {
                targetEntity = Submarine.FindClosest(GameMain.GameScreen.Cam.WorldViewCenter);
            }

            bool monstersDead = true;
            foreach (Character monster in monsters)
            {
                if (!monster.IsDead)
                {
                    monstersDead = false;

                    if (targetEntity != null && Vector2.DistanceSquared(monster.WorldPosition, targetEntity.WorldPosition) < 5000.0f * 5000.0f)
                    {
                        isActive = true;
                        break;
                    }
                }
            }

            if (monstersDead) Finished();
        }
    }
}
