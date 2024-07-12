using System;
using Barotrauma.Extensions;
using Barotrauma.RuinGeneration;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    [TypePreviouslyKnownAs("AlienRuinMission")]
    partial class EliminateTargetsMission : Mission
    {
        private readonly Identifier[] targetItemIdentifiers;
        private readonly Identifier[] targetEnemyIdentifiers;
        private readonly int minEnemyCount;
        private readonly HashSet<Entity> existingTargets = new HashSet<Entity>();
        private readonly HashSet<Character> spawnedTargets = new HashSet<Character>();
        private readonly HashSet<Entity> allTargets = new HashSet<Entity>();

        public readonly SubmarineType TargetSubType;
        public readonly bool PrioritizeThalamus;

        private Submarine TargetSub { get; set; }

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get
            {
                if (State == 0)
                {
                    return allTargets
                        .Where(t => (t is Item i && !IsItemDestroyed(i)) || (t is Character c && !IsEnemyDefeated(c)))
                        .Select(t => (Prefab.SonarLabel, t.WorldPosition));
                }
                else
                {
                    return Enumerable.Empty<(LocalizedString Label, Vector2 Position)>();
                }
            }
        }

        public EliminateTargetsMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            targetItemIdentifiers = prefab.ConfigElement.GetAttributeIdentifierArray("targetitems", Array.Empty<Identifier>());
            targetEnemyIdentifiers = prefab.ConfigElement.GetAttributeIdentifierArray("targetenemies", Array.Empty<Identifier>());
            minEnemyCount = prefab.ConfigElement.GetAttributeInt("minenemycount", 0);
            TargetSubType = prefab.ConfigElement.GetAttributeEnum("targetsub", SubmarineType.Ruin);
            PrioritizeThalamus = prefab.RequireThalamusWreck;
        }

        protected override void StartMissionSpecific(Level level)
        {
            existingTargets.Clear();
            spawnedTargets.Clear();
            allTargets.Clear();
            if (IsClient) { return; }

            TargetSub = TargetSubType switch
            {
                SubmarineType.Wreck => FindWreck(),
                SubmarineType.Ruin => Level.Loaded?.Ruins?.GetRandom(Rand.RandSync.ServerAndClient).Submarine,
                SubmarineType.BeaconStation => Level.Loaded?.BeaconStation,
                _ => null
            };

            Submarine FindWreck()
            {
                var wrecks = Level.Loaded?.Wrecks;
                if (wrecks == null || wrecks.None()) { return null; }

                if (PrioritizeThalamus)
                {
                    var thalamusWrecks = wrecks.Where(w => w.WreckAI != null).ToArray();
                    if (thalamusWrecks.Any())
                    {
                        return thalamusWrecks.GetRandom(Rand.RandSync.ServerAndClient);
                    }
                }

                return wrecks.GetRandom(Rand.RandSync.ServerAndClient);
            }

            if (TargetSub == null)
            {
                DebugConsole.ThrowError($"Failed to initialize an {nameof(EliminateTargetsMission)} mission (\"{Prefab.Identifier}\"): level contains no submarines",
                    contentPackage: Prefab.ContentPackage);
                return;
            }
            if (targetItemIdentifiers.Length < 1 && targetEnemyIdentifiers.Length < 1)
            {
                DebugConsole.ThrowError($"Failed to initialize an {nameof(EliminateTargetsMission)} mission (\"{Prefab.Identifier}\"): no target identifiers set in the mission definition",
                    contentPackage: Prefab.ContentPackage);
                return;
            }
            foreach (var item in Item.ItemList)
            {
                if (!targetItemIdentifiers.Contains(item.Prefab.Identifier)) { continue; }
                if (item.Submarine != TargetSub) { continue; }
                existingTargets.Add(item);
                allTargets.Add(item);
            }
            int existingEnemyCount = 0;
            foreach (var character in Character.CharacterList)
            {
                if (character.SpeciesName.IsEmpty) { continue; }
                if (!targetEnemyIdentifiers.Contains(character.SpeciesName)) { continue; }
                if (character.Submarine != TargetSub) { continue; }
                existingTargets.Add(character);
                allTargets.Add(character);
                existingEnemyCount++;
            }
            if (existingEnemyCount < minEnemyCount)
            {
                var enemyPrefabs = new HashSet<CharacterPrefab>();
                foreach (Identifier identifier in targetEnemyIdentifiers)
                {
                    var prefab = CharacterPrefab.FindBySpeciesName(identifier);
                    if (prefab != null)
                    {
                        enemyPrefabs.Add(prefab);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in an Alien Ruin mission (\"{Prefab.Identifier}\"): could not find a character prefab with the species \"{identifier}\"",
                            contentPackage: Prefab.ContentPackage);
                    }
                }
                if (enemyPrefabs.None())
                {
                    DebugConsole.ThrowError($"Error in an Alien Ruin mission (\"{Prefab.Identifier}\"): no enemy species defined that could be used to spawn more ({minEnemyCount - existingEnemyCount}) enemies",
                        contentPackage: Prefab.ContentPackage);
                    return;
                }
                for (int i = 0; i < (minEnemyCount - existingEnemyCount); i++)
                {
                    var prefab = enemyPrefabs.GetRandomUnsynced();
                    var spawnPos = TargetSub.GetWaypoints(false).GetRandomUnsynced(w => w.CurrentHull != null)?.WorldPosition;
                    if (!spawnPos.HasValue)
                    {
                        DebugConsole.ThrowError($"Error in an Alien Ruin mission (\"{Prefab.Identifier}\"): no valid spawn positions could be found for the additional ({minEnemyCount - existingEnemyCount}) enemies to be spawned",
                            contentPackage: Prefab.ContentPackage);
                        return;
                    }
                    var newEnemy = Character.Create(prefab.Identifier, spawnPos.Value, ToolBox.RandomSeed(8), createNetworkEvent: false);
                    spawnedTargets.Add(newEnemy);
                    allTargets.Add(newEnemy);
                }
            }
#if DEBUG
            DebugConsole.NewMessage("********** CLEAR RUIN MISSION INFO **********");
            DebugConsole.NewMessage($"Existing item targets: {existingTargets.Count - existingEnemyCount}");
            DebugConsole.NewMessage($"Existing enemy targets: {existingEnemyCount}");
            DebugConsole.NewMessage($"Spawned enemy targets: {spawnedTargets.Count}");
#endif
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (IsClient) { return; }
            switch (State)
            {
                case 0:
                    if (!AllTargetsEliminated()) { return; }
                    State = 1;
                    break;
            }
        }

        private bool AllTargetsEliminated()
        {
            foreach (var target in allTargets)
            {
                if (target is Item targetItem)
                {
                    if (!IsItemDestroyed(targetItem))
                    {
                        return false;
                    }
                }
                else if (target is Character targetEnemy)
                {
                    if (!IsEnemyDefeated(targetEnemy))
                    {
                        return false;
                    }
                }
#if DEBUG
                else
                {
                    DebugConsole.ThrowError($"Error in Alien Ruin mission (\"{Prefab.Identifier}\"): unexpected target of type {target?.GetType()?.ToString()}",
                        contentPackage: Prefab.ContentPackage);
                }
#endif
            }
            return true;
        }

        private static bool IsItemDestroyed(Item item) => item == null || item.Removed || item.Condition <= 0.0f;

        private static bool IsEnemyDefeated(Character enemy) => enemy == null ||enemy.Removed || enemy.IsDead;

        protected override bool DetermineCompleted()
        {
            bool exitingLevel = GameMain.GameSession?.GameMode is CampaignMode campaign ?
                campaign.GetAvailableTransition() != CampaignMode.TransitionType.None :
                Submarine.MainSub is { } sub && sub.AtEitherExit;

            return State > 0 && exitingLevel;            
        }

        protected override void EndMissionSpecific(bool completed)
        {
            failed = !completed && State > 0;
        }
    }
}