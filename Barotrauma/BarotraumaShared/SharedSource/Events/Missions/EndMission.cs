using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    partial class EndMission : Mission
    {
        enum MissionPhase
        {
            Initial,
            NoItemsDestroyed,
            SomeItemsDestroyed,
            AllItemsDestroyed,
            BossKilled
        }

        private readonly CharacterPrefab bossPrefab;
        private readonly CharacterPrefab minionPrefab;

        private readonly Identifier spawnPointTag;
        private readonly Identifier destructibleItemTag;

        private readonly string endCinematicSound;

        private ImmutableArray<Character> minions;
        private readonly int minionCount;
        private readonly float minionScatter;

        private Character boss;

        private readonly ItemPrefab projectilePrefab;

        private float projectileTimer = 30.0f;

        private readonly float startCinematicDistance = 30.0f;

        private float endCinematicTimer;

        private readonly List<Item> destructibleItems = new List<Item>();

        protected readonly float wakeUpCinematicDelay = 5.0f;
        protected readonly float bossWakeUpDelay = 7.0f;
        protected readonly float cameraWaitDuration = 7.0f;

        public override IEnumerable<(LocalizedString Label, Vector2 Position)> SonarLabels
        {
            get { return destructibleItems.Where(it => it.Condition > 0.0f).Select(it => (Prefab.SonarLabel, it.WorldPosition)); }
        }

        public override int State
        {
            get { return base.State; }
            set
            {

                if (state != value)
                {
                    base.State = value;
                    OnStateChangedProjSpecific();
                    if (Phase == MissionPhase.AllItemsDestroyed)
                    {
                        CoroutineManager.Invoke(() =>
                        {
                            if (boss != null && !boss.Removed)
                            {
                                boss.AnimController.ColliderIndex = 1;
                            }
                        }, delay: wakeUpCinematicDelay + bossWakeUpDelay + 2);
                    }
                }
            }
        }

        private MissionPhase Phase
        {
            get
            {
                //state 0: nothing happens yet, play a cinematic and skip to the next state when close enough to the boss
                //state 1: start cinematic played
                //state 2: first destructibleItems destroyed
                //state 3: 2nd destructibleItems destroyed
                //state 4: all destructibleItems destroyed
                //state 5: boss killed
                if (state == 0) { return MissionPhase.Initial; }
                if (state == 1) { return MissionPhase.NoItemsDestroyed; }
                if (state < destructibleItems.Count + 1) { return MissionPhase.SomeItemsDestroyed; }
                if (state < destructibleItems.Count + 2) { return MissionPhase.AllItemsDestroyed; }
                return MissionPhase.BossKilled;
            }
        }

        public EndMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            Identifier speciesName = prefab.ConfigElement.GetAttributeIdentifier("bossfile", Identifier.Empty);
            if (!speciesName.IsEmpty)
            {
                bossPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                if (bossPrefab == null)
                {
                    DebugConsole.ThrowError($"Error in end mission \"{prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".");
                }
            }
            else
            {
                DebugConsole.ThrowError($"Error in end mission \"{prefab.Identifier}\". Monster file not set.");
            }

            Identifier minionName = prefab.ConfigElement.GetAttributeIdentifier("minionfile", Identifier.Empty);
            if (!minionName.IsEmpty)
            {
                minionPrefab = CharacterPrefab.FindBySpeciesName(minionName);
                if (minionPrefab == null)
                {
                    DebugConsole.ThrowError($"Error in end mission \"{prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".");
                }
            }

            minionCount = Math.Min(prefab.ConfigElement.GetAttributeInt(nameof(minionCount), 0), 255);
            minionScatter = Math.Min(prefab.ConfigElement.GetAttributeFloat(nameof(minionScatter), 0), 10000);

            Identifier projectileId = prefab.ConfigElement.GetAttributeIdentifier("projectile", Identifier.Empty);
            if (!projectileId.IsEmpty)
            {
                projectilePrefab = MapEntityPrefab.FindByIdentifier(projectileId) as ItemPrefab;
                if (projectilePrefab == null)
                {
                    DebugConsole.ThrowError($"Error in end mission \"{prefab.Identifier}\". Could not find an item prefab with the name \"{projectileId}\".");
                }
            }

            spawnPointTag = prefab.ConfigElement.GetAttributeIdentifier(nameof(spawnPointTag), Identifier.Empty);
            destructibleItemTag = prefab.ConfigElement.GetAttributeIdentifier(nameof(destructibleItemTag), Identifier.Empty);
            endCinematicSound = prefab.ConfigElement.GetAttributeString(nameof(endCinematicSound), string.Empty);
            startCinematicDistance = prefab.ConfigElement.GetAttributeFloat(nameof(startCinematicDistance), 0);
        }

        protected override void StartMissionSpecific(Level level)
        {
            var spawnPoint = WayPoint.WayPointList.FirstOrDefault(wp => wp.Tags.Contains(spawnPointTag));
            if (spawnPoint == null)
            {
                DebugConsole.ThrowError($"Error in end mission \"{Prefab.Identifier}\". Could not find a spawn point \"{spawnPointTag}\".");
                return;
            }
            if (!IsClient)
            {
                boss = Character.Create(bossPrefab.Identifier, spawnPoint.WorldPosition, ToolBox.RandomSeed(8), createNetworkEvent: false);
                var minionList = new List<Character>();
                float angle = 0;
                float angleStep = MathHelper.TwoPi / Math.Max(minionCount, 1);
                for (int i = 0; i < minionCount; i++)
                {
                    minionList.Add(Character.Create(minionPrefab.Identifier, MathUtils.GetPointOnCircumference(spawnPoint.WorldPosition, minionScatter, angle), ToolBox.RandomSeed(8), createNetworkEvent: false));
                    angle += angleStep;
                }
                SwarmBehavior.CreateSwarm(minionList.Cast<AICharacter>());
                minions = minionList.ToImmutableArray();
            }
            if (destructibleItemTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in end mission \"{Prefab.Identifier}\". Destructible item tag not set.");
                return;
            }
            destructibleItems.Clear();
            destructibleItems.AddRange(Item.ItemList.FindAll(it => it.HasTag(destructibleItemTag)));
            if (destructibleItems.None())
            {
                DebugConsole.ThrowError($"Error in end mission \"{Prefab.Identifier}\". Could not find any destructible items with the tag \"{spawnPointTag}\".");
                return;
            }
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            UpdateProjSpecific();

            if (state == 0)
            {
                if (startCinematicDistance <= 0.0f ||  
                    boss == null || Submarine.MainSub == null || 
                    Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, boss.WorldPosition) <= startCinematicDistance * startCinematicDistance)
                {
                    State = 1;
                }
                return;
            }

            if (!IsClient && State > 0)
            {
                State = Math.Max(State, destructibleItems.Count(it => it.Condition <= 0.0f) + 1);
            }

            if (Phase == MissionPhase.AllItemsDestroyed)
            {
                if (projectilePrefab != null && boss != null && !boss.IsDead && !boss.Removed)
                {
                    projectileTimer -= deltaTime;
                    if (projectileTimer <= 0.0f)
                    {
                        float dist = Vector2.Distance(Submarine.MainSub.WorldPosition, boss.WorldPosition);
                        float distanceFactor = Math.Min(dist / 10000.0f, 1.0f);
                        int projectileAmount = Rand.Range(3, 6);
                        //more concentrated shots the further the sub is
                        float spread = MathHelper.ToRadians(Rand.Range(20.0f, 180.0f)) * Math.Max(1.0f - distanceFactor, 0.2f);
                        for (int i = 0; i < projectileAmount; i++)
                        {
                            int index = i;
                            Entity.Spawner.AddItemToSpawnQueue(projectilePrefab, boss.WorldPosition, onSpawned: it =>
                            {
                                var projectile = it.GetComponent<Projectile>();
                                float angle = MathUtils.VectorToAngle(Submarine.MainSub.WorldPosition - boss.WorldPosition);
                                if (projectileAmount > 1)
                                {
                                    angle += (index / (float)(projectileAmount - 1) - 0.5f) * spread;
                                }
                                it.body.SetTransform(it.SimPosition, angle);
                                it.UpdateTransform();
                                //faster launch velocity the further the sub is
                                projectile.Use(launchImpulseModifier: MathHelper.Lerp(0, 5, distanceFactor));
                            });
                        }

                        //the closer the sub is, more likely it is to shoot frequently
                        float shortIntervalProbability = MathHelper.Lerp(0.9f, 0.05f, distanceFactor);
                        if (Rand.Range(0.0f, 1.0f) < shortIntervalProbability)
                        {
                            projectileTimer = Rand.Range(3.0f, 5.0f);
                        }
                        else
                        {
                            projectileTimer = Rand.Range(15f, 30f);
                        }
                    }
                }
                else
                {
                    State = Math.Max(destructibleItems.Count + 2, State);
                }
            }
            else if (Phase == MissionPhase.BossKilled)
            {
                const float EndCinematicDuration = 20.0f;

                endCinematicTimer += deltaTime;
#if CLIENT
                Screen.Selected.Cam.Shake = MathHelper.Clamp(MathF.Pow(endCinematicTimer, 3), 5.0f, 200.0f);


                Screen.Selected.Cam.Rotation = 
                    Math.Max((endCinematicTimer - 5.0f) * 0.05f, 0.0f)
                     + (PerlinNoise.GetPerlin(endCinematicTimer * 0.1f, endCinematicTimer * 0.05f) - 0.5f) * 0.5f * (endCinematicTimer / EndCinematicDuration);
                if (Rand.Range(0.0f, 100.0f) < endCinematicTimer)
                {
                    Level.Loaded.Renderer.Flash();
                }
                Level.Loaded.Renderer.ChromaticAberrationStrength = endCinematicTimer * 5;
                Level.Loaded.Renderer.CollapseEffectOrigin = boss.WorldPosition;
                Level.Loaded.Renderer.CollapseEffectStrength = endCinematicTimer / EndCinematicDuration;
#endif
                if (endCinematicTimer > 5 && !IsClient)
                {
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c.AIController is EnemyAIController enemyAI && enemyAI.PetBehavior == null)
                        {
                            c.SetAllDamage(200.0f, 0.0f, 0.0f);
                        }
                    }
                }

                if (endCinematicTimer > EndCinematicDuration && !IsClient)
                {
                    //endCinematicTimer = 0;
                    GameMain.GameSession.Campaign?.LoadNewLevel();
                }
            }
            
        }

        partial void UpdateProjSpecific();

        partial void OnStateChangedProjSpecific();

        protected override bool DetermineCompleted()
        {
            return Phase == MissionPhase.BossKilled;
        }
    }
}
