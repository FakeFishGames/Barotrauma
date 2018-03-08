using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
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

        private int Respawned;

        public int MaxRespawned;
        
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

            MaxRespawned = maxAmount * GameMain.NilMod.CreatureMaxRespawns;

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
                string characterName = Path.GetFileName(Path.GetDirectoryName(characterFile)).ToLower();
                string tryKey = monsterNames.Find(s => characterName == s.ToLower());
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
                if (monsters != null)
                {
                    DebugConsole.NewMessage("Initialized MonsterEvent (" + monsters[0]?.SpeciesName + " x" + monsters.Length + ")", Color.White);
                }
            }
        }

        private Character[] SpawnMonsters(int amount, bool createNetworkEvent)
        {
            if (disallowed) return null;
            
            Vector2 spawnPos;
            float minDist = spawnPosType == Level.PositionType.Ruin ? 0.0f : 20000.0f;
            if (!Level.Loaded.TryGetInterestingPosition(true, spawnPosType, minDist, out spawnPos))
            {
                //no suitable position found, disable the event
                repeat = false;
                Finished();
                return null;
            }            

            var monsters = new Character[amount];

            if (spawnDeep)
            {
                spawnPos.Y -= Level.Loaded.Size.Y;
                //disable the event if the ocean floor is too high up to spawn the monster deep 
                if (spawnPos.Y < Level.Loaded.GetBottomPosition(spawnPos.X).Y)
                {
                    repeat = false;
                    Finished();
                    return null;
                }
            }

            for (int i = 0; i < amount; i++)
            {
                spawnPos.X += Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
                spawnPos.Y += Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server);
                monsters[i] = Character.Create(characterFile, spawnPos, null, GameMain.Client != null, true, createNetworkEvent);
#if CLIENT
                if (GameMain.Server != null)
                {
                    GameSession.inGameInfo.AddNoneClientCharacter(monsters[i]);

                    if (createNetworkEvent && GameMain.NilMod.CreatureLimitRespawns)
                    {
                        GameMain.Server.ServerLog.WriteLine("Respawning creature: " + monsters[i].Name + " - respawns used: " + Respawned + " / " + MaxRespawned, Networking.ServerLog.MessageType.Spawns);
                    }
                    else if (createNetworkEvent && !GameMain.NilMod.CreatureLimitRespawns)
                    {
                        GameMain.Server.ServerLog.WriteLine("Respawning creature: " + monsters[i].Name + " - respawns used: " + Respawned + " / Infinite", Networking.ServerLog.MessageType.Spawns);
                    }
                }
#endif
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
            
            if (repeat && GameMain.NilMod.CreatureRespawnMonsterEvents)
            {
                //clients aren't allowed to spawn more monsters mid-round
                if (GameMain.Client != null)
                {
                    return;
                }

                if ((GameMain.NilMod.CreatureLimitRespawns && Respawned < MaxRespawned) || !GameMain.NilMod.CreatureLimitRespawns)
                {
                    for (int i = 0; i < monsters.Length; i++)
                    {
                        if (monsters[i] == null || monsters[i].Removed || monsters[i].IsDead)
                        {
                            monsters[i] = SpawnMonsters(1, true)[0];
                            Respawned += 1;
                        }
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
