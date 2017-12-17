using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class MonsterEvent : ScriptedEvent
    {
        private string characterFile;

        private int minAmount, maxAmount;

        private Character[] monsters;

        private bool spawnDeep;

        private bool disallowed;

        private bool repeat;
        
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
            characterFile = element.GetAttributeString("characterfile", "");

            int defaultAmount = element.GetAttributeInt("amount", 1);

            minAmount = element.GetAttributeInt("minamount", defaultAmount);
            maxAmount = Math.Max(element.GetAttributeInt("maxamount", 1), minAmount);

            var spawnPosTypeStr = element.GetAttributeString("spawntype", "");

            if (string.IsNullOrWhiteSpace(spawnPosTypeStr) ||
                !Enum.TryParse<Level.PositionType>(spawnPosTypeStr, true, out spawnPosType))
            {
                spawnPosType = Level.PositionType.MainPath;
            }

            spawnDeep = element.GetAttributeBool("spawndeep", false);

            repeat = element.GetAttributeBool("repeat", repeat);

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

            monsters = SpawnMonsters(Rand.Range(minAmount, maxAmount, Rand.RandSync.Server), false);
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized MonsterEvent (" + monsters[0]?.SpeciesName + " x" + monsters.Length + ")", Color.White);
            }
        }

        private Character[] SpawnMonsters(int amount, bool createNetworkEvent)
        {
            if (disallowed) return null;
            
            Vector2 spawnPos;
            if (!Level.Loaded.TryGetInterestingPosition(true, spawnPosType, 20000.0f, out spawnPos))
            {
                //no suitable position found, disable the event
                repeat = false;
                Finished();
                return null;
            }            

            var monsters = new Character[amount];

            if (spawnDeep) spawnPos.Y -= Level.Loaded.Size.Y;
                
            for (int i = 0; i < amount; i++)
            {
                spawnPos.X += Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
                spawnPos.Y += Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
                monsters[i] = Character.Create(characterFile, spawnPos, null, GameMain.Client != null, true, createNetworkEvent);
            }

            return monsters;
        }

        public override void Update(float deltaTime)
        {
            if (disallowed)
            {
                Finished();
                return;
            }
            
            if (repeat)
            {
                //clients aren't allowed to spawn more monsters mid-round
                if (GameMain.Client != null)
                {
                    return;
                }

                for (int i = 0; i < monsters.Length; i++)
                {
                    if (monsters[i] == null || monsters[i].Removed || monsters[i].IsDead)
                    {
                        monsters[i] = SpawnMonsters(1, true)[0];
                    }
                }
            }

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

            if (monstersDead && !repeat) Finished();
        }
    }
}
