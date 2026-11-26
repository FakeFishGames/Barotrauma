using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {

        private class MonsterWave
        {
            public readonly List<(CharacterPrefab character, Point amountRange)> MonsterPrefabs;
            /// <summary>
            /// A delay after spawning the previous wave after which this wave if forced to spawn, regardless if the previous wave has been killed or not. 
            /// If 0, the wave is not forced to spawn, but will spawn after the previous wave has been killed and the <see cref="DelayAfterPreviousWave"/> has passed.
            /// </summary>
            public readonly float ForceSpawnAfter;
            /// <summary>
            /// A delay between the previous wave getting killed and this wave spawning.
            /// </summary>
            public readonly float DelayAfterPreviousWave;
            public readonly List<Character> Monsters = [];

            public float ForcedSpawnTimer;
            public float TimerAfterPreviousWave;

            public MonsterWave(List<(CharacterPrefab character, Point amountRange)> monsterPrefabs, float forceSpawnAfter, float delayAfterPreviousWave)
            {
                MonsterPrefabs = monsterPrefabs;
                ForceSpawnAfter = forceSpawnAfter;
                DelayAfterPreviousWave = delayAfterPreviousWave;
            }
        }

        private const int CompletedState = 1;

        private readonly List<MonsterWave> monsterWaves = [];
        private readonly List<Character> allMonsters = [];
        private readonly List<Vector2> sonarPositions = [];
        private readonly List<Vector2> tempSonarPositions = [];

        private readonly float maxSonarMarkerDistance = 10000.0f;
        private readonly Level.PositionType spawnPosType;
        private readonly List<Vector2> spawnPositions = [];

        /// <summary>
        /// How many seconds it takes for the monsters to appear at the start of the mission. Not really that relevant in normal levels where the monsters spawn quite far away from the sub,
        /// but can matter in outpost levels.
        /// </summary>
        private readonly float startDelay;

        private float startTimer;

        private int currentWaveIndex = 0;

        /// <summary>
        /// If the monsters are not killed, should it be considered to have failed (meaning the mission can't be reattempted unless <see cref="MissionPrefab.AllowRetry"/> is true?).
        /// Note that confusingly failing is not the same as "not completing the mission", but rather represents failing the mission to the point it can't be by default reattempted any longer.
        /// </summary>
        private bool failIfMonstersNotKilled = false;

        /// <summary>
        /// If set to true, the monsters don't spawn at the same position, but a random suitable spawn position is chosen individually for each monster.
        /// </summary>
        private bool separateSpawnPositionForEachMonster = false;

        /// <summary>
        /// Minimum distance from subs to spawn the monsters at, if there are suitable spawn positions that far away. If not set, defaults to 1/3 of the width of the level.
        /// </summary>
        private float? minDistFromSubs;

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (State > 0)
                {
                    yield break;
                }
                else
                {
                    foreach (Vector2 sonarPos in sonarPositions)
                    {
                        yield return (Prefab.SonarLabel, sonarPos);
                    }
                }
            }
        }

        public override bool DisallowExploreOutsideOutpost => state < CompletedState;

        public MonsterMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            Identifier speciesName = prefab.ConfigElement.GetAttributeIdentifier("monsterfile", Identifier.Empty);
            if (!speciesName.IsEmpty)
            {
                var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                if (characterPrefab != null)
                {
                    int monsterCount = Math.Min(prefab.ConfigElement.GetAttributeInt("monstercount", 1), 255);
                    monsterWaves.Add(new MonsterWave(new List<(CharacterPrefab character, Point amountRange)>() { (characterPrefab, new Point(monsterCount)) }, forceSpawnAfter: 0.0f, delayAfterPreviousWave: 0.0f));
                }
                else
                {
                    DebugConsole.ThrowError($"Error in monster mission \"{prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".",
                        contentPackage: prefab.ContentPackage);
                }
            }

            failIfMonstersNotKilled = prefab.ConfigElement.GetAttributeBool(nameof(failIfMonstersNotKilled), false);

            startDelay = prefab.ConfigElement.GetAttributeFloat(nameof(startDelay), 0.0f);
            maxSonarMarkerDistance = prefab.ConfigElement.GetAttributeFloat("maxsonarmarkerdistance", 10000.0f);

            minDistFromSubs = prefab.ConfigElement.GetAttributeNullableFloat(nameof(minDistFromSubs));            

            var spawnPosTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");
            if (string.IsNullOrWhiteSpace(spawnPosTypeStr) ||
                !Enum.TryParse(spawnPosTypeStr, true, out spawnPosType))
            {
                spawnPosType = Level.PositionType.MainPath | Level.PositionType.SidePath;
            }
            separateSpawnPositionForEachMonster = prefab.ConfigElement.GetAttributeBool(nameof(separateSpawnPositionForEachMonster), false);

            //backwards compatibility: monsters defined directly under the prefab, not in a wave -> load them as a single wave
            if (prefab.ConfigElement.GetChildElement("monster") != null)
            {
                LoadMonsterWave(prefab.ConfigElement,
                    //for backwards compatibility, default to no delay or waiting
                    defaultForceSpawnAfter: 0.0f,
                    defaultDelayAfterPreviousWave: 0.0f);
            }

            foreach (var monsterElement in prefab.ConfigElement.GetChildElements("MonsterWave"))
            {
                LoadMonsterWave(monsterElement);
            }

            if (monsterWaves.Any() && monsterWaves.First().MonsterPrefabs.Any())
            {
                var characterParams = new CharacterParams(monsterWaves.First().MonsterPrefabs.First().character.ContentFile as CharacterFile);
                description = description.Replace("[monster]",
                    TextManager.Get("character." + characterParams.SpeciesTranslationOverride).Fallback(
                    TextManager.Get("character." + characterParams.SpeciesName)));
            }
        }

        private void LoadMonsterWave(ContentXElement element, float? defaultForceSpawnAfter = null, float? defaultDelayAfterPreviousWave = null)
        {
            float forceSpawnAfter = element.GetAttributeFloat(nameof(MonsterWave.ForceSpawnAfter), defaultForceSpawnAfter ?? 10.0f);
            float delayAfterPreviousWave = element.GetAttributeFloat(nameof(MonsterWave.DelayAfterPreviousWave), defaultDelayAfterPreviousWave ?? 1.0f);
            var monsterWave = new MonsterWave([], forceSpawnAfter, delayAfterPreviousWave);

            foreach (var monsterElement in element.GetChildElements("monster"))
            {
                if (GameMain.NetworkMember == null && monsterElement.GetAttributeBool("multiplayeronly", false)) { continue; }

                Identifier speciesName = monsterElement.GetAttributeIdentifier("character", Identifier.Empty);
                int defaultCount = monsterElement.GetAttributeInt("count", -1);
                if (defaultCount < 0)
                {
                    defaultCount = monsterElement.GetAttributeInt("amount", 1);
                }
                int min = Math.Min(monsterElement.GetAttributeInt("min", defaultCount), 255);
                int max = Math.Min(Math.Max(min, monsterElement.GetAttributeInt("max", defaultCount)), 255);
                var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                if (characterPrefab != null)
                {
                    monsterWave.MonsterPrefabs.Add((characterPrefab, new Point(min, max)));
                }
                else
                {
                    DebugConsole.ThrowError($"Error in monster mission \"{Prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".",
                        contentPackage: Prefab.ContentPackage);
                }
            }
            monsterWaves.Add(monsterWave);
        }

        protected override void StartMissionSpecific(Level level)
        {
            spawnPositions.Clear();

            currentWaveIndex = 0;
            foreach (var monsterWave in monsterWaves)
            {
                monsterWave.TimerAfterPreviousWave = 0.0f;
                monsterWave.ForcedSpawnTimer = 0.0f;
                monsterWave.Monsters.Clear();
            }

            if (allMonsters.Count > 0)
            {
#if DEBUG
                throw new Exception($"monsters.Count > 0 ({allMonsters.Count})");
#else
                DebugConsole.AddWarning("Monster list was not empty at the start of a monster mission. The mission instance may not have been ended correctly on previous rounds.");
                allMonsters.Clear();            
#endif
            }

            if (tempSonarPositions.Count > 0)
            {
#if DEBUG
                throw new Exception($"tempSonarPositions.Count > 0 ({tempSonarPositions.Count})");
#else
                DebugConsole.AddWarning("Sonar position list was not empty at the start of a monster mission. The mission instance may not have been ended correctly on previous rounds.");
                tempSonarPositions.Clear();            
#endif
            }

            if (!IsClient)
            {

                foreach (var monsterWave in monsterWaves)
                {
                    foreach (var (character, amountRange) in monsterWave.MonsterPrefabs)
                    {
                        int amount = Rand.Range(amountRange.X, amountRange.Y + 1);
                        for (int i = 0; i < amount; i++)
                        {
                            if (separateSpawnPositionForEachMonster || spawnPositions.None())
                            {
                                spawnPositions.Add(GetSpawnPosition());
                            }
                            var newMonster = Character.Create(character.Identifier, spawnPositions.Last(), ToolBox.RandomSeed(8), createNetworkEvent: false);
                            monsterWave.Monsters.Add(newMonster);
                            allMonsters.Add(newMonster);
                        }
                    }
                }
                InitializeMonsters(allMonsters);
            }
        }

        private Vector2 GetSpawnPosition()
        {
            float minDistBetweenMonsterMissions = 10000;
            float mindDistFromSub = minDistFromSubs ?? Level.Loaded.Size.X * 0.3f;
            var monsterMissions = GameMain.GameSession.Missions.Select(e => e as MonsterMission).Where(m => m != null && m != this && m.spawnPositions.Any());
            if (!Level.Loaded.TryGetInterestingPosition(useSyncedRand: true, spawnPosType, mindDistFromSub, out Level.InterestingPosition spawnPos,
                    filter: p => monsterMissions.None(m => m.spawnPositions.Min(spawnPos => Vector2.DistanceSquared(p.Position.ToVector2(), spawnPos)) < minDistBetweenMonsterMissions * minDistBetweenMonsterMissions),
                    suppressWarning: true))
            {
                Level.Loaded.TryGetInterestingPosition(useSyncedRand: true, spawnPosType, mindDistFromSub, out spawnPos);
            }
            return spawnPos.Position.ToVector2();
        }

        private void InitializeMonsters(IEnumerable<Character> monsters)
        {
            foreach (var monster in monsters)
            {
                monster.Enabled = false;
                if (monster.Params.AI != null && monster.Params.AI.EnforceAggressiveBehaviorForMissions)
                {
                    monster.Params.AI.FleeHealthThreshold = 0;
                    foreach (var targetParam in monster.Params.AI.Targets)
                    {
                        if (targetParam.Tag == "engine") { continue; }
                        switch (targetParam.State)
                        {
                            case AIState.Avoid:
                            case AIState.Escape:
                            case AIState.Flee:
                            case AIState.PassiveAggressive:
                                targetParam.State = AIState.Attack;
                                break;
                        }
                    }
                }
            }
            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
            foreach (Character monster in monsters)
            {
                tempSonarPositions.Add(monster.WorldPosition + Rand.Vector(maxSonarMarkerDistance));
                if (startDelay > 0)
                {
                    monster.DisabledByEvent = true;
                }
                monster.WasSpawnedByMission = true;
            }
            if (monsters.Count() != tempSonarPositions.Count)
            {
                throw new Exception($"monsters.Count != tempSonarPositions.Count ({monsters.Count()} != {tempSonarPositions.Count})");
            }
        }

        protected override void MissionStateChanged(int previousState)
        {
            // state of 1+ here means the mission is completed
            if (previousState == 0 && State >= CompletedState)
            {
#if CLIENT
                SteamTimelineManager.OnMonsterMissionTargetsKilled(this);
#endif
            }
        }

        private static void EnableWave(MonsterWave monsterWave)
        {
            //a bit of delay between spawning the monsters to further spread them out
            float delayBetweenMonsters = Math.Min(0.5f, 
                //ensure we spawn all monsters within 2 seconds no matter how many there are
                2.0f / monsterWave.Monsters.Count);
            float delay = 0.0f;
            foreach (var monster in monsterWave.Monsters)
            {
                CoroutineManager.Invoke(() => { monster.DisabledByEvent = false; }, delay);
                delay += delayBetweenMonsters;
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (startTimer < startDelay)
            {
                bool anyPlayerInGame = true;
#if SERVER
                anyPlayerInGame = GameMain.Server.ConnectedClients.Any(c => c.InGame);
#endif
                if (anyPlayerInGame)
                {
                    startTimer += deltaTime;
                    if (startTimer >= startDelay && monsterWaves.Count > 0)
                    {
                        EnableWave(monsterWaves.First());
                    }
                }
                return;
            }

#if SERVER
            bool failureConditionMet = false;
            if (FailureCondition != MissionResultCondition.Never)
            {
                if (CheckMissionResultCondition(FailureCondition, level.StartOutpost ?? Submarine.MainSub, 
                    characters: GameSession.GetSessionCrewCharacters(CharacterType.Both), out OutpostDestroyedState outpostDestroyedReason))
                {
                    OutpostDestroyedReason = outpostDestroyedReason;
                    failureConditionMet = true;
                    endTimer += deltaTime;
                }
            }
#endif

            switch (State)
            {
                case 0:
                    //keep sonar markers within maxSonarMarkerDistance from the monster(s)
                    for (int i = 0; i < tempSonarPositions.Count; i++)
                    {
                        if (allMonsters.Count != tempSonarPositions.Count)
                        {
                            throw new Exception($"monsters.Count != tempSonarPositions.Count ({allMonsters.Count} != {tempSonarPositions.Count})");
                        }

                        if (i < 0 || i >= allMonsters.Count)
                        {
                            throw new Exception($"Index {i} outside of bounds 0-{allMonsters.Count} ({tempSonarPositions.Count})");
                        }

                        if (allMonsters[i].Removed || allMonsters[i].IsDead || allMonsters[i].DisabledByEvent) { continue; }
                        Vector2 diff = tempSonarPositions[i] - allMonsters[i].WorldPosition;

                        float maxDist = maxSonarMarkerDistance; 
                        Submarine refSub = Character.Controlled?.Submarine ?? Submarine.MainSub;
                        if (refSub != null)
                        {
                            Vector2 refPos = refSub == null ? Vector2.Zero : refSub.WorldPosition;
                            float subDist = Vector2.Distance(refPos, tempSonarPositions[i]) / maxDist;

                            maxDist = Math.Min(subDist * subDist * maxDist, maxDist);
                            maxDist = Math.Min(Vector2.Distance(refPos, allMonsters[i].WorldPosition), maxDist);
                        }

                        if (diff.LengthSquared() > maxDist * maxDist)
                        {
                            tempSonarPositions[i] = allMonsters[i].WorldPosition + Vector2.Normalize(diff) * maxDist;
                        }
                    }
                    
                    sonarPositions.Clear();
                    for (int i = 0; i < allMonsters.Count; i++)
                    {
                        if (allMonsters[i].Removed || allMonsters[i].IsDead || allMonsters[i].DisabledByEvent) { continue; }
                        //don't add another label if there's another monster roughly at the same spot
                        if (sonarPositions.All(p => Vector2.DistanceSquared(p, tempSonarPositions[i]) > 1000.0f * 1000.0f))
                        {
                            sonarPositions.Add(tempSonarPositions[i]);
                        }
                    }

                    if (!IsClient)
                    {
                        bool allMonstersKilled = allMonsters.Where(static m => !m.DisabledByEvent).All(IsEliminated);
                        if (allMonstersKilled)
                        {
                            //all currently spawned monsters killed, complete the mission if there are no more waves to spawn
                            if (currentWaveIndex >= monsterWaves.Count - 1)
                            {
                                State = CompletedState;
                            }
                        }

                        if (currentWaveIndex < monsterWaves.Count - 1)
                        {
                            //more waves to go, check if the next one is ready to spawn
                            int nextWaveIndex = currentWaveIndex + 1;
                            MonsterWave nextWave = monsterWaves[nextWaveIndex];

                            if (nextWave.ForceSpawnAfter > 0.0f)
                            {
                                nextWave.ForcedSpawnTimer += deltaTime;
                            }
                            if (allMonstersKilled)
                            {
                                nextWave.TimerAfterPreviousWave += deltaTime;
                            }
                            if ((nextWave.ForceSpawnAfter > 0.0f && nextWave.ForcedSpawnTimer > nextWave.ForceSpawnAfter) ||
                                (allMonstersKilled && nextWave.TimerAfterPreviousWave > nextWave.DelayAfterPreviousWave))
                            {
                                currentWaveIndex = nextWaveIndex;
                                EnableWave(nextWave);                                
                            }
                        }
                    }
                    break;
                case CompletedState:
#if SERVER
                    if (!failureConditionMet &&
                        Level.Loaded is { Type: LevelData.LevelType.Outpost } && 
                        GameMain.GameSession.GameMode is not CampaignMode)
                    {
                        endTimer += deltaTime;
                    }
#endif
                    break;
            }
#if SERVER
            //outside the campaign mode, end the mission automatically one failed/completed (no need to navigate to the end of the level)
            if (endTimer > EndDelay && GameMain.GameSession.GameMode is not CampaignMode)
            {
                GameMain.Server?.EndGame();
            }
#endif
        }

        protected override bool DetermineCompleted(CampaignMode.TransitionType transitionType)
        {
            return state > 0;
        }

        protected override void EndMissionSpecific(bool completed)
        {
            foreach (Character monster in allMonsters)
            {
                //monsters in player subs can be saved and persist in the sub,
                //monsters in an outpost shouldn't (we can assume the people in the outpost would've killed them eventually)
                if (!monster.InPlayerSubmarine)
                {
                    monster.DisableCreatureSaving = true;
                }
            }

            tempSonarPositions.Clear();
            allMonsters.Clear();
            if (completed)
            {
                if (level?.LevelData != null && Prefab.Tags.Contains("huntinggrounds"))
                {
                    level.LevelData.HasHuntingGrounds = false;
                }
            }
            else if (failIfMonstersNotKilled)
            {
                failed = true;
            }
#if SERVER
            endTimer = 0.0f;
#endif
            currentWaveIndex = 0;
        }

        public static bool IsEliminated(Character enemy) =>
            enemy == null ||
            enemy.Removed || 
            enemy.IsDead || 
            enemy.AIController is EnemyAIController ai && ai.State == AIState.Flee;
    }
}
