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
        private string speciesName;

        private int minAmount, maxAmount;

        private List<Character> monsters;

        private bool spawnDeep;

        private Vector2? spawnPos;

        private bool disallowed;
                
        private Level.PositionType spawnPosType;

        private bool spawnPending;

        private string characterFileName;
        
        public override Vector2 DebugDrawPos
        {
            get { return spawnPos.HasValue ? spawnPos.Value : Vector2.Zero; }
        }
        
        public override string ToString()
        {
            if (maxAmount <= 1)
            {
                return "MonsterEvent (" + characterFileName + ")";
            }
            else if (minAmount < maxAmount)
            {
                return "MonsterEvent (" + characterFileName + " x" + minAmount + "-" + maxAmount + ")";
            }
            else
            {
                return "MonsterEvent (" + characterFileName + " x" + maxAmount + ")";
            }
        }

        public MonsterEvent(ScriptedEventPrefab prefab)
            : base (prefab)
        {
            speciesName = prefab.ConfigElement.GetAttributeString("characterfile", "");
            if (string.IsNullOrEmpty(speciesName))
            {
                throw new Exception("speciesname is null!");
            }

            int defaultAmount = prefab.ConfigElement.GetAttributeInt("amount", 1);
            minAmount = prefab.ConfigElement.GetAttributeInt("minamount", defaultAmount);
            maxAmount = Math.Max(prefab.ConfigElement.GetAttributeInt("maxamount", 1), minAmount);

            var spawnPosTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");

            if (string.IsNullOrWhiteSpace(spawnPosTypeStr) ||
                !Enum.TryParse(spawnPosTypeStr, true, out spawnPosType))
            {
                spawnPosType = Level.PositionType.MainPath;
            }

            spawnDeep = prefab.ConfigElement.GetAttributeBool("spawndeep", false);
            characterFileName = Path.GetFileName(Path.GetDirectoryName(speciesName)).ToLower();

            if (GameMain.NetworkMember != null)
            {
                List<string> monsterNames = GameMain.NetworkMember.ServerSettings.MonsterEnabled.Keys.ToList();
                string tryKey = monsterNames.Find(s => characterFileName == s.ToLower());

                if (!string.IsNullOrWhiteSpace(tryKey))
                {
                    if (!GameMain.NetworkMember.ServerSettings.MonsterEnabled[tryKey]) disallowed = true; //spawn was disallowed by host
                }
            }
        }

        public override IEnumerable<ContentFile> GetFilesToPreload()
        {
            string path = Character.GetConfigFilePath(speciesName);
            if (string.IsNullOrWhiteSpace(path))
            {
                DebugConsole.ThrowError($"Failed to find config file for species \"{speciesName}\"");
                yield break;
            }
            else
            {
                yield return new ContentFile(path, ContentType.Character);
            }
        }

        public override bool CanAffectSubImmediately(Level level)
        {
            float maxRange = Items.Components.Sonar.DefaultSonarRange * 0.8f;

            List<Vector2> positions = GetAvailableSpawnPositions();
            foreach (Vector2 position in positions)
            {
                if (Vector2.DistanceSquared(position, Submarine.MainSub.WorldPosition) < maxRange * maxRange)
                {
                    return true;
                }
            }

            return false;
        }

        public override void Init(bool affectSubImmediately)
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized MonsterEvent (" + speciesName + ")", Color.White);
            }
        }

        private List<Vector2> GetAvailableSpawnPositions()
        {
            var availablePositions = Level.Loaded.PositionsOfInterest.FindAll(p => spawnPosType.HasFlag(p.PositionType));

            List<Vector2> positions = new List<Vector2>();
            foreach (var allowedPosition in availablePositions)
            {
                if (Level.Loaded.ExtraWalls.Any(w => w.Cells.Any(c => c.IsPointInside(allowedPosition.Position.ToVector2())))) { continue; }
                positions.Add(allowedPosition.Position.ToVector2());
            }

            if (spawnDeep)
            {
                for (int i = 0; i < positions.Count; i++)
                {
                    positions[i] = new Vector2(positions[i].X, positions[i].Y - Level.Loaded.Size.Y);
                }
            }

            positions.RemoveAll(pos => pos.Y < Level.Loaded.GetBottomPosition(pos.X).Y);
            
            return positions;
        }

        private void FindSpawnPosition(bool affectSubImmediately)
        {
            if (disallowed) { return; }

            spawnPos = Vector2.Zero;
            var availablePositions = GetAvailableSpawnPositions();
            if (affectSubImmediately && spawnPosType != Level.PositionType.Ruin)
            {
                if (availablePositions.Count == 0)
                {
                    //no suitable position found, disable the event
                    Finished();
                    return;
                }

                float closestDist = float.PositiveInfinity;
                //find the closest spawnposition that isn't too close to any of the subs
                foreach (Vector2 position in availablePositions)
                {
                    float dist = Vector2.DistanceSquared(position, Submarine.MainSub.WorldPosition);
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub.IsOutpost) { continue; }
                        float minDistToSub = GetMinDistanceToSub(sub);
                        if (dist > minDistToSub * minDistToSub && dist < closestDist)
                        {
                            closestDist = dist;
                            spawnPos = position;
                        }
                    }
                }

                //only found a spawnpos that's very far from the sub, pick one that's closer
                //and wait for the sub to move further before spawning
                if (closestDist > 15000.0f * 15000.0f)
                {
                    foreach (Vector2 position in availablePositions)
                    {
                        float dist = Vector2.DistanceSquared(position, Submarine.MainSub.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            spawnPos = position;
                        }
                    }
                }
            }
            else
            {
                float minDist = spawnPosType == Level.PositionType.Ruin ? 0.0f : 20000.0f;
                availablePositions.RemoveAll(p => Vector2.Distance(Submarine.MainSub.WorldPosition, p) < minDist);
                if (availablePositions.Count == 0)
                {
                    //no suitable position found, disable the event
                    Finished();
                    return;
                }

                spawnPos = availablePositions[Rand.Int(availablePositions.Count, Rand.RandSync.Server)];
            }
            spawnPending = true;
        }

        private float GetMinDistanceToSub(Submarine submarine)
        {
            //9000 units is slightly less than the default range of the sonar
            return Math.Max(Math.Max(submarine.Borders.Width, submarine.Borders.Height), 9000.0f);
        }

        public override void Update(float deltaTime)
        {
            if (disallowed)
            {
                Finished();
                return;
            }

            if (isFinished) { return; }

            if (spawnPos == null)
            {
                FindSpawnPosition(affectSubImmediately: true);
                spawnPending = true;
            }

            bool spawnReady = false;
            if (spawnPending)
            {
                //wait until there are no submarines at the spawnpos
                foreach (Submarine submarine in Submarine.Loaded)
                {
                    if (submarine.IsOutpost) { continue; }
                    float minDist = GetMinDistanceToSub(submarine);
                    if (Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value) < minDist * minDist) return;
                }

                spawnPending = false;

                //+1 because Range returns an integer less than the max value
                int amount = Rand.Range(minAmount, maxAmount + 1);
                monsters = new List<Character>();
                float offsetAmount = spawnPosType == Level.PositionType.MainPath ? 1000 : 100;
                for (int i = 0; i < amount; i++)
                {
                    CoroutineManager.InvokeAfter(() =>
                    {
                        System.Diagnostics.Debug.Assert(GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer, "Clients should not create monster events.");

                        monsters.Add(Character.Create(speciesName, spawnPos.Value + Rand.Vector(offsetAmount), Level.Loaded.Seed + i.ToString(), null, false, true, true));

                        if (monsters.Count == amount)
                        {
                            spawnReady = true;
                            //this will do nothing if the monsters have no swarm behavior defined, 
                            //otherwise it'll make the spawned characters act as a swarm
                            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
                        }
                    }, Rand.Range(0f, amount / 2));
                }
            }

            if (!spawnReady) { return; }

            Entity targetEntity = Submarine.FindClosest(GameMain.GameScreen.Cam.WorldViewCenter);
#if CLIENT
            if (Character.Controlled != null) targetEntity = (Entity)Character.Controlled;
#endif
            
            bool monstersDead = true;
            foreach (Character monster in monsters)
            {
                if (!monster.IsDead)
                {
                    monstersDead = false;

                    if (targetEntity != null && Vector2.DistanceSquared(monster.WorldPosition, targetEntity.WorldPosition) < 5000.0f * 5000.0f)
                    {
                        break;
                    }
                }
            }

            if (monstersDead) Finished();
        }
    }
}
