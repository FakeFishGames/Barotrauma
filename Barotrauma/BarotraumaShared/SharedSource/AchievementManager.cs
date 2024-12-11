using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.Steam;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    [NetworkSerialize]
    internal readonly record struct NetIncrementedStat(AchievementStat Stat, float Amount) : INetSerializableStruct;

    static class AchievementManager
    {
        private static readonly ImmutableHashSet<Identifier> SupportedAchievements = ImmutableHashSet.Create(
            "killmoloch".ToIdentifier(),
            "killhammerhead".ToIdentifier(),
            "killendworm".ToIdentifier(),
            "artifactmission".ToIdentifier(),
            "combatmission1".ToIdentifier(),
            "combatmission2".ToIdentifier(),
            "healcrit".ToIdentifier(),
            "repairdevice".ToIdentifier(),
            "traitorwin".ToIdentifier(),
            "killtraitor".ToIdentifier(),
            "killclown".ToIdentifier(),
            "healopiateaddiction".ToIdentifier(),
            "survivecrushdepth".ToIdentifier(),
            "survivereactormeltdown".ToIdentifier(),
            "healhusk".ToIdentifier(),
            "killpoison".ToIdentifier(),
            "killnuke".ToIdentifier(),
            "killtool".ToIdentifier(),
            "clowncostume".ToIdentifier(),
            "lastmanstanding".ToIdentifier(),
            "lonesailor".ToIdentifier(),
            "subhighvelocity".ToIdentifier(),
            "nodamagerun".ToIdentifier(),
            "subdeep".ToIdentifier(),
            "maxintensity".ToIdentifier(),
            "discovercoldcaverns".ToIdentifier(),
            "discovereuropanridge".ToIdentifier(),
            "discoverhydrothermalwastes".ToIdentifier(),
            "discovertheaphoticplateau".ToIdentifier(),
            "discoverthegreatsea".ToIdentifier(),
            "travel10".ToIdentifier(),
            "travel100".ToIdentifier(),
            "xenocide".ToIdentifier(),
            "genocide".ToIdentifier(),
            "cargomission".ToIdentifier(),
            "subeditor24h".ToIdentifier(),
            "crewaway".ToIdentifier(),
            "captainround".ToIdentifier(),
            "securityofficerround".ToIdentifier(),
            "engineerround".ToIdentifier(),
            "mechanicround".ToIdentifier(),
            "medicaldoctorround".ToIdentifier(),
            "assistantround".ToIdentifier(),
            "campaigncompleted".ToIdentifier(),
            "salvagewreckmission".ToIdentifier(),
            "escortmission".ToIdentifier(),
            "killcharybdis".ToIdentifier(),
            "killlatcher".ToIdentifier(),
            "killspineling_giant".ToIdentifier(),
            "killcrawlerbroodmother".ToIdentifier(),
            "ascension".ToIdentifier(),
            "campaignmetadata_pathofthebikehorn_7".ToIdentifier(),
            "campaignmetadata_coalitionspecialhire1_hired_true".ToIdentifier(),
            "campaignmetadata_coalitionspecialhire2_hired_true".ToIdentifier(),
            "campaignmetadata_separatistspecialhire1_hired_true".ToIdentifier(),
            "campaignmetadata_separatistspecialhire2_hired_true".ToIdentifier(),
            "campaignmetadata_huskcultspecialhire1_hired_true".ToIdentifier(),
            "campaignmetadata_clownspecialhire1_hired_true".ToIdentifier(),
            "scanruin".ToIdentifier(),
            "clearruin".ToIdentifier(),
            "beaconmission".ToIdentifier(),
            "abandonedoutpostrescue".ToIdentifier(),
            "abandonedoutpostassassinate".ToIdentifier(),
            "abandonedoutpostdestroyhumans".ToIdentifier(),
            "abandonedoutpostdestroymonsters".ToIdentifier(),
            "nestmission".ToIdentifier(),
            "miningmission".ToIdentifier(),
            "combatmissionseparatistsvscoalition".ToIdentifier(),
            "combatmissioncoalitionvsseparatists".ToIdentifier(),
            "getoutalive".ToIdentifier(),
            "abyssbeckons".ToIdentifier(),
            "europasfinest".ToIdentifier(),
            "kingofthehull".ToIdentifier(),
            "killmantis".ToIdentifier(),
            "ancientnovelty".ToIdentifier(),
            "whatsmirksbelow".ToIdentifier());

        private const float UpdateInterval = 1.0f;

        private static readonly HashSet<Identifier> unlockedAchievements = new HashSet<Identifier>();

        public static bool CheatsEnabled = false;

        private static float updateTimer;

        /// <summary>
        /// Keeps track of things that have happened during the round
        /// </summary>
        private sealed class RoundData
        {
            public readonly List<Reactor> Reactors = new List<Reactor>();

            public readonly HashSet<Character> EnteredCrushDepth = new HashSet<Character>();
            public readonly HashSet<Character> ReactorMeltdown = new HashSet<Character>();

            public bool SubWasDamaged;
        }

        private static RoundData roundData;

        // Used for the Extravehicular Activity ("crewaway") achievement
        private static PathFinder pathFinder;
        private static readonly Dictionary<Character, CachedDistance> cachedDistances = new Dictionary<Character, CachedDistance>();

        static AchievementManager()
        {
#if DEBUG
            if (SteamManager.IsInitialized && SteamManager.TryGetAllAvailableAchievements(out var achievements) && achievements.Any())
            {
                foreach (var achievement in achievements)
                {
                    if (!SupportedAchievements.Contains(achievement.Identifier.ToIdentifier()))
                    {
                        DebugConsole.ThrowError($"Achievement \"{achievement.Identifier}\" is present on Steam's backend but not in achievements supported by {nameof(AchievementManager)}.");
                    }
                }
                foreach (Identifier achievementId in SupportedAchievements)
                {
                    if (achievements.None(a => a.Identifier.ToIdentifier() == achievementId))
                    {
                        DebugConsole.ThrowError($"Could not find achievement \"{achievementId}\" on Steam's backend.");
                    }
                }
            }
#endif
        }

        public static void OnStartRound(Biome biome = null)
        {
            roundData = new RoundData();
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine == null || item.Submarine.Info.Type != SubmarineType.Player) { continue; }
                Reactor reactor = item.GetComponent<Reactor>();
                if (reactor != null && reactor.Item.Condition > 0.0f) { roundData.Reactors.Add(reactor); }
            }
            pathFinder = new PathFinder(WayPoint.WayPointList, false);
            cachedDistances.Clear();
            
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif
            
            if (biome != null && GameMain.GameSession?.GameMode is CampaignMode)
            {
                string shortBiomeIdentifier = biome.Identifier.Value.Replace(" ", "");
                UnlockAchievement($"discover{shortBiomeIdentifier}".ToIdentifier(), unlockClients: true);
                
                // Just got out of Cold Caverns
                if (shortBiomeIdentifier == "europanridge".ToIdentifier() &&
                    GameMain.NetworkMember?.ServerSettings?.RespawnMode == RespawnMode.Permadeath)
                {
                    UnlockAchievement("getoutalive".ToIdentifier(), unlockClients: true,
                        clientConditions: static client => GameMain.GameSession.PermadeathCountForAccount(client.AccountId) <= 0);
                }
            }
        }

        public static void Update(float deltaTime)
        {
            if (GameMain.GameSession == null) { return; }
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif

            updateTimer -= deltaTime;
            if (updateTimer > 0.0f) { return; }
            updateTimer = UpdateInterval;

            if (Level.Loaded != null && roundData != null && Screen.Selected == GameMain.GameScreen)
            {
                if (GameMain.GameSession.EventManager.CurrentIntensity > 0.99f)
                {
                    UnlockAchievement(
                        identifier: "maxintensity".ToIdentifier(),
                        unlockClients: true,
                        characterConditions: static c => c is { IsDead: false, IsUnconscious: false });
                }

                foreach (Character c in Character.CharacterList)
                {
                    if (c.IsDead) { continue; }
                    //achievement for descending below crush depth and coming back
                    if (GameMain.GameSession.RoundDuration > 30.0f)
                    {
                        if (c.Submarine != null && c.Submarine.AtDamageDepth || Level.Loaded.GetRealWorldDepth(c.WorldPosition.Y) > Level.Loaded.RealWorldCrushDepth)
                        {
                            roundData.EnteredCrushDepth.Add(c);
                        }
                        else if (Level.Loaded.GetRealWorldDepth(c.WorldPosition.Y) < Level.Loaded.RealWorldCrushDepth - 500.0f)
                        {
                            //all characters that have entered crush depth and are still alive get an achievement
                            if (roundData.EnteredCrushDepth.Contains(c)) { UnlockAchievement(c, "survivecrushdepth".ToIdentifier()); }
                        }
                    }
                }

                foreach (Submarine sub in Submarine.Loaded)
                {
                    foreach (Reactor reactor in roundData.Reactors)
                    {
                        if (reactor.Item.Condition <= 0.0f && reactor.Item.Submarine == sub)
                        {
                            //characters that were inside the sub during a reactor meltdown 
                            //get an achievement if they're still alive at the end of the round
                            foreach (Character c in Character.CharacterList)
                            {
                                if (!c.IsDead && c.Submarine == sub) { roundData.ReactorMeltdown.Add(c); }
                            }
                        }
                    }

                    //convert submarine velocity to km/h
                    Vector2 submarineVel = Physics.DisplayToRealWorldRatio * ConvertUnits.ToDisplayUnits(sub.Velocity) * 3.6f;
                    //achievement for going > 100 km/h
                    if (Math.Abs(submarineVel.X) > 100.0f)
                    {
                        //all conscious characters inside the sub get an achievement
                        UnlockAchievement("subhighvelocity".ToIdentifier(), true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
                    }

                    //achievement for descending ridiculously deep
                    float realWorldDepth = sub.RealWorldDepth;
                    if (realWorldDepth > 5000.0f && GameMain.GameSession.RoundDuration > 30.0f)
                    {
                        //all conscious characters inside the sub get an achievement
                        UnlockAchievement("subdeep".ToIdentifier(), true, c => c != null && c.Submarine == sub && !c.IsDead && !c.IsUnconscious);
                    }
                }

                if (!roundData.SubWasDamaged)
                {
                    roundData.SubWasDamaged = SubWallsDamaged(Submarine.MainSub);
                }
            }

            if (GameMain.GameSession != null)
            {
#if CLIENT
                if (Character.Controlled != null && !(GameMain.GameSession.GameMode is TestGameMode)) 
                { 
                    CheckMidRoundAchievements(Character.Controlled); 
                }
#else
                foreach (Client client in GameMain.Server.ConnectedClients)
                {
                    if (client.Character != null)
                    {
                        CheckMidRoundAchievements(client.Character);
                    }
                }
#endif          
            }
        }

        private static void CheckMidRoundAchievements(Character c)
        {
            if (c == null || c.Removed) { return; }

            if (c.HasEquippedItem("clownmask".ToIdentifier()) &&
                c.HasEquippedItem("clowncostume".ToIdentifier()))
            {
                UnlockAchievement(c, "clowncostume".ToIdentifier());
            }

            if (Submarine.MainSub != null && c.Submarine == null && c.SpeciesName == CharacterPrefab.HumanSpeciesName)
            {
                float requiredDist = 500 / Physics.DisplayToRealWorldRatio;
                float distSquared = Vector2.DistanceSquared(c.WorldPosition, Submarine.MainSub.WorldPosition);
                if (cachedDistances.TryGetValue(c, out var cachedDistance))
                {
                    if (cachedDistance.ShouldUpdateDistance(c.WorldPosition, Submarine.MainSub.WorldPosition))
                    {
                        cachedDistances.Remove(c);
                        cachedDistance = CalculateNewCachedDistance(c);
                        if (cachedDistance != null)
                        {
                            cachedDistances.Add(c, cachedDistance);
                        }
                    }
                }
                else
                {
                    cachedDistance = CalculateNewCachedDistance(c);
                    if (cachedDistance != null)
                    {
                        cachedDistances.Add(c, cachedDistance);
                    }
                }
                if (cachedDistance != null)
                {
                    distSquared = Math.Max(distSquared, cachedDistance.Distance * cachedDistance.Distance);
                }
                if (distSquared > requiredDist * requiredDist)
                {
                    UnlockAchievement(c, "crewaway".ToIdentifier());
                }

                static CachedDistance CalculateNewCachedDistance(Character c)
                {
                    pathFinder ??= new PathFinder(WayPoint.WayPointList, false);
                    var path = pathFinder.FindPath(ConvertUnits.ToSimUnits(c.WorldPosition), ConvertUnits.ToSimUnits(Submarine.MainSub.WorldPosition));
                    if (path.Unreachable) { return null; }
                    return new CachedDistance(c.WorldPosition, Submarine.MainSub.WorldPosition, path.TotalLength, Timing.TotalTime + Rand.Range(1.0f, 5.0f));
                }
            }
        }

        private static bool SubWallsDamaged(Submarine sub)
        {
            foreach (Structure structure in Structure.WallList)
            {
                if (structure.Submarine != sub || structure.HasBody) { continue; }
                for (int i = 0; i < structure.SectionCount; i++)
                {
                    if (structure.SectionIsLeaking(i))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static void OnCampaignMetadataSet(Identifier identifier, object value, bool unlockClients = false)
        {
            if (identifier.IsEmpty || value is null) { return; }
            UnlockAchievement($"campaignmetadata_{identifier}_{value}".ToIdentifier(), unlockClients);
        }

        public static void OnItemRepaired(Item item, Character fixer)
        {
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif
            if (fixer == null) { return; }
            
            UnlockAchievement(fixer, "repairdevice".ToIdentifier());
            UnlockAchievement(fixer, $"repair{item.Prefab.Identifier}".ToIdentifier());
        }
        
        public static void OnButtonTerminalSignal(Item item, Character user)
        {
            if (item == null || user == null) { return; }
            
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif
            if ((item.Prefab.Identifier == "alienterminal" || item.Prefab.Identifier == "alienterminal_new") && 
                item.Condition <= 0)
            {
                UnlockAchievement(user, "ancientnovelty".ToIdentifier());    
            }
        }

        public static void OnAfflictionReceived(Affliction affliction, Character character)
        {
            if (affliction.Prefab.AchievementOnReceived.IsEmpty) { return; }
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif
            UnlockAchievement(character, affliction.Prefab.AchievementOnReceived);
        }

        public static void OnAfflictionRemoved(Affliction affliction, Character character)
        {
            if (affliction.Prefab.AchievementOnRemoved.IsEmpty) { return; }

#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif
            UnlockAchievement(character, affliction.Prefab.AchievementOnRemoved);
        }

        public static void OnCharacterRevived(Character character, Character reviver)
        {
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null) { return; }
#endif
            if (reviver == null) { return; }
            UnlockAchievement(reviver, "healcrit".ToIdentifier());
        }

        public static void OnCharacterKilled(Character character, CauseOfDeath causeOfDeath)
        {
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null || GameMain.GameSession == null) { return; }

            if (character != Character.Controlled &&
                causeOfDeath.Killer != null &&
                causeOfDeath.Killer == Character.Controlled)
            {
                IncrementStat(causeOfDeath.Killer, character.IsHuman ? AchievementStat.HumansKilled : AchievementStat.MonstersKilled , 1);
            }
#elif SERVER
            if (character != causeOfDeath.Killer && causeOfDeath.Killer != null)
            {
                IncrementStat(causeOfDeath.Killer, character.IsHuman ? AchievementStat.HumansKilled : AchievementStat.MonstersKilled , 1);
            }
#endif

            UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName}".ToIdentifier());
            if (character.CurrentHull != null)
            {
                UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName}indoors".ToIdentifier());
            }
            if (character.SpeciesName.EndsWith("boss"))
            {
                UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("boss", "")}".ToIdentifier());
                if (character.CurrentHull != null)
                {
                    UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("boss", "")}indoors".ToIdentifier());
                }
            }
            if (character.SpeciesName.EndsWith("_m"))
            {
                UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("_m", "")}".ToIdentifier());
                if (character.CurrentHull != null)
                {
                    UnlockAchievement(causeOfDeath.Killer, $"kill{character.SpeciesName.Replace("_m", "")}indoors".ToIdentifier());
                }
            }
#if SERVER
            if (character.SpeciesName == "Jove" &&
                GameMain.GameSession.Campaign is MultiPlayerCampaign &&
                GameMain.Server?.ServerSettings is { IronmanModeActive: true })
            {
                UnlockAchievement(
                    identifier: "europasfinest".ToIdentifier(),
                    unlockClients: true,
                    characterConditions: static c => c is { IsDead: false });
            }
#endif

            if (character.HasEquippedItem("clownmask".ToIdentifier()) && 
                character.HasEquippedItem("clowncostume".ToIdentifier()) &&
                causeOfDeath.Killer != character)
            {
                UnlockAchievement(causeOfDeath.Killer, "killclown".ToIdentifier());
            }
            
            if (character.CharacterHealth?.GetAffliction("psychoclown") != null &&
                character.CurrentHull?.Submarine.Info is { Type: SubmarineType.BeaconStation })
            {
                UnlockAchievement(causeOfDeath.Killer, "whatsmirksbelow".ToIdentifier());
            }

            // TODO: should we change this? Morbusine used to be the strongest poison. Now Cyanide is strongest.
            if (character.CharacterHealth?.GetAffliction("morbusinepoisoning") != null)
            {
                UnlockAchievement(causeOfDeath.Killer, "killpoison".ToIdentifier());
            }

            if (causeOfDeath.DamageSource is Item item)
            {
                if (item.HasTag(Tags.ToolItem))
                {
                    UnlockAchievement(causeOfDeath.Killer, "killtool".ToIdentifier());
                }
                else
                {
                    // TODO: should we change this? Morbusine used to be the strongest poison. Now Cyanide is strongest.
                    if (item.Prefab.Identifier == "morbusine")
                    {
                        UnlockAchievement(causeOfDeath.Killer, "killpoison".ToIdentifier());
                    }
                    else if (item.Prefab.Identifier == "nuclearshell" ||
                             item.Prefab.Identifier == "nucleardepthcharge")
                    {
                            UnlockAchievement(causeOfDeath.Killer, "killnuke".ToIdentifier());
                    }
                }
            }
            
#if SERVER
            if (GameMain.Server?.ServerSettings?.RespawnMode == RespawnMode.Permadeath)
            {
                UnlockAchievement(character, "abyssbeckons".ToIdentifier());
            }

            if (GameMain.Server?.TraitorManager != null)
            {
                if (GameMain.Server.TraitorManager.IsTraitor(character))
                {
                    UnlockAchievement(causeOfDeath.Killer, "killtraitor".ToIdentifier());
                }
            }
#endif
        }

        public static void OnTraitorWin(Character character)
        {
#if CLIENT
            // If this is a multiplayer game, the client should let the server handle achievements
            if (GameMain.Client != null || GameMain.GameSession == null) { return; }
#endif
            UnlockAchievement(character, "traitorwin".ToIdentifier());
        }

        public static void OnRoundEnded(GameSession gameSession)
        {
            if (CheatsEnabled) { return; }

            //made it to the destination
            if (gameSession?.Submarine != null && Level.Loaded != null && gameSession.Submarine.AtEndExit)
            {
                float levelLengthMeters = Physics.DisplayToRealWorldRatio * Level.Loaded.Size.X;
                float levelLengthKilometers = levelLengthMeters / 1000.0f;
                //in multiplayer the client's/host's character must be inside the sub (or end outpost) and alive
                if (GameMain.NetworkMember != null)
                {
#if CLIENT
                    Character myCharacter = Character.Controlled;
                    if (myCharacter != null &&
                        !myCharacter.IsDead &&
                        (myCharacter.Submarine == gameSession.Submarine || (Level.Loaded?.EndOutpost != null && myCharacter.Submarine == Level.Loaded.EndOutpost)))
                    {
                        IncrementStat(AchievementStat.KMsTraveled, levelLengthKilometers);
                    }
#endif
                }
                else
                {
                    //in sp making it to the end is enough
                    IncrementStat(AchievementStat.KMsTraveled, levelLengthKilometers);
                }
            }

            //make sure changed stats (kill count, kms traveled) get stored
            SteamManager.StoreStats();

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            foreach (Mission mission in gameSession.Missions)
            {
                // For PvP missions, all characters on the winning team that are still alive get achievements (if available)
                if (mission is CombatMission && GameMain.GameSession.WinningTeam.HasValue)
                {
                    // Attempt unlocking team-specific achievement (if one has been set in the achievement backend)
                    var achvIdentifier =
                        $"{mission.Prefab.AchievementIdentifier}{(int) GameMain.GameSession.WinningTeam}"
                            .ToIdentifier();
                    UnlockAchievement(achvIdentifier, true,
                        c => c != null && !c.IsDead && !c.IsUnconscious && CombatMission.IsInWinningTeam(c));
                    
                    // Attempt unlocking mission-specific achievement (if one has been set in the achievement backend)
                    UnlockAchievement(mission.Prefab.AchievementIdentifier, true,
                        c => c != null && !c.IsDead && !c.IsUnconscious && CombatMission.IsInWinningTeam(c));
                }
                else if (mission is not CombatMission && mission.Completed)
                {
                    //all characters get an achievement
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        UnlockAchievement(mission.Prefab.AchievementIdentifier, true, c => c != null);
                    }
                    else
                    {
                        UnlockAchievement(mission.Prefab.AchievementIdentifier);
                    }
                }
            }
            
            //made it to the destination
            if (gameSession.Submarine != null && gameSession.Submarine.AtEndExit)
            {
                bool noDamageRun = !roundData.SubWasDamaged && !gameSession.Casualties.Any();

#if SERVER
                if (GameMain.Server != null)
                {
                    //in MP all characters that were inside the sub during reactor meltdown and still alive at the end of the round get an achievement
                    UnlockAchievement("survivereactormeltdown".ToIdentifier(), true, c => c != null && !c.IsDead && roundData.ReactorMeltdown.Contains(c));
                    if (noDamageRun)
                    {
                        UnlockAchievement("nodamagerun".ToIdentifier(), true, c => c != null && !c.IsDead);                    
                    }
                }
#endif
#if CLIENT
                if (noDamageRun) { UnlockAchievement("nodamagerun".ToIdentifier()); }
                if (roundData.ReactorMeltdown.Any()) //in SP getting to the destination after a meltdown is enough
                {
                    UnlockAchievement("survivereactormeltdown".ToIdentifier());
                }
#endif
                var charactersInSub = Character.CharacterList.FindAll(c => 
                    !c.IsDead && 
                    c.TeamID != CharacterTeamType.FriendlyNPC &&
                    c.AIController is not EnemyAIController &&
                    (c.Submarine == gameSession.Submarine || gameSession.Submarine.GetConnectedSubs().Contains(c.Submarine) || (Level.Loaded?.EndOutpost != null && c.Submarine == Level.Loaded.EndOutpost)));

                if (charactersInSub.Count == 1)
                {
                    //there must be some casualties to get the last man standing achievement
                    if (gameSession.Casualties.Any())
                    {
                        UnlockAchievement(charactersInSub[0], "lastmanstanding".ToIdentifier());
                    }
#if CLIENT
                    else if (GameMain.GameSession.CrewManager.GetCharacters().Count() == 1)
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor".ToIdentifier());
                    }
#else
                    //lone sailor achievement if alone in the sub and there are no other characters with the same team ID
                    else if (!Character.CharacterList.Any(c => 
                        c != charactersInSub[0] && 
                        c.TeamID == charactersInSub[0].TeamID && 
                        !(c.AIController is EnemyAIController)))
                    {
                        UnlockAchievement(charactersInSub[0], "lonesailor".ToIdentifier());
                    }
#endif

                }
                foreach (Character character in charactersInSub)
                {
                    if (roundData.EnteredCrushDepth.Contains(character))
                    {
                        UnlockAchievement(character, "survivecrushdepth".ToIdentifier());
                    }
                    if (character.Info.Job == null) { continue; }
                    UnlockAchievement(character, $"{character.Info.Job.Prefab.Identifier}round".ToIdentifier());
                }
            }

            pathFinder = null;
            roundData = null;
        }

        private static void UnlockAchievement(Character recipient, Identifier identifier)
        {
            if (CheatsEnabled || recipient == null) { return; }
#if CLIENT
            if (recipient == Character.Controlled)
            {
                UnlockAchievement(identifier);
            }
#elif SERVER
            GameMain.Server?.GiveAchievement(recipient, identifier);
#endif
        }

        private static void IncrementStat(Character recipient, AchievementStat stat, int amount)
        {
            if (CheatsEnabled || recipient == null) { return; }
#if CLIENT
            if (recipient == Character.Controlled)
            {
                IncrementStat(stat, amount);
            }
#elif SERVER
            GameMain.Server?.IncrementStat(recipient, stat, amount);
#endif
        }

        public static void UnlockAchievement(Identifier identifier, bool unlockClients = false, Func<Character, bool> characterConditions = null, Func<Client, bool> clientConditions = null)
        {
            if (CheatsEnabled) { return; }
            if (Screen.Selected is { IsEditor: true }) { return; }
            if (!SupportedAchievements.Contains(identifier)) { return; }
#if CLIENT
            if (GameMain.GameSession?.GameMode is TestGameMode) { return; }
#endif
#if SERVER
            if (unlockClients && GameMain.Server != null)
            {
                foreach (Client client in GameMain.Server.ConnectedClients)
                {
                    if (clientConditions != null && !clientConditions(client)) { continue; }
                    if (characterConditions != null && !characterConditions(client.Character)) { continue; }
                    GameMain.Server.GiveAchievement(client, identifier);
                }
            }
#endif

#if CLIENT
            if (characterConditions != null && !characterConditions(Character.Controlled)) { return; }
#endif

            UnlockAchievementsOnPlatforms(identifier);
        }

        private static void UnlockAchievementsOnPlatforms(Identifier identifier)
        {
            if (unlockedAchievements.Contains(identifier)) { return; }
            
            if (SteamManager.IsInitialized)
            {
                if (SteamManager.UnlockAchievement(identifier))
                {
                    unlockedAchievements.Add(identifier);
                }
            }

            if (EosInterface.Core.IsInitialized)
            {
                TaskPool.Add("Eos.UnlockAchievementsOnPlatforms", EosInterface.Achievements.UnlockAchievements(identifier), t =>
                {
                    if (!t.TryGetResult(out Result<uint, EosInterface.AchievementUnlockError> result)) { return; }
                    if (result.IsSuccess) { unlockedAchievements.Add(identifier); }
                });
            }
        }

        public static void IncrementStat(AchievementStat stat, float amount)
        {
            if (CheatsEnabled) { return; }

            IncrementStatOnPlatforms(stat, amount);
        }

        private static void IncrementStatOnPlatforms(AchievementStat stat, float amount)
        {
            if (SteamManager.IsInitialized)
            {
                SteamManager.IncrementStats(stat.ToSteam(amount));
            }

            if (EosInterface.Core.IsInitialized)
            {
                TaskPool.Add("Eos.IncrementStat", EosInterface.Achievements.IngestStats(stat.ToEos(amount)), TaskPool.IgnoredCallback);
            }
        }

        public static void SyncBetweenPlatforms()
        {
            if (!SteamManager.IsInitialized || !EosInterface.Core.IsInitialized) { return; }

            var steamStats = SteamManager.GetAllStats();

            TaskPool.AddWithResult("Eos.SyncBetweenPlatforms.QueryStats", EosInterface.Achievements.QueryStats(AchievementStatExtension.EosStats), result =>
            {
                result.Match(
                    success: stats => SyncStats(stats, steamStats),
                    failure: static error => DebugConsole.ThrowError($"Failed to query stats from EOS: {error}"));
            });

            static void SyncStats(ImmutableDictionary<AchievementStat, int> eosStats,
                                  ImmutableDictionary<AchievementStat, float> steamStats)
            {
                var steamStatsConverted = steamStats.Select(static s => s.Key.ToEos(s.Value)).ToImmutableDictionary(static s => s.Stat, static s => s.Value);
                var eosStatsConverted = eosStats.Select(static s => s.Key.ToEos(s.Value)).ToImmutableDictionary(static s => s.Stat, static s => s.Value);

                static int GetStatValue(AchievementStat stat, ImmutableDictionary<AchievementStat, int> stats) => stats.TryGetValue(stat, out int value) ? value : 0;

                var highestStats = AchievementStatExtension.EosStats.ToDictionary(
                    static key => key,
                    value =>
                        Math.Max(
                            GetStatValue(value, steamStatsConverted),
                            GetStatValue(value, eosStatsConverted)));

                List<(AchievementStat Stat, int Value)> eosStatsToIngest = new(),
                                                        steamStatsToIncrement = new();

                foreach (var (stat, value) in highestStats)
                {
                    int steamDiff = value - GetStatValue(stat, steamStatsConverted),
                        eosDiff = value - GetStatValue(stat, eosStatsConverted);

                    if (steamDiff > 0) { steamStatsToIncrement.Add((stat, steamDiff)); }
                    if (eosDiff > 0) { eosStatsToIngest.Add((stat, eosDiff)); }
                }

                if (steamStatsToIncrement.Any())
                {
                    SteamManager.IncrementStats(steamStatsToIncrement.Select(static s => s.Stat.ToSteam(s.Value)).ToArray());
                    SteamManager.StoreStats();
                }

                if (eosStatsToIngest.Any())
                {
                    TaskPool.Add("Eos.SyncBetweenPlatforms.IngestStats", EosInterface.Achievements.IngestStats(eosStatsToIngest.ToArray()), TaskPool.IgnoredCallback);
                }
            }

            if (!SteamManager.TryGetUnlockedAchievements(out List<Steamworks.Data.Achievement> steamUnlockedAchievements))
            {
                DebugConsole.ThrowError("Failed to query unlocked achievements from Steam");
                return;
            }

            TaskPool.AddWithResult("Eos.SyncBetweenPlatforms.QueryPlayerAchievements", EosInterface.Achievements.QueryPlayerAchievements(), t =>
            {
                t.Match(
                    success: eosAchievements => SyncAchievements(eosAchievements, steamUnlockedAchievements),
                    failure: static error => DebugConsole.ThrowError($"Failed to query achievements from EOS: {error}"));
            });

            static void SyncAchievements(
                ImmutableDictionary<Identifier, double> eosAchievements,
                List<Steamworks.Data.Achievement> steamUnlockedAchievements)
            {
                foreach (var (identifier, progress) in eosAchievements)
                {
                    if (!IsUnlocked(progress)) { continue; }

                    if (steamUnlockedAchievements.Any(a => a.Identifier.ToIdentifier() == identifier)) { continue; }

                    SteamManager.UnlockAchievement(identifier);
                }

                List<Identifier> eosAchievementsToUnlock = new();
                foreach (var achievement in steamUnlockedAchievements)
                {
                    Identifier identifier = achievement.Identifier.ToIdentifier();
                    if (eosAchievements.TryGetValue(identifier, out double progress) && IsUnlocked(progress)) { continue; }

                    eosAchievementsToUnlock.Add(achievement.Identifier.ToIdentifier());
                }

                if (eosAchievementsToUnlock.Any())
                {
                    TaskPool.Add("Eos.SyncBetweenPlatforms.UnlockAchievements", EosInterface.Achievements.UnlockAchievements(eosAchievementsToUnlock.ToArray()), TaskPool.IgnoredCallback);
                }

                static bool IsUnlocked(double progress) => progress >= 100.0d;
            }
        }
    }
}
