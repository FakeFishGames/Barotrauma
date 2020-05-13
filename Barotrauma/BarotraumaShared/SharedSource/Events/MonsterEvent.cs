using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class MonsterEvent : ScriptedEvent
    {
        private readonly string speciesName;
        private readonly int minAmount, maxAmount;
        private List<Character> monsters;

        private readonly bool spawnDeep;

        private Vector2? spawnPos;

        private readonly bool disallowed;

        private readonly Level.PositionType spawnPosType;

        private bool spawnPending;

        public override Vector2 DebugDrawPos
        {
            get { return spawnPos ?? Vector2.Zero; }
        }

        public override string ToString()
        {
            if (maxAmount <= 1)
            {
                return "MonsterEvent (" + speciesName + ")";
            }
            else if (minAmount < maxAmount)
            {
                return "MonsterEvent (" + speciesName + " x" + minAmount + "-" + maxAmount + ")";
            }
            else
            {
                return "MonsterEvent (" + speciesName + " x" + maxAmount + ")";
            }
        }

        public MonsterEvent(ScriptedEventPrefab prefab)
            : base (prefab)
        {
            speciesName = prefab.ConfigElement.GetAttributeString("characterfile", "");
            CharacterPrefab characterPrefab = CharacterPrefab.FindByFilePath(speciesName);
            if (characterPrefab != null)
            {
                speciesName = characterPrefab.Identifier;
            }

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

            if (GameMain.NetworkMember != null)
            {
                List<string> monsterNames = GameMain.NetworkMember.ServerSettings.MonsterEnabled.Keys.ToList();
                string tryKey = monsterNames.Find(s => speciesName.ToLower() == s.ToLower());

                if (!string.IsNullOrWhiteSpace(tryKey))
                {
                    if (!GameMain.NetworkMember.ServerSettings.MonsterEnabled[tryKey])
                    {
                        disallowed = true; //spawn was disallowed by host
                    }
                }
            }
        }

        public override IEnumerable<ContentFile> GetFilesToPreload()
        {
            string path = CharacterPrefab.FindBySpeciesName(speciesName)?.FilePath;
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
            float maxRange = Sonar.DefaultSonarRange * 0.8f;
            return GetAvailableSpawnPositions().Any(p => Vector2.DistanceSquared(p.Position.ToVector2(), Submarine.MainSub.WorldPosition) < maxRange * maxRange);
        }

        public override void Init(bool affectSubImmediately)
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized MonsterEvent (" + speciesName + ")", Color.White);
            }
        }

        private List<Level.InterestingPosition> GetAvailableSpawnPositions()
        {
            var availablePositions = Level.Loaded.PositionsOfInterest.FindAll(p => spawnPosType.HasFlag(p.PositionType));
            var removals = new List<Level.InterestingPosition>();
            foreach (var position in availablePositions)
            {
                if (position.Submarine != null)
                {
                    if (position.Submarine.WreckAI != null && position.Submarine.WreckAI.IsAlive)
                    {
                        removals.Add(position);
                    }
                    else
                    {
                        continue;
                    }
                }
                if (position.PositionType != Level.PositionType.MainPath) { continue; }
                if (Level.Loaded.ExtraWalls.Any(w => w.Cells.Any(c => c.IsPointInside(position.Position.ToVector2()))))
                {
                    removals.Add(position);
                }
                if (spawnDeep)
                {
                    for (int i = 0; i < availablePositions.Count; i++)
                    {
                        var pos = availablePositions[i].Position;
                        pos = new Point(pos.X, pos.Y - Level.Loaded.Size.Y);
                        availablePositions[i] = new Level.InterestingPosition(pos, availablePositions[i].PositionType);
                    }
                }
                if (position.Position.Y < Level.Loaded.GetBottomPosition(position.Position.X).Y)
                {
                    removals.Add(position);
                }
            }
            removals.ForEach(r => availablePositions.Remove(r));         
            return availablePositions;
        }

        private void FindSpawnPosition(bool affectSubImmediately)
        {
            if (disallowed) { return; }

            spawnPos = Vector2.Zero;
            var availablePositions = GetAvailableSpawnPositions();
            var chosenPosition = new Level.InterestingPosition(Point.Zero, Level.PositionType.MainPath, isValid: false);
            var removedPositions = new List<Level.InterestingPosition>();
            foreach (var position in availablePositions)
            {
                if (Rand.Value(Rand.RandSync.Server) > prefab.SpawnProbability)
                {
                    removedPositions.Add(position);
                }
            }
            removedPositions.ForEach(p => availablePositions.Remove(p));
            bool isSubOrWreck = spawnPosType == Level.PositionType.Ruin || spawnPosType == Level.PositionType.Wreck;
            if (affectSubImmediately && !isSubOrWreck)
            {
                if (availablePositions.None())
                {
                    //no suitable position found, disable the event
                    Finished();
                    return;
                }
                float closestDist = float.PositiveInfinity;
                //find the closest spawnposition that isn't too close to any of the subs
                foreach (var position in availablePositions)
                {
                    Vector2 pos = position.Position.ToVector2();
                    float dist = Vector2.DistanceSquared(pos, Submarine.MainSub.WorldPosition);
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub.Info.Type != SubmarineInfo.SubmarineType.Player) { continue; }
                        float minDistToSub = GetMinDistanceToSub(sub);
                        if (dist > minDistToSub * minDistToSub && dist < closestDist)
                        {
                            closestDist = dist;
                            chosenPosition = position;
                        }
                    }
                }
                //only found a spawnpos that's very far from the sub, pick one that's closer
                //and wait for the sub to move further before spawning
                if (closestDist > 15000.0f * 15000.0f)
                {
                    foreach (var position in availablePositions)
                    {
                        float dist = Vector2.DistanceSquared(position.Position.ToVector2(), Submarine.MainSub.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            chosenPosition = position;
                        }
                    }
                }
            }
            else
            {
                if (!isSubOrWreck)
                {
                    float minDistance = 20000;
                    availablePositions.RemoveAll(p => Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, p.Position.ToVector2()) < minDistance * minDistance);
                }
                if (availablePositions.None())
                {
                    //no suitable position found, disable the event
                    Finished();
                    return;
                }
                chosenPosition = availablePositions.GetRandom();
            }
            if (chosenPosition.IsValid)
            {
                spawnPos = chosenPosition.Position.ToVector2();
                if (chosenPosition.Submarine != null || chosenPosition.Ruin != null)
                {
                    var spawnPoint = WayPoint.GetRandom(SpawnType.Enemy, sub: chosenPosition.Submarine, ruin: chosenPosition.Ruin, useSyncedRand: false);
                    if (spawnPoint != null) 
                    {
                        System.Diagnostics.Debug.Assert(spawnPoint.Submarine == chosenPosition.Submarine);
                        System.Diagnostics.Debug.Assert(spawnPoint.ParentRuin == chosenPosition.Ruin);
                        spawnPos = spawnPoint.WorldPosition; 
                    }
                }
                spawnPending = true;
            }
        }

        private float GetMinDistanceToSub(Submarine submarine)
        {
            return Math.Max(Math.Max(submarine.Borders.Width, submarine.Borders.Height), Sonar.DefaultSonarRange * 0.9f);
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
                if (spawnPosType == Level.PositionType.MainPath)
                {
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineInfo.SubmarineType.Player) { continue; }
                        float minDist = GetMinDistanceToSub(submarine);
                        if (Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value) < minDist * minDist) { return; }
                    }
                }

                //if spawning in a ruin/cave, wait for someone to be close to it to spawning 
                //unnecessary monsters in places the players might never visit during the round
                if (spawnPosType == Level.PositionType.Ruin || spawnPosType == Level.PositionType.Cave || spawnPosType == Level.PositionType.Wreck)
                {
                    bool someoneNearby = false;
                    float minDist = Sonar.DefaultSonarRange * 0.8f;
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineInfo.SubmarineType.Player) { continue; }
                        if (Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value) < minDist * minDist)
                        {
                            someoneNearby = true;
                            break;
                        }
                    }
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c == Character.Controlled || c.IsRemotePlayer)
                        {
                            if (Vector2.DistanceSquared(c.WorldPosition, spawnPos.Value) < minDist * minDist)
                            {
                                someoneNearby = true;
                                break;
                            }
                        }
                    }
                    if (!someoneNearby) { return; }
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
                        //round ended before the coroutine finished
                        if (GameMain.GameSession == null || Level.Loaded == null) { return; }
						
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
            if (Character.Controlled != null) { targetEntity = Character.Controlled; }
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

            if (monstersDead) { Finished(); }
        }
    }
}
